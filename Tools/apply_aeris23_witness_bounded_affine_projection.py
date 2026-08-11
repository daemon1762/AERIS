#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()

if 'oh_affine_bridge=' in text and 'TryResolveWitnessAffineBridge(' in text:
    print('[AERIS23 Witness Affine Projection] already applied')
    raise SystemExit(0)

if 'oh_terrain_single_build=' not in text or 'PackedTerrainMesh' not in text:
    raise SystemExit('[AERIS23 Witness Affine Projection] Single-Authority Terrain Pack must be applied first')


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old, new, 1)

# Per-Entry immutable witness authority captured only after an exact projection/upload.
text = replace_once(text,
'''            internal AERISTerrainRenderTargetOrientation LastProjectionOrientation =
                (AERISTerrainRenderTargetOrientation)(-1);
            internal float[] LandElevationMeters;''',
'''            internal AERISTerrainRenderTargetOrientation LastProjectionOrientation =
                (AERISTerrainRenderTargetOrientation)(-1);
            // AERIS23 Witness-Bounded Affine Projection. The cached mesh always remains
            // the last exact spherical projection. Up to eight extreme witnesses sample
            // terrain + contour + coastline geometry and validate any affine reuse before
            // the matrix is allowed to reach DrawMeshNow.
            internal GeographicUnitPoint[] ProjectionWitnessPoints;
            internal Vector2[] ProjectionWitnessExactVertices;
            internal int ProjectionWitnessCount;
            internal int ProjectionWitnessBasisA = -1;
            internal int ProjectionWitnessBasisB = -1;
            internal int ProjectionWitnessBasisC = -1;
            internal float[] LandElevationMeters;''',
'Entry witness state')

text = replace_once(text,
'''        const float ProjectionBridgeMinimumLatitudeScale = 0.35f;
        const float ProjectionBridgeLatitudeLimitDeg = 70f;''',
'''        const float ProjectionBridgeMinimumLatitudeScale = 0.35f;
        const float ProjectionBridgeLatitudeLimitDeg = 70f;
        // Successor bridge: affine reuse is accepted only after exact witness validation.
        // 0.08 px is intentionally tighter than the already-accepted 0.20 px translation
        // bridge budget. Four seconds is only a freshness rail; witness error remains the
        // primary authority and may force exact projection much sooner.
        const int AffineWitnessMaximumCount = 8;
        const float AffineWitnessAcceptancePixels = 0.08f;
        const float AffineWitnessMaximumAgeSeconds = 4.00f;
        const float AffineWitnessSourceAreaEpsilon = 0.000000001f;
        const float AffineWitnessDeterminantMinimum = 0.80f;
        const float AffineWitnessDeterminantMaximum = 1.25f;''',
'affine safety constants')

text = replace_once(text,
'''        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();
        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();
        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];''',
'''        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();
        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();
        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];
        // Fixed-size witness scratch is renderer-owned and allocation-free on the 10 Hz path.
        readonly double[] affineWitnessScoreScratch = new double[AffineWitnessMaximumCount];
        readonly bool[] affineWitnessValidScratch = new bool[AffineWitnessMaximumCount];
        readonly GeographicUnitPoint[] affineWitnessPointScratch =
            new GeographicUnitPoint[AffineWitnessMaximumCount];
        readonly Vector2[] affineWitnessExactScratch =
            new Vector2[AffineWitnessMaximumCount];
        readonly Vector2[] affineWitnessCurrentScratch =
            new Vector2[AffineWitnessMaximumCount];''',
'affine reusable scratch')

text = replace_once(text,
'''        long operationHealthProjectionExactRefreshes;
        long operationHealthProjectionBridgeUses;
        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthProjectionExactRefreshes;
        long operationHealthProjectionBridgeUses;
        long operationHealthAffineBridgeUses;
        long operationHealthAffineBridgeRejects;
        long operationHealthAffineWitnessTests;
        long operationHealthAffineExactFallbacks;
        long operationHealthAffineWitnessMaxMilliPixels;
        long operationHealthLoadingBackdropFrames;''',
'affine telemetry fields')

