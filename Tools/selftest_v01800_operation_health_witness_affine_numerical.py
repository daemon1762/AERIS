#!/usr/bin/env python3
import math,sys
sys.dont_write_bytecode=True

BODY=600000.0
W=366.0*3.0
HPIX=188.0*3.0
ACCEPT=0.08
DENSE_LIMIT=0.20

def unit(lat,lon):
    c=math.cos(lat); return (c*math.cos(lon),c*math.sin(lon),math.sin(lat))
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def basis(lat,lon):
    return unit(lat,lon),(-math.sin(lon),math.cos(lon),0.0),(-math.sin(lat)*math.cos(lon),-math.sin(lat)*math.sin(lon),math.cos(lat))
def project(p,lat,lon,rng):
    c,e,n=basis(lat,lon); eu=dot(p,e); nu=dot(p,n); rs=max(0.0,eu*eu+nu*nu)
    if rs<=0.18:
        f=1.0+rs*(1/6+rs*(3/40+rs*(5/112+rs*(35/1152+rs*63/2816))))
    else:
        radial=math.sqrt(rs); f=1.0 if radial<=1e-12 else math.atan2(radial,dot(p,c))/radial
    return (0.5+eu*BODY*f/(rng*1.30),0.5+nu*BODY*f/rng)
def dest_unit(lat,lon,east,north):
    dist=math.hypot(east,north)
    if dist<=1e-12:return unit(lat,lon)
    br=math.atan2(east,north); ad=dist/BODY
    lat2=math.asin(math.sin(lat)*math.cos(ad)+math.cos(lat)*math.sin(ad)*math.cos(br))
    lon2=lon+math.atan2(math.sin(br)*math.sin(ad)*math.cos(lat),math.cos(ad)-math.sin(lat)*math.sin(lat2))
    return unit(lat2,lon2)
def center_after(lat,lon,east,north):
    p=dest_unit(lat,lon,east,north); return math.asin(p[2]),math.atan2(p[1],p[0])
def affine(p0,p1,p2,q0,q1,q2):
    px1=p1[0]-p0[0]; py1=p1[1]-p0[1]; px2=p2[0]-p0[0]; py2=p2[1]-p0[1]
    qx1=q1[0]-q0[0]; qy1=q1[1]-q0[1]; qx2=q2[0]-q0[0]; qy2=q2[1]-q0[1]
    d=px1*py2-px2*py1
    if abs(d)<1e-12:return None
    a00=(qx1*py2-qx2*py1)/d; a01=(-qx1*px2+qx2*px1)/d
    a10=(qy1*py2-qy2*py1)/d; a11=(-qy1*px2+qy2*px1)/d
    tx=q0[0]-a00*p0[0]-a01*p0[1]; ty=q0[1]-a10*p0[0]-a11*p0[1]
    return a00,a01,a10,a11,tx,ty
def apply(a,p):
    return a[0]*p[0]+a[1]*p[1]+a[4],a[2]*p[0]+a[3]*p[1]+a[5]
def extrema(points):
    metrics=[lambda p:p[0],lambda p:-p[0],lambda p:p[1],lambda p:-p[1],lambda p:p[0]+p[1],lambda p:-(p[0]+p[1]),lambda p:p[0]-p[1],lambda p:-p[0]+p[1]]
    out=[]
    for fn in metrics:
        idx=max(range(len(points)),key=lambda i:fn(points[i]))
        if idx not in out:out.append(idx)
    return out
def best_basis(points,ids):
    best=None; area=0.0
    for ai in range(len(ids)-2):
      for bi in range(ai+1,len(ids)-1):
       for ci in range(bi+1,len(ids)):
        a,b,c=points[ids[ai]],points[ids[bi]],points[ids[ci]]
        ar=abs((b[0]-a[0])*(c[1]-a[1])-(c[0]-a[0])*(b[1]-a[1]))
        if ar>area:area=ar;best=(ids[ai],ids[bi],ids[ci])
    return best

accepted=0; rejected=0; worst=0.0; worst_case=None
for rng in (80000.0,160000.0):
 for latdeg in (0.0,30.0,60.0,69.0):
  lat=math.radians(latdeg); lon=0.0
  for scale in (0.125,0.25,0.50):
   # Representative Entry footprint inside the ND viewport; dense 17x17 truth grid.
   geo=[]; old=[]
   for iy in range(17):
    north=(-0.5+iy/16.0)*rng*scale
    for ix in range(17):
     east=(-0.65+1.30*ix/16.0)*rng*scale
     p=dest_unit(lat,lon,east,north); geo.append(p); old.append(project(p,lat,lon,rng))
   ids=extrema(old); basis_ids=best_basis(old,ids)
   if basis_ids is None:continue
   for eastmove,northmove in ((50,0),(200,0),(500,0),(1000,0),(0,500),(500,500),(1000,-500)):
    nlat,nlon=center_after(lat,lon,eastmove,northmove)
    current=[project(p,nlat,nlon,rng) for p in geo]
    ia,ib,ic=basis_ids
    a=affine(old[ia],old[ib],old[ic],current[ia],current[ib],current[ic])
    if a is None:continue
    det=a[0]*a[3]-a[1]*a[2]
    if det<0.80 or det>1.25:
        rejected+=1;continue
    witness_error=0.0
    for i in ids:
      pred=apply(a,old[i]); exact=current[i]
      witness_error=max(witness_error,math.hypot((pred[0]-exact[0])*W,(pred[1]-exact[1])*HPIX))
    if witness_error>ACCEPT:
      rejected+=1;continue
    accepted+=1
    dense=0.0
    for i in range(len(old)):
      pred=apply(a,old[i]); exact=current[i]
      dense=max(dense,math.hypot((pred[0]-exact[0])*W,(pred[1]-exact[1])*HPIX))
    if dense>worst:
      worst=dense;worst_case=(rng,latdeg,scale,eastmove,northmove,witness_error)
    if dense>=DENSE_LIMIT:
      print('[FAIL] witness-accepted affine exceeds dense 0.20 px safety: case=%r dense=%.6f witness=%.6f'%(worst_case,dense,witness_error))
      raise SystemExit(1)

if accepted<=0:
 print('[FAIL] numerical sweep produced no accepted affine cases');raise SystemExit(1)
print('[PASS] numerical sweep accepted %d cases and rejected %d unsafe/nonlinear cases'%(accepted,rejected))
print('[PASS] worst dense-grid error among accepted cases = %.6f px < %.2f px; case=%r'%(worst,DENSE_LIMIT,worst_case))
print('[Operation Health Witness-Bounded Affine Numerical] PASS')