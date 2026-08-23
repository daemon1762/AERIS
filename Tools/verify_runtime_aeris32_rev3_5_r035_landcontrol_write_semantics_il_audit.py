#!/usr/bin/env python3
from pathlib import Path
import argparse,re,sys
sys.dont_writebytecode=True
PREFIX='[AERIS32 R035 LANDCONTROL WRITE SEMANTICS IL AUDIT RUNTIME VERIFY]'
ap=argparse.ArgumentParser();ap.add_argument('log_path');args=ap.parse_args()
p=Path(args.log_path).expanduser().resolve()
if not p.is_file(): raise SystemExit(PREFIX+' log missing '+str(p))
lines=p.read_text(errors='replace').splitlines()
starts=[i for i,l in enumerate(lines) if '[R035][LANDCTRL_IL] event=AUDIT_START' in l]
if not starts:
    fails=[l for l in lines if '[R035][LANDCTRL_IL_FAIL]' in l]
    if fails: print('\n'.join(fails[-30:]))
    raise SystemExit(PREFIX+' FAIL no R035 audit start event')
section=lines[starts[-1]:]
fails=[l for l in section if '[R035][LANDCTRL_IL_FAIL]' in l]
completes=[l for l in section if '[R035][LANDCTRL_IL] event=WRITE_SEMANTICS_IL_AUDIT_COMPLETE' in l]
if fails:
    print('\n'.join(fails[-50:])); raise SystemExit(PREFIX+' FAIL runtime failure telemetry present')
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
(field(complete,'direct_vertHeight_writes')=='2','exact two direct vertHeight write sites'),
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
configs=[l for l in section if '[R035][LANDCTRL_CONFIG]' in l]
for body in ('Minmus','Ike','Gilly','Pol'):
    rows=[l for l in configs if 'body='+body+';' in l]
    if len(rows)!=1:
        bad.append(body+' config telemetry count='+str(len(rows))); continue
    r=rows[0]
    print('[INFO] '+body+' useHeightMap='+field(r,'useHeightMap')+
          ' landClasses='+field(r,'landClasses')+
          ' nonzero_alterRealHeight='+field(r,'nonzero_alterRealHeight')+
          ' nonzero_alterApparentHeight='+field(r,'nonzero_alterApparentHeight')+
          ' nonzero_minimumRealHeight='+field(r,'nonzero_minimumRealHeight'))
    if field(r,'useHeightMap').lower()!='false': bad.append(body+' useHeightMap unexpectedly true')
    if field(r,'nonzero_alterRealHeight')!='0': bad.append(body+' nonzero alterRealHeight')
    if field(r,'nonzero_alterApparentHeight')!='0': bad.append(body+' nonzero alterApparentHeight')
    if field(r,'nonzero_minimumRealHeight')!='0': bad.append(body+' nonzero minimumRealHeight')
method=[l for l in section if '[R035][LANDCTRL_METHOD]' in l]
if len(method)!=1:
    bad.append('method summary count='+str(len(method)))
else:
    m=method[0]; ins=int(field(m,'instructions')); writes=int(field(m,'direct_vertHeight_writes')); mf=int(field(m,'failures'))
    print('[INFO] method code_size='+field(m,'code_size')+' instructions='+str(ins)+' direct_vertHeight_writes='+str(writes)+' failures='+str(mf))
    if ins<1: bad.append('no decoded instructions')
    if writes!=2: bad.append('method direct write count='+str(writes))
    if mf!=0: bad.append('method failures='+str(mf))
il=[l for l in section if '[R035][LANDCTRL_IL_INSN]' in l]
write_lines=[l for l in il if 'vertHeight_write=True;' in l]
print('[INFO] il_telemetry='+str(len(il))+' write_sites='+str(len(write_lines)))
for l in write_lines: print('[WRITE_SITE] '+l)
if method and len(il)!=int(field(method[0],'instructions')): bad.append('IL telemetry count mismatch')
if len(write_lines)!=2: bad.append('write-site telemetry expected 2 got '+str(len(write_lines)))

# Hotfix2: print one bounded, overlapping window that includes both known write sites.
# This is verifier-only diagnostics over an already captured log; no KSP/runtime action occurs.
if write_lines:
    try:
        site_indices=sorted(int(field(l,'index')) for l in write_lines)
        lo=max(0,site_indices[0]-28)
        hi=min(len(il)-1,site_indices[-1]+16)
        print('[INFO] write_semantics_window=index '+str(lo)+'..'+str(hi))
        for l in il:
            idx=int(field(l,'index'))
            if lo <= idx <= hi:
                tag='[IL_WRITE_WINDOW*] ' if idx in site_indices else '[IL_WRITE_WINDOW] '
                print(tag+l)
    except Exception as ex:
        bad.append('write-window extraction failed: '+str(ex))

if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('[INFO] next=reconstruct both vertHeight write expressions and branch guards from [IL_WRITE_WINDOW]')
