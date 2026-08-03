#!/usr/bin/env python3
from pathlib import Path
import hashlib,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from v01700_testlib import CheckSuite,read,strip_csharp_comments_and_literals
SOURCE=ROOT/'Source/AERISFlightControl'
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 14 Solid-Surface Preload Exclusion Hotfix 1')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
builder=read(SOURCE/'Terrain/AERISTerrainPreloadBuilder.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')
for name,text in (('tiles',tiles),('builder',builder)):
    stripped=strip_csharp_comments_and_literals(text)
    suite.check(stripped.count('{')==stripped.count('}'),name+' braces balanced')
    suite.check(stripped.count('(')==stripped.count(')'),name+' parens balanced')
# Surface capability must be body-local and fail closed.
method=tiles[tiles.find('internal static bool BodyHasSolidSurface'):tiles.find('internal static bool GameDataHashReady')]
suite.check('pqsController' in method,'solid-surface predicate requires body-local PQS controller')
suite.check('TerrainSamplingAvailableShared' not in method,'global/shared PQS fallback removed from solid-surface predicate')
suite.check('catch { return false; }' in method and method.rstrip().endswith('}'), 'surface reflection failure is fail-closed')
suite.check(method.count('return false;')>=3,'unknown/non-PQS bodies are unsupported')
# Automatic plan discovery and scheduling must reject unsupported bodies.
suite.check('if (!AERISTerrainTileSystem.BodyHasSolidSurface(body))' in builder and
            'database.SetBodyRetentionPriority(body.name,' in builder and
            'AERISTerrainBodyPriority.Disabled' in builder,
            'body refresh excludes surface-less bodies before plan creation')
suite.check('candidateBody == null ||\n                        !AERISTerrainTileSystem.BodyHasSolidSurface(candidateBody)' in builder,
            'automatic scheduler selection rejects surface-less bodies')
suite.check('body == null ||\n                        !AERISTerrainTileSystem.BodyHasSolidSurface(body)' in builder,
            'automatic refinement promotion rejects surface-less bodies')
suite.check('body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body)' in builder,
            'automatic priority marks surface-less bodies disabled')
# Status/UI must not expose stale cached gas/star plans.
suite.check('indexedBody != null &&\n                    AERISTerrainTileSystem.BodyHasSolidSurface(indexedBody)' in builder,
            'preload status filters stale database bodies by current solid-surface capability')
suite.check('!AERISTerrainTileSystem.BodyHasSolidSurface(supportedBody)) continue;' in builder,
            'preload status filters persisted plans for unsupported bodies')
suite.check('No supported solid-surface bodies are indexed yet.' in window,
            'preload UI remains solid-surface-only')
# Manual generation paths also fail closed so unsupported bodies cannot bypass automatic safety.
suite.check('BodyPlan GetSupportedBodyPlan(string bodyName)' in builder,
            'shared supported-body gate exists for manual preload operations')
for name in ('RequestBuild','Pause','Resume'):
    start=builder.find('internal void '+name+'(')
    block=builder[start:start+420]
    suite.check('GetSupportedBodyPlan(bodyName)' in block,name+' rejects unsupported bodies')
start=builder.find('internal void RequestRebuild('); block=builder[start:start+260]
suite.check('GetSupportedBodyPlan(bodyName) == null' in block,'REBUILD rejects unsupported bodies before maintenance queue')
suite.check('PRELOAD SKIPPED / NO SOLID SURFACE:' in builder,
            'unsupported manual request reports deterministic skip state')
# Current-body terrain/resident contract uses the same strict predicate.
suite.check('bool supported = BodyHasSolidSurface(body);' in tiles and
            'BodySupported = BodyHasSolidSurface(body);' in tiles,
            'flight/current-body terrain support shares strict solid-surface predicate')
# No name whitelist: mod stars/gas giants are handled generically.
method_lower=strip_csharp_comments_and_literals(method).lower()
suite.check('jool' not in method_lower and 'sun' not in method_lower and 'kerbin' not in method_lower,
            'surface exclusion is capability-based, not stock-name whitelist')
# Candidate 13 final UI/policy and runway/control boundaries are untouched.
expected={
 'UI/AERISWindow.cs':'9053619f2c662c85a6f2762d950e940d2489000572b75ea643f1762c0a4fd9d9',
 'Landing/AERISAirfieldRegistry.cs':'c1e70635741b779f585d0dd3d7a486e0c5761588f14cee41a710ba4f69cf800e',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
}
for rel,h in expected.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==h,
                'Candidate 13 protected boundary byte-identical: '+rel)
for rel,h in {
 'GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg':'a8116c248ced084b264f0174a54b3ee1cd679614bbc4923f36efb3e44ea336eb',
 'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg':'6bfd7d57ad066ae042a4759ae8e60931a13f96c21a24b5d1a7aa9c4be8af5345',
}.items():
    suite.check(hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()==h,
                'runway baseline unchanged: '+Path(rel).name)
identity='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 14 — SOLID-SURFACE PRELOAD EXCLUSION HOTFIX 1"'
suite.check(identity in version and identity in build,'Candidate 14 in-game/build identity exact')
suite.check('Candidate 14 Solid-Surface Preload Exclusion Hotfix 1' in avc,'Candidate 14 AVC identity')
suite.check('Candidate 14' in readme and 'body-local PQS terrain controller' in readme,
            'README documents solid-surface preload exclusion')
suite.check('selftest_v01800_cp3_gate5_candidate14_solid_surface_preload_exclusion_hotfix1.py' in runner,
            'Gate 5 runner executes Candidate 14 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE14_SOLID_SURFACE_PRELOAD_EXCLUSION_HOTFIX1.txt').is_file(),
            'Candidate 14 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE14_SOLID_SURFACE_PRELOAD_EXCLUSION_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 14 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE14_SOLID_SURFACE_PRELOAD_EXCLUSION_HOTFIX1.txt').is_file(),
            'Candidate 14 source audit evidence present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,'build_ubuntu.sh executable bit retained')
suite.finish()
