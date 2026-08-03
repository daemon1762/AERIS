#!/usr/bin/env python3
import csv, io, os, re, sys, tempfile, zipfile
from pathlib import Path

IDENTITY = "DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 4 NATIVE SPAWN WARP UTILITY"
REQUIRED_RANGES = {5000,10000,20000,40000,80000,160000}

class Result:
    def __init__(self): self.rows=[]; self.hard_fail=False; self.missing=False
    def add(self, ok, label, detail="", missing=False):
        state = "PASS" if ok else ("NOT OBSERVED" if missing else "FAIL")
        self.rows.append((state,label,detail))
        if not ok:
            if missing: self.missing=True
            else: self.hard_fail=True

def find_files(arg):
    p=Path(arg)
    temp=None
    roots=[]
    if p.is_file() and p.suffix.lower()=='.zip':
        temp=tempfile.TemporaryDirectory(prefix='aeris_gate5_')
        with zipfile.ZipFile(str(p),'r') as z: z.extractall(temp.name)
        roots=[Path(temp.name)]
    elif p.is_dir(): roots=[p]
    elif p.is_file(): roots=[p.parent]
    else: raise SystemExit("input not found: "+arg)
    logs=[]; csvs=[]
    if p.is_file() and p.suffix.lower()!='.zip':
        if p.suffix.lower()=='.csv': csvs=[p]
        else: logs=[p]
    for root in roots:
        logs += list(root.rglob('AERISFlightControl.log')) + list(root.rglob('*_session.log'))
        csvs += list(root.rglob('*_performance_runtime.csv'))
    # Prefer main log and newest/largest performance CSV.
    logs=sorted(set(logs), key=lambda x:(x.name!='AERISFlightControl.log',-x.stat().st_size))
    csvs=sorted(set(csvs), key=lambda x:-x.stat().st_size)
    return (logs[0] if logs else None), (csvs[0] if csvs else None), temp

def read_text(path):
    if path is None: return ''
    return path.read_text(errors='replace').replace('\r','')

def parse_perf(path):
    if path is None: return []
    with path.open(newline='',errors='replace') as f:
        return list(csv.DictReader(f))

def num(row,key,default=0.0):
    try: return float(row.get(key,'') or default)
    except: return default

def integer(row,key,default=0):
    try: return int(float(row.get(key,'') or default))
    except: return default

