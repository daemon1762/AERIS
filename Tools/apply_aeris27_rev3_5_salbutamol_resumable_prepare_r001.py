#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
GENERATOR = ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_resumable_prepare.py'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 APPLY]'
OLD = 'PendingEntryCommitStage.UploadPackedTerrain'
NEW = 'PendingEntryCommitStage.AcquirePackedTerrainMesh'

if not GENERATOR.is_file():
    raise SystemExit(PREFIX + ' base R001 generator missing')

source = GENERATOR.read_text()
if 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001' not in source:
    raise SystemExit(PREFIX + ' unexpected base generator identity')
count = source.count(OLD)
if count != 2:
    raise SystemExit(PREFIX + ' expected exactly two legacy REV002 successor anchors, got %d' % count)

adapted = source.replace(OLD, NEW)
if adapted.count(NEW) < 2:
    raise SystemExit(PREFIX + ' REV003 successor adaptation failed')

# Execute the original reviewed generator with only the REV003 successor name adapted.
# Restore the tracked generator byte-for-byte afterwards so runtime preparation does
# not leave a dirty Tools tree. All actual generated runtime edits remain in place.
GENERATOR.write_text(adapted)
try:
    subprocess.run([sys.executable, str(GENERATOR)], cwd=str(ROOT), check=True)
finally:
    GENERATOR.write_text(source)

print(PREFIX + ' PASS')
print('successor=' + NEW)
print('worker_prepare=0 speculative=0 presentation_cache=0')
