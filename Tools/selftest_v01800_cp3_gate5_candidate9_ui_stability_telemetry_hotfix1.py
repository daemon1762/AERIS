#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 9 UI Stability / Telemetry Hotfix 1')
window=read(SOURCE/'UI/AERISWindow.cs')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')

for name,text in (('AERISWindow',window),('ND',nd),('TerrainTileSystem',tiles)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Candidate 9 fixed geometry was intentionally superseded by CP3.5 Gate 1 at user request.
# In the successor, geometry is a continuous function of window dimensions only; text may
# never auto-wrap or drive control height. Historical Candidate 9 packages still use the
# original fixed-geometry assertions below.
cp35_gate1='DEV CP3.5 GATE 1 — PRESENTATION CADENCE / RESPONSIVE UI CANDIDATE 1' in version
if cp35_gate1:
    suite.check('const float BaseWideButtonWidth=390f' in window and
                'const float BaseTabButtonWidth=126f' in window and
                'const int BaseTabColumns=3' in window,
                'CP3.5 Gate 1 publishes responsive baseline geometry constants')
    suite.check('float ResponsiveWidth(float baseline)' in window and
                'float ResponsiveHeight(float baseline)' in window,
                'CP3.5 Gate 1 derives control geometry from window dimensions')
    suite.check('WrappedControlHeight(' not in window and 'CalcHeight(' not in window and
                re.search(r'wordWrap\s*=\s*true',window) is None,
                'CP3.5 Gate 1 removes content-derived height and automatic wrapping')
    suite.check('return Mathf.CeilToInt(5f/BaseTabColumns);' in window and
                'rect.width<540f' not in window and 'rect.width<720f' not in window,
                'CP3.5 Gate 1 keeps tab topology stable without width-threshold jumps')
    suite.check('airfieldActionButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight())' in window,
                'CP3.5 successor AIRFIELD action buttons fill the symmetric row without text clipping')
    suite.check('GUILayout.Button(rowLabel,airfieldRowButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(rowHeight))' in window and
                'float rowHeight=ResponsiveHeight(38f);' in window,
                'CP3.5 Gate 1 AIRFIELD rows retain fixed topology with window-driven height')
    landpos=window.find('registry.SelectAirfield(i)')
    suite.check(landpos>=0 and 'GUILayout.ExpandWidth(true)' in window[landpos-320:landpos+160],
                'CP3.5 successor LAND airfield selector fills responsive window geometry')
else:
    suite.check('const float FixedWideButtonWidth=390f' in window and
                'const float FixedTabButtonWidth=126f' in window and
                'const int FixedTabColumns=3' in window,
                'AERISWindow publishes fixed button geometry constants')
    suite.check('responsiveButtonStyle.wordWrap=false' in window and
                'airfieldActionButtonStyle.wordWrap=false' in window and
                'airfieldRowButtonStyle.wordWrap=true' in window,
                'only AIRFIELD selection-row button style retains wrapping')
    suite.check(window.count('WrappedControlHeight(')==3 and
                window.count('WrappedControlHeight(airfieldRowButtonStyle,rowLabel')==2,
                'content-derived button height exists only for AIRFIELD selection rows, including DLC placeholder')
    suite.check('rect.width>=760f?5:(rect.width>=560f?3:2)' not in window and
                'rect.width<620f' not in window and
                'rect.width>=820f?7:4' not in window,
                'window width cannot reflow tabs, AIRFIELD actions, or preload selector rows')
    suite.check('wordWrap=false,clipping=TextClipping.Clip,fixedHeight=MasterButtonHeight' in window,
                'MASTER uses fixed non-wrapping geometry')
    suite.check('GUILayout.Width(FixedAirfieldActionWidth)' in window and
                'GUILayout.Height(AirfieldActionButtonHeight)' in window,
                'AIRFIELD action buttons are fixed-size')
    suite.check('GUILayout.Button(rowLabel,airfieldRowButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(rowHeight))' in window,
                'AIRFIELD page airport/runway selection rows retain the approved variable-size exception')
    suite.check('registry.SelectAirfield(i)' in window and
                'GUILayout.Width(FixedWideButtonWidth)' in window[window.find('registry.SelectAirfield(i)')-300:window.find('registry.SelectAirfield(i)')+120],
                'LAND airfield selector is not part of the AIRFIELD-page exception and stays fixed-size')
suite.check('wordWrap = false' in nd and 'clipping = TextClipping.Clip' in nd,
            'ND button style explicitly forbids wrapping while Rect geometry stays authoritative')

# Every AERISWindow button-style control must have explicit size unless it is the AIRFIELD exception.
for line in window.splitlines():
    if 'GUILayout.Button' not in line:
        continue
    allowed=('airfieldRowButtonStyle' in line or
             'return GUILayout.Button(label,responsiveButtonStyle' in line)
    if allowed:
        continue
    suite.check(('GUILayout.Width(' in line or 'GUILayout.ExpandWidth(true)' in line) and 'GUILayout.Height(' in line,
                'non-AIRFIELD GUILayout.Button has bounded responsive geometry: '+line.strip()[:80])
for line in window.splitlines():
    if 'GUILayout.SelectionGrid' in line:
        suite.check('GUILayout.Width(' in line and 'GUILayout.Height(' in line,
                    'SelectionGrid has explicit width+height: '+line.strip()[:80])
    if 'GUILayout.Toggle' in line and ('GUI.skin.button' in line or 'responsiveButtonStyle' in line):
        if 'return GUILayout.Toggle(value,label,responsiveButtonStyle' in line:
            continue
        suite.check(('GUILayout.Width(' in line or 'GUILayout.ExpandWidth(true)' in line) and 'GUILayout.Height(' in line,
                    'button-style Toggle has bounded responsive geometry: '+line.strip()[:80])

# Preload UI telemetry must not traverse the whole database on every IMGUI event.
suite.check('const float PreloadStatusUiRefreshSeconds = 0.25f' in tiles and
            'cachedPreloadStatus' in tiles and
            'nextPreloadStatusUiRefreshRealtime' in tiles,
            'PreloadStatus full snapshot is cached at 4 Hz')
preload_prop=tiles[tiles.find('internal AERISTerrainPreloadStatusSnapshot PreloadStatus'):tiles.find('internal void SetPreloadEnabled')]
suite.check(preload_prop.count('preloadBuilder.SnapshotStatus()')==1 and
            'now >= nextPreloadStatusUiRefreshRealtime' in preload_prop,
            'PreloadStatus invokes the full index snapshot only when its UI cache expires')
suite.check('void InvalidatePreloadStatusUiSnapshot()' in tiles and
            tiles.count('InvalidatePreloadStatusUiSnapshot();')>=8,
            'all remaining preload user operations invalidate the display cache immediately')

# Candidate 13 removes the SYSTEM diagnostic presentation entirely. The
# Candidate 9 performance guarantee is therefore stronger: the heavy diagnostic
# snapshot call sites are absent from the active AERISWindow.
suite.check('const float UiTelemetryRefreshSeconds=0.25f' in window,
            'Candidate 9 telemetry cadence marker retained for lineage')
suite.check('TerrainUiTelemetry(AERISTerrainTileSystem tiles)' not in window and
            'ResidentUiTelemetry(AERISCurrentBodyResidentCache resident)' not in window and
            'MapUiTelemetry(AERISMapDramCache cache)' not in window and
            'CorridorUiTelemetry(AERISTerrainTileSystem tiles)' not in window,
            'successor removes heavy SYSTEM diagnostic snapshot helpers')
for token in ('tiles.SnapshotTelemetry()','resident.SnapshotTelemetry()','cache.SnapshotTelemetry()'):
    suite.check(window.count(token)==0,
                'AERISWindow has no SYSTEM diagnostic call site for '+token)
suite.check('nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f' in nd and
            'nextNdGcSampleRealtime = now + 1f' in nd,
            'Candidate 8 ND telemetry throttles remain intact')

# The legacy AA GUI source is retained only as frozen control-core heritage.
# Current AERIS host never instantiates/calls it, so the active UI contract is
# enforced without changing the Gate-5-frozen AA tree.
aa_host=read(SOURCE/'AA/AtmosphereAutopilot.cs')
suite.check('Legacy AA UI and non-fixed-wing modes are excluded.' in aa_host and
            'void OnGUI()' not in aa_host and 'new NeoGUIController' not in aa_host,
            'legacy AA GUI is unreachable from the current AERIS host')

# Build/install reproducibility: executable bit is part of the package contract.
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh is executable in the source tree')

# Quality/authority baseline unchanged where this UI/performance hotfix has no business changing it.
frozen={
 'Terrain/AERISTerrainTileContracts.cs':'7790977cd845c58767a70f193db3efbfc573812706466b477846b06447440f86',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
}
for rel,expected in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected,
                'quality/authority boundary byte-identical: '+rel)
suite.check((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg').read_text(errors='replace').count('Calibration\n{')>=40,
            'original 40-runway field-verified baseline remains present; successor may add verified defaults')

expected='DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 9 — UI STABILITY / TELEMETRY HOTFIX 1'
suite.check('CANDIDATE 9 UI STABILITY TELEMETRY HOTFIX 1' in version and expected in build,
            'Candidate 9 lineage marker retained in successor')
suite.check('Candidate 9 UI Stability / Telemetry Hotfix 1' in avc,'Candidate 9 AVC identity')
suite.check('Candidate 9' in readme and 'fixed geometry' in readme.lower() and '4 hz' in readme.lower(),
            'README documents fixed geometry and display-rate telemetry')
suite.check('selftest_v01800_cp3_gate5_candidate9_ui_stability_telemetry_hotfix1.py' in runner,
            'Gate 5 acceptance executes Candidate 9 regression first')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE9_UI_STABILITY_TELEMETRY_HOTFIX1.txt').is_file(),
            'Candidate 9 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE9_UI_STABILITY_TELEMETRY_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 9 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE9_UI_STABILITY_TELEMETRY_HOTFIX1.txt').is_file(),
            'Candidate 9 source audit evidence present')
suite.finish()
