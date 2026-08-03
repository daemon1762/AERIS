#!/usr/bin/env python3
from pathlib import Path
import hashlib, re, sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3.5 Gate 1 Presentation Cadence / Responsive UI Candidate 1')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
readme=read(ROOT/'README.md')

for name,text in (('renderer',renderer),('window',window),('nd',nd)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Gate 1: there is no rendering bypass after FRONT exists.
suite.check('forcedRecoveryBackRenders++' not in renderer,
            'old forced recovery full-render increment removed')
suite.check('forced_recovery_suppressed=' in renderer and
            'suppressedForcedRecoveryFrames++' in renderer,
            'suppressed forced-recovery demand is observable without rendering')
suite.check('refreshRequired || !directCompatibleBeforeRefresh' not in renderer and
            'projectionRefreshRequired || !directCompatibleBeforeRefresh' in renderer,
            'FRONT compatibility loss requests a scheduled BACK refresh')
should=renderer[renderer.find('bool ShouldRefreshBackBuffer'):renderer.find('static float ResolveBackRefreshCadenceSeconds')]
suite.check('lastBackAttemptViewGeneration != visible.ViewGeneration' not in should and
            'lastBackAttemptContentRevision != gpuContentRevision' not in should,
            'ViewGeneration/content revision cannot bypass Gate 1 cadence')
suite.check('!frontBufferValid && lastBackAttemptViewGeneration < 0L' in should and
            'Time.realtimeSinceStartup >= nextBackRefreshRealtime' in should,
            'only initial FRONT attempt bypasses cadence')
for token in (
    'BackRefresh5To20KmSeconds = 0.20f',
    'BackRefresh40KmSeconds = 0.25f',
    'BackRefresh80KmSeconds = 0.33f',
    'BackRefresh160KmSeconds = 0.50f'):
    suite.check(token in renderer,'range-aware cadence present: '+token)
suite.check('lastBackRefreshCadenceSeconds = ResolveBackRefreshCadenceSeconds(rangeMeters)' in renderer and
            'nextBackRefreshRealtime = Time.realtimeSinceStartup +' in renderer,
            'every BACK attempt arms the next explicit cadence deadline')
suite.check('CanPresentLatchedFront(visible, vessel)' in renderer and
            'CapturePresentedProjection(true)' in renderer,
            'last complete FRONT remains latched with exact projection authority')
suite.check('GUI.matrix' not in strip_csharp_comments_and_literals(renderer),
            'no executable GUI.matrix temporal terrain warp')
suite.check('cpuTerrainDrawCount++' not in renderer and 'cpu_terrain_draw=0' in renderer,
            'CPU terrain presentation remains prohibited')

# UI: geometry is a pure function of window dimensions, never text content.
suite.check('BaseWideButtonWidth=390f' in window and
            'BaseTabButtonWidth=126f' in window and 'BaseTabColumns=3' in window,
            'responsive geometry keeps stable baseline proportions and row topology')
suite.check('float ResponsiveWidth(float baseline)' in window and
            'rect.width-34f' in window and 'AERISSettings.DefaultMainWindowWidth-34f' in window,
            'button/control width scales continuously from window width')
suite.check('float ResponsiveHeight(float baseline)' in window and
            'rect.height/Mathf.Max(1f,AERISSettings.DefaultMainWindowHeight)' in window,
            'button/control height follows window height within bounded scaling')
suite.check('CalcHeight(' not in window and 'WrappedControlHeight(' not in window,
            'text content cannot derive button height')
suite.check(re.search(r'wordWrap\s*=\s*true',window) is None and
            re.search(r'wordWrap\s*=\s*true',nd) is None,
            'automatic word wrapping is disabled in AERIS main window and ND UI')
suite.check('skinLabel.wordWrap=false' in window and 'skinButton.wordWrap=false' in window and
            'skinToggle.wordWrap=false' in window and 'skinBox.wordWrap=false' in window and
            'skinLabel.wordWrap=oldLabelWrap' in window and 'skinButton.wordWrap=oldButtonWrap' in window,
            'AERIS window temporarily enforces no-wrap on raw skin text styles and restores them')
suite.check('rect.width<540f' not in window and 'rect.width<720f' not in window and
            'rect.width<560f' not in window and 'rect.width<760f' not in window,
            'window resize geometry has no width-threshold layout jumps')
suite.check('airfieldRowButtonStyle.wordWrap=false' in window and
            'airfieldRowButtonStyle.clipping=TextClipping.Clip' in window,
            'AIRFIELDS rows no longer grow because a name wraps')
suite.check('centerStyle = new GUIStyle(textStyle)' in nd and
            'wordWrap = false' in nd and 'clipping = TextClipping.Clip' in nd,
            'ND text/button shared styles clip instead of auto-wrapping')
suite.check('return Mathf.CeilToInt(5f/BaseTabColumns);' in window,
            'tab row topology does not jump at width thresholds')

# Every button-style GUILayout control with an explicit width must use responsive width.
for line in window.splitlines():
    if not any(token in line for token in ('GUILayout.Button','GUILayout.SelectionGrid','GUILayout.Toggle')):
        continue
    if 'GUILayout.Width(' not in line:
        continue
    # Label toggles are not button-style controls; all explicit-width button-style rows in this
    # source are expected to be responsive after Gate 1.
    if ('GUI.skin.button' in line or 'responsiveButtonStyle' in line or
        'GUILayout.Button' in line or 'GUILayout.SelectionGrid' in line):
        suite.check('ResponsiveWidth(' in line,
                    'explicit button/control width follows window: '+line.strip()[:88])

# Candidate 9 telemetry optimization stays; only its fixed-geometry policy is superseded.
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
suite.check('const float PreloadStatusUiRefreshSeconds = 0.25f' in tiles and
            'cachedPreloadStatus' in tiles,
            'Candidate 9 preload UI snapshot caching retained')
suite.check('nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f' in nd and
            'nextNdGcSampleRealtime = now + 1f' in nd,
            'Candidate 8 ND telemetry throttles retained')

# Supply/authority boundaries frozen.
frozen={
 'Terrain/AERISTerrainTileContracts.cs':'7790977cd845c58767a70f193db3efbfc573812706466b477846b06447440f86',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Landing/AERISAirfieldRegistry.cs':'c1e70635741b779f585d0dd3d7a486e0c5761588f14cee41a710ba4f69cf800e',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
}
for rel,expected in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected,
                'frozen supply/authority boundary byte-identical: '+rel)
cal=ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg'
suite.check(cal.read_text(errors='replace').count('Calibration\n{')>=41,
            '41 physical runway field-verified baseline remains present')

identity='DEV CP3.5 GATE 1 — PRESENTATION CADENCE / RESPONSIVE UI CANDIDATE 1'
suite.check(identity in version and identity in build,'source/build UI checkpoint updated')
suite.check('CP3.5 Gate 1 Presentation Cadence / Responsive UI Candidate 1' in avc,
            'AVC identity updated')
suite.check('CP3.5 Gate 1' in readme and 'forced_recovery_suppressed' in readme,
            'README documents Gate 1 cadence and telemetry')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE1_PRESENTATION_CADENCE_RESPONSIVE_UI_CANDIDATE1.txt').is_file(),
            'Gate 1 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3.5_GATE1_PRESENTATION_CADENCE_RESPONSIVE_UI_CANDIDATE1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Gate 1 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3.5_GATE1_PRESENTATION_CADENCE_RESPONSIVE_UI_CANDIDATE1.txt').is_file(),
            'Gate 1 source diff audit present')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh executable permission retained')
suite.finish()
