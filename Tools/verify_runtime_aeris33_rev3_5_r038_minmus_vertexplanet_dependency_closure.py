#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS33 R038 MINMUS VERTEXPLANET DEPENDENCY CLOSURE RUNTIME VERIFY]'
MAIN_IL='513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args()
p=Path(args.log_path).expanduser().resolve()
if not p.is_file():raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
def field(line,name):
    m=re.search(r'(?:^|; |\] )'+re.escape(name)+r'=([^;]+)',line)
    if not m:raise SystemExit(PREFIX+' FAIL missing '+name+' in '+line)
    return m.group(1).strip()
begins=[i for i,l in enumerate(lines) if '[R038][VERTEXPLANET] event=SNAPSHOT_BEGIN' in l]
if not begins:
    fails=[l for l in lines if '[R038][VERTEXPLANET_FAIL]' in l]
    if fails:print('\n'.join(fails[-80:]))
    raise SystemExit(PREFIX+' FAIL no R038 snapshot begin')
start=begins[-1]
completions=[i for i,l in enumerate(lines) if i>start and '[R038][VERTEXPLANET] event=DEPENDENCY_CLOSURE_COMPLETE' in l]
if not completions:raise SystemExit(PREFIX+' FAIL no R038 completion after latest snapshot')
end=completions[0];section=lines[start:end+1]
fails=[l for l in section if '[R038][VERTEXPLANET_FAIL]' in l]
if fails:
    print('\n'.join(fails[-100:]));raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
begin=lines[start];complete=lines[end];bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok:bad.append(label)
check(field(begin,'body')=='Minmus','Minmus target')
check(field(begin,'main_il_sha256')==MAIN_IL,'VertexPlanet accepted main IL hash')
check(field(complete,'body')=='Minmus','completion body')
check(field(complete,'wrappers')=='5','five runtime wrappers')
check(field(complete,'simplex_wrappers')=='4','four simplex wrappers')
check(field(complete,'noise_wrappers')=='1','one noise wrapper')
check(field(complete,'helper_methods')=='3','three helper methods')
check(field(complete,'witness_points')=='12','twelve PQS witnesses')
check(field(complete,'failures')=='0','closure failures')
check(field(complete,'worker_ready').lower()=='false','Minmus remains fail-closed')
check(field(complete,'pending')=='PQSMod_VertexPlanet:PURE_CPU_FORMULA_RECONSTRUCTION_PENDING','expected Minmus pending reason')
check(field(complete,'runtime_object_invocation_thread')=='MAIN_THREAD_ONLY','runtime object access main-thread only')
check(field(complete,'worker_invokes_runtime_object').lower()=='false','worker runtime-object isolation')
check(field(complete,'db_write').lower()=='false','terrain DB untouched')
check(field(complete,'producer_switch').lower()=='false','producer authority unchanged')
check(field(complete,'gpu').lower()=='false','GPU path untouched')
check(field(complete,'authority')=='PQS','PQS remains authority')

helpers=[l for l in section if '[R038][HELPER_IL]' in l]
for name in ('Lerp','Clamp','CubicHermite'):
    rows=[l for l in helpers if 'label='+name+';' in l]
    print('[INFO] helper '+name+' rows='+str(len(rows)))
    if len(rows)!=1:bad.append(name+' helper IL count='+str(len(rows)))
helper_w=[l for l in section if '[R038][HELPER_WITNESS]' in l]
for name in ('Lerp','Clamp','CubicHermite'):
    rows=[l for l in helper_w if 'method='+name+';' in l]
    print('[INFO] helper witness '+name+' rows='+str(len(rows)))
    if len(rows)!=3:bad.append(name+' helper witness count='+str(len(rows)))

getters=[l for l in section if '[R038][GETTER_IL]' in l]
expected_getters=(
    'continental.get_simplex','continentalSmoothing.get_simplex',
    'continentalSharpnessMap.get_simplex','continentalRuggedness.get_simplex',
    'continentalSharpness.get_noise')
for label in expected_getters:
    rows=[l for l in getters if 'label='+label+';' in l]
    print('[INFO] getter '+label+' rows='+str(len(rows)))
    if len(rows)!=1:bad.append(label+' getter IL count='+str(len(rows)))

native=[l for l in section if '[R038][NATIVE]' in l]
for label in ('continental','continentalSmoothing','continentalSharpnessMap','continentalRuggedness'):
    for method in ('noise','noiseNormalized'):
        rows=[l for l in native if 'label='+label+';' in l and 'method='+method+';' in l and 'index=' in l]
        print('[INFO] native '+label+'.'+method+' rows='+str(len(rows)))
        if len(rows)!=6:bad.append(label+'.'+method+' native witness count='+str(len(rows)))
rows=[l for l in native if 'label=continentalSharpness;' in l and 'method=GetValue;' in l and 'index=' in l]
print('[INFO] native continentalSharpness.GetValue rows='+str(len(rows)))
if len(rows)!=6:bad.append('continentalSharpness.GetValue native witness count='+str(len(rows)))

witness=[l for l in section if '[R038][PQS_WITNESS]' in l]
print('[INFO] PQS witness rows='+str(len(witness)))
if len(witness)!=12:bad.append('PQS witness count='+str(len(witness)))
indices=sorted(int(field(l,'index')) for l in witness) if len(witness)==12 else []
if indices and indices!=list(range(12)):bad.append('PQS witness indices not 0..11')

scalars=[l for l in section if '[R038][SCALAR]' in l]
for name in ('deformity','oceanLevel','oceanSnap','oceanDepth','oceanStep','terrainRidgeBalance','terrainRidgesMin','terrainRidgesMax','terrainShapeStart','terrainShapeEnd'):
    rows=[l for l in scalars if 'name='+name+';' in l]
    if len(rows)!=1:bad.append(name+' scalar count='+str(len(rows)))

r037=[l for l in lines[:end+1] if '[R037][COMMON_CPU] event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE' in l]
if not r037:
    bad.append('parent R037 completion missing')
else:
    parent=r037[-1]
    print('[INFO] parent R037 worker_ready_bodies='+field(parent,'worker_ready_bodies')+' pending_bodies='+field(parent,'pending_bodies'))
    if field(parent,'worker_ready_bodies')!='3':bad.append('parent R037 worker-ready regression')
    if field(parent,'pending_bodies')!='1':bad.append('parent R037 pending regression')
    if field(parent,'body_failures')!='0':bad.append('parent R037 body failures')
    if field(parent,'terrain_failures')!='0':bad.append('parent R037 terrain failures')

print('[INFO] selected_start_line='+str(start+1)+' completion_line='+str(end+1))
if bad:raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('[INFO] accepted=VertexPlanet runtime dependency/helper/native/PQS witness closure captured; Minmus intentionally not worker-ready')
print('[INFO] next=R039 reconstruct PQSMod_VertexPlanet as pure CPU math and compare exact worker output against the captured native/PQS witnesses')
