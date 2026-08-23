#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS33 R037 POL FLATTEN OCEAN EXACT CLOSURE RUNTIME VERIFY]'
EXPECTED_IL={
    'PQSMod_VertexPlanet':'513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687',
    'PQSMod_FlattenOcean':'4b00ff62f5a99eeae99d7236b16a0aa1dfed1d22a6c9cc991d6da38fce55a112',
    'PQSMod_VertexSimplexHeight':'7750cd3ec2087cc89c494c814286dd893c7e51e5957461adf13f55e76aa867e4',
}
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args()
p=Path(args.log_path).expanduser().resolve()
if not p.is_file(): raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
snapshots=[i for i,l in enumerate(lines) if '[R037][COMMON_CPU] event=SNAPSHOT_COMPLETE' in l]
if not snapshots:
    fails=[l for l in lines if '[R037][COMMON_CPU_FAIL]' in l or '[R037][IL_FAIL]' in l]
    if fails: print('\n'.join(fails[-50:]))
    raise SystemExit(PREFIX+' FAIL no R037 snapshot event')
snapshot_index=snapshots[-1]
completion_indices=[i for i,l in enumerate(lines) if '[R037][COMMON_CPU] event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE' in l]
after=[i for i in completion_indices if i>snapshot_index]
if not after: raise SystemExit(PREFIX+' FAIL no completion after latest snapshot')
completion_index=after[0]
previous=[i for i in completion_indices if i<snapshot_index]
run_start=(previous[-1]+1) if previous else 0
section=lines[run_start:completion_index+1]
fails=[l for l in section if '[R037][COMMON_CPU_FAIL]' in l or '[R037][IL_FAIL]' in l]
if fails:
    print('\n'.join(fails[-80:])); raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
completes=[l for l in section if '[R037][COMMON_CPU] event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE' in l]
if len(completes)!=1: raise SystemExit(PREFIX+' FAIL expected exactly one completion in selected R037 run, got '+str(len(completes)))
complete=completes[0]
def field(line,name):
    m=re.search(r'(?:^|; )'+re.escape(name)+r'=([^;]+)',line)
    if not m: raise SystemExit(PREFIX+' FAIL missing '+name+' in '+line)
    return m.group(1).strip()
checks=[
(field(complete,'bodies')=='4','target body count'),
(field(complete,'worker_ready_bodies')=='3','expected three worker-ready bodies'),
(field(complete,'pending_bodies')=='1','expected one formula-pending body'),
(field(complete,'body_failures')=='0','worker-ready body failures'),
(field(complete,'primitive_failures')=='0','primitive exactness'),
(field(complete,'terrain_failures')=='0','terrain exactness'),
(field(complete,'worker_off_main').lower()=='true','worker off main thread'),
(field(complete,'runtime_object_invocation_thread')=='MAIN_THREAD_ONLY','runtime object access main-thread only'),
(field(complete,'worker_invokes_runtime_object').lower()=='false','worker runtime-object isolation'),
(field(complete,'db_write').lower()=='false','terrain DB untouched'),
(field(complete,'producer_switch').lower()=='false','producer authority unchanged'),
(field(complete,'gpu').lower()=='false','GPU path untouched'),
(field(complete,'authority')=='PQS','PQS remains authority')]
bad=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
bodies=[l for l in section if '[R037][BODY]' in l]
results=[l for l in section if '[R037][BODY_RESULT]' in l]
expected={'Gilly':'true','Ike':'true','Pol':'true','Minmus':'false'}
for body,want in expected.items():
    br=[l for l in bodies if 'body='+body+';' in l]
    rr=[l for l in results if 'body='+body+';' in l]
    if len(br)!=1: bad.append(body+' body telemetry count='+str(len(br))); continue
    if len(rr)!=1: bad.append(body+' result telemetry count='+str(len(rr))); continue
    ready=field(br[0],'worker_ready').lower()
    inert=field(br[0],'landcontrol_inert').lower()
    evaluated=field(rr[0],'evaluated').lower()
    pending=field(br[0],'pending')
    print('[INFO] '+body+' worker_ready='+ready+' landcontrol_inert='+inert+
          ' pending='+pending+' evaluated='+evaluated+
          ' max_primitive_abs_error='+field(rr[0],'max_primitive_abs_error')+
          ' max_terrain_abs_error_m='+field(rr[0],'max_terrain_abs_error_m'))
    if ready!=want: bad.append(body+' worker_ready expected '+want+' got '+ready)
    if inert!='true': bad.append(body+' LandControl inert guard failed')
    if evaluated!=want: bad.append(body+' evaluated expected '+want+' got '+evaluated)
    if body=='Minmus' and pending!='PQSMod_VertexPlanet:FORMULA_CLOSURE_PENDING':
        bad.append('Minmus pending reason changed: '+pending)
    if want=='true':
        if field(rr[0],'primitive_failures')!='0': bad.append(body+' primitive failures')
        if field(rr[0],'terrain_failures')!='0': bad.append(body+' terrain failures')
