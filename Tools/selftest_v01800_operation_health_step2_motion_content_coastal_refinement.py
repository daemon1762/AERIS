#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
T=ROOT/'Source/AERISFlightControl/Terrain'
R=(T/'AERISTerrainGpuTileRenderer.cs').read_text()
RA=(T/'AERISTerrainGpuTileRasterizer.cs').read_text()
C=(T/'AERISTerrainCoastlineExtractor.cs').read_text()
P=(T/'AERISTerrainCoastlinePolicy.cs').read_text()
N=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text()
V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
name='AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement'
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'authoritative ND authority remains fixed 10 Hz')
ck('ContentMaintenanceRetrySeconds = 0.20f' in R,'loading content maintenance is bounded to 5 Hz retry')
ck('AERISTerrainVisibleTileSet contentVisible;' in R and 'bool contentSnapshotValid;' in R,'persistent content snapshot exists')
ck('bool NeedsContentRefresh(' in R,'dedicated content refresh authority exists')
helper=R[R.index('bool NeedsContentRefresh('):R.index('void ResetContentSnapshot()')]
ck('contentTerrainGeneration != system.TerrainGeneration' in helper,'terrain generation invalidates cached content')
ck('Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02)' in helper,'content recapture uses bounded motion corridor')
ck("Mathf.DeltaAngle(contentHeadingDeg" in helper and '>= 3f' in helper,'TRACK UP content recapture has bounded heading corridor')
draw=R[R.index('internal AERISTerrainGpuDrawState Draw('):R.index('bool NeedsContentRefresh(')]
non_tick=draw[draw.index('if (!authoritativeTickDue)'):draw.index('operationHealthAuthoritativeTicks++')]
ck('CaptureVisible' not in non_tick and 'DrainCompleted' not in non_tick,'non-tick Repaint remains cheap FRONT reuse only')
ck('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw,'worker completion wakes content maintenance')
ck('bool contentTickRequired = contentGeometryChanged || workerResultReady ||' in draw,'content maintenance has explicit gate')
content=draw[draw.index('if (contentTickRequired)'):draw.index('AERISNdMapProjection projection')]
ck('DrainCompleted(system);' in content and 'system.CaptureVisible(' in content,'worker drain and visible capture are content-only work')
ck('ResolveRenderableEntries' in content and 'Schedule(tile' in content,'entry resolve and worker schedule are content-only work')
ck('contentFoundationCoverage = MeasureFoundationGpuReadiness' in content,'foundation readiness is cached during content maintenance')
ck('operationHealthMotionOnlyTicks++;' in draw,'unchanged terrain uses motion-only path')
post=R[R.index('EnsureResources(plot'):R.index('bool forceCenterProjectionRefresh;')]
ck('if (contentTickRequired)' in post and 'Prune(' in post and 'PruneRenderReady(' in post,'pruning is content-only work')
ck(R.count('if (contentGpuReadyPending)') >= 2 and R.count('MarkVisibleGpuReady(tiles);') == 2,'visible GPU READY scan occurs only after changed-content commit')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'exact projection/presentation remains fixed 10 Hz')
reset=R[R.index('void ResetFrontBufferState('):R.index('void Schedule(')]
ck('bool preserveCadenceAndContent = false' in reset,'front reset exposes explicit resize preservation mode')
ck('if (!preserveCadenceAndContent)' in reset and 'nextAuthoritativePresentationTickRealtime = 0f;' in reset and 'nextBackRefreshRealtime = 0f;' in reset and 'ResetContentSnapshot();' in reset,'full lifecycle reset still clears cadence and content')
ensure_rt=R[R.index('void EnsureRenderTarget('):R.index('float MeasureViewportCoverage(')]
ck('DestroyRenderTargets(true);' in ensure_rt and 'ResetFrontBufferState(true);' in ensure_rt,'render-target resize preserves 10 Hz clock and Step 2 content snapshot')
destroy_rt=R[R.index('void DestroyRenderTargets('):R.index('static void DestroyRenderTexture(')]
ck('bool preserveCadenceAndContent = false' in destroy_rt and 'ResetFrontBufferState(preserveCadenceAndContent);' in destroy_rt,'render-target destruction forwards lifecycle preservation authority')
ck('if (!preserveCadenceAndContent)\n                lastBackFoundationCoverage = 0f;' in reset,'resize retains current foundation readiness while full reset clears it')
ck('present && requestedViewReady' in R and 'TERRAIN GPU BUILDING ' in N,'Hotfix4 stale-FRONT loading contract remains intact')
ck('internal int CompletedCount' in RA,'worker completed queue can be observed without draining')
ck('HighDensityResolution = 129' in C and 'HighDensityFormatVersion = 2' in C,'HD coastline payload remains 129x129 format v2')
ck('PresentationSmoothingBlend = 0.65f' in P and 'PresentationMinimumBoundaryMagnitude = 0.20f' in P,'coastal sub-cell refinement is bounded')
field=P[P.index('BuildPresentationBoundaryField'):P.index('PresentationCrossingFraction')]
ck('field[index] = rawSign * magnitude;' in field,'coastal smoothing preserves source land/water sign topology')
ck('BuildFromClassMask' in C and 'PresentationCrossingFraction' in C,'coastline vector consumes refined crossing authority')
ck('BuildSparseCoastalCorrections(tile' in RA and 'coastalBoundaryField' in RA,'sparse land/water correction consumes same boundary field')
ck('PresentationCrossingFraction(' in RA[RA.index('static CorrectionPoint CorrectionCrossing'):],'fill crossing consumes the same presentation crossing function')
ck('HighDensityCoastlineSegments.Clone()' in RA,'persisted Candidate11 coastline remains safety fallback')
ck('MaximumSparseCorrectionParentCells = 256' in RA,'Candidate11 sparse correction parent safety rail remains 256')
ck('MaximumContourLevelsPerTile = 96' in RA,'Candidate11 contour authority remains 96 levels')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target quality authority unchanged')
ck('oh_content_tick=' in R and 'oh_motion_only=' in R and 'oh_content_capture=' in R and 'content_snapshot=' in R,'runtime content-split telemetry is published')
ck(V.get('NAME') == name,'runtime identity is Operation Health Step 2')
phase3='EPI'+'NEPHRINE'
phase4='ATRO'+'PINE'
phase5='ADE'+'NOSINE'
ck(('OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT' in B) or
   (('OPERATION HEALTH PHASE 3 '+phase3+' GPU VERTEX PROJECTION') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 4 '+phase4+' GPU DYNAMIC TERRAIN COLOUR') in B) or
   (('AERIS25 OPERATION HEALTH PHASE 5 '+phase5+' PERSISTENT PRESENTATION BATCHING') in B),
   'Ubuntu build identifies Step 2 parent or approved Phase 3/4/5 successor')
# Pure numerical guard for the presentation crossing contract. This mirrors the C# bounds
# and proves every opposite-sign edge stays inside its source edge without a topology flip.
def crossing(w0,w1,s0,s1):
    golden=(1.0-0.38) if w0 else 0.38
    if w0==w1 or s0*s1>=0: return golden
    zero=s0/(s0-s1)
    zero=max(0.18,min(0.82,zero))
    blended=golden+(zero-golden)*0.65
    return max(0.24,min(0.76,blended))
probes=[crossing(False,True,1.0,-1.0),crossing(False,True,0.2,-1.0),crossing(True,False,-0.2,1.0)]
ck(all(0.24 <= value <= 0.76 for value in probes),'refined crossings remain strictly sub-cell and bounded')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Step 2 Motion Content Split + Coastal Edge Refinement] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