start = text.index('        Matrix4x4 EnsureProjectedGeometry(')
end = text.index('        void ProjectMesh(', start)
replacement = r'''        Matrix4x4 EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,
            double currentCenterLatitudeDeg, double currentCenterLongitudeDeg,
            bool forceCenterProjectionRefresh)
        {
            if (entry == null) return Matrix4x4.identity;
            bool structuralProjectionChange =
                double.IsNaN(entry.LastProjectionCenterLatitudeDeg) ||
                double.IsNaN(entry.LastProjectionCenterX) ||
                Math.Abs(entry.LastProjectionBodyRadius - context.RadiusMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionRangeMeters - context.VerticalMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionAnchorBottom - context.AnchorRenderV) > 0.000001f ||
                entry.LastProjectionOrientation != context.Orientation;

            double east = 0.0, north = 0.0;
            bool centerMoved = false;
            double centerMotionSquared = 0.0;
            if (!structuralProjectionChange)
            {
                ToLocalMeters(context.RadiusMeters,
                    entry.LastProjectionCenterLatitudeDeg,
                    entry.LastProjectionCenterLongitudeDeg,
                    currentCenterLatitudeDeg, currentCenterLongitudeDeg,
                    out east, out north);
                centerMotionSquared = east * east + north * north;
                centerMoved = centerMotionSquared > 0.0001;
            }

            float latitudeScale = Mathf.Max(ProjectionBridgeMinimumLatitudeScale,
                Mathf.Abs(Mathf.Cos((float)currentCenterLatitudeDeg * Mathf.Deg2Rad)));
            double exactDistanceThreshold = Math.Max(0.01,
                movementThresholdMeters * ProjectionBridgeThresholdScale * latitudeScale);
            bool polarExactOnly = Math.Abs(currentCenterLatitudeDeg) >=
                ProjectionBridgeLatitudeLimitDeg;
            float exactAge = entry.LastExactProjectionRealtime < 0f ? float.MaxValue :
                Math.Max(0f, Time.realtimeSinceStartup - entry.LastExactProjectionRealtime);

            // Structural changes and polar center motion remain exact-only. Outside that
            // safety boundary, try a witness-proved affine mapping first. The old 0.20 px
            // translation bridge remains a secondary fallback if affine validation rejects.
            bool exactProjectionDue = structuralProjectionChange ||
                centerMoved && polarExactOnly;
            if (!exactProjectionDue)
            {
                if (centerMoved || forceCenterProjectionRefresh)
                {
                    Matrix4x4 affineBridge;
                    float witnessErrorPixels;
                    if (!polarExactOnly && exactAge < AffineWitnessMaximumAgeSeconds &&
                        TryResolveWitnessAffineBridge(entry, context,
                            out affineBridge, out witnessErrorPixels))
                    {
                        operationHealthProjectionBridgeUses++;
                        operationHealthAffineBridgeUses++;
                        long milliPixels = (long)Math.Round(
                            Math.Max(0f, witnessErrorPixels) * 1000.0);
                        if (milliPixels > operationHealthAffineWitnessMaxMilliPixels)
                            operationHealthAffineWitnessMaxMilliPixels = milliPixels;
                        return affineBridge;
                    }

                    bool translationExactDue = centerMoved &&
                        (centerMotionSquared >= exactDistanceThreshold * exactDistanceThreshold ||
                         exactAge >= ProjectionRefreshAgeSeconds);
                    if (!translationExactDue)
                    {
                        float oldCenterU, oldCenterV;
                        context.ProjectUnitToRenderNUp(entry.LastProjectionCenterX,
                            entry.LastProjectionCenterY, entry.LastProjectionCenterZ,
                            out oldCenterU, out oldCenterV);
                        float deltaU = oldCenterU - 0.5f;
                        float deltaV = oldCenterV - context.AnchorRenderV;
                        if (Mathf.Abs(deltaU) > 0.0000001f ||
                            Mathf.Abs(deltaV) > 0.0000001f)
                        {
                            operationHealthProjectionBridgeUses++;
                            return Matrix4x4.Translate(new Vector3(deltaU, deltaV, 0f));
                        }
                        return Matrix4x4.identity;
                    }
                    exactProjectionDue = true;
                    operationHealthAffineExactFallbacks++;
                }
                else return Matrix4x4.identity;
            }

            if (!exactProjectionDue) return Matrix4x4.identity;
            ProjectMesh(entry.PackedTerrainMesh,
                entry.PackedTerrainGeographicPoints,
                entry.PackedTerrainProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,
                entry.ContourProjectedVertices, context);
            ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices, context);
            CaptureProjectionWitnesses(entry);
            entry.LastProjectionCenterLatitudeDeg = currentCenterLatitudeDeg;
            entry.LastProjectionCenterLongitudeDeg = currentCenterLongitudeDeg;
            entry.LastProjectionCenterX = context.CenterX;
            entry.LastProjectionCenterY = context.CenterY;
            entry.LastProjectionCenterZ = context.CenterZ;
            entry.LastExactProjectionRealtime = Time.realtimeSinceStartup;
            entry.LastProjectionBodyRadius = context.RadiusMeters;
            entry.LastProjectionRangeMeters = (float)context.VerticalMeters;
            entry.LastProjectionAnchorBottom = context.AnchorRenderV;
            entry.LastProjectionOrientation = context.Orientation;
            operationHealthProjectionExactRefreshes++;
            return Matrix4x4.identity;
        }

        void CaptureProjectionWitnesses(Entry entry)
        {
            if (entry == null) return;
            for (int i = 0; i < AffineWitnessMaximumCount; i++)
            {
                affineWitnessScoreScratch[i] = double.NegativeInfinity;
                affineWitnessValidScratch[i] = false;
            }
            AccumulateProjectionWitnessCandidates(entry.PackedTerrainGeographicPoints,
                entry.PackedTerrainProjectedVertices);
            AccumulateProjectionWitnessCandidates(entry.ContourGeographicPoints,
                entry.ContourProjectedVertices);
            AccumulateProjectionWitnessCandidates(entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices);

            if (entry.ProjectionWitnessPoints == null ||
                entry.ProjectionWitnessPoints.Length != AffineWitnessMaximumCount)
                entry.ProjectionWitnessPoints =
                    new GeographicUnitPoint[AffineWitnessMaximumCount];
            if (entry.ProjectionWitnessExactVertices == null ||
                entry.ProjectionWitnessExactVertices.Length != AffineWitnessMaximumCount)
                entry.ProjectionWitnessExactVertices =
                    new Vector2[AffineWitnessMaximumCount];

            int count = 0;
            for (int i = 0; i < AffineWitnessMaximumCount; i++)
            {
                if (!affineWitnessValidScratch[i]) continue;
                Vector2 exact = affineWitnessExactScratch[i];
                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    Vector2 prior = entry.ProjectionWitnessExactVertices[j];
                    if ((prior - exact).sqrMagnitude <= 0.000000000001f)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;
                entry.ProjectionWitnessPoints[count] = affineWitnessPointScratch[i];
                entry.ProjectionWitnessExactVertices[count] = exact;
                count++;
            }
            entry.ProjectionWitnessCount = count;
            entry.ProjectionWitnessBasisA = -1;
            entry.ProjectionWitnessBasisB = -1;
            entry.ProjectionWitnessBasisC = -1;
            float bestArea = 0f;
            for (int a = 0; a < count - 2; a++)
            for (int b = a + 1; b < count - 1; b++)
            for (int c = b + 1; c < count; c++)
            {
                Vector2 p0 = entry.ProjectionWitnessExactVertices[a];
                Vector2 p1 = entry.ProjectionWitnessExactVertices[b];
                Vector2 p2 = entry.ProjectionWitnessExactVertices[c];
                float area = Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) -
                    (p2.x - p0.x) * (p1.y - p0.y));
                if (area <= bestArea) continue;
                bestArea = area;
                entry.ProjectionWitnessBasisA = a;
                entry.ProjectionWitnessBasisB = b;
                entry.ProjectionWitnessBasisC = c;
            }
            if (bestArea < AffineWitnessSourceAreaEpsilon)
            {
                entry.ProjectionWitnessCount = 0;
                entry.ProjectionWitnessBasisA = -1;
                entry.ProjectionWitnessBasisB = -1;
                entry.ProjectionWitnessBasisC = -1;
            }
        }

        void AccumulateProjectionWitnessCandidates(GeographicUnitPoint[] points,
            Vector3[] projectedVertices)
        {
            if (points == null || projectedVertices == null) return;
            int count = Math.Min(points.Length, projectedVertices.Length);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = projectedVertices[i];
                if (float.IsNaN(p.x) || float.IsInfinity(p.x) ||
                    float.IsNaN(p.y) || float.IsInfinity(p.y)) continue;
                GeographicUnitPoint point = points[i];
                ConsiderProjectionWitness(0, p.x, point, p);
                ConsiderProjectionWitness(1, -p.x, point, p);
                ConsiderProjectionWitness(2, p.y, point, p);
                ConsiderProjectionWitness(3, -p.y, point, p);
                ConsiderProjectionWitness(4, p.x + p.y, point, p);
                ConsiderProjectionWitness(5, -(p.x + p.y), point, p);
                ConsiderProjectionWitness(6, p.x - p.y, point, p);
                ConsiderProjectionWitness(7, -p.x + p.y, point, p);
            }
        }

        void ConsiderProjectionWitness(int slot, double score,
            GeographicUnitPoint point, Vector3 exact)
        {
            if (slot < 0 || slot >= AffineWitnessMaximumCount ||
                score <= affineWitnessScoreScratch[slot]) return;
            affineWitnessScoreScratch[slot] = score;
            affineWitnessValidScratch[slot] = true;
            affineWitnessPointScratch[slot] = point;
            affineWitnessExactScratch[slot] = new Vector2(exact.x, exact.y);
        }

        bool TryResolveWitnessAffineBridge(Entry entry, AERISNdMapProjection context,
            out Matrix4x4 bridge, out float maximumErrorPixels)
        {
            bridge = Matrix4x4.identity;
            maximumErrorPixels = float.MaxValue;
            if (entry == null || entry.ProjectionWitnessCount < 3 ||
                entry.ProjectionWitnessPoints == null ||
                entry.ProjectionWitnessExactVertices == null ||
                entry.ProjectionWitnessBasisA < 0 ||
                entry.ProjectionWitnessBasisB < 0 ||
                entry.ProjectionWitnessBasisC < 0 ||
                backTarget == null || !backTarget.IsCreated()) return false;

            int count = Math.Min(AffineWitnessMaximumCount,
                entry.ProjectionWitnessCount);
            for (int i = 0; i < count; i++)
            {
                GeographicUnitPoint point = entry.ProjectionWitnessPoints[i];
                float u, v;
                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                if (float.IsNaN(u) || float.IsInfinity(u) ||
                    float.IsNaN(v) || float.IsInfinity(v))
                {
                    operationHealthAffineBridgeRejects++;
                    return false;
                }
                affineWitnessCurrentScratch[i] = new Vector2(u, v);
            }
            operationHealthAffineWitnessTests += count;

            int ia = entry.ProjectionWitnessBasisA;
            int ib = entry.ProjectionWitnessBasisB;
            int ic = entry.ProjectionWitnessBasisC;
            if (ia >= count || ib >= count || ic >= count)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            Vector2 p0 = entry.ProjectionWitnessExactVertices[ia];
            Vector2 p1 = entry.ProjectionWitnessExactVertices[ib];
            Vector2 p2 = entry.ProjectionWitnessExactVertices[ic];
            Vector2 q0 = affineWitnessCurrentScratch[ia];
            Vector2 q1 = affineWitnessCurrentScratch[ib];
            Vector2 q2 = affineWitnessCurrentScratch[ic];
            float px1 = p1.x - p0.x, py1 = p1.y - p0.y;
            float px2 = p2.x - p0.x, py2 = p2.y - p0.y;
            float qx1 = q1.x - q0.x, qy1 = q1.y - q0.y;
            float qx2 = q2.x - q0.x, qy2 = q2.y - q0.y;
            float sourceDeterminant = px1 * py2 - px2 * py1;
            if (Mathf.Abs(sourceDeterminant) < AffineWitnessSourceAreaEpsilon)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            float inverse = 1f / sourceDeterminant;
            float a00 = (qx1 * py2 - qx2 * py1) * inverse;
            float a01 = (-qx1 * px2 + qx2 * px1) * inverse;
            float a10 = (qy1 * py2 - qy2 * py1) * inverse;
            float a11 = (-qy1 * px2 + qy2 * px1) * inverse;
            float determinant = a00 * a11 - a01 * a10;
            if (float.IsNaN(determinant) || float.IsInfinity(determinant) ||
                determinant < AffineWitnessDeterminantMinimum ||
                determinant > AffineWitnessDeterminantMaximum)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            float tx = q0.x - a00 * p0.x - a01 * p0.y;
            float ty = q0.y - a10 * p0.x - a11 * p0.y;
            bridge = Matrix4x4.identity;
            bridge.m00 = a00;
            bridge.m01 = a01;
            bridge.m03 = tx;
            bridge.m10 = a10;
            bridge.m11 = a11;
            bridge.m13 = ty;

            float width = Math.Max(1f, backTarget.width);
            float height = Math.Max(1f, backTarget.height);
            float maximum = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 source = entry.ProjectionWitnessExactVertices[i];
                float predictedU = a00 * source.x + a01 * source.y + tx;
                float predictedV = a10 * source.x + a11 * source.y + ty;
                Vector2 exact = affineWitnessCurrentScratch[i];
                float dx = (predictedU - exact.x) * width;
                float dy = (predictedV - exact.y) * height;
                float errorPixels = Mathf.Sqrt(dx * dx + dy * dy);
                if (errorPixels > maximum) maximum = errorPixels;
                if (errorPixels > AffineWitnessAcceptancePixels)
                {
                    maximumErrorPixels = maximum;
                    operationHealthAffineBridgeRejects++;
                    return false;
                }
            }
            maximumErrorPixels = maximum;
            return true;
        }

'''
text = text[:start] + replacement + text[end:]