steps=[l for l in section if '[R037][STEP]' in l]
flatten=[l for l in steps if 'body=Pol;' in l and 'type=PQSMod_FlattenOcean;' in l]
if len(flatten)!=1:
    bad.append('Pol FlattenOcean step telemetry count='+str(len(flatten)))
else:
    adapter=field(flatten[0],'adapter'); supported=field(flatten[0],'supported').lower()
    print('[INFO] Pol FlattenOcean adapter='+adapter+' supported='+supported)
    if adapter!='FLATTENOCEAN': bad.append('Pol FlattenOcean adapter='+adapter)
    if supported!='true': bad.append('Pol FlattenOcean not supported')
flat_scalars=[l for l in section if '[R037][FLATTEN]' in l]
if len(flat_scalars)!=1:
    bad.append('FlattenOcean scalar telemetry count='+str(len(flat_scalars)))
else:
    ocean=field(flat_scalars[0],'oceanRad')
    radius=field(flat_scalars[0],'sphere_radius')
    floor=field(flat_scalars[0],'relative_floor_m')
    fsha=field(flat_scalars[0],'il_sha256')
    print('[INFO] FlattenOcean oceanRad='+ocean+' sphere_radius='+radius+' relative_floor_m='+floor+' il_sha256='+fsha)
    if ocean!='44001': bad.append('stock Pol oceanRad expected 44001 got '+ocean)
    if radius!='44000': bad.append('stock Pol sphere radius expected 44000 got '+radius)
    if floor!='1': bad.append('stock Pol relative ocean floor expected 1 got '+floor)
    if fsha!=EXPECTED_IL['PQSMod_FlattenOcean']: bad.append('FlattenOcean scalar IL hash mismatch')
ils=[l for l in section if '[R037][IL]' in l]
for typ,sha in EXPECTED_IL.items():
    rows=[l for l in ils if 'type='+typ+';' in l]
    print('[INFO] '+typ+' il_records='+str(len(rows)))
    if len(rows)!=1:
        bad.append(typ+' IL summary count='+str(len(rows))); continue
    got=field(rows[0],'il_sha256')
    writes=field(rows[0],'direct_vertHeight_writes')
    print('[INFO] '+typ+' il_sha256='+got+' direct_vertHeight_writes='+writes)
    if got!=sha: bad.append(typ+' IL hash mismatch')
    if writes!='1': bad.append(typ+' direct vertHeight writes expected 1 got '+writes)
print('[INFO] selected_run_start_line='+str(run_start+1)+' snapshot_line='+str(snapshot_index+1)+' completion_line='+str(completion_index+1))
print('[INFO] max_primitive_abs_error='+field(complete,'max_primitive_abs_error')+
      ' max_terrain_abs_error_m='+field(complete,'max_terrain_abs_error_m'))
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('[INFO] accepted=Gilly,Ike,Pol worker-ready; Minmus remains fail-closed pending VertexPlanet exact closure')
print('[INFO] next=R038 dedicated PQSMod_VertexPlanet dependency/helper/formula closure; no approximation')
