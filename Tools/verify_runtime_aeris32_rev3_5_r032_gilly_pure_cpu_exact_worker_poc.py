#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS32 R032 GILLY PURE CPU RUNTIME VERIFY]'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args();p=Path(args.log_path).expanduser().resolve()
if not p.is_file():raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
starts=[i for i,l in enumerate(lines) if '[R032][PTC_GILLY_CPU] event=SNAPSHOT_COMPLETE' in l]
if not starts:raise SystemExit(PREFIX+' FAIL no R032 snapshot event')
start=starts[-1];section=lines[start:]
completes=[l for l in section if '[R032][PTC_GILLY_CPU] event=PURE_CPU_EXACT_WORKER_COMPLETE' in l]
if not completes:raise SystemExit(PREFIX+' FAIL no R032 completion event after latest snapshot')
complete=completes[-1]
fails=[l for l in section if '[R032][PTC_GILLY_CPU_FAIL]' in l]
if fails:
 print('\n'.join(fails[-10:]));raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
def field(name):
 m=re.search(r'(?:^|; )'+re.escape(name)+r'=([^;]+)',complete)
 if not m:raise SystemExit(PREFIX+' FAIL completion missing '+name)
 return m.group(1).strip()
checks=[
(field('worker_off_main').lower()=='true','worker executed off main thread'),
(field('primitive_failures')=='0','primitive exactness'),
(field('terrain_failures')=='0','full Gilly terrain exactness'),
(field('worker_invokes_runtime_object').lower()=='false','worker runtime-object isolation'),
(field('db_write').lower()=='false','terrain DB untouched'),
(field('producer_switch').lower()=='false','producer authority unchanged'),
(field('gpu').lower()=='false','GPU path untouched'),
(field('authority')=='PQS','PQS remains authority')]
bad=[]
for ok,label in checks:
 print(('[PASS] ' if ok else '[FAIL] ')+label)
 if not ok:bad.append(label)
prim=[l for l in section if '[R032][PTC_GILLY_CPU_PRIMITIVE]' in l]
terr=[l for l in section if '[R032][PTC_GILLY_CPU_TERRAIN]' in l]
print('[INFO] primitive_lines='+str(len(prim))+' terrain_lines='+str(len(terr)))
print('[INFO] max_primitive_abs_error='+field('max_primitive_abs_error'))
print('[INFO] max_terrain_abs_error_m='+field('max_terrain_abs_error_m'))
if len(prim)<6:bad.append('primitive witness telemetry incomplete')
if len(terr)<12:bad.append('terrain witness telemetry incomplete')
if bad:raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