# Runtime observability beside the retained legacy bridge counters.
text = replace_once(text,
'''                "; oh_project_exact=" + operationHealthProjectionExactRefreshes +
                "; oh_project_bridge=" + operationHealthProjectionBridgeUses +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_project_exact=" + operationHealthProjectionExactRefreshes +
                "; oh_project_bridge=" + operationHealthProjectionBridgeUses +
                "; oh_affine_bridge=" + operationHealthAffineBridgeUses +
                "; oh_affine_reject=" + operationHealthAffineBridgeRejects +
                "; oh_affine_witness=" + operationHealthAffineWitnessTests +
                "; oh_affine_exact_fallback=" + operationHealthAffineExactFallbacks +
                "; oh_affine_max_mpx=" + operationHealthAffineWitnessMaxMilliPixels +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'affine telemetry publication')

renderer.write_text(text)

# Dedicated source/runtime regression.
test = ROOT / 'Tools/selftest_v01800_operation_health_witness_affine_projection.py'
test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(',R.index('Matrix4x4 EnsureProjectedGeometry('))]
ck('AffineWitnessMaximumCount = 8' in R,'eight extreme witnesses bound affine reuse')
ck('AffineWitnessAcceptancePixels = 0.08f' in R,'accepted witness error is capped at 0.08 rendered pixel')
ck('AffineWitnessMaximumAgeSeconds = 4.00f' in R,'affine reuse has a finite exact-refresh freshness rail')
ck('CaptureProjectionWitnesses(entry)' in project,'every exact projection refreshes witness authority')
ck('entry.PackedTerrainGeographicPoints' in R and 'entry.ContourGeographicPoints' in R and
   'entry.CoastlineGeographicPoints' in R,'witness selection spans terrain contour and coastline')
ck('ProjectionWitnessBasisA' in R and 'bestArea' in R,'basis is chosen from maximum-area non-collinear witness triple')
ck('context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z' in project,
   'current witness positions use the exact spherical projector')
ck('errorPixels > AffineWitnessAcceptancePixels' in project and
   'backTarget.width' in project and 'backTarget.height' in project,
   'acceptance is measured in actual render-target pixels')
ck('AffineWitnessDeterminantMinimum = 0.80f' in R and
   'AffineWitnessDeterminantMaximum = 1.25f' in R,'pathological affine scale/reflection is rejected')
ck('operationHealthAffineBridgeRejects++' in project and
   'operationHealthAffineExactFallbacks++' in project,'rejection and exact fallback are observable')
ck('Matrix4x4.Translate' in project and 'ProjectionBridgeThresholdScale' in R,
   'accepted 0.20 px translation bridge remains secondary fallback')
ck('Math.Abs(currentCenterLatitudeDeg) >=' in project and
   'ProjectionBridgeLatitudeLimitDeg' in project,'polar motion remains exact-only')
ck('ProjectMesh(entry.PackedTerrainMesh' in project and
   'ProjectMesh(entry.ContourMesh' in project and 'ProjectMesh(entry.CoastlineMesh' in project,
   'exact fallback still uploads every visible geographic layer')
ck('oh_affine_bridge=' in R and 'oh_affine_reject=' in R and
   'oh_affine_witness=' in R and 'oh_affine_exact_fallback=' in R and
   'oh_affine_max_mpx=' in R,'runtime exposes affine acceptance/error/fallback telemetry')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz presentation authority is unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'visual RenderTexture authority is unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Witness-Bounded Affine Projection] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb = prebuild.read_text()
marker = " ('Operation Health Projection Motion Bridge','selftest_v01800_operation_health_projection_motion_bridge.py'),"
addition = " ('Operation Health Witness-Bounded Affine Projection','selftest_v01800_operation_health_witness_affine_projection.py'),"
if 'selftest_v01800_operation_health_witness_affine_projection.py' not in pb:
    if marker not in pb:
        raise SystemExit('prebuild Projection Motion Bridge marker absent')
    pb = pb.replace(marker, marker + '\n' + addition, 1)
prebuild.write_text(pb)

print('[AERIS23 Witness Affine Projection] patch applied')
print('Authority: exact cached meshes + 8-point witness validation + affine matrix; old translation bridge remains fallback')
print('Acceptance: <=0.08 render-target px witness error; polar/structural/rejected cases use accepted fallback/exact path')