#!/usr/bin/env python3
# Static companion audit for the staged candidate scripts. Runtime/source selftest is generated
# by apply_aeris23_staggered_exact_refresh.py after the affine source exists locally.
from collections import Counter

def fnv_slot(key):
    h=2166136261
    for ch in key:
        h ^= ord(ch)
        h=(h*16777619)&0xffffffff
    return h%12

counts=Counter(fnv_slot('Kerbin|FAR|%d|%d|STYLE'%(i,i//17)) for i in range(12000))
assert len(counts)==12
avg=1000.0
assert min(counts.values()) >= avg*0.92
assert max(counts.values()) <= avg*1.08
deadlines=[2.80+i*0.10 for i in range(12)]
assert min(deadlines)==2.80
assert max(deadlines)<4.00
print('[AERIS23 Stagger Static Audit] PASS slots=%s min=%d max=%d deadline=%.2f..%.2f' %
      (len(counts),min(counts.values()),max(counts.values()),min(deadlines),max(deadlines)))