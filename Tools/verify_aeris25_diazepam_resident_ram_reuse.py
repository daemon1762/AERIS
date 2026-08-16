#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT/'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C = (ROOT/'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U = (ROOT/'build_ubuntu.sh').read_text()
P5 = (ROOT/'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
checks=[]
def ck(v,n):
    ok=bool(v); checks.append((ok,n)); print(('[PASS] ' if ok else '[FAIL] ')+n)

def method(signature):
    start=R.find(signature)
    if start<0:return ''
    op=R.find('{',start)
    if op<0:return ''
    depth=0; state='code'; i=op
    while i<len(R):
        c=R[i]; n=R[i+1] if i+1<len(R) else ''
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c=='"': state='str'; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c=='{': depth+=1
            elif c=='}':
                depth-=1
                if depth==0:return R[start:i+1]
            i+=1; continue
        if state=='line':
            if c=='\n': state='code'
            i+=1; continue
        if state=='block':
            if c=='*' and n=='/': state='code'; i+=2; continue
            i+=1; continue
        if state in ('str','char'):
            if c=='\\': i+=2; continue
            if (state=='str' and c=='"') or (state=='char' and c=="'"): state='code'
            i+=1
    return ''

ck('internal const string Codename = "DIAZEPAM";' in M and
   'internal const string Revision = "OH_PHASE7_001";' in M and
   'internal const string Candidate = "AERIS25_RESIDENT_RAM_REUSE_STRENGTHENING";' in M and
   'codename = DIAZEPAM' in C,
   'DIAZEPAM OH_PHASE7_001 identity is authoritative')
ck('OPERATION HEALTH PHASE 7 DIAZEPAM RESIDENT RAM REUSE STRENGTHENING REV006' in U and
   'OPERATION HEALTH PHASE 7 DIAZEPAM — RESIDENT RAM REUSE STRENGTHENING — REV006' in U,
   'build/in-game identity is DIAZEPAM REV006')
ck('AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE' in R,
   'REV006 resident/RAM reuse marker exists')
ck('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in R and
   'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' not in R and
   'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION' not in R,
   'REV006 is based on frozen REV003 and excludes rejected REV004/005 worker designs')

payload=R[R.find('sealed class ResidentPreparedPresentation'):R.find('struct SurfacePoint',R.find('sealed class ResidentPreparedPresentation'))]
ck(payload and 'Mesh ' not in payload and 'RenderTexture' not in payload and
   'GameObject' not in payload and 'Transform' not in payload,
   'prepared resident payload contains managed data only, never Unity Object/VRAM owners')
ck('readonly Dictionary<string, ResidentPreparedPresentation>' in R and
   'residentPreparedPresentations' in R and
   'must have the same cacheKey in renderReadyFields' in R,
   'prepared payload is a sidecar of existing render-ready cache ownership')

begin=method('        bool TryBeginPendingEntryCommit(AERISTerrainRenderReadyHeightField result)')
create=method('        PendingEntryCommit CreatePendingFromResidentPrepared(')
store=method('        void StoreResidentPrepared(PendingEntryCommit pending)')
remove=method('        void RemoveRenderReadyField(string cacheKey,')
advance=method('        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
ck(begin and 'TryGetResidentPrepared(cacheKey, out prepared)' in begin and
   'CreatePendingFromResidentPrepared(cacheKey, result,' in begin and
   'operationHealthResidentPrepMisses++' in begin,
   'commit admission checks exact prepared-RAM reuse before clip path')
ck(create and 'ResidentPreparedReuse = true' in create and
   'Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh' in create and
   'PackedGeographic = prepared.PackedGeographic' in create and
   'ContourGeographic = prepared.ContourGeographic' in create and
   'CoastlineGeographic = prepared.CoastlineGeographic' in create,
   'cache hit starts at Unity mesh acquisition with geographic arrays already prepared')
ck(advance and 'if (pending.ResidentPreparedReuse)' in advance and
   'pending.Stage = PendingEntryCommitStage.Finalize;' in advance and
   'PendingEntryCommitStage.GeographicPacked' in advance,
   'cache hit bypasses geographic stages while cache miss keeps frozen staged fallback')
ck(store and 'PackedSource = pending.PackedSource' in store and
   'PackedGeographic = pending.PackedGeographic' in store and
   'LandElevation = pending.LandElevation' in store and
   'operationHealthResidentPrepStores++' in store,
   'first successful commit retains immutable prepared arrays by reference')
ck(remove and 'residentPreparedPresentations.Remove(normalizedKey);' in remove and
   'operationHealthResidentPrepBytes' in remove,
   'render-ready eviction removes prepared sidecar under the same lifetime authority')
ck('residentBudget * 3L / 4L' in R and
   'Math.Min(512L * 1024L * 1024L, target)' in R,
   'prepared presentation RAM budget is bounded to 75% resident budget and <=512 MiB')
ck('CoastalLandCorrectionElevationMeters =\n                    result.CoastalLandCorrectionElevationMeters' in R and
   'CoastalLandCorrectionShade = result.CoastalLandCorrectionShade' in R and
   'Valid = result.Valid' in R,
   'immutable worker/result arrays are shared instead of cloned on final commit')

for field in ('oh_resident_prep_hit=','oh_resident_prep_miss=',
              'oh_resident_prep_store=','oh_resident_prep_evict=',
              'oh_resident_prep_bytes=','oh_resident_prep_bytes_peak=',
              'oh_resident_prep_reuse_vertices='):
    ck(field in R,'runtime telemetry publishes '+field[:-1])

ck('runtime.Scheduler.SubmitRequired(' not in R and 'WaitManagedPreparation' not in R,
   'DIAZEPAM adds no managed-preparation worker dependency')
ck('Task.Run' not in R and 'new Thread' not in R,
   'DIAZEPAM renderer adds no ad-hoc worker/thread path')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'visible ND presentation authority remains fixed 10 Hz')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'ARGB32/Bilinear quality authority unchanged')
ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'presentationEntryPins.Contains(entry)' in R,
   'ADENOSINE persistent packet/snapshot pin architecture remains intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R,
   'snapshot Mesh lifetime guard remains intact')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R,
   'ATROPINE content generation burst governor remains intact')
ck('OH_PHASE7_001' in P5 and 'DIAZEPAM' in P5 and
   'verify_aeris25_diazepam_resident_ram_reuse.py' in P5,
   'inherited ADENOSINE verifier admits only explicit DIAZEPAM successor')
active='\n'.join(line for line in U.splitlines()
                 if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('verify_aeris25_diazepam_resident_ram_reuse.py' in active and
   'verify_aeris25_authoritative_publication_lifetime_hotfix.py' not in active,
   'DIAZEPAM build has one final Phase7 verifier')

frozen=['Source/AERISFlightControl/AA','Source/AERISFlightControl/Autopilot',
        'Source/AERISFlightControl/Protect','Source/AERISFlightControl/Landing']
if (ROOT/'.git').exists():
    try:
        changed=subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only','HEAD','--']+frozen,
                                        text=True, stderr=subprocess.DEVNULL).strip().splitlines()
    except Exception:
        changed=['GIT_DIFF_UNAVAILABLE']
else:
    changed=[]
ck(changed==[],'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed=[n for ok,n in checks if not ok]
print('\n[AERIS25 DIAZEPAM PHASE7_001 RESIDENT RAM REUSE REV006] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+'; '.join(failed)); raise SystemExit(1)
print('[AERIS25 DIAZEPAM PHASE7_001 RESIDENT RAM REUSE REV006] STATIC PASS')
