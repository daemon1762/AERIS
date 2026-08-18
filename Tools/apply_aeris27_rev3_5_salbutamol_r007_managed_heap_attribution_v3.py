#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
V2 = ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution_v2.py'
PREFIX = '[AERIS27 R007 APPLY V3]'
if not V2.is_file():
    raise SystemExit(PREFIX + ' V2 apply wrapper missing')
source = V2.read_text()
needle = '''namespace = {
    '__name__': '__main__',
'''
inject = r'''patch_once(
''' + "'''" + r'''oh, _ = replace_once(
    oh,
    '        internal const string Candidate = \"AERIS23_OH_PENICILLIN\";\\n',
    '        internal const string Candidate = \"AERIS23_OH_PENICILLIN\";\\n'
    '        internal const string Rev35R007ManagedHeapAttribution = \"' + R007 + '\";\\n',
    'R007 OH identity')
''' + "'''" + r''',
''' + "'''" + r'''oh, _ = replace_once(
    oh,
    '        internal const string Codename = \"PENICILLIN\";\\n',
    '        internal const string Codename = \"PENICILLIN\";\\n'
    '        internal const string Rev35R007ManagedHeapAttribution = \"' + R007 + '\";\\n',
    'R007 OH identity')
''' + "'''" + r''', 'OH identity')

'''
if source.count(needle) != 1:
    raise SystemExit(PREFIX + ' V2 namespace anchor mismatch=' + str(source.count(needle)))
source = source.replace(needle, inject + needle, 1)
namespace = {'__name__': '__main__', '__file__': str(V2), '__package__': None}
exec(compile(source, str(V2), 'exec'), namespace, namespace)
