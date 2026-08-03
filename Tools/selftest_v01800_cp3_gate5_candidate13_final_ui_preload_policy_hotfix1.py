#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from v01700_testlib import CheckSuite,read,strip_csharp_comments_and_literals
SOURCE=ROOT/'Source/AERISFlightControl'
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 13 Final UI / Preload Policy Hotfix 1')
settings=read(SOURCE/'Settings/AERISSettings.cs')
builder=read(SOURCE/'Terrain/AERISTerrainPreloadBuilder.cs')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
registry=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
config=read(ROOT/'GameData/AERISFlightControl/Config/AERISSettings.cfg')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')

for name,text in (('settings',settings),('builder',builder),('tiles',tiles),('window',window)):
    stripped=strip_csharp_comments_and_literals(text)
    suite.check(stripped.count('{')==stripped.count('}'),name+' braces balanced')
    suite.check(stripped.count('(')==stripped.count(')'),name+' parens balanced')

# 1: fixed preload policy; ON/OFF is the only user setting.
suite.check('internal bool TerrainPreloadEnabled = true;' in settings,
            'preload exposes one persisted ON/OFF setting')
for token in ('internal AERISTerrainPreloadMode TerrainPreloadMode',
              'TerrainPreloadSpeedProfile =', 'TerrainPreloadStorageLimitMiB',
              'TerrainPreloadIdleSeconds'):
    suite.check(token not in settings,'obsolete preload setting removed: '+token)
suite.check('terrainPreloadEnabled' in settings and
            'node.AddValue("terrainPreloadEnabled", TerrainPreloadEnabled)' in settings,
            'only preload enabled state is persisted')
suite.check('terrainPreloadMode' in settings and
            'legacyMode != AERISTerrainPreloadMode.Off' in settings,
            'legacy preload mode migrates only ON/OFF intent')
for token in ('terrainPreloadStorageLimitMiB','terrainPreloadIdleSeconds','terrainPreloadSpeedProfile'):
    suite.check(token not in config,'shipped config removes obsolete preload field: '+token)
suite.check('terrainPreloadEnabled = True' in config,'shipped automatic preload default is ON')
suite.check('settings.TerrainPreloadEnabled ? AERISTerrainPreloadMode.AggressiveIdle' in builder,
            'enabled policy is fixed to AggressiveIdle')
suite.check('speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;' in builder,
            'producer profile is fixed to Balanced')
suite.check('float ResolveIdleSeconds()' in builder and 'return 5f;' in builder[builder.find('float ResolveIdleSeconds()'):builder.find('CelestialBody FindBody')],
            'idle threshold is fixed to five seconds')
suite.check('internal void SetEnabled(bool enabled)' in builder and
            'internal void SetPreloadEnabled(bool enabled)' in tiles,
            'ON/OFF mutation path remains')
for token in ('SetPreloadMode','SetPreloadSpeedProfile','SetPreloadStorageLimitMiB',
              'SetPreloadBodyPriority','SetPreloadBodyQuality','SetPreloadBodyStorageLimitMiB'):
    suite.check(token not in tiles,'removed preload mutation API: '+token)
for token in ('internal void SetPriority','internal void SetQualityLimit','internal void SetBodyStorageLimitMiB'):
    suite.check(token not in builder,'removed builder tuning mutation: '+token)

# State migration cannot resurrect legacy policy/tuning.
load=builder[builder.find('bool TryLoadState'):builder.find('void SaveStateIfNeeded')]
suite.check('plan.PriorityOverride = false;' in load and 'plan.QualityOverride = false;' in load,
            'legacy per-body override flags are discarded')
suite.check('plan.StorageLimitBytes = 0L;' in load and
            'plan.Priority = AERISTerrainBodyPriority.Normal;' in load and
            'plan.QualityLimit = AERISTerrainTileLod.Far;' in load,
            'legacy per-body tuning values are normalized to fixed defaults')
suite.check('mode = settings == null || settings.TerrainPreloadEnabled ?' in load and
            'AERISTerrainPreloadMode.AggressiveIdle : AERISTerrainPreloadMode.Off;' in load,
            'legacy state mode cannot override current ON/OFF policy')

# 2/4/5: user preload presentation is body + percent + four operations only.
page=window[window.find('void DrawPreloadTerrainMapsPage()'):window.find('void DrawAirfieldsPage()')]
row=window[window.find('void DrawPreloadBodyRow'):window.find('void DrawAirfieldsPage()')]
suite.check('AUTOMATIC PRELOAD' in page and 'TerrainPreloadEnabled' in page,
            'preload page exposes automatic preload ON/OFF')
suite.check('CoverageRatio' in row and '*100.0' in row,
            'per-body preload progress percentage remains visible')
for token in ('Priority','Quality','BodyCap','StorageLimitBytes','SpeedProfile','Mode:','Idle:','Database','Builder','Backpressure','Throughput','Bottleneck'):
    suite.check(token not in page,'preload UI omits tuning/debug token: '+token)
for label in ('"BUILD"','"RESUME"','"PAUSE"','"DELETE"','"REBUILD"'):
    suite.check(label in row,'allowed per-body operation present: '+label)
suite.check('PreloadCancel(' not in row and 'PreloadVerify(' not in row,
            'CANCEL and VERIFY are absent from per-body UI')
suite.check(row.count('SmallButton(')==4,
            'per-body preload row has exactly four button controls')

# 3: preload persistence capacity is unlimited and body cap cannot stop builds.
limit=tiles[tiles.find('static long ResolvePreloadLimitBytes'):tiles.find('internal static bool BodyHasSolidSurface')]
suite.check('return long.MaxValue;' in limit,'persistent preload capacity limit removed')
body_limit=builder[builder.find('bool BodyAtStorageLimit'):builder.find('long EstimateTargetTiles')]
suite.check('return false;' in body_limit,'per-body storage cap enforcement removed')
suite.check('preloadDatabase.SetLimit(ResolvePreloadLimitBytes(settings));' in tiles,
            'database receives unlimited preload policy on refresh')

