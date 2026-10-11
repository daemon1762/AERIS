#!/usr/bin/env python3
"""Production recorder startup/cleanup, with memory-only admission/clock boundaries.

Methods and fields are extracted verbatim except the isolated diagnostic flag is
mutable so both policies can run in one Mono process (readonly gets JIT-folded).
No game objects, threads or game files.
The same isolated fixture can be compiled into the temporary runtime observer.
Disk failure delivery/archive ordering remain covered by R-REC-01/02 regressions.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'


def method(source, signature):
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


CASES = ['disabled', 'disabled-diagnostic-reject', 'enabled', 'diagnostic-reject', 'core-reject', 'burst',
         'deadline', 'recovery', 'vessel-change', 'invalid-vessel', 'explicit-end',
         'healthy-reentry', 'cleanup', 'recovery-scan', 'metadata']

HARNESS = r'''
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
namespace AERIS53RecorderBatchIsolated {
static class Time {internal static float realtimeSinceStartup;}
class Vessel {internal Guid id=Guid.NewGuid();internal string vesselName="fixture";
 internal object transform=new object();internal Body mainBody=new Body();}
class Body {internal string bodyName="fixture-body";}
static class AERISBuildVersion {internal const string Semantic="fixture";}
class AERISRecorderTelemetrySchema {}
class AaComparisonConditionSummary {}
enum AERISFileRecordPriority {CriticalEvent,Continuous,Verbose}
class AERISAsyncFileChannel:IDisposable {
 internal static List<AERISAsyncFileChannel> All=new List<AERISAsyncFileChannel>();
 internal static string Reject="";
 internal string PathName;internal bool Available,Closed;
 internal List<string> Lines=new List<string>();
 internal AERISAsyncFileChannel(string path,bool append,AERISFileRecordPriority priority){
  PathName=path;Available=priority.ToString()!=Reject;All.Add(this);}
 internal bool WriteLine(string line){if(!Available)return false;Lines.Add(line);return true;}
 internal void Flush(){}
 public void Dispose(){Closed=true;Available=false;}
}
static class AERISLogger {internal static void Info(string line){}}
static class AERISFlightDataArchive {
 internal static int Scans,Archives;
 internal static void DrainResults(){}
 internal static void QueueRecoveryArchives(string root,string folder,string legacy){Scans++;}
 internal static void QueueArchive(string folder){Archives++;}
}
static class AERISBackgroundFileWriter {
 internal static int Retained;
 internal static void RetainRawSession(string path,string detail){Retained++;}
 internal static bool SealSession(string path,Action callback){callback();return true;}
}
class Recorder {
 // FIELDS
 static string RootPath {get{return "memory-only";}}
 static string LegacyRootPath {get{return "memory-only-legacy";}}
 internal void RecordCvr(string source,string level,string message){
  if(cvrWriter!=null)cvrWriter.WriteLine(message);eventCount++;}
 void WriteAaComparisonConditionSummary(){}
 void WriteSummary(string reason){}
 internal long Ordinal {get{return sessionOrdinal;}}
 internal bool Running {get{return cvrWriter!=null&&fdrWriter!=null;}}
 // METHODS
}
public static class Cases {
 public static readonly string[] Names={"disabled","disabled-diagnostic-reject","enabled","diagnostic-reject","core-reject","burst","deadline","recovery","vessel-change","invalid-vessel","explicit-end","healthy-reentry","cleanup","recovery-scan","metadata"};
 static void Assert(bool condition,string detail){if(!condition)throw new Exception(detail);}
 static int Opens {get{return AERISAsyncFileChannel.All.Count;}}
 static int Live {get{int n=0;foreach(var c in AERISAsyncFileChannel.All)if(!c.Closed)n++;return n;}}
 static bool Has(string name){foreach(var c in AERISAsyncFileChannel.All)
  if(Path.GetFileName(c.PathName)==name)return true;return false;}
 public static void Run(string mode){
  Time.realtimeSinceStartup=0;AERISAsyncFileChannel.All.Clear();AERISAsyncFileChannel.Reject="";
  AERISFlightDataArchive.Scans=0;AERISFlightDataArchive.Archives=0;AERISBackgroundFileWriter.Retained=0;
  typeof(Recorder).GetField("R016HighRateDiagnosticsEnabled",System.Reflection.BindingFlags.Static|
   System.Reflection.BindingFlags.NonPublic).SetValue(null,mode=="enabled"||mode=="diagnostic-reject");
  var r=new Recorder();var v=new Vessel();
  if(mode=="disabled") {r.BeginFlight(v);Assert(r.Running,"core recording unavailable");
   Assert(!Has("fdr_bank_diagnostics.csv")&&!Has("fdr_ground_takeoff.csv"),"disabled files opened");
   Assert(Opens==3,"disabled session should open CVR/FDR/metadata only");}
  else if(mode=="disabled-diagnostic-reject") {AERISAsyncFileChannel.Reject="Verbose";
   r.BeginFlight(v);Assert(r.Running,"unused diagnostic admission stopped core recording");}
  else if(mode=="enabled") {r.BeginFlight(v);Assert(Opens==13&&r.Running,"enabled diagnostic paths changed");
   foreach(var c in AERISAsyncFileChannel.All)Assert(c.Lines.Count>0,"missing enabled header");}
  else if(mode=="diagnostic-reject") {AERISAsyncFileChannel.Reject="Verbose";
   Assert(Fails(r,v)&&!r.Running&&Live==0,"enabled diagnostic admission must fail cleanly");}
  else if(mode=="core-reject") {AERISAsyncFileChannel.Reject="Continuous";
   Assert(Fails(r,v)&&!r.Running&&Live==0,"rejected core must fail cleanly");}
  else if(mode=="burst") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   for(int i=0;i<500;i++){Time.realtimeSinceStartup=i/100f;r.BeginFlight(v);}
   Assert(r.Ordinal==1&&Opens==2&&Live==0,"failed startup retried before deadline");}
  else if(mode=="deadline") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   Time.realtimeSinceStartup=4.999f;r.BeginFlight(v);Assert(r.Ordinal==1,"early retry");
   Time.realtimeSinceStartup=5;Assert(Fails(r,v)&&r.Ordinal==2,"deadline must permit retry");
   Time.realtimeSinceStartup=9.999f;r.BeginFlight(v);Assert(r.Ordinal==2,"second early retry");}
  else if(mode=="recovery") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   AERISAsyncFileChannel.Reject="";Time.realtimeSinceStartup=5;r.BeginFlight(v);
   Assert(r.Running&&r.Ordinal==2,"healthy retry failed");r.BeginFlight(v);
   Assert(r.Ordinal==2,"successful session reopened");}
  else if(mode=="vessel-change") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   AERISAsyncFileChannel.Reject="";r.BeginFlight(new Vessel());Assert(r.Running&&r.Ordinal==2,"new vessel delayed");}
  else if(mode=="invalid-vessel") {r.BeginFlight(null);v.transform=null;r.BeginFlight(v);
   Assert(Opens==0&&r.Ordinal==0,"invalid vessel consumed attempt");v.transform=new object();r.BeginFlight(v);
   Assert(r.Running&&r.Ordinal==1,"first valid vessel delayed");}
  else if(mode=="explicit-end") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   r.EndFlight("scene-exit");AERISAsyncFileChannel.Reject="";r.BeginFlight(v);
   Assert(r.Running&&r.Ordinal==2,"new flight inherited failed retry delay");}
  else if(mode=="healthy-reentry") {r.BeginFlight(v);r.BeginFlight(v);
   Assert(r.Ordinal==1,"healthy session reopened");r.EndFlight("scene-exit");
   Assert(Live==0,"session channel leak");r.BeginFlight(v);Assert(r.Ordinal==2&&r.Running,"new flight failed");}
  else if(mode=="cleanup") {AERISAsyncFileChannel.Reject="Continuous";Fails(r,v);
   Assert(Live==0&&AERISBackgroundFileWriter.Retained>0,"partial start cleanup/retention lost");}
  else if(mode=="recovery-scan") {r.BeginFlight(v);r.EndFlight("done");r.BeginFlight(new Vessel());
   Assert(AERISFlightDataArchive.Scans==1,"repeated recovery scan");}
  else if(mode=="metadata") {r.BeginFlight(v);foreach(var c in AERISAsyncFileChannel.All)
   if(Path.GetFileName(c.PathName)=="metadata.txt"){
    Assert(c.Lines.Contains("HIGH_RATE_DIAGNOSTICS_ENABLED=False"),"missing disabled metadata");
    foreach(var l in c.Lines)Assert(!l.StartsWith("BANK_DIAGNOSTICS="),"disabled file advertised");}}
  else throw new Exception("unknown case "+mode);
 }
 static bool Fails(Recorder r,Vessel v){try{r.BeginFlight(v);return false;}catch(IOException){return true;}}
#if !AUDIT_LIB
 static int Main(string[] args){try{if(args[0]=="all")foreach(var name in Names)Run(name);else Run(args[0]);Console.WriteLine("PASS: "+args[0]);return 0;}
 catch(Exception ex){Console.Error.WriteLine("FAIL: "+args[0]+": "+ex.Message);return 1;}}
#endif
}
}
'''


def fixture_source(source=None):
    source = SOURCE.read_text() if source is None else source
    fields = source[source.index('readonly object sync'):source.index('sealed class AaComparisonMetric')]
    constants = source[source.index('const float SampleIntervalSeconds'):source.index('internal static string R016IsolationMarker')]
    # Only startup constants/state; no comparison schemas or game capture logic.
    constants += '\n' + \
        source[source.index('long sessionOrdinal'):source.index('long sessionOrdinal')+len('long sessionOrdinal;\n        bool recoveryScanRequested;')]
    constants = constants.replace('static readonly bool R016HighRateDiagnosticsEnabled',
                                  'static bool R016HighRateDiagnosticsEnabled')
    signatures = ['internal void BeginFlight(', 'void BeginFlightCore(', 'internal void EndFlight(',
                  'static void CloseWriter(', 'static void CloseWriterBestEffort(',
                  'AERISAsyncFileChannel OpenDiagnostic(', 'bool CoreChannelsAvailable(',
                  'static string StableHash8(', 'static string Sanitize(', 'void WriteMetadata(']
    return HARNESS.replace('// FIELDS', constants + fields).replace(
        '// METHODS', '\n'.join(method(source, s) for s in signatures))


def main():
    with tempfile.TemporaryDirectory(prefix='aeris-rec-start-') as td:
        path = Path(td)
        (path/'Fixture.cs').write_text(fixture_source())
        subprocess.run(['mcs', '-warn:0', '-out:'+str(path/'Fixture.exe'), str(path/'Fixture.cs')], check=True)
        results = [subprocess.run(['mono', str(path/'Fixture.exe'), mode]).returncode for mode in CASES]
        subprocess.run(['mono', str(path/'Fixture.exe'), 'all'], check=True)
        passed = results.count(0)
        print(f'R-REC-03/04 STARTUP {passed}/{len(CASES)} PASS')
        raise SystemExit(0 if passed == len(CASES) else 1)


if __name__ == '__main__':
    main()
