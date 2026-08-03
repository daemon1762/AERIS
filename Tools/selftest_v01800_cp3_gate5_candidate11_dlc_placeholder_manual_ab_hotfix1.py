#!/usr/bin/env python3
from pathlib import Path
import hashlib,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 11 DLC Placeholder Manual A/B Hotfix 1')
window=read(SOURCE/'UI/AERISWindow.cs')
reg=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
witness=read(SOURCE/'Landing/AERISRunwayWitnessLibrary.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')

for name,text in (('AERISWindow',window),('Registry',reg),('WitnessLibrary',witness)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Candidate 10 placeholder remains the field-capture surface.
start=window.find('void DrawDlcRunwayPlaceholder')
end=window.find('bool DirectionMatchesCategory',start)
placeholder=window[start:end]
suite.check(start>=0 and end>start,'DLC placeholder draw method present')
suite.check('SmallButton("MARK A")' in placeholder and 'SmallButton("MARK B")' in placeholder,
            'DLC placeholder exposes MARK A and MARK B')
suite.check('SmallButton("CLEAR")' in placeholder and 'ClearUserRunwayCalibration' in placeholder,
            'DLC placeholder exposes calibration clear')
suite.check('HandleDlcPlaceholderCalibrationMark' in placeholder,
            'placeholder routes marks through dedicated field-capture handler')
suite.check('UserRunwayCalibrationSummary(airfield)' in placeholder and
            'UserRunwayCalibrationEndpointSummary(airfield)' in placeholder,
            'placeholder shows calibration state and exact endpoint summary')
suite.check('SelectRunway' not in placeholder and 'SelectDirection' not in placeholder and 'Arm' not in placeholder,
            'placeholder field-capture UI has no direct LAND/ND operational selection path')

handler_start=window.find('void HandleDlcPlaceholderCalibrationMark')
handler_end=window.find('void HandleRunwayCalibrationMark',handler_start)
handler=window[handler_start:handler_end]
suite.check('registry.MarkUserRunwayCalibration' in handler,
            'DLC placeholder reuses normal manual A/B authority')
suite.check('settings.AirfieldsPendingExpanded=true' in handler and
            'AirfieldsUserCalibratedExpanded=true' not in handler,
            'DLC field-capture handler keeps placeholder visible instead of redirecting to an empty category')

# Registry/witness path accepts an airfield without any Runway/Direction object.
mark_start=witness.find('internal bool MarkCalibration')
mark_end=witness.find('internal bool RecordPlacementMismatch',mark_start)
mark=witness[mark_start:mark_end]
suite.check('airfield.Runways' not in mark and 'AERISRunwayDirectionDefinition' not in mark,
            'manual calibration capture does not require pre-existing runway geometry objects')
suite.check('LatitudeDeg = vessel.latitude' in mark and 'LongitudeDeg = NormalizeLongitude(vessel.longitude)' in mark,
            'manual endpoint is captured from active vessel geodetic position')
suite.check('SaveUserCalibrations' in mark and 'BODY_FIXED_GEODETIC_ABSOLUTE' in mark,
            'manual endpoint persists and logs absolute body-fixed coordinates')

suite.check('internal string CalibrationEndpointSummary' in witness,
            'witness library exposes exact field-capture endpoint summary')
suite.check('ToString("0.00000000", CultureInfo.InvariantCulture)' in witness and
            'A/B ABSOLUTE GEO' in witness,
            'endpoint summary preserves 8-decimal LAT/LON precision')
suite.check('internal string UserRunwayCalibrationEndpointSummary' in reg and
            'witnessLibrary.CalibrationEndpointSummary(airfield)' in reg,
            'registry bridges endpoint summary to active AIRFIELDS UI')

# No guessed DLC default geometry is introduced in this capture-only hotfix.
foundation=ROOT/'GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg'
field_defaults=ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg'
foundation_text=foundation.read_text(errors='replace')
field_text=field_defaults.read_text(errors='replace')
dessert=foundation_text[foundation_text.find('id = DLC_DESSERT_AIRFIELD'):foundation_text.find('id = DLC_WOOMERANG')]
suite.check(('thresholdLat' not in dessert) or ('certificationBasis = UserCalibrated' in dessert and 'FIELD-VERIFIED' in dessert),
            'Dessert endpoints are never guessed; successor geometry must be explicit field-verified manual authority')
suite.check(field_text.count('Calibration\n{')>=40,
            'original 40-runway field-verified baseline remains preserved before/after DLC extension')

# Candidate 10 provider filtering and Candidate 9 performance/UI rules remain.
suite.check('IsAirfieldPresentationAvailable(airfield)' in reg and
            'IsDlcRunwayPresentationPlaceholder' in reg,
            'Candidate 10 provider-aware presentation contract inherited')
suite.check('const float UiTelemetryRefreshSeconds=0.25f' in window,
            'Candidate 9 4 Hz heavy UI telemetry cache inherited')
suite.check('airfieldRowButtonStyle.wordWrap=true' in window,
            'AIRFIELDS row remains the approved variable-size button family')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh executable bit retained')

# Version/package identity.  Use the normalized Display lineage because build_ubuntu.sh
# regenerates AERISBuildVersion.generated.cs before running inherited selftests.  Historical
# decorative comments are traceability only and are not runtime/generated-source authority.
lineage='CANDIDATE 11 DLC PLACEHOLDER MANUAL A B HOTFIX 1'
suite.check(lineage in version and lineage in build,
            'Candidate 11 lineage identity retained in successor build')
suite.check('CANDIDATE 10 AIRFIELD PROVIDER VISIBILITY DLC LIST HOTFIX 1' in version and
            'CANDIDATE 10 — AIRFIELD PROVIDER VISIBILITY / DLC LIST HOTFIX 1' in build,
            'Candidate 10 lineage marker retained')
suite.check('Candidate 11 DLC Placeholder Manual A/B Hotfix 1' in avc,
            'Candidate 11 AVC identity')
suite.check('Candidate 11' in readme and 'MARK A' in readme and 'LAT/LON/ALT' in readme,
            'README documents DLC placeholder field capture')
suite.check('selftest_v01800_cp3_gate5_candidate11_dlc_placeholder_manual_ab_hotfix1.py' in runner,
            'Gate 5 acceptance executes Candidate 11 regression')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE11_DLC_PLACEHOLDER_MANUAL_AB_HOTFIX1.txt').is_file(),
            'Candidate 11 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE11_DLC_PLACEHOLDER_MANUAL_AB_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 11 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE11_DLC_PLACEHOLDER_MANUAL_AB_HOTFIX1.txt').is_file(),
            'Candidate 11 source diff evidence present')
suite.finish()
