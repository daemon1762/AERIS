#!/usr/bin/env python3
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
legacy_exact_projection=('ProjectMesh(entry.LandMesh' in project and 'mesh.vertices = projectedVertices' in R)
accepted_packed_exact_projection=(
    'if (gpuVertexProjection.Active && EnsureGpuVertexProjectionAttributes(entry))' in project and
    'operationHealthGpuVertexExactBypasses++' in project and
    'ProjectMesh(entry.PackedTerrainMesh,' in project and
    'entry.PackedTerrainGeographicPoints,' in project and
    'entry.PackedTerrainProjectedVertices, context);' in project and
    'ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,' in project and
    'entry.ContourProjectedVertices, context);' in project and
    'ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,' in project and
    'entry.CoastlineProjectedVertices, context);' in project and
    'mesh.vertices = projectedVertices;' in R
)
ck(legacy_exact_projection or accepted_packed_exact_projection,
   'exact projection/upload path remains intact through legacy or accepted packed GPU/CPU descendant')
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
