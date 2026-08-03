#!/usr/bin/env python3
import sys
from pathlib import Path
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 3 Candidate 3 Ownship/Prediction/Range Authority + Palette V3 Hotfix 1')
nav=read(SOURCE/'UI/AERISNavigationDisplay.cs')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
identity='DEV CP3.5 GATE 3 — CANDIDATE 3 OWNSHIP / PREDICTION / RANGE AUTHORITY — PALETTE V3 HOTFIX 1'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated hotfix identity')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build-generated hotfix identity')
suite.check('Ownship / Prediction / Range Authority' in avc and 'Palette V3 Hotfix 1' in avc,'AVC hotfix identity')
# Ownship is a live overlay outside PLAN; no PresentedProjection drift is allowed to move it.
suite.check('if (!planMode)' in nav and 'plan.x + plan.width * 0.5f' in nav and 'plan.y + plan.height * anchorV' in nav,'normal ND ownship uses fixed live anchor')
suite.check('PLAN mode remains geographic' in nav,'PLAN geographic exception documented')
# Track vector endpoint/ticks are deltas from projected zero, then translated to live ownship.
suite.check('Vector2 projectionOrigin, projectedEnd;' in nav,'track vector establishes projection delta origin')
suite.check('Vector2 end = aircraftPoint + (projectedEnd - projectionOrigin);' in nav,'track endpoint is ownship-relative')
suite.check('Vector2 tick = aircraftPoint + (projectedTick - projectionOrigin);' in nav,'track ticks are ownship-relative')
# Range changes may not display an exact FRONT from a different scale/orientation.
suite.check('float currentRangeMeters, bool currentTrackUp, float currentAnchorV' in renderer,'latched-front compatibility receives current display scale')
suite.check('Math.Abs(frontRangeMeters - currentRangeMeters)' in renderer,'latched FRONT rejects range mismatch')
suite.check('frontTrackUp != currentTrackUp || frontOrientation != currentOrientation' in renderer,'latched FRONT rejects orientation mode mismatch')
suite.check('CanPresentLatchedFront(visible, vessel, rangeMeters,' in renderer,'draw path uses strict latch compatibility')
suite.check('CancelProjectionBatch();\n            nextBackRefreshRealtime = 0f;' in renderer,'manual view change requests successor exact FRONT immediately')
# Palette V3: ocean depths do not compress land gradient and profile endpoints are deliberately separated.
suite.check('if (minimum < 0f && maximum > 0f) minimum = 0f;' in renderer,'TOPO land window excludes ocean-depth minima')
suite.check('new Color32(255, 48, 196, 255)' in renderer,'RedGreenAssist danger is strong magenta')
suite.check('new Color32(202, 72, 224, 255)' in renderer,'BlueYellowAssist topo midpoint is strong magenta')
suite.check('new Color32(18, 20, 24, 255)' in renderer and 'new Color32(255, 255, 255, 255)' in renderer,'HighContrast spans near-black to white')
suite.check('return new Color32(6, 42, 112, 255);' in renderer,'water remains stable deep-blue reference')
# Preserve hard safety boundaries from Candidate 3.
suite.check('const bool TemporalPresentationAuthorityEnabled = false;' in renderer,'temporal presentation remains quarantined')
suite.check('GUI.matrix =' not in renderer and 'GUI.matrix=' not in renderer,'GUI.matrix warp remains prohibited')
suite.check('new string[]{"AUTO","LOW","MEDIUM","HIGH"}' in window,'terrain quality remains exactly AUTO/LOW/MEDIUM/HIGH')
suite.check('internal int FlightDataArchiveLimit = 10;' in settings,'FDR/CVR archive default remains 10')
# Syntax-level dense-file checks.
for label,text in [('nav',nav),('renderer',renderer)]:
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),label+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),label+' parens balanced')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE3_CANDIDATE3_OWNSHIP_PREDICTION_RANGE_AUTHORITY_PALETTEV3_HOTFIX1.txt').is_file(),'hotfix acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE3_CANDIDATE3_OWNSHIP_PREDICTION_RANGE_AUTHORITY_PALETTEV3_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'hotfix runtime test card included')
suite.finish()