# 6: PROTECT defaults and legacy migration.
suite.check('internal bool GroundParkingHold = true;' in settings,
            'Parking Hold default is ON')
suite.check('internal bool GroundReverseThrustAuto = true;' in settings,
            'Reverse Thrust Auto default is ON')
suite.check('CurrentProtectDefaultPolicyRevision = 1' in settings and
            'protectDefaultPolicyRevision < CurrentProtectDefaultPolicyRevision' in settings,
            'one-time PROTECT default migration revision exists')
suite.check('settings.GroundParkingHold = true;' in settings and
            'settings.GroundReverseThrustAuto = true;' in settings and
            'saveSettingsMigration = true;' in settings,
            'legacy installs migrate both new PROTECT defaults ON once')
suite.check('settings.GroundParkingHold = ReadBool(node, "groundParkingHold", true);' in settings and
            'settings.GroundReverseThrustAuto = ReadBool(node, "groundReverseThrustAuto", true);' in settings,
            'post-migration explicit user OFF choices remain persistent')
suite.check('protectDefaultPolicyRevision = 1' in config and
            'groundParkingHold = True' in config and 'groundReverseThrustAuto = True' in config,
            'shipped PROTECT config defaults are ON with current revision')

# 7: PROTECT page keeps controls, drops live debug numerics.
protect=window[window.find('void DrawProtect()'):window.find('void DrawAutopilot()')]
for token in ('CurrentAoA','FilteredAoA','CurrentSurfaceSpeed','FilteredAcceleration','RadarAltitude','HDG target/current/error','Decel target/measured','PROTECT telemetry'):
    suite.check(token not in protect,'PROTECT live debug presentation removed: '+token)
suite.check('GroundSettingSlider' in protect and 'GroundParkingHold' in protect and
            'GroundReverseThrustAuto' in protect,
            'PROTECT user settings remain available without live debug readouts')

# 8: SYSTEM debug presentation is removed, runtime owners remain elsewhere.
suite.check('"DIAGNOSTICS"' not in window and 'void DrawDebug' not in window,
            'SYSTEM diagnostics page removed')
for token in ('TerrainUiTelemetry(','ResidentUiTelemetry(','MapUiTelemetry(','CorridorUiTelemetry(',
              'CP3 Corridor:','G/F/R/L/LD','ND main EMA: layout','SSD GUARD  OBSERVED','LAST VIOLATION'):
    suite.check(token not in window,'SYSTEM debug presentation removed: '+token)
suite.check('SnapshotTelemetry()' not in window,'SYSTEM window no longer triggers heavy diagnostic snapshots')

# 9: DLC appears as vanilla only when available, internal authority stays manual/DLC.
suite.check('"VANILLA RUNWAYS"' in window and 'bool dlcVanilla=airfield!=null&&airfield.Source==AERISAirfieldSource.Dlc;' in window,
            'DLC runway presentation is grouped under VANILLA RUNWAYS')
suite.check('if(dlcVanilla)return !manualCalibratedOnly;' in window,
            'DLC does not appear in MOD/USER manual category')
suite.check('FIELD VERIFIED' in window,'DLC vanilla row reports field-verified status')
suite.check('case AERISAirfieldSource.Dlc:' in registry and
            'AERISExpansionStatus.MakingHistoryInstalled' in registry,
            'Making History availability still gates DLC presentation')
suite.check('return record != null && record.Source == AERISAirfieldSource.Stock;' in registry,
            'DLC vanilla categorization does not grant automatic certification')

# Dessert / protected boundaries unchanged.
expected_hashes={
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
 'Landing/AERISAirfieldRegistry.cs':'c1e70635741b779f585d0dd3d7a486e0c5761588f14cee41a710ba4f69cf800e',
}
for rel,h in expected_hashes.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==h,
                'protected operational boundary byte-identical: '+rel)
suite.check(hashlib.sha256((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg').read_bytes()).hexdigest()==
            'a8116c248ced084b264f0174a54b3ee1cd679614bbc4923f36efb3e44ea336eb',
            'Dessert DLC foundation geometry byte-identical to Candidate 12')
suite.check(hashlib.sha256((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg').read_bytes()).hexdigest()==
            '6bfd7d57ad066ae042a4759ae8e60931a13f96c21a24b5d1a7aa9c4be8af5345',
            '41-runway field-verified baseline byte-identical to Candidate 12')

# Identity/package/evidence.
lineage='CANDIDATE 13 FINAL UI PRELOAD POLICY HOTFIX 1'
suite.check(lineage in version and lineage in build,'Candidate 13 lineage retained in successor build')
suite.check('Candidate 13 Final UI / Preload Policy Hotfix 1' in avc,'Candidate 13 AVC identity')
suite.check('Candidate 13' in readme and 'AggressiveIdle' in readme and 'VANILLA RUNWAYS' in readme,
            'README documents final policy')
suite.check('selftest_v01800_cp3_gate5_candidate13_final_ui_preload_policy_hotfix1.py' in runner,
            'Gate 5 runner executes Candidate 13 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE13_FINAL_UI_PRELOAD_POLICY_HOTFIX1.txt').is_file(),
            'Candidate 13 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE13_FINAL_UI_PRELOAD_POLICY_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 13 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE13_FINAL_UI_PRELOAD_POLICY_HOTFIX1.txt').is_file(),
            'Candidate 13 source audit evidence present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,'build_ubuntu.sh executable bit retained')
suite.finish()