def main():
    if len(sys.argv)!=2:
        print("usage: analyze_v01800_cp3_gate5_runtime.py <AERISFlightControl.zip|log|directory>")
        return 2
    log_path,csv_path,temp=find_files(sys.argv[1])
    text=read_text(log_path); rows=parse_perf(csv_path)
    r=Result()
    print("CP3 Gate 5 runtime evidence analyzer")
    print("log:",log_path or 'NOT FOUND')
    print("csv:",csv_path or 'NOT FOUND')

    r.add(bool(log_path),"main/session log available",str(log_path or ''),missing=not bool(log_path))
    r.add(IDENTITY in text.upper(),"Gate 5 Candidate 4 identity present",missing=IDENTITY not in text.upper())

    error_lines=[x for x in text.splitlines() if '[ERROR]' in x]
    exception_lines=[x for x in text.splitlines() if re.search(r'\b(Exception|StackTrace|NullReferenceException|InvalidOperationException)\b',x,re.I)]
    r.add(len(error_lines)==0,"AERIS ERROR count = 0",f"count={len(error_lines)}")
    r.add(len(exception_lines)==0,"exception evidence = 0",f"count={len(exception_lines)}")
    r.add('startup neutral; airport=NONE; runway=NONE' in text,"startup airport/runway neutral",missing='startup neutral; airport=NONE; runway=NONE' not in text)

    scenes=re.findall(r'UI scene boundary ([A-Z]+) -> ([A-Z]+)',text)
    scene_pairs=set(scenes)
    required_scene={('MAINMENU','SPACECENTER'),('SPACECENTER','FLIGHT'),('FLIGHT','SPACECENTER')}
    r.add(required_scene.issubset(scene_pairs),"scene transition coverage",f"seen={sorted(scene_pairs)}",missing=not required_scene.issubset(scene_pairs))

    bodies=set(re.findall(r'\[CP3_TELEMETRY\] body=([^;]*);',text))
    bodies={b.strip() for b in bodies if b.strip()}
    r.add(len(bodies)>=2,"body transition coverage >= 2 bodies",f"bodies={sorted(bodies)}",missing=len(bodies)<2)

    terrain_activation=[x for x in text.splitlines() if '[CP2.5/TERRAIN_ACTIVATION]' in x]
    off_high=any('AT/ABOVE 40.5 KM ASL' in x for x in terrain_activation)
    hold_off=any('OFF — 39.5–40.5 KM HYSTERESIS HOLD' in x for x in terrain_activation)
    on_low=any('ON — BELOW 39.5 KM ASL' in x for x in terrain_activation)
    r.add(off_high and hold_off and on_low,"40 km altitude hysteresis OFF/HOLD/ON",f"off_high={off_high} hold_off={hold_off} on_low={on_low}",missing=not(off_high and hold_off and on_low))

    nd_modes=set(re.findall(r'\[SYSTEM/OPTIONS\] ND display mode=([A-Za-z]+)',text))
    r.add('Off' in nd_modes and 'Automatic' in nd_modes,"ND AUTO/OFF exercised",f"modes={sorted(nd_modes)}",missing=not('Off' in nd_modes and 'Automatic' in nd_modes))
    terr_modes=set(re.findall(r'(?:\[SYSTEM/OPTIONS\] Terrain mode=|\[ND/TERRAIN\] display mode=)([A-Za-z]+)',text))
    r.add('Off' in terr_modes and 'Automatic' in terr_modes,"Terrain AUTO/OFF exercised",f"modes={sorted(terr_modes)}",missing=not('Off' in terr_modes and 'Automatic' in terr_modes))

    seen_ranges=set()
    for m in re.finditer(r'\[ND/TERRAIN\] range=([0-9]+)m',text): seen_ranges.add(int(m.group(1)))
    for m in re.finditer(r'\[ND/TERRAIN_ALIGN\][^\n]*?range=([0-9]+)',text): seen_ranges.add(int(m.group(1)))
    r.add(REQUIRED_RANGES.issubset(seen_ranges),"all ND ranges 5/10/20/40/80/160 km",f"seen={sorted(seen_ranges)}",missing=not REQUIRED_RANGES.issubset(seen_ranges))

    track_vals=set(re.findall(r'\[ND/TERRAIN_ALIGN\][^\n]*?trackUp=(True|False)',text))
    r.add({'True','False'}.issubset(track_vals),"TRACK UP and NORTH UP exercised",f"trackUp={sorted(track_vals)}",missing=not {'True','False'}.issubset(track_vals))

    arm='[LAND_FOUNDATION] ARM accepted:' in text
    disarm='[LAND_FOUNDATION] DISARM:' in text
    r.add(arm and disarm,"LAND ARM/DISARM exercised",f"arm={arm} disarm={disarm}",missing=not(arm and disarm))

    gate4_lines=[x for x in text.splitlines() if '[CP3_GATE4C_VIRTUAL_DETAIL]' in x]
    ready_bad=sum(1 for x in gate4_lines if not re.search(r'ready_build_violation=0(?:;|\.)',x))
    cpu_bad=sum(1 for x in gate4_lines if not re.search(r'cpu_terrain_draw=0(?:;|\.)',x))
    r.add(bool(gate4_lines) and ready_bad==0,"ready_build_violation always 0",f"samples={len(gate4_lines)} bad={ready_bad}",missing=not bool(gate4_lines))
    r.add(bool(gate4_lines) and cpu_bad==0,"cpu_terrain_draw always 0",f"samples={len(gate4_lines)} bad={cpu_bad}",missing=not bool(gate4_lines))
    front_modes={"DIRECT":0,"LATCHED":0,"BUILDING":0}
    max_latch_age=0.0
    for x in gate4_lines:
        m=re.search(r'front=(DIRECT|LATCHED|BUILDING)',x)
        if m: front_modes[m.group(1)]+=1
        m=re.search(r'latch_age=([0-9.]+)',x)
        if m: max_latch_age=max(max_latch_age,float(m.group(1)))
    r.add(bool(gate4_lines) and max_latch_age<=8.001,"presentation latch age <= 8 s",f"modes={front_modes} max_latch_age={max_latch_age:.3f}",missing=not bool(gate4_lines))
    max_bridge=0; max_bridge_reject=0
    for x in gate4_lines:
        m=re.search(r'gen_bridge_frames=([0-9]+)',x)
        if m: max_bridge=max(max_bridge,int(m.group(1)))
        m=re.search(r'gen_bridge_rejects=([0-9]+)',x)
        if m: max_bridge_reject=max(max_bridge_reject,int(m.group(1)))
    print(f"INFO: Gate5 C3 presentation modes={front_modes}; max_latch_age={max_latch_age:.3f}s; generation_bridge_frames={max_bridge}; bridge_rejects={max_bridge_reject}")

    align_lines=[x for x in text.splitlines() if '[ND/TERRAIN_ALIGN]' in x and 'visualCoverage=' in x]
    max_lock=0.0; min_visual=1.0; min_requested=1.0
    for x in align_lines:
        m=re.search(r'runwayMapLockErrorPx=([0-9.]+)',x); max_lock=max(max_lock,float(m.group(1))) if m else max_lock
        m=re.search(r'visualCoverage=([0-9.]+)',x); min_visual=min(min_visual,float(m.group(1))) if m else min_visual
        m=re.search(r'requestedCoverage=([0-9.]+)',x); min_requested=min(min_requested,float(m.group(1))) if m else min_requested
    r.add(bool(align_lines) and max_lock<=0.25,"runway map-lock error <= 0.25 px",f"samples={len(align_lines)} max={max_lock:.3f}",missing=not bool(align_lines))
    r.add(bool(align_lines) and min_visual>=0.999,"presented terrain visual coverage >= 0.999",f"min_visual={min_visual:.3f} min_requested_observed={min_requested:.3f}",missing=not bool(align_lines))

    r.add(bool(rows),"performance runtime CSV available",str(csv_path or ''),missing=not bool(rows))
    if rows:
        active_rows=[x for x in rows if integer(x,'cp3_resident_active')==1]
        active_seconds=len(active_rows)  # telemetry is approximately 1 Hz
        r.add(active_seconds>=1800,"active CP3 soak >= 30 min",f"approx_active_seconds={active_seconds}",missing=active_seconds<1800)
        if active_seconds>=3600:
            print("INFO: extended >=60 min soak observed")
        max_gpu_fail=max(integer(x,'terrain_gpu_failures') for x in rows)
        max_crc=max(integer(x,'terrain_db_crc_failures') for x in rows)
        max_hash=max(integer(x,'terrain_db_hash_mismatches') for x in rows)
        max_decomp=max(integer(x,'terrain_decompress_failures') for x in rows)
        max_decode=max(integer(x,'cp3_resident_decode_failures') for x in rows)
        max_budget=max(integer(x,'cp3_resident_budget_rejects') for x in rows)
        max_writer=max(integer(x,'writer_failures') for x in rows)
        max_archive=max(integer(x,'archive_failed') for x in rows)
        r.add(max_gpu_fail==0,"terrain GPU failures = 0",f"max={max_gpu_fail}")
        r.add(max_crc==0 and max_hash==0,"terrain DB CRC/hash failures = 0",f"crc={max_crc} hash={max_hash}")
        r.add(max_decomp==0 and max_decode==0,"terrain decompress/resident decode failures = 0",f"decomp={max_decomp} resident_decode={max_decode}")
        r.add(max_budget==0,"resident budget rejects = 0",f"max={max_budget}")
        r.add(max_writer==0 and max_archive==0,"writer/archive failures = 0",f"writer={max_writer} archive={max_archive}")
        over=[x for x in rows if num(x,'cp3_resident_ram_bytes')>num(x,'cp3_resident_ram_budget_bytes')+0.5]
        r.add(len(over)==0,"resident RAM never exceeds budget",f"over_budget_rows={len(over)}")
        inactive=[x for x in rows if integer(x,'cp3_resident_active')==0]
        inactive_tail=inactive[-10:]
        ram_zero=bool(inactive_tail) and all(integer(x,'cp3_resident_ram_bytes')==0 for x in inactive_tail)
        r.add(ram_zero,"scene/body inactive resident RAM releases to 0",f"inactive_samples={len(inactive_tail)}",missing=not bool(inactive_tail))
        # terrain_gpu_* in PerformanceRuntime is the last published renderer/cache snapshot;
        # once ND/Flight stops drawing it can legitimately remain stale. GPU release is therefore
        # verified by the frozen source contract plus video/scene evidence, not by demanding zero
        # from a stale CSV sample.
        if inactive_tail:
            stale_gpu_bytes=max(integer(x,'terrain_gpu_bytes') for x in inactive_tail)
            print(f"INFO: inactive terrain_gpu_bytes telemetry may be stale; tail_max={stale_gpu_bytes}")
        if active_rows:
            ram_max=max(integer(x,'cp3_resident_ram_bytes') for x in active_rows)
            ram_budget=max(integer(x,'cp3_resident_ram_budget_bytes') for x in active_rows)
            gpu_max=max(integer(x,'terrain_gpu_bytes') for x in active_rows)
            print(f"INFO: active RAM high-water={ram_max}/{ram_budget} bytes; terrain GPU high-water={gpu_max} bytes")

    summaries=[x for x in text.splitlines() if '[CP2.5/MAP_DRAM_SUMMARY]' in x]
    sync_ok=bool(summaries) and any('synchronousSSD=0' in x and 'result=PASS' in x for x in summaries)
    r.add(sync_ok,"Map DRAM summary synchronousSSD=0 / PASS",missing=not sync_ok)

    for state,label,detail in r.rows:
        print(f"{state}: {label}" + (f" — {detail}" if detail else ""))
    if r.hard_fail:
        overall='FAIL'
        code=1
    elif r.missing:
        overall='INCOMPLETE'
        code=2
    else:
        overall='PASS'
        code=0
    print("OVERALL:",overall)
    if temp is not None: temp.cleanup()
    return code

if __name__=='__main__':
    raise SystemExit(main())
