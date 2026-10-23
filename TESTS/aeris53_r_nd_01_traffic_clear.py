#!/usr/bin/env python3
"""Real traffic pipeline/scheduler + extracted ND lifecycle, private game boundaries.

No production objects/files. Pending-result tests run the actual worker and commit
queue; restoring frame-only clears must retain/revive the seeded alert and fail.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'Source/AERISFlightControl'
CASES = ['traffic-off', 'invalid-vessel', 'missing-runtime', 'viewport-off',
         'dispose', 'profile-change', 'pending-result', 'resume', 'normal-clear']


def method(source, signature):
    start = source.index(signature)
    end = source.index('{', start)+1
    depth = 1
    while depth:
        depth += (source[end] == '{')-(source[end] == '}')
        end += 1
    return source[start:end]


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AERISFlightControl.Performance;
namespace AERISFlightControl.Logging {
 static class AERISLogger {internal static void Info(string s){} internal static void Warn(string s){} internal static void Error(string s){}}
}
namespace AERISFlightControl.Performance {
 static class AERISPreparedNavigationFrameApi {internal static void Clear(){}}
 static class AERISNavigationDisplayPipeline {internal const string JobKey="navigation-display-preprocess";}
 sealed class AERISPerformanceRuntime {
  internal readonly AERISWorkerScheduler scheduler;
  internal readonly AERISNavigationTrafficPipeline navigationTraffic;
  internal readonly AERISGenerationRegistry generations;
  bool disposed;
  internal AERISPerformanceRuntime(){generations=new AERISGenerationRegistry();scheduler=new AERISWorkerScheduler(generations,new AERISActivePermitController(),2);navigationTraffic=new AERISNavigationTrafficPipeline(scheduler);}
  internal AERISRuntimeGenerationStamp CaptureStamp(){return generations.Capture(Stopwatch.GetTimestamp());}
  // RUNTIME_METHODS
 }
}
namespace AERIS53NdTestHost {
 static class Time {internal static float realtimeSinceStartup;}
 static class Mathf {internal static int Clamp(int x,int a,int b){return Math.Max(a,Math.Min(b,x));}}
 class Body {internal string name="Kerbin";internal double Radius=600000;}
 class Vessel {
  internal uint persistentId=1;internal Body mainBody=new Body();internal bool LandedOrSplashed;
  internal enum Situations {PRELAUNCH,FLYING,SUB_ORBITAL} internal Situations situation=Situations.FLYING;
  internal enum Types {Plane,DEBRIS,FLAG,EVA,SPACEOBJECT,UNKNOWN} internal Types vesselType=Types.Plane;
  internal double latitude,longitude,altitude=1000,srfSpeed=100;internal string vesselName="fixture";
  internal List<object> parts=new List<object>();
 }
 static class FlightGlobals {internal static IList<Vessel> Vessels=new List<Vessel>();}
 class Settings {internal bool NavigationDisplayTrafficEnabled=true;internal float NavigationDisplayManualRangeMeters=20000;internal static float NormalizeNavigationRange(float f){return f;}}
 class AERISSettings {internal static float NormalizeNavigationRange(float f){return f;}}
 class Profile {internal int MaximumFacilitySymbols=16;}
 class TerrainPerformance {internal Profile ActiveProfile=new Profile();}
 class Terrain {internal TerrainPerformance Performance=new TerrainPerformance();}
 class Core {internal AERISPerformanceRuntime Performance;internal Terrain Terrain=new Terrain();}
 class Renderer {internal void ResumeVisibilityWarm(){} internal void SuspendVisibilityWarm(){} internal void Dispose(){}}
 class Profiles {internal string Apply(Vessel v,Settings s){return "fixture";}}
 static class AERISNavigationDisplayProfileStore {internal static string CreateSignature(Vessel v){return "fixture";}}
 class TrafficCaptureEntry {internal double DistanceMeters;internal AERISNavigationTrafficSource Source;}
 class NdFixture {
  internal Core core;internal Settings settings=new Settings();
  readonly Profiles profileStore=new Profiles();readonly Renderer terrainTileRenderer=new Renderer();
  internal bool flightViewportActive=true;
  float nextNavigationSnapshotRealtime,nextSymbologySnapshotRealtime,nextNavigationCaptureRealtime,nextNdGcSampleRealtime,nextTerrainTelemetrySampleRealtime,pendingManualRangeMeters,pendingManualRangeApplyRealtime,nextTrailSampleRealtime,nextTrafficCaptureRealtime;
  object cachedLandingObservation,cachedLandingDirection,cachedTerrainTileTelemetry;
  string capturedBodyName,previewRunwayStableId,previewTrafficStableId,windProviderName,activeProfileSignature,activeProfileLabel,trailBodyName;
  long capturedDatabaseRevision,capturedSelectionRevision;int previewAirfieldIndex,previewDirectionIndex,activeProfilePartCount;
  bool windValid,planMode,mapPointerDown,mapDragging;Vessel activeProfileVessel;uint activeProfileRuntimeVesselId,trailVesselPersistentId;
  readonly List<object> trailSamples=new List<object>();readonly List<TrafficCaptureEntry> trafficCaptureScratch=new List<TrafficCaptureEntry>();
  internal NdFixture(AERISPerformanceRuntime p){core=new Core{Performance=p};}
  void ResetTerrainSynchronizedSymbology(){} void SaveActiveProfile(){}
  static double ResolveMapHeading(Vessel v){return 0;}
  static void ToLocalMeters(Body b,double a,double c,double d,double e,out double east,out double north){east=d-a;north=e-c;}
  internal void Capture(Vessel v){CaptureTrafficSnapshot(v);}
  internal void Profile(Vessel v){SyncVesselProfile(v);}
  // DISPLAY_METHODS
 }
 public static class TrafficCases {
  public static readonly string[] Names={"traffic-off","invalid-vessel","missing-runtime","viewport-off","dispose","profile-change","pending-result","resume","normal-clear"};
  static void Assert(bool b,string why){if(!b)throw new Exception(why);}
  static void Empty(){AERISPreparedTrafficFrame f;AERISTrafficAlertState a;Assert(!AERISPreparedTrafficFrameApi.TryGetLatest(out f),"traffic frame retained/revived");Assert(!AERISTrafficAlertApi.TryGetLatest(out a),"traffic alert retained/revived");}
  static void Seed(){var f=new AERISPreparedTrafficFrame{Traffic=new[]{new AERISPreparedTrafficSymbol{StableId="seed",Name="seed",ThreatLevel=2}}};AERISPreparedTrafficFrameApi.Publish(f);AERISTrafficAlertApi.Publish(f);}
  static void Queue(AERISPerformanceRuntime p){Assert(p.SubmitNavigationTraffic(new AERISNavigationTrafficSnapshot{
   Generation=p.CaptureStamp(),BodyName="Kerbin",BodyRadiusMeters=600000,OwnAltitudeAslMeters=1000,
   Traffic=new[]{new AERISNavigationTrafficSource{StableId="pending",Name="pending",AltitudeAslMeters=1000}}}),"submit rejected");
   var watch=Stopwatch.StartNew();while(p.scheduler.SnapshotTelemetry().ResultDepth==0){if(watch.ElapsedMilliseconds>5000)throw new Exception("worker timeout");Thread.Yield();}}
  public static void Run(string mode){
   Time.realtimeSinceStartup=0;FlightGlobals.Vessels=new List<Vessel>();AERISPreparedTrafficFrameApi.Clear();AERISTrafficAlertApi.Clear();
   var p=new AERISPerformanceRuntime();var nd=new NdFixture(p);Seed();
   try{
    if(mode=="traffic-off"){nd.settings.NavigationDisplayTrafficEnabled=false;nd.Capture(new Vessel());Empty();}
    else if(mode=="invalid-vessel"){nd.Capture(null);Empty();}
    else if(mode=="missing-runtime"){nd.core.Performance=null;nd.Capture(new Vessel());Empty();}
    else if(mode=="viewport-off"){nd.SetFlightViewportActive(false);Empty();}
    else if(mode=="dispose"){nd.Dispose();Empty();}
    else if(mode=="profile-change"){nd.Profile(new Vessel());Empty();}
    else if(mode=="pending-result"){Queue(p);nd.SetFlightViewportActive(false);p.scheduler.DrainCommits(24);Empty();}
    else if(mode=="resume"){nd.settings.NavigationDisplayTrafficEnabled=false;nd.Capture(new Vessel());Empty();nd.settings.NavigationDisplayTrafficEnabled=true;Queue(p);p.scheduler.DrainCommits(24);AERISTrafficAlertState a;Assert(AERISTrafficAlertApi.TryGetLatest(out a)&&a.StableId=="pending","fresh traffic did not resume");}
    else if(mode=="normal-clear"){AERISTrafficAlertApi.Publish(new AERISPreparedTrafficFrame());AERISTrafficAlertState a;Assert(!AERISTrafficAlertApi.TryGetLatest(out a),"normal empty frame did not clear");}
    else throw new Exception("unknown case");
   }finally{p.navigationTraffic.Dispose();p.scheduler.Dispose();}
  }
#if !AUDIT_LIB
  static int Main(string[] args){try{Run(args[0]);Console.WriteLine("PASS: "+args[0]);return 0;}catch(Exception e){Console.Error.WriteLine("FAIL: "+args[0]+": "+e.Message);return 1;}}
#endif
 }
}
'''


def fixture_source():
    display = (SOURCE/'UI/AERISNavigationDisplay.cs').read_text()
    runtime = (SOURCE/'Performance/AERISPerformanceRuntime.cs').read_text()
    signatures = ['internal void SetFlightViewportActive(', 'internal void Dispose(',
                  'void CaptureTrafficSnapshot(', 'void SyncVesselProfile(']
    if 'void ClearTrafficSnapshot(' in display:
        signatures.append('void ClearTrafficSnapshot(')
    runtime_signatures = ['internal bool SubmitNavigationTraffic(']
    if 'internal void ClearNavigationTraffic(' in runtime:
        runtime_signatures.append('internal void ClearNavigationTraffic(')
    return HARNESS.replace('// DISPLAY_METHODS', '\n'.join(method(display, s) for s in signatures)).replace(
        '// RUNTIME_METHODS', '\n'.join(method(runtime, s) for s in runtime_signatures))


def production_sources():
    return [SOURCE/'Performance'/name for name in ['AERISNavigationTrafficPipeline.cs',
            'AERISWorkerScheduler.cs', 'AERISRuntimeGeneration.cs']]


if __name__ == '__main__':
    with tempfile.TemporaryDirectory(prefix='aeris-nd-clear-') as td:
        p = Path(td);(p/'Fixture.cs').write_text(fixture_source())
        subprocess.run(['mcs', '-warn:0', '-out:'+str(p/'Cases.exe'), str(p/'Fixture.cs'),
                        *map(str, production_sources())], check=True)
        results = [subprocess.run(['mono', str(p/'Cases.exe'), case], timeout=15).returncode for case in CASES]
        print(f'R-ND-01 TRAFFIC CLEAR {results.count(0)}/{len(CASES)} PASS')
        raise SystemExit(0 if not any(results) else 1)
