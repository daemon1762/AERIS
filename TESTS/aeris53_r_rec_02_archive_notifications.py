#!/usr/bin/env python3
"""Real ordered writer -> archive -> logger -> production RecordCvr regression.

Archive, logger, ordered writer, scheduler and generation registry are compiled
whole. RecordCvr and its CSV helpers are extracted verbatim; the rest of the
recorder is omitted. Unity/config/runtime hosting and the game clock are boundary
stubs. Each case gets a new process and private scratch paths; no KSP files.
"""
from pathlib import Path
import csv
from datetime import datetime
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl"


def method(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
public static class KSPUtil { public static string ApplicationRootPath; }
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
    internal enum AERISOperationHealthLogLevel { Off,Normal,Diagnostic,Trace }
    internal static class AERISNavigationDisplayPipeline { internal const string JobKey="navigation-display-preprocess"; }
    internal static class AERISNavigationTrafficPipeline { internal const string JobKey="navigation-traffic-preprocess"; }
    // Scene host only; scheduler, jobs, generations and worker threads are real.
    internal sealed class AERISPerformanceRuntime {
        internal static AERISPerformanceRuntime Current;
        readonly AERISGenerationRegistry registry=new AERISGenerationRegistry();
        internal readonly AERISWorkerScheduler Scheduler;
        internal AERISPerformanceRuntime(){Scheduler=new AERISWorkerScheduler(registry,new AERISActivePermitController(),2);}
        internal AERISRuntimeGenerationStamp CaptureStamp(){return registry.Capture(Stopwatch.GetTimestamp());}
    }
}
class RecorderSink {
    readonly object sync=new object();
    internal AERISAsyncFileChannel cvrWriter;
    int eventCount;
    // RECORD_CVR_AND_HELPERS
}
class Test {
    static string root;
    static RecorderSink recorder;
    static void Assert(bool yes,string message){if(!yes)throw new Exception(message);}
    static void OffThread(Action action){
        Exception error=null;var t=new Thread(()=>{try{action();}catch(Exception e){error=e;}});
        t.Start();Assert(t.Join(5000),"producer timeout");if(error!=null)throw error;
    }
    static void WriterBarrier(){
        using(var done=new ManualResetEvent(false)){
            Assert(AERISBackgroundFileWriter.SealSession(root,()=>done.Set()),"barrier rejected");
            Assert(done.WaitOne(5000),"writer timeout");
        }
    }
    static void Seal(string folder){
        // Same callback as production EndFlight, on the real ordered writer.
        using(var done=new ManualResetEvent(false)){
            Assert(AERISBackgroundFileWriter.SealSession(folder,()=>{
                AERISFlightDataArchive.QueueArchive(folder);done.Set();}),"seal rejected");
            Assert(done.WaitOne(5000),"seal timeout");
        }
    }
    static void Safe(){Assert(Planetarium.WorkerCalls==0,"game clock called from worker "+Planetarium.WorkerCalls+" time(s)");}
    static List<string> Logs(string level=null){
        var values=new List<string>();lock(UnityEngine.Debug.Lines)
            foreach(string line in UnityEngine.Debug.Lines)
                if(line.Contains("[FDR][ARCHIVE]")&&(level==null||line.Contains("["+level+"]")))values.Add(line);
        return values;
    }
    static void Drain(){AERISFlightDataArchive.DrainResults();}
    static void Runtime(){AERISPerformanceRuntime.Current=new AERISPerformanceRuntime();}
    static void Pump(){
        var watch=Stopwatch.StartNew();
        while(AERISFlightDataArchive.CompletedCount<1){
            AERISPerformanceRuntime.Current.Scheduler.DrainCommits(24);Drain();
            Assert(watch.ElapsedMilliseconds<5000,"archive completion timeout");Thread.Sleep(1);
        }
        Drain();
    }
    static void Run(string name){
        string folder=Path.Combine(root,"flight");
        switch(name){
            case "seal_without_runtime":
                Seal(folder);Safe();Assert(Logs().Count==0,"notification emitted before main drain");
                Drain();Assert(Logs("INFO").Count==1,"queued message lost");
                Assert(Logs("WARN").Count==1,"unavailable result lost");
                Assert(Planetarium.MainCalls==2,"CVR did not receive both main-thread events");break;
            case "seal_with_runtime":
                Runtime();Seal(folder);Safe();Assert(Logs().Count==0,"worker emitted archive log");
                Drain();var logs=Logs("INFO");Assert(logs.Count==2,"queued/accepted messages lost");
                Assert(logs[0].Contains("queued; pending=1; folder="+folder),"queued text/order changed");
                Assert(logs[1].Contains("scheduler accepted; folder="+folder),"accepted text/order changed");
                Assert(Planetarium.MainCalls==2,"main-thread CVR count mismatch");break;
            case "main_producer":
                AERISFlightDataArchive.QueueArchive(folder);
                Assert(Logs().Count==0,"notification bypassed main drain");Drain();Safe();
                Assert(Logs("INFO").Count==1&&Logs("WARN").Count==1,"main producer event loss");
                int count=Logs().Count;Drain();Assert(Logs().Count==count,"notifications delivered twice");break;
            case "duplicate_and_empty":
                Runtime();OffThread(()=>{
                    AERISFlightDataArchive.QueueArchive(null);AERISFlightDataArchive.QueueArchive("");
                    AERISFlightDataArchive.QueueArchive(folder);AERISFlightDataArchive.QueueArchive(folder);
                });Safe();Drain();Assert(Logs("INFO").Count==2,"duplicate/empty admission changed");break;
            case "zip_success":
                Runtime();Directory.CreateDirectory(folder);
                var raw=new AERISAsyncFileChannel(Path.Combine(folder,"sample.csv"),false,AERISFileRecordPriority.CriticalEvent);
                raw.WriteLine("value");raw.WriteLine("42");raw.Dispose();Seal(folder);Safe();Pump();
                Assert(File.Exists(folder+".zip")&&!Directory.Exists(folder),"verified archive/delete failed");
                Assert(Logs("INFO").Count==3&&Logs("WARN").Count==0,"success messages changed");break;
            case "empty_raw_retained":
                Runtime();Directory.CreateDirectory(folder);Seal(folder);Safe();Pump();
                Assert(Directory.Exists(folder)&&!File.Exists(folder+".zip"),"failed archive removed raw folder");
                Assert(Logs("INFO").Count==2&&Logs("WARN").Count==1,"failed result missing");break;
            case "unsealable_raw_retained":
                Directory.CreateDirectory(folder);int callbacks=0;
                AERISBackgroundFileWriter.RetainRawSession(folder,"fixture failure");
                AERISBackgroundFileWriter.SealSession(folder,()=>{Interlocked.Increment(ref callbacks);AERISFlightDataArchive.QueueArchive(folder);});
                WriterBarrier();Drain();Safe();Assert(callbacks==0&&Logs().Count==0,"failed seal scheduled archive");
                Assert(Directory.Exists(folder),"unsealable raw removed");break;
            case "notification_burst_preserves_results":
                OffThread(()=>{for(int i=0;i<300;i++)AERISFlightDataArchive.QueueArchive(Path.Combine(root,"burst"+i));});
                Safe();Drain();Assert(Logs("INFO").Count==256,"notification queue not bounded");
                Assert(Logs("WARN").Count==256,"notifications evicted archive results");break;
            case "logging_off":
                Seal(folder);Safe();Drain();Assert(Logs("INFO").Count==0,"OFF policy bypassed");
                Assert(Logs("WARN").Count==1,"OFF hid warning");break;
            case "reentrant_sink":
                bool injected=false,blocked=false;
                AERISLogger.EventSink=(level,message)=>{
                    recorder.RecordCvr("AERIS",level,message);
                    if(injected)return;injected=true;
                    var producer=new Thread(()=>AERISFlightDataArchive.QueueArchive(Path.Combine(root,"nested")));
                    producer.Start();if(!producer.Join(1000))blocked=true;
                };
                AERISFlightDataArchive.QueueArchive(folder);Drain();Safe();
                Assert(!blocked,"logger sink held archive lock and blocked producer");
                Assert(Logs("INFO").Count==2&&Logs("WARN").Count==2,"nested notification/result lost");break;
            default:throw new Exception("unknown case");
        }
        Safe();
    }
    static int Main(string[] args){
        string name=args[0];root=args[1];Directory.CreateDirectory(root);
        KSPUtil.ApplicationRootPath=root;Planetarium.MainId=Thread.CurrentThread.ManagedThreadId;
        ConfigNode.Off=name=="logging_off";AERISLogger.Initialize();
        lock(UnityEngine.Debug.Lines)UnityEngine.Debug.Lines.Clear();
        recorder=new RecorderSink();recorder.cvrWriter=new AERISAsyncFileChannel(Path.Combine(root,"cvr.csv"),false,AERISFileRecordPriority.CriticalEvent);
        recorder.cvrWriter.WriteLine("utc,ut,source,level,message");
        AERISLogger.EventSink=(level,message)=>recorder.RecordCvr("AERIS",level,message);
        int exit=0;try{Run(name);Console.WriteLine("PASS: "+name);}
        catch(Exception e){Console.WriteLine("FAIL: "+name+": "+e.Message);exit=1;}
        finally{
            AERISLogger.EventSink=null;recorder.cvrWriter.Dispose();AERISLogger.Shutdown();WriterBarrier();
            if(AERISPerformanceRuntime.Current!=null)AERISPerformanceRuntime.Current.Scheduler.Dispose();
            AERISBackgroundFileWriter.ShutdownNoWait();
        }
        return exit;
    }
}
'''

recorder_source = (SOURCE / "Recording/AERISFlightDataRecorder.cs").read_text()
helpers = ["internal void RecordCvr(string source, string level, string message)",
           "static AERISCsvField F(double value)",
           "static AERISCsvField Utc(DateTime value)",
           "static AERISCsvField Csv(string value)"]
harness = HARNESS.replace("// RECORD_CVR_AND_HELPERS", "\n".join(
    method(recorder_source, signature) for signature in helpers))
cases = ["seal_without_runtime", "seal_with_runtime", "main_producer",
         "duplicate_and_empty", "zip_success", "empty_raw_retained",
         "unsealable_raw_retained", "notification_burst_preserves_results", "logging_off", "reentrant_sink"]

with tempfile.TemporaryDirectory(prefix="aeris-rec02-") as temp:
    folder = Path(temp)
    fixture = folder / "Fixture.cs"
    fixture.write_text(harness)
    exe = folder / "Notifications.exe"
    sources = ["Recording/AERISFlightDataArchive.cs", "Logging/AERISLogger.cs",
               "Performance/AERISBackgroundFileWriter.cs",
               "Performance/AERISWorkerScheduler.cs", "Performance/AERISRuntimeGeneration.cs"]
    subprocess.run(["mcs", "-out:" + str(exe), str(fixture),
                    *(str(SOURCE / name) for name in sources)], check=True)
    passed = 0
    # Successful archive also emits the existing retention status to CVR.
    expected_rows = dict(zip(cases, [2, 2, 2, 2, 4, 3, 0, 512, 1, 4]))
    for name in cases:
        scratch = folder / name
        result = subprocess.run(["mono", str(exe), name, str(scratch)], timeout=20)
        if result.returncode:
            continue
        with (scratch / "cvr.csv").open(newline="") as stream:
            rows = list(csv.reader(stream))
        assert rows[0] == ["utc", "ut", "source", "level", "message"]
        assert len(rows) - 1 == expected_rows[name], (name, "CVR row count", len(rows) - 1)
        for row in rows[1:]:
            assert len(row) == 5 and row[1] == "1234.5000" and row[2] == "AERIS"
            assert row[3] in ("INFO", "WARN")
            assert row[4].startswith("[FDR][ARCHIVE]") or (
                name == "zip_success" and row[4].startswith("[FDR][RETENTION]"))
            datetime.fromisoformat(row[0].replace("Z", "+00:00"))
        if name == "zip_success":
            # Independent standard reader verifies the actual produced ZIP bytes.
            with zipfile.ZipFile(scratch / "flight.zip") as archive:
                assert archive.namelist() == ["sample.csv"]
                assert archive.read("sample.csv").replace(b"\r\n", b"\n") == b"value\n42\n"
        passed += 1
    print(f"R-REC-02 ARCHIVE NOTIFICATIONS {passed}/{len(cases)} PASS")
    raise SystemExit(0 if passed == len(cases) else 1)
