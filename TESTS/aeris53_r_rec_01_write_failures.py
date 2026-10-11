#!/usr/bin/env python3
"""Actual ordered-writer failures -> main drain -> logger -> production RecordCvr.

Writer/archive/logger/scheduler/generations compile whole. RecordCvr and its CSV
helpers are extracted verbatim. Unity/config/runtime/clock are boundary stubs;
fault streams replace only the disk boundary for Write/Flush/Dispose failures.
No production test hooks or game files. Each case runs in a fresh process.
Removing main-drain delivery must fail the warning assertions; delivering from
MarkFailure must fail the thread/undrained assertions.
"""
from pathlib import Path
import csv
from datetime import datetime
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl"


def method(source, signature):
    start = source.index(signature)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Recording;
namespace UnityEngine {
 public static class Debug {
  public static readonly List<string> Lines=new List<string>();
  public static void Log(object line){lock(Lines)Lines.Add(line.ToString());}
  public static void LogWarning(object line){Log(line);}
 }
}
public static class KSPUtil {public static string ApplicationRootPath;}
public class ConfigNode {
 public string name="AERIS_OPERATION_HEALTH";
 public static bool Off;
 public static ConfigNode Load(string path){return Off?new ConfigNode():null;}
 public ConfigNode GetNode(string name){return this;}
 public string GetValue(string name){return name=="enabled"?"false":null;}
}
public static class Planetarium {
 public static int MainId,WorkerCalls,MainCalls;
 public static double GetUniversalTime(){
  if(Thread.CurrentThread.ManagedThreadId==MainId)Interlocked.Increment(ref MainCalls);
  else Interlocked.Increment(ref WorkerCalls);
  return 1234.5;
 }
}
namespace AERISFlightControl.Performance {
 internal enum AERISOperationHealthLogLevel {Off,Normal,Diagnostic,Trace}
 internal static class AERISNavigationDisplayPipeline {internal const string JobKey="navigation-display-preprocess";}
 internal static class AERISNavigationTrafficPipeline {internal const string JobKey="navigation-traffic-preprocess";}
 internal sealed class AERISPerformanceRuntime {
  internal static AERISPerformanceRuntime Current {get{return null;}}
  internal AERISWorkerScheduler Scheduler {get{return null;}}
  internal AERISRuntimeGenerationStamp CaptureStamp(){return default(AERISRuntimeGenerationStamp);}
 }
}
class RecorderSink {
 readonly object sync=new object();
 internal AERISAsyncFileChannel cvrWriter;
 int eventCount;
 // RECORD_CVR_AND_HELPERS
}
class FaultStream:MemoryStream {
 internal string Mode;
 public override void Write(byte[] b,int o,int n){if(Mode=="write")throw new IOException("fixture write failure");base.Write(b,o,n);}
 public override void Flush(){if(Mode=="flush")throw new IOException("fixture flush failure");base.Flush();}
 protected override void Dispose(bool disposing){if(Mode=="close")throw new IOException("fixture close failure");base.Dispose(disposing);}
}
class Test {
 static string root;
 static RecorderSink recorder;
 static void Assert(bool yes,string why){if(!yes)throw new Exception(why);}
 static void Barrier(){
  using(var done=new ManualResetEvent(false)){
   Assert(AERISBackgroundFileWriter.SealSession(Path.Combine(root,"barrier"),()=>done.Set()),"barrier rejected");
   Assert(done.WaitOne(5000),"writer timeout");
  }
 }
 static List<string> Notices(){
  var lines=new List<string>();lock(UnityEngine.Debug.Lines)
   foreach(string line in UnityEngine.Debug.Lines)if(line.Contains("[FILE_WRITER]"))lines.Add(line);
  return lines;
 }
 static void Drain(){AERISFlightDataArchive.DrainResults();}
 static void Safe(){Assert(Planetarium.WorkerCalls==0,"off-main game clock");}
 static AERISAsyncFileChannel BadOpen(string name){
  string path=Path.Combine(root,"fault",name+".csv");Directory.CreateDirectory(path);
  var c=new AERISAsyncFileChannel(path,false,AERISFileRecordPriority.CriticalEvent);Barrier();
  Assert(Directory.Exists(path),"failed open removed source");return c;
 }
 static AERISAsyncFileChannel DiskFault(string mode){
  string path=Path.Combine(root,"fault",mode+".csv");
  var c=new AERISAsyncFileChannel(path,false,AERISFileRecordPriority.CriticalEvent);Barrier();
  long id=(long)typeof(AERISAsyncFileChannel).GetField("channelId",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c);
  var type=typeof(AERISBackgroundFileWriter);
  object sync=type.GetField("Sync",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  var writers=(Dictionary<long,StreamWriter>)type.GetField("Writers",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  // Worker is idle behind the ordered barrier. Replace only its stream I/O boundary.
  lock(sync){writers[id].Dispose();writers[id]=new StreamWriter(new FaultStream{Mode=mode},new UTF8Encoding(false),1024);}
  if(mode=="write")c.WriteLine(new string('x',131072));
  else if(mode=="flush"){c.WriteLine("buffered");c.Flush();}
  else c.Dispose();
  Barrier();Assert(File.Exists(path),"failed I/O removed raw file");return c;
 }
 static void Warning(long count,string operation,string pathPart){
  Safe();Assert(Notices().Count==0,"failure logged before main drain");
  Drain();Safe();var lines=Notices();Assert(lines.Count==1,"expected one coalesced failure notice, got "+lines.Count);
  Assert(lines[0].Contains("[WARN]")&&lines[0].Contains("failures="+count+";"),"severity/count lost");
  Assert(lines[0].Contains("operation="+operation+";")&&lines[0].Contains(pathPart),"operation/path lost");
  Drain();Assert(Notices().Count==1,"failure redelivered");
 }
 static void RetainedAndHealthy(){
  int sealedCount=0;AERISBackgroundFileWriter.SealSession(Path.Combine(root,"fault"),()=>Interlocked.Increment(ref sealedCount));
  string path=Path.Combine(root,"healthy.csv");var healthy=new AERISAsyncFileChannel(path,false,AERISFileRecordPriority.CriticalEvent);
  Assert(healthy.WriteLine("healthy"),"healthy write rejected");healthy.Dispose();Barrier();
  Assert(sealedCount==0,"failed raw folder sealed");Assert(File.ReadAllText(path).Trim()=="healthy","healthy channel stopped");
 }
 static void Run(string name){
  switch(name){
   case "healthy":
    var healthy=new AERISAsyncFileChannel(Path.Combine(root,"healthy.csv"),false,AERISFileRecordPriority.CriticalEvent);
    healthy.WriteLine("value,42");healthy.Dispose();Barrier();Drain();Drain();
    Assert(Notices().Count==0&&AERISBackgroundFileWriter.SnapshotTelemetry().Failures==0,"healthy path warned");
    Assert(File.ReadAllText(Path.Combine(root,"healthy.csv")).Trim()=="value,42","healthy output changed");break;
   case "open":
    var bad=BadOpen("open");Assert(AERISBackgroundFileWriter.SnapshotTelemetry().Failures==1,"open not failed");
    Assert(bad.Available&&bad.WriteLine("discarded"),"producer admission semantics changed");Barrier();
    Warning(1,"Open","open.csv");Assert(Notices()[0].Contains("UnauthorizedAccessException"),"exception detail lost");
    RetainedAndHealthy();bad.Dispose();break;
   case "write":case "flush":case "close":
    var faulty=DiskFault(name);Assert(AERISBackgroundFileWriter.SnapshotTelemetry().Failures==1,"I/O failure not tracked");
    Warning(1,name=="write"?"Text":name=="flush"?"Flush":"Close",name+".csv");
    Assert(Notices()[0].Contains("fixture "+name+" failure"),"I/O reason lost");RetainedAndHealthy();faulty.Dispose();break;
   case "burst":
    for(int i=0;i<300;i++)BadOpen("burst"+i).Dispose();Barrier();
    Assert(AERISBackgroundFileWriter.SnapshotTelemetry().Failures==300,"burst failure count changed");
    Warning(300,"Open","burst299.csv");RetainedAndHealthy();break;
   case "logger_files_fail":
    Assert(AERISBackgroundFileWriter.SnapshotTelemetry().Failures==3,"expected rotate and two log-open failures");
    Warning(3,"Open","_session.log");Barrier();
    for(int i=0;i<10;i++){Drain();Barrier();}
    Assert(Notices().Count==1&&AERISBackgroundFileWriter.SnapshotTelemetry().Failures==3,"warning caused failure feedback loop");break;
   case "cvr_fail":
    recorder.cvrWriter.Dispose();Barrier();recorder.cvrWriter=BadOpen("cvr");
    Warning(1,"Open","cvr.csv");Barrier();Drain();Assert(Notices().Count==1,"failed CVR feedback loop");break;
   case "logging_off":
    BadOpen("off").Dispose();Warning(1,"Open","off.csv");break;
   case "reentrant_sink":
    bool blocked=false;AERISLogger.EventSink=(level,message)=>{
     recorder.RecordCvr("AERIS",level,message);
     var producer=new Thread(()=>AERISBackgroundFileWriter.SnapshotTelemetry());producer.Start();
     if(!producer.Join(1000))blocked=true;
     // Nested main drain must not redeliver the claimed failure.
     Drain();
    };
    BadOpen("reentrant").Dispose();Warning(1,"Open","reentrant.csv");Assert(!blocked,"sink ran under writer lock");break;
   case "repeat_and_later":
    var first=BadOpen("first");Warning(1,"Open","first.csv");
    for(int i=0;i<100;i++)first.WriteLine("discarded");Barrier();Drain();
    Assert(Notices().Count==1,"discarded writes warned repeatedly");first.Dispose();
    BadOpen("second").Dispose();Drain();Assert(Notices().Count==2,"later failure lost");
    Assert(Notices()[1].Contains("failures=1;")&&Notices()[1].Contains("second.csv"),"later warning stale");break;
   case "retain_raw":
    AERISBackgroundFileWriter.RetainRawSession(Path.Combine(root,"fault"),"existing caller retention");Barrier();Drain();
    Assert(Notices().Count==0&&AERISBackgroundFileWriter.SnapshotTelemetry().Failures==1,"retention policy changed");break;
   case "rotate":
    string src=Path.Combine(root,"rotate-source.log"),blockedParent=Path.Combine(root,"rotate-parent");
    string dst=Path.Combine(blockedParent,"rotate-target.log");
    File.WriteAllText(src,"original");File.WriteAllText(blockedParent,"blocking parent");
    Assert(AERISBackgroundFileWriter.Rotate(src,dst),"rotate admission changed");Barrier();
    Assert(AERISBackgroundFileWriter.SnapshotTelemetry().Failures==1,"rotation did not fail");
    Warning(1,"Rotate","rotate-source.log");Assert(File.ReadAllText(src)=="original","failed rotation altered source");break;
   case "seal_callback":
    string sealedFolder=Path.Combine(root,"seal-source");Directory.CreateDirectory(sealedFolder);
    AERISBackgroundFileWriter.SealSession(sealedFolder,()=>{throw new IOException("fixture seal failure");});Barrier();
    Warning(1,"Seal","seal-source");Assert(Directory.Exists(sealedFolder),"failed callback removed folder");break;
   default:throw new Exception("unknown case");
  }
  Safe();
 }
 static int Main(string[] args){
  string name=args[0];root=args[1];Directory.CreateDirectory(root);KSPUtil.ApplicationRootPath=root;
  Planetarium.MainId=Thread.CurrentThread.ManagedThreadId;ConfigNode.Off=name=="logging_off";
  if(name=="logger_files_fail")File.WriteAllText(Path.Combine(root,"GameData"),"blocking parent");
  AERISLogger.Initialize();Barrier();lock(UnityEngine.Debug.Lines)UnityEngine.Debug.Lines.Clear();
  recorder=new RecorderSink();recorder.cvrWriter=new AERISAsyncFileChannel(Path.Combine(root,"cvr.csv"),false,AERISFileRecordPriority.CriticalEvent);
  recorder.cvrWriter.WriteLine("utc,ut,source,level,message");Barrier();
  AERISLogger.EventSink=(level,message)=>recorder.RecordCvr("AERIS",level,message);
  int code=0;try{Run(name);Console.WriteLine("PASS: "+name);}
  catch(Exception e){Console.WriteLine("FAIL: "+name+": "+e.Message);code=1;}
  finally{AERISLogger.EventSink=null;recorder.cvrWriter.Dispose();AERISLogger.Shutdown();Barrier();AERISBackgroundFileWriter.ShutdownNoWait();}
  return code;
 }
}
'''

recorder = (SOURCE / "Recording/AERISFlightDataRecorder.cs").read_text()
signatures = ["internal void RecordCvr(string source, string level, string message)",
              "static AERISCsvField F(double value)", "static AERISCsvField Utc(DateTime value)",
              "static AERISCsvField Csv(string value)"]
harness = HARNESS.replace("// RECORD_CVR_AND_HELPERS", "\n".join(
    method(recorder, signature) for signature in signatures))
cases = ["healthy", "open", "write", "flush", "close", "burst", "logger_files_fail",
         "cvr_fail", "logging_off", "reentrant_sink", "repeat_and_later", "retain_raw",
         "rotate", "seal_callback"]

with tempfile.TemporaryDirectory(prefix="aeris-rec01-") as temp:
    folder = Path(temp)
    fixture = folder / "Fixture.cs"
    fixture.write_text(harness)
    exe = folder / "WriteFailures.exe"
    sources = ["Performance/AERISBackgroundFileWriter.cs", "Recording/AERISFlightDataArchive.cs",
               "Logging/AERISLogger.cs", "Performance/AERISWorkerScheduler.cs",
               "Performance/AERISRuntimeGeneration.cs"]
    subprocess.run(["mcs", "-out:" + str(exe), str(fixture),
                    *(str(SOURCE / name) for name in sources)], check=True)
    passed = 0
    for name in cases:
        scratch = folder / name
        result = subprocess.run(["mono", str(exe), name, str(scratch)], timeout=20)
        if result.returncode:
            continue
        with (scratch / "cvr.csv").open(newline="") as stream:
            rows = list(csv.reader(stream))
        expected = 0 if name in ("healthy", "retain_raw", "cvr_fail") else 2 if name == "repeat_and_later" else 1
        assert rows[0] == ["utc", "ut", "source", "level", "message"]
        assert len(rows) - 1 == expected, (name, "CVR row count", len(rows) - 1)
        for row in rows[1:]:
            assert len(row) == 5 and row[1:4] == ["1234.5000", "AERIS", "WARN"]
            assert row[4].startswith("[FILE_WRITER]")
            datetime.fromisoformat(row[0].replace("Z", "+00:00"))
        passed += 1
    print(f"R-REC-01 WRITE FAILURES {passed}/{len(cases)} PASS")
    raise SystemExit(0 if passed == len(cases) else 1)
