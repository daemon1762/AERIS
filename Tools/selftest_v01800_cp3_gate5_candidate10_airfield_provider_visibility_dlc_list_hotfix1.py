#!/usr/bin/env python3
from pathlib import Path
import hashlib,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 10 Airfield Provider Visibility / DLC List Hotfix 1')
reg=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
exp=read(SOURCE/'Landing/AERISExpansionStatus.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')
foundation=read(ROOT/'GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg')

for name,text in (('Registry',reg),('AERISWindow',window),('ND',nd)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Runtime provider presence is current-generation authority, not stale cache state.
suite.check('NormalizeRuntimeProviderPresence(stagedRecords);' in reg,
            'validation normalizes runtime provider presence after retaining cache evidence')
suite.check('Cached/configured records may carry ProviderDetected=true from an older' in reg and
            'airfield.ProviderDetected = false;' in reg,
            'stale cached ProviderDetected state is explicitly cleared')
suite.check('FindConfiguredMatch(record)' in reg and 'FindDiscoveredGroup(record)' in reg and
            'airfield.ProviderDetected = true;' in reg,
            'only current provider records re-authorize provider presence')

# Presentation availability contract.
suite.check('internal bool IsAirfieldPresentationAvailable(AERISAirfieldDefinition airfield)' in reg,
            'registry exposes one provider-aware presentation gate')
suite.check('case AERISAirfieldSource.Stock:' in reg and 'return true;' in reg,
            'base-game stock remains always presentable')
suite.check('case AERISAirfieldSource.KerbalKonstructs:' in reg and
            'case AERISAirfieldSource.StockLaunchsitesExpansion:' in reg and
            'return airfield.ProviderDetected;' in reg,
            'KK/SLE configured or cached facilities require current runtime detection')
suite.check('case AERISAirfieldSource.UserCfg:' in reg,
            'local USERCFG data is not accidentally tied to an external mod provider')
suite.check('AERISExpansionStatus.MakingHistoryInstalled' in reg and
            'AERISExpansionStatus.MakingHistoryLoaded' in reg,
            'Making History installation state can expose the DLC placeholder without inventing provider geometry')

# Absent mod facilities cannot become LAND selections.
suite.check('!IsAirfieldPresentationAvailable(selected)' in reg,
            'SelectAirfield rejects unavailable provider facilities')
selectable=reg[reg.find('internal int SelectableDirectionCount'):reg.find('internal AERISRunwayDirectionDefinition SelectableDirectionAt')]
suite.check('!IsAirfieldPresentationAvailable(airfield)' in selectable,
            'SelectableDirectionCount returns zero for unavailable provider facilities')
selectable_at=reg[reg.find('internal AERISRunwayDirectionDefinition SelectableDirectionAt'):reg.find('void RevokeForRevalidation')]
suite.check('!IsAirfieldPresentationAvailable(airfield)' in selectable_at,
            'SelectableDirectionAt cannot leak hidden provider geometry')

# SYSTEM > AIRFIELDS list filters every ordinary category row.
suite.check(window.count('!registry.IsAirfieldPresentationAvailable(airfield)')>=3,
            'AIRFIELDS counting/drawing and LAND selector apply provider visibility')
suite.check('IsDlcRunwayPresentationPlaceholder(airfield)' in window and
            'DrawDlcRunwayPlaceholder(registry,airfield,state,manualCalibratedOnly)' in window,
            'DLC runway without geometry is represented as a PENDING placeholder row')
suite.check('RWY -- | '+'' in window and 'DlcRunwayPresentationText(airfield)' in window,
            'DLC placeholder clearly states missing runway geometry/status')
placeholder=window[window.find('void DrawDlcRunwayPlaceholder'):window.find('bool DirectionMatchesCategory')]
suite.check('SelectRunway' not in placeholder and 'SelectDirection' not in placeholder,
            'DLC placeholder has no direct operational runway-selection path')

# ND must not render or capture airports from absent mods.
suite.check(nd.count('!registry.IsAirfieldPresentationAvailable(airfield)')==2,
            'both ND navigation snapshot and facility-symbol paths filter unavailable providers')

# DLC definition remains data-only: no guessed coordinates are introduced.
suite.check('id = DLC_DESSERT_AIRFIELD' in foundation and 'source = Dlc' in foundation,
            'Dessert Airfield DLC definition remains present')
dessert=foundation[foundation.find('id = DLC_DESSERT_AIRFIELD'):foundation.find('id = DLC_WOOMERANG')]
suite.check(('Direction' not in dessert and 'thresholdLat' not in dessert) or
            ('certificationBasis = UserCalibrated' in dessert and 'FIELD-VERIFIED' in dessert),
            'Candidate 10 never invents DLC thresholds; a later successor may add only field-verified manual authority')
suite.check('id = DLC_WOOMERANG' in foundation and 'facilityType = LaunchPad' in foundation,
            'Woomerang remains excluded from the fixed-wing runway list')

# UI-facing summary counts use presentation-filtered values, while database evidence is retained.
suite.check('PresentationRunwayCount()' in reg and 'PresentationApproachCount()' in reg and
            'CountPresentationRunways' in reg and 'CountPresentationApproaches' in reg,
            'AIRFIELDS summary counters do not advertise hidden mod runways as live UI entries')
suite.check('REGISTERED " + PresentationRunwayCount()' in reg,
            'REGISTERED summary is presentation-scoped')

# Candidate 9 UI/performance contract remains inherited.
suite.check('const float UiTelemetryRefreshSeconds=0.25f' in window,
            'Candidate 9 4 Hz heavy UI telemetry cache remains')
suite.check('airfieldRowButtonStyle.wordWrap=true' in window and
            window.count('WrappedControlHeight(airfieldRowButtonStyle,rowLabel')==2,
            'only AIRFIELDS rows, including DLC placeholder, retain variable-size wrapping')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh remains executable')

# Frozen quality/control/data boundaries.
frozen={
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
 'Landing/AERISExpansionStatus.cs':'c69bd788e2dc5a6c03fa0741213672de0892bff837406d2dcf8480f7b09e2253',
}
for rel,expected in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected,
                'unrelated quality/control boundary byte-identical: '+rel)
suite.check('id = KSC_MAIN' in foundation and 'id = ISLAND_AIRFIELD' in foundation and 'id = DLC_WOOMERANG' in foundation,
            'Stock/DLC foundation legacy definitions remain present')
suite.check((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg').read_text(errors='replace').count('Calibration\n{')>=40,
            'original 40-runway field-verified baseline remains present')

expected='DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 10 — AIRFIELD PROVIDER VISIBILITY / DLC LIST HOTFIX 1'
suite.check('CANDIDATE 10 AIRFIELD PROVIDER VISIBILITY DLC LIST HOTFIX 1' in version and expected in build,'Candidate 10 lineage marker retained in successor')
suite.check('Candidate 10 Airfield Provider Visibility / DLC List Hotfix 1' in avc,
            'Candidate 10 AVC identity')
suite.check('Candidate 10' in readme and 'Dessert Airfield' in readme and 'provider-aware' in readme,
            'README documents provider-aware list and DLC placeholder')
suite.check('selftest_v01800_cp3_gate5_candidate10_airfield_provider_visibility_dlc_list_hotfix1.py' in runner,
            'Gate 5 acceptance executes Candidate 10 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE10_AIRFIELD_PROVIDER_VISIBILITY_DLC_LIST_HOTFIX1.txt').is_file(),
            'Candidate 10 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE10_AIRFIELD_PROVIDER_VISIBILITY_DLC_LIST_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 10 runtime test card present')
suite.finish()
