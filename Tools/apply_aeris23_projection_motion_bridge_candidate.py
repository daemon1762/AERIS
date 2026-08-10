#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()

if 'oh_project_bridge=' in text and 'Matrix4x4 EnsureProjectedGeometry(' in text:
    print('[AERIS23 Projection Motion Bridge] already applied')
    raise SystemExit(0)


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old, new, 1)

text = replace_once(text,
'''            internal double LastProjectionCenterLatitudeDeg = double.NaN;
            internal double LastProjectionCenterLongitudeDeg = double.NaN;
            internal double LastProjectionBodyRadius = double.NaN;''',
'''            internal double LastProjectionCenterLatitudeDeg = double.NaN;
            internal double LastProjectionCenterLongitudeDeg = double.NaN;
            // Exact-projection origin retained as a unit vector. Motion-only 10 Hz ticks
            // can translate the immutable projected mesh instead of rewriting every vertex.
            internal double LastProjectionCenterX = double.NaN;
            internal double LastProjectionCenterY = double.NaN;
            internal double LastProjectionCenterZ = double.NaN;
            internal float LastExactProjectionRealtime = -1f;
            internal double LastProjectionBodyRadius = double.NaN;''',
'entry projection fields')

text = replace_once(text,
'''        const float ProjectionRefreshAgeSeconds = 0.50f;
        const float ProjectionRefreshHeadingDeg = 8f;''',
'''        const float ProjectionRefreshAgeSeconds = 0.50f;
        const float ProjectionRefreshHeadingDeg = 8f;
        // AERIS23 Projection Motion Bridge. Full CPU projection/upload remains the exact
        // authority. Between exact commits only a tiny N-UP translation may be applied.
        // The bridge budget is 80% of the existing quarter-pixel movement threshold
        // (=0.20 pixel), tightens with latitude, and is disabled in polar convergence.
        const float ProjectionBridgeThresholdScale = 0.80f;
        const float ProjectionBridgeMinimumLatitudeScale = 0.35f;
        const float ProjectionBridgeLatitudeLimitDeg = 70f;''',
'projection constants')

text = replace_once(text,
'''        long operationHealthForcedProjectionRefreshes;
        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthForcedProjectionRefreshes;
        long operationHealthProjectionExactRefreshes;
        long operationHealthProjectionBridgeUses;
        long operationHealthLoadingBackdropFrames;''',
'projection telemetry fields')

text = replace_once(text,
'''                    EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    bool entryRendered = DrawEntry(drawEntry, mapRotation, true, effectiveMode,''',
'''                    Matrix4x4 projectionBridge = EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    // Cached geometry is N-UP. Apply the tiny center-motion bridge first,
                    // then the existing exact scale-corrected TRACK-UP rotation.
                    Matrix4x4 entryMapMatrix = mapRotation * projectionBridge;
                    bool entryRendered = DrawEntry(drawEntry, entryMapMatrix, true, effectiveMode,''',
'RenderBackBuffer projection call')

