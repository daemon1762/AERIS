#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS32 R033 PURE PROCEDURAL INVENTORY RUNTIME VERIFY]'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args();p=Path(args.log_path).expanduser().resolve()
if not p.is_file():raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
starts=[i for i,l in enumerate(lines) if '[R033][PTC_PROC] event=INVENTORY_START' in l]
if not starts:
 fails=[l for l in lines if '[R033][PTC_PROC_FAIL]' in l]
 if fails:print('\n'.join(fails[-20:]))
 raise SystemExit(PREFIX+' FAIL no R033 inventory start event')
section=lines[starts[-1]:]
fails=[l for l in section if '[R033][PTC_PROC_FAIL]' in l]
completes=[l for l in section if '[R033][PTC_PROC] event=PURE_PROCEDURAL_INVENTORY_COMPLETE' in l]
if fails:
 print('\n'.join(fails[-30:]));raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
if not completes:raise SystemExit(PREFIX+' FAIL no completion after latest inventory start')
complete=completes[-1]
def field(line,name):
 m=re.search(r'(?:^|; )'+re.escape(name)+r'=([^;]+)',line)
 if not m:raise SystemExit(PREFIX+' FAIL missing '+name+' in '+line)
 return m.group(1).strip()
checks=[
(field(complete,'bodies')=='4','target body count'),
(field(complete,'bodies_found')=='4','all target bodies found'),
(field(complete,'failures')=='0','inventory failures'),
(field(complete,'runtime_object_invocation_thread')=='MAIN_THREAD_ONLY','runtime object access main-thread only'),
(field(complete,'worker_dispatch').lower()=='false','no worker dispatch'),
(field(complete,'worker_invokes_runtime_object').lower()=='false','worker runtime-object isolation'),
(field(complete,'db_write').lower()=='false','terrain DB untouched'),
(field(complete,'producer_switch').lower()=='false','producer authority unchanged'),
(field(complete,'gpu').lower()=='false','GPU path untouched'),
(field(complete,'authority')=='PQS','PQS remains authority')]
bad=[]
for ok,label in checks:
 print(('[PASS] ' if ok else '[FAIL] ')+label)
 if not ok:bad.append(label)
expected={'Minmus':1,'Ike':3,'Gilly':2,'Pol':5}
body_lines=[l for l in section if '[R033][PTC_PROC_BODY]' in l]
dep_lines=[l for l in section if '[R033][PTC_PROC_DEP]' in l]
mods=[l for l in section if '[R033][PTC_PROC_MOD]' in l]
for body,known in expected.items():
 bl=[l for l in body_lines if 'body='+body+';' in l]
 dl=[l for l in dep_lines if 'body='+body+';' in l]
 if len(bl)!=1:bad.append(body+' body summary count='+str(len(bl)));continue
 if len(dl)!=1:bad.append(body+' dependency summary count='+str(len(dl)));continue
 c=int(field(bl[0],'height_contributors'));m=int(field(dl[0],'methods'));a=int(field(dl[0],'arrays'));av=int(field(dl[0],'array_values'));f=int(field(dl[0],'failures'))
 print('[INFO] '+body+' contributors='+str(c)+' known_r031='+str(known)+' methods='+str(m)+' arrays='+str(a)+' array_values='+str(av)+' failures='+str(f))
 if c!=known:bad.append(body+' contributor shape changed expected '+str(known)+' got '+str(c))
 if m<1:bad.append(body+' no method closure')
 if f!=0:bad.append(body+' failures='+str(f))
for body in expected:
 n=sum(1 for l in mods if 'body='+body+';' in l)
 if n!=expected[body]:bad.append(body+' mod telemetry count expected '+str(expected[body])+' got '+str(n))
print('[INFO] total_contributors='+field(complete,'contributors')+' methods='+field(complete,'methods')+' arrays='+field(complete,'arrays')+' array_values='+field(complete,'array_values'))
if bad:raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
