#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_write_bytecode=True
PREFIX='[AERIS32 R034 LANDCONTROL HEIGHT PATH AUDIT RUNTIME VERIFY]'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args()
p=Path(args.log_path).expanduser().resolve()
if not p.is_file(): raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
starts=[i for i,l in enumerate(lines) if '[R034][LANDCTRL] event=AUDIT_START' in l]
if not starts:
    fails=[l for l in lines if '[R034][LANDCTRL_FAIL]' in l]
    if fails: print('\n'.join(fails[-30:]))
    raise SystemExit(PREFIX+' FAIL no R034 audit start event')
section=lines[starts[-1]:]
fails=[l for l in section if '[R034][LANDCTRL_FAIL]' in l]
completes=[l for l in section if '[R034][LANDCTRL] event=HEIGHT_PATH_AUDIT_COMPLETE' in l]
if fails:
    print('\n'.join(fails[-50:]))
    raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
if not completes: raise SystemExit(PREFIX+' FAIL no completion after latest audit start')
complete=completes[-1]
def field(line,name):
    m=re.search(r'(?:^|; )'+re.escape(name)+r'=([^;]+)',line)
    if not m: raise SystemExit(PREFIX+' FAIL missing '+name+' in '+line)
    return m.group(1).strip()
checks=[
(field(complete,'bodies')=='4','target body count'),
(field(complete,'bodies_found')=='4','all target bodies found'),
(field(complete,'land_controls')=='4','one LandControl per target body'),
(field(complete,'failures')=='0','audit failures'),
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
    if not ok: bad.append(label)
bodies=['Minmus','Ike','Gilly','Pol']
body_lines=[l for l in section if '[R034][LANDCTRL_BODY]' in l]
il_lines=[l for l in section if '[R034][LANDCTRL_IL_SUMMARY]' in l]
class_lines=[l for l in section if '[R034][LANDCTRL_CLASS]' in l]
for body in bodies:
    bl=[l for l in body_lines if 'body='+body+';' in l]
    il=[l for l in il_lines if 'body='+body+';' in l]
    if len(bl)!=1:
        bad.append(body+' body telemetry count='+str(len(bl))); continue
    if len(il)!=1:
        bad.append(body+' IL summary count='+str(len(il))); continue
    classes=int(field(bl[0],'landClasses'))
    direct=int(field(il[0],'direct_vertHeight_writes'))
    heightwrites=int(field(il[0],'height_named_field_writes'))
    methods=int(field(il[0],'methods'))
    instructions=int(field(il[0],'instructions'))
    ilfails=int(field(il[0],'failures'))
    observed_classes=sum(1 for l in class_lines if 'body='+body+';' in l)
    print('[INFO] '+body+
          ' useHeightMap='+field(bl[0],'useHeightMap')+
          ' heightMap_present='+field(bl[0],'heightMap_present')+
          ' vHeightMax='+field(bl[0],'vHeightMax')+
          ' landClasses='+str(classes)+
          ' class_telemetry='+str(observed_classes)+
          ' direct_vertHeight_writes='+str(direct)+
          ' height_named_field_writes='+str(heightwrites)+
          ' methods='+str(methods)+
          ' instructions='+str(instructions))
    if classes>=0 and observed_classes!=classes: bad.append(body+' class telemetry expected '+str(classes)+' got '+str(observed_classes))
    if methods<1: bad.append(body+' no IL methods')
    if instructions<1: bad.append(body+' no IL instructions')
    if ilfails!=0: bad.append(body+' IL failures='+str(ilfails))
print('[INFO] total_methods='+field(complete,'methods')+
      ' total_instructions='+field(complete,'instructions')+
      ' direct_vertHeight_writes='+field(complete,'direct_vertHeight_writes')+
      ' height_named_field_writes='+field(complete,'height_named_field_writes'))
print('[INFO] classification=LANDCONTROL_HEIGHT_PATH_AUDIT; geometric activity is NOT inferred by verifier')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
