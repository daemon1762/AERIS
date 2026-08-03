#!/usr/bin/env python3
import re,sys
from pathlib import Path
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 3 Candidate 2 Presentation Authority Hotfix 1')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
identity='DEV CP3.5 GATE 3 — CP3 FROZEN VISUAL PATH RECOVERY / BOUNDED EXACT REFINEMENT CANDIDATE 2 — PRESENTATION AUTHORITY HOTFIX 1'
build_hotfix_identity=identity+' — BUILD ENTRYPOINT HOTFIX 2'
suite.check(any('internal const string UiCheckpoint = "'+x+'"' in version for x in (identity,build_hotfix_identity)),'generated hotfix identity lineage')
suite.check(any('internal const string UiCheckpoint = "'+x+'"' in build for x in (identity,build_hotfix_identity)),'build generator hotfix identity lineage')
suite.check('PRESENTATION AUTHORITY HOTFIX 1' in build,'build display contains hotfix identity')
suite.check('Presentation Authority Hotfix 1' in avc,'AVC hotfix identity')
suite.check('run_v01800_cp35_gate3_candidate2_presentation_authority_hotfix1_acceptance.py' in build,'build invokes hotfix acceptance')

c=strip_csharp_comments_and_literals(renderer)
suite.check(c.count('{')==c.count('}'),'renderer braces balanced')
suite.check(c.count('(')==c.count(')'),'renderer parens balanced')

suite.check('const bool TemporalPresentationAuthorityEnabled = false;' in renderer,'temporal presentation authority is quarantined')
suite.check('temporalShadowEligibleFrames++' in renderer,'temporal shadow eligibility telemetry increments')
suite.check('temporalPresentationBlockedFrames++' in renderer,'blocked temporal authority telemetry increments')
suite.check('if (TemporalPresentationAuthorityEnabled)' in renderer,'temporal submit is gated behind explicit authority switch')
# The exact latch must no longer rebuild itself through temporal reprojection.
latch=renderer[renderer.index('// Exact FRONT is the hard presentation authority.'):renderer.index('if (!present && frontBufferValid) generationBridgeRejects++;')]
suite.check('PresentFrontDirect(plot, frontOrientation)' in latch,'exact latch directly presents FRONT')
suite.check('RenderTemporalReprojection' not in latch,'exact latch does not route through temporal reprojection')
suite.check('TryBuildTemporalReprojection' not in latch,'exact latch does not depend on temporal UV validity')
suite.check('directFrontFrames++' in latch and 'exactFrontAuthorityFrames++' in latch,'exact authority counters increment')
suite.check('CapturePresentedProjection(true);' in latch,'world-lock projection follows committed exact FRONT')

# Overscan direct presentation must crop around projection pivots rather than scale full surface.
present=renderer[renderer.index('bool PresentFrontDirect(Rect plot,'):renderer.index('static void PresentTextureDirect',renderer.index('bool PresentFrontDirect(Rect plot,'))]
for token in ('frontRangeMeters /','frontSurfaceRangeMeters','float u0 = 0.5f','float u1 = 0.5f','frontAnchorV','guiV0','guiV1','GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true)','return true;'):
    suite.check(token in present,'exact overscan crop contains '+token)
suite.check('new Rect(0f, 1f, 1f, -1f)' not in present,'exact authority does not blindly present full overscan surface')

# Temporal path hardening.
suite.check('double.IsNaN(maxErrorPixels)' in renderer and 'double.IsInfinity(maxErrorPixels)' in renderer,'temporal NaN/Infinity rejected')
suite.check('static bool Finite(float value)' in renderer,'finite helper exists')
render_temporal=renderer[renderer.index('bool RenderTemporalReprojection(Rect plot,'):renderer.index('static void EmitTemporalVertex',renderer.index('bool RenderTemporalReprojection(Rect plot,'))]
suite.check('if (!reprojectionMaterial.SetPass(0)) return false;' in render_temporal,'temporal material pass remains fail-closed')
suite.check(render_temporal.index('if (!reprojectionMaterial.SetPass(0)) return false;') < render_temporal.index('GL.Clear(true, true, Color.clear);'),'SetPass happens before presentation clear')
suite.check('Finite(temporalSourceUv[i].x)' in render_temporal and 'Finite(temporalSourceUv[i].y)' in render_temporal,'temporal UVs validated before submit')

suite.check('frontMode = !lastFrontBufferPresented ? "BUILDING"' in renderer and '"EXACT_LATCH"' in renderer,'telemetry identifies exact latch')
suite.check('presentation_authority=' in renderer and 'EXACT_FRONT_ONLY' in renderer,'telemetry publishes exact-front-only authority')
suite.check('exact_authority_frames=' in renderer and 'temporal_shadow_eligible=' in renderer,'authority counters logged')

# Frozen safety boundaries stay intact.
suite.check('GUI.matrix =' not in renderer and 'GUI.matrix=' not in renderer,'GUI.matrix terrain warp remains prohibited')
suite.check('cpu_terrain_draw=0' in renderer,'CPU terrain presentation remains prohibited')
suite.check('terrainTileRenderer.PresentedProjection' in nd,'ND still consumes renderer-published projection')

suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE3_CP3_FROZEN_VISUAL_PATH_RECOVERY_CANDIDATE2_PRESENTATION_AUTHORITY_HOTFIX1.txt').is_file(),'hotfix acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE3_CANDIDATE2_PRESENTATION_AUTHORITY_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'hotfix runtime test card included')
suite.check((ROOT/'Evidence/RUNTIME_DIAGNOSIS_AERIS58_PRESENTATION_AUTHORITY_2026-08-03.txt').is_file(),'AERIS58 diagnosis evidence included')
suite.finish()
