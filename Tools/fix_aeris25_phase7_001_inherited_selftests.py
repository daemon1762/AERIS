#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
P=ROOT/'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
PREFIX='[AERIS25 DIAZEPAM PHASE7_001 SELFTEST]'
text=P.read_text()
old="""phase6='NOREPI'+'NEPHRINE'\nck(('OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT' in B) or\n   (('OPERATION HEALTH PHASE 3 '+phase3+' GPU VERTEX PROJECTION') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 4 '+phase4+' GPU DYNAMIC TERRAIN COLOUR') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 5 '+phase5+' PERSISTENT PRESENTATION BATCHING') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 6 '+phase6+' MAIN THREAD COMMIT GOVERNOR') in B),\n   'Ubuntu build identifies Step 2 parent or approved Phase 3/4/5/6 successor')"""
new="""phase6='NOREPI'+'NEPHRINE'\nphase7='DIA'+'ZEPAM'\nck(('OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT' in B) or\n   (('OPERATION HEALTH PHASE 3 '+phase3+' GPU VERTEX PROJECTION') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 4 '+phase4+' GPU DYNAMIC TERRAIN COLOUR') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 5 '+phase5+' PERSISTENT PRESENTATION BATCHING') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 6 '+phase6+' MAIN THREAD COMMIT GOVERNOR') in B) or\n   (('AERIS25 OPERATION HEALTH PHASE 7 '+phase7+' RESIDENT RAM REUSE STRENGTHENING') in B),\n   'Ubuntu build identifies Step 2 parent or approved Phase 3/4/5/6/7 successor')"""
if new in text:
    print(PREFIX+' exact Phase7 Step2 build successor already present')
elif text.count(old)==1:
    P.write_text(text.replace(old,new,1))
    print(PREFIX+' exact DIAZEPAM Phase7 Step2 build successor admitted')
else:
    raise SystemExit(PREFIX+' Step2 build identity anchor mismatch old=%d'%text.count(old))
print('Invariant: Phase7 admission changes identity-only inheritance; fixed 10 Hz/content/Golden behavior remains tested by the original Step2 suite.')
