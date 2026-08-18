#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
ORIGINAL = ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r007_runtime.py'
OLD = 'apply_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py'
NEW = 'apply_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution_v2.py'
PREFIX = '[AERIS27 R007 V2 RUNTIME ROUTER]'

if not ORIGINAL.is_file():
    raise SystemExit(PREFIX + ' original runtime preparer missing')
source = ORIGINAL.read_text()
count = source.count(OLD)
if count != 1:
    raise SystemExit(PREFIX + ' apply-route anchor mismatch=' + str(count))
source = source.replace(OLD, NEW, 1)
print(PREFIX + ' using generated-anchor compatibility apply v2')
namespace = {'__name__': '__main__', '__file__': str(ORIGINAL), '__package__': None}
exec(compile(source, str(ORIGINAL), 'exec'), namespace, namespace)
