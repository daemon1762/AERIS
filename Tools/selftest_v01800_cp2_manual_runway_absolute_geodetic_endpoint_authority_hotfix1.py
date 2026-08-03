#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 manual runway absolute geodetic endpoint authority hotfix 1")
snapshot = read(SOURCE / "Landing/AERISRunwaySnapshotBuilder.cs")
worker = read(SOURCE / "Landing/AERISRunwayGeometryWorker.cs")
witness = read(SOURCE / "Landing/AERISRunwayWitnessLibrary.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

# Storage remains body-fixed geodetic and now declares that coordinate frame.
suite.check('LatitudeDeg = vessel.latitude' in witness and
            'LongitudeDeg = NormalizeLongitude(vessel.longitude)' in witness,
            "MARK A/B capture vessel body-fixed latitude and longitude")
suite.check('node.AddValue("coordinateFrame", "BODY_FIXED_GEODETIC_ABSOLUTE")' in witness,
            "saved manual calibration declares its body-fixed absolute coordinate frame")
suite.check('coordinateFrame=BODY_FIXED_GEODETIC_ABSOLUTE' in witness,
            "runtime calibration log exposes coordinate authority")

# The old Unity-world/local-basis path mirrored longitude/east at Kola.
method = snapshot[snapshot.index('static WitnessFrame BuildWitnessFrame'):
                  snapshot.index('static AERISSurveyPoint[] ToArray')]
suite.check('GetWorldSurfacePosition' not in method,
            "manual witness projection no longer passes through Unity world coordinates")
suite.check('Vector3.Dot' not in method,
            "manual witness projection no longer depends on provider local handedness")
suite.check('TryProjectBodyFixedGeodetic' in method,
            "snapshot builder uses spherical body-fixed inverse projection")
suite.check('NormalizeSignedLongitude' in method,
            "geodetic projection safely handles signed/dateline longitude deltas")
suite.check('eastMeters = Math.Sin(bearing) * distance' in method and
            'northMeters = Math.Cos(bearing) * distance' in method,
            "east/north components come from geodesic bearing rather than Unity cross products")
suite.check('witness.Start.ElevationMeters - frame.Elevation' in method and
            'witness.End.ElevationMeters - frame.Elevation' in method,
            "endpoint altitude is retained relative to the survey reference without remapping")

# Worker keeps user A/B as the positional authority and physical geometry as width evidence only.
suite.check('USER BODY-FIXED GEODETIC A/B AXIS — NO MESH/ANCHOR REALIGNMENT' in worker,
            "user endpoint axis explicitly forbids mesh/launch-anchor realignment")
suite.check('BODY-FIXED ABSOLUTE LAT/LON/ALT ENDPOINT AUTHORITY' in worker,
            "absolute placement detail names the final endpoint authority")
suite.check('coordinateAuthority=BODY_FIXED_GEODETIC_ABSOLUTE' in worker,
            "worker result records the selected coordinate authority")
suite.check('Physical scan data' in worker and 'never moves the marked axis' in worker,
            "existing safety boundary keeps physical scan data advisory for user placement")

# Reproduce the AERISFlightControl(24) failure numerically.  The marked coordinates
# have a true A->B initial bearing near 195.85 degrees; an east-sign mirror produces
# the erroneous 163-164 degree runway seen in the runtime log.
def norm_lon(v):
    v %= 360.0
    if v > 180.0: v -= 360.0
    if v < -180.0: v += 360.0
    return v

def inverse(origin_lat, origin_lon, target_lat, target_lon, radius=600000.0):
    lat1 = math.radians(origin_lat)
    lat2 = math.radians(target_lat)
    dlon = math.radians(norm_lon(target_lon-origin_lon))
    y = math.sin(dlon)*math.cos(lat2)
    x = math.cos(lat1)*math.sin(lat2)-math.sin(lat1)*math.cos(lat2)*math.cos(dlon)
    bearing = math.atan2(y,x)
    dlat = lat2-lat1
    h = math.sin(dlat/2.0)**2 + math.cos(lat1)*math.cos(lat2)*math.sin(dlon/2.0)**2
    angle = 2.0*math.atan2(math.sqrt(max(0.0,min(1.0,h))), math.sqrt(max(0.0,1.0-h)))
    dist = radius*angle
    return math.sin(bearing)*dist, math.cos(bearing)*dist

def heading(east, north):
    return math.degrees(math.atan2(east,north)) % 360.0

origin=(-4.15965054,-72.10936743)
a=(-4.0581871709924062,-72.08052478927371)
b=(-4.260410797011267,-72.138091665285287)
ae,an=inverse(*origin,*a)
be,bn=inverse(*origin,*b)
true_heading=heading(be-ae,bn-an)
mirrored_heading=heading(-(be-ae),bn-an)
suite.check(abs(true_heading-195.84820718569881) < 0.05,
            "Kola A/B geodesic projection preserves the recorded 195.85 degree axis")
suite.check(163.0 < mirrored_heading < 165.0,
            "east-sign mirror reproduces the erroneous 163-164 degree runtime axis")
suite.check(abs(true_heading-mirrored_heading) > 30.0,
            "the observed displacement is a coordinate-frame reflection, not rounding noise")

# Scope/package checks.
changed = strip_csharp_comments_and_literals(snapshot + worker + witness)
suite.check('Kola' not in changed,
            "absolute endpoint authority remains airport-agnostic")
suite.check('MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1' in build,
            "build identity names this hotfix")
suite.check('selftest_v01800_cp2_manual_runway_absolute_geodetic_endpoint_authority_hotfix1.py' in runner,
            "full CP2 acceptance includes this regression")
for rel in (
    'Docs/CP2_MANUAL_RUNWAY_ABSOLUTE_GEODETIC_ENDPOINT_AUTHORITY_HOTFIX_1_v0.18.0.0_ja.md',
    'Docs/ND_CP2_MANUAL_RUNWAY_ABSOLUTE_GEODETIC_ENDPOINT_AUTHORITY_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md',
    'Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl24_MANUAL_AB_EAST_WEST_REFLECTION.txt',
    'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MANUAL_RUNWAY_ABSOLUTE_GEODETIC_ENDPOINT_AUTHORITY_HOTFIX1.txt'):
    suite.check((ROOT / rel).is_file(), 'current hotfix evidence exists: ' + rel)
suite.finish()