start = text.index('        void EnsureProjectedGeometry(Entry entry,')
end = text.index('        void ProjectMesh(', start)
replacement = '''        Matrix4x4 EnsureProjectedGeometry(Entry entry,
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

            // movementThresholdMeters is 0.25 rendered pixel in the current BACK target.
            // The bridge may use only 80% of that distance. Latitude convergence tightens
            // the allowance; at |lat| >= 70 degrees any center motion is exact-only.
            float latitudeScale = Mathf.Max(ProjectionBridgeMinimumLatitudeScale,
                Mathf.Abs(Mathf.Cos((float)currentCenterLatitudeDeg * Mathf.Deg2Rad)));
            double exactDistanceThreshold = Math.Max(0.01,
                movementThresholdMeters * ProjectionBridgeThresholdScale * latitudeScale);
            bool polarExactOnly = Math.Abs(currentCenterLatitudeDeg) >=
                ProjectionBridgeLatitudeLimitDeg;
            float exactAge = entry.LastExactProjectionRealtime < 0f ? float.MaxValue :
                Math.Max(0f, Time.realtimeSinceStartup - entry.LastExactProjectionRealtime);
            bool exactProjectionDue = structuralProjectionChange ||
                centerMoved && (polarExactOnly ||
                    centerMotionSquared >= exactDistanceThreshold * exactDistanceThreshold ||
                    exactAge >= ProjectionRefreshAgeSeconds);

            if (!exactProjectionDue)
            {
                // The authoritative BACK still commits at fixed 10 Hz. Instead of
                // reprojecting/uploading every vertex, move the cached N-UP geometry by
                // the current projection of its last exact geographic center. Heading is
                // handled afterwards by the existing scale-corrected map matrix.
                if (centerMoved || forceCenterProjectionRefresh)
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
                }
                return Matrix4x4.identity;
            }

            ProjectMesh(entry.LandMesh, entry.LandGeographicPoints,
                entry.LandProjectedVertices, context);
            ProjectMesh(entry.WaterMesh, entry.WaterGeographicPoints,
                entry.WaterProjectedVertices, context);
            ProjectMesh(entry.CoastalLandCorrectionMesh,
                entry.CoastalLandCorrectionGeographicPoints,
                entry.CoastalLandCorrectionProjectedVertices, context);
            ProjectMesh(entry.CoastalWaterCorrectionMesh,
                entry.CoastalWaterCorrectionGeographicPoints,
                entry.CoastalWaterCorrectionProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,
                entry.ContourProjectedVertices, context);
            ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices, context);
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

'''
text = text[:start] + replacement + text[end:]

text = replace_once(text,
'''                "; oh_motion_refresh=" + operationHealthMotionRefreshes +
                "; oh_forced_project=" + operationHealthForcedProjectionRefreshes +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_motion_refresh=" + operationHealthMotionRefreshes +
                "; oh_forced_project=" + operationHealthForcedProjectionRefreshes +
                "; oh_project_exact=" + operationHealthProjectionExactRefreshes +
                "; oh_project_bridge=" + operationHealthProjectionBridgeUses +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'projection telemetry log')

renderer.write_text(text)

# Update the old Hotfix3 source contract: 10 Hz motion must still reach BACK, while
# a full per-vertex upload is no longer mandatory for every subpixel center movement.
hotfix = ROOT / 'Tools/selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'
h = hotfix.read_text()
old = "ck('bool forceCenterProjectionRefresh' in project and 'bool projectionChanged = forceCenterProjectionRefresh ||' in project,'subpixel movement bypasses old 0.25px projection hold')"
new = "ck('bool forceCenterProjectionRefresh' in project and 'Matrix4x4.Translate' in project and 'ProjectionBridgeThresholdScale' in R,'subpixel movement keeps 10 Hz motion through bounded projection bridge')"
hotfix.write_text(replace_once(h, old, new, 'hotfix3 bridge assertion'))

bridge_test = ROOT / 'Tools/selftest_v01800_operation_health_projection_motion_bridge.py'
bridge_test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import math,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness')]
project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(')]
ck('Matrix4x4 projectionBridge = EnsureProjectedGeometry' in render and 'mapRotation * projectionBridge' in render,'motion bridge precedes existing TRACK-UP map matrix')
ck('ProjectMesh(entry.LandMesh' in project and 'mesh.vertices = projectedVertices' in R,'exact projection/upload path remains intact')
ck('ProjectionBridgeThresholdScale = 0.80f' in R,'bridge distance is capped at 0.20 rendered pixel')
ck('ProjectionBridgeLatitudeLimitDeg = 70f' in R and 'polarExactOnly' in project,'polar convergence disables motion approximation')
ck('exactAge >= ProjectionRefreshAgeSeconds' in project and 'ProjectionRefreshAgeSeconds = 0.50f' in R,'moving bridge exact-refreshes within 0.50 seconds')
ck('context.ProjectUnitToRenderNUp(entry.LastProjectionCenterX' in project and 'Matrix4x4.Translate' in project,'bridge derives translation from prior exact geographic center')
ck('oh_project_exact=' in R and 'oh_project_bridge=' in R,'runtime distinguishes exact projection and bridge use')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target visual quality authority unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),'Candidate11 contour authority unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'10 Hz presentation authority unchanged')

# Numerical safety audit: exact spherical projection vs translation bridge at the
# worst permitted latitude just below the 70-degree exact-only fence, 160 km range,
# and the supported 3x map scale. Motion is 99% of the tightened 0.20-pixel threshold.
BODY=600000.0; RANGE=160000.0; W=366.0*3.0; HPIX=188.0*3.0
HM=RANGE*1.30; VM=RANGE
lat0=math.radians(69.9); lon0=0.0
base_threshold=RANGE/188.0*0.25
lat_scale=max(0.35,abs(math.cos(lat0)))
move=base_threshold*0.80*lat_scale*0.99

def unit(lat,lon):
    c=math.cos(lat); return (c*math.cos(lon),c*math.sin(lon),math.sin(lat))
def basis(lat,lon):
    c=unit(lat,lon); e=(-math.sin(lon),math.cos(lon),0.0)
    n=(-math.sin(lat)*math.cos(lon),-math.sin(lat)*math.sin(lon),math.cos(lat))
    return c,e,n
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def project(p,lat,lon):
    c,e,n=basis(lat,lon); eu=dot(p,e); nu=dot(p,n); rs=max(0.0,eu*eu+nu*nu)
    if rs<=0.18:
        f=1.0+rs*(1/6+rs*(3/40+rs*(5/112+rs*(35/1152+rs*63/2816))))
    else:
        radial=math.sqrt(rs); f=1.0 if radial<=1e-12 else math.atan2(radial,dot(p,c))/radial
    return (0.5+eu*BODY*f/HM,0.5+nu*BODY*f/VM)
def dest(lat,lon,east,north):
    dist=math.hypot(east,north)
    if dist<=1e-12:return unit(lat,lon)
    br=math.atan2(east,north); ad=dist/BODY
    lat2=math.asin(math.sin(lat)*math.cos(ad)+math.cos(lat)*math.sin(ad)*math.cos(br))
    lon2=lon+math.atan2(math.sin(br)*math.sin(ad)*math.cos(lat),math.cos(ad)-math.sin(lat)*math.sin(lat2))
    return unit(lat2,lon2)
newc=dest(lat0,lon0,move,0.0); newlat=math.asin(newc[2]); newlon=math.atan2(newc[1],newc[0])
oldcenter=unit(lat0,lon0); cproj=project(oldcenter,newlat,newlon); delta=(cproj[0]-0.5,cproj[1]-0.5)
maxerr=0.0
for ix in range(11):
    east=-0.65*RANGE+1.30*RANGE*ix/10.0
    for iy in range(11):
        north=-0.50*RANGE+RANGE*iy/10.0
        p=dest(lat0,lon0,east,north); oldp=project(p,lat0,lon0); exact=project(p,newlat,newlon)
        bridge=(oldp[0]+delta[0],oldp[1]+delta[1])
        err=math.hypot((exact[0]-bridge[0])*W,(exact[1]-bridge[1])*HPIX)
        maxerr=max(maxerr,err)
ck(maxerr < 0.20,'worst permitted 160 km bridge remains sub-0.20 pixel at 3x (%.4f px)'%maxerr)
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Projection Motion Bridge] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb = prebuild.read_text()
marker = " ('Operation Health Pass 3 Cadence Hotfix 3 Motion Commit','selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'),"
if "selftest_v01800_operation_health_projection_motion_bridge.py" not in pb:
    if marker not in pb:
        raise SystemExit('prebuild motion suite marker absent')
    pb = pb.replace(marker,
        " ('Operation Health Projection Motion Bridge','selftest_v01800_operation_health_projection_motion_bridge.py'),\n" + marker, 1)
    prebuild.write_text(pb)

print('[AERIS23 Projection Motion Bridge] patch applied')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
