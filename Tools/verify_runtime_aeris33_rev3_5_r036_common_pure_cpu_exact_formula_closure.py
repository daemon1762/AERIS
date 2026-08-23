#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS33 R036 COMMON PURE CPU EXACT FORMULA CLOSURE RUNTIME VERIFY]'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args()
p=Path(args.log_path).expanduser().resolve()
if not p.is_file(): raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
starts=[i for i,l in enumerate(lines) if '[R036][COMMON_CPU] event=SNAPSHOT_COMPLETE' in l]
if not starts:
    fails=[l for l in lines if '[R036][COMMON_CPU_FAIL]' in l or '[R036][IL_FAIL]' in l]
    if fails: print('\n'.join(fails[-50:]))
    raise SystemExit(PREFIX+' FAIL no R036 snapshot event')
section=lines[starts[-1]:]
fails=[l for l in section if '[R036][COMMON_CPU_FAIL]' in l or '[R036][IL_FAIL]' in l]
if fails:
    print('\n'.join(fails[-80:])); raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
completes=[l for l in section if '[R036][COMMON_CPU] event=FORMULA_CLOSURE_WORKER_COMPLETE' in l]
if not completes: raise SystemExit(PREFIX+' FAIL no completion after latest snapshot')
complete=completes[-1]
def field(line,name):
    m=re.search(r'(?:^|; )'+re.escape(name)+r'=([^;]+)',line)
    if not m: raise SystemExit(PREFIX+' FAIL missing '+name+' in '+line)
    return m.group(1).strip()
checks=[
(field(complete,'bodies')=='4','target body count'),
(field(complete,'worker_ready_bodies')=='2','expected two worker-ready bodies'),
(field(complete,'pending_bodies')=='2','expected two formula-pending bodies'),
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
bodies=[l for l in section if '[R036][BODY]' in l]
results=[l for l in section if '[R036][BODY_RESULT]' in l]
expected={'Gilly':'true','Ike':'true','Minmus':'false','Pol':'false'}
for body,want in expected.items():
    br=[l for l in bodies if 'body='+body+';' in l]
    rr=[l for l in results if 'body='+body+';' in l]
    if len(br)!=1: bad.append(body+' body telemetry count='+str(len(br))); continue
    if len(rr)!=1: bad.append(body+' result telemetry count='+str(len(rr))); continue
    ready=field(br[0],'worker_ready').lower()
    inert=field(br[0],'landcontrol_inert').lower()
    evaluated=field(rr[0],'evaluated').lower()
    print('[INFO] '+body+' worker_ready='+ready+' landcontrol_inert='+inert+
          ' pending='+field(br[0],'pending')+' evaluated='+evaluated+
          ' max_primitive_abs_error='+field(rr[0],'max_primitive_abs_error')+
          ' max_terrain_abs_error_m='+field(rr[0],'max_terrain_abs_error_m'))
    if ready!=want: bad.append(body+' worker_ready expected '+want+' got '+ready)
    if inert!='true': bad.append(body+' LandControl inert guard failed')
    if evaluated!=want: bad.append(body+' evaluated expected '+want+' got '+evaluated)
    if want=='true':
        if field(rr[0],'primitive_failures')!='0': bad.append(body+' primitive failures')
        if field(rr[0],'terrain_failures')!='0': bad.append(body+' terrain failures')
ils=[l for l in section if '[R036][IL]' in l]
for typ in ('PQSMod_VertexPlanet','PQSMod_FlattenOcean','PQSMod_VertexSimplexHeight'):
    rows=[l for l in ils if 'type='+typ+';' in l]
    print('[INFO] '+typ+' il_records='+str(len(rows)))
    if len(rows)<1: bad.append(typ+' IL closure telemetry missing')
print('[INFO] max_primitive_abs_error='+field(complete,'max_primitive_abs_error')+
      ' max_terrain_abs_error_m='+field(complete,'max_terrain_abs_error_m'))
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('[INFO] next=use R036 IL closure for VertexPlanet/FlattenOcean and signed-simplex confirmation before expanding worker readiness')
