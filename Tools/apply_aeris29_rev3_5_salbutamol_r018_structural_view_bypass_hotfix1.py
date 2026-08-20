#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
PREFIX = '[AERIS29 R018 STRUCTURAL VIEW BYPASS HOTFIX1]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_COMPLETE_FOUNDATION_DEFERRED_ADOPTION'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


if not R.is_file():
    fail('generated renderer missing')
renderer = R.read_text()
if R018 not in renderer:
    fail('R018 generated parent required')

field_old = '''        long operationHealthRev35R018ProtectedPruneSkips;
'''
field_new = field_old + '''        long operationHealthRev35R018StructuralBypasses;
'''
renderer, _ = replace_once(renderer, field_old, field_new,
                           'R018 structural bypass counter')

helper_anchor = '''        bool Rev35R018NeedsDeferredTargetRefresh(AERISTerrainTileSystem system,
'''
helper = r'''        bool Rev35R018CanDeferCurrentGeometry(
            AERISTerrainTileSystem system, float rangeMeters, bool trackUp,
            float anchorV, AERISTerrainRenderTargetOrientation orientation,
            string styleKey)
        {
            if (!contentSnapshotValid || contentVisible == null || system == null)
                return false;
            // Deferred handover is only safe inside the same structural display
            // contract. Range/style/TRACK-UP/orientation/terrain-generation changes
            // retain the inherited immediate fail-closed handover semantics.
            return contentTerrainGeneration == system.TerrainGeneration &&
                string.Equals(contentStyleKey, styleKey, StringComparison.Ordinal) &&
                contentTrackUp == trackUp &&
                contentOrientation == orientation &&
                Math.Abs(contentAnchorV - anchorV) <= 0.001f &&
                Math.Abs(contentRangeMeters - rangeMeters) <= 0.5f;
        }

'''
renderer, _ = replace_once(renderer, helper_anchor, helper + helper_anchor,
                           'R018 structural compatibility helper')

old = '''            bool rev35R018DeferredTargetChanged =
                rev35R018DeferredAdoptionPending &&
                Rev35R018NeedsDeferredTargetRefresh(system, vessel,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation, styleKey);
            if (rev35R018DeferredAdoptionPending &&
                !rev35R018DeferredTargetChanged)
                contentGeometryChanged = false;
            if (contentGeometryChanged && contentSnapshotValid &&
                contentVisible != null)
            {
                if (!rev35R018DeferredAdoptionPending)
                {
                    rev35R018DeferredAdoptionPending = true;
                    operationHealthRev35R018HandoverRequested++;
                    Rev35R018ProtectActiveSnapshotKeys();
                }
                else
                {
                    operationHealthRev35R018HandoverRetargeted++;
                }
                Rev35R018SetDeferredTarget(system, centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation, styleKey);
            }
'''
new = '''            bool rev35R018StructuralCompatible =
                Rev35R018CanDeferCurrentGeometry(system, rangeMeters, trackUp,
                    anchorV, orientation, styleKey);
            if (rev35R018DeferredAdoptionPending &&
                !rev35R018StructuralCompatible)
            {
                operationHealthRev35R018StructuralBypasses++;
                Rev35R018ClearDeferredAdoption();
            }
            bool rev35R018DeferredTargetChanged =
                rev35R018DeferredAdoptionPending &&
                Rev35R018NeedsDeferredTargetRefresh(system, vessel,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation, styleKey);
            if (rev35R018DeferredAdoptionPending &&
                !rev35R018DeferredTargetChanged)
                contentGeometryChanged = false;
            if (contentGeometryChanged && rev35R018StructuralCompatible &&
                contentSnapshotValid && contentVisible != null)
            {
                if (!rev35R018DeferredAdoptionPending)
                {
                    rev35R018DeferredAdoptionPending = true;
                    operationHealthRev35R018HandoverRequested++;
                    Rev35R018ProtectActiveSnapshotKeys();
                }
                else
                {
                    operationHealthRev35R018HandoverRetargeted++;
                }
                Rev35R018SetDeferredTarget(system, centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation, styleKey);
            }
'''
renderer, _ = replace_once(renderer, old, new,
                           'R018 structural-compatible admission')

telemetry_old = (
    '                "; oh_rev35_r018_protected_prune_skip=" + '
    'operationHealthRev35R018ProtectedPruneSkips +\n')
telemetry_new = telemetry_old + (
    '                "; oh_rev35_r018_structural_bypass=" + '
    'operationHealthRev35R018StructuralBypasses +\n')
renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                           'R018 structural bypass telemetry')

R.write_text(renderer)
print(PREFIX + ' APPLY PASS')
print('deferred_scope=HEADING_OR_POSITION_WITHIN_SAME_STRUCTURAL_VIEW')
print('structural_change=INHERITED_IMMEDIATE_HANDOVER')
print('range_change=NOT_DEFERRED style_change=NOT_DEFERRED trackup_change=NOT_DEFERRED')
print('orientation_change=NOT_DEFERRED terrain_generation_change=NOT_DEFERRED')
