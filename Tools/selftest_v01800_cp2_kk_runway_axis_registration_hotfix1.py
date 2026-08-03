#!/usr/bin/env python3
import math
import random
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 KK runway axis registration hotfix 1")
contracts = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySurveyContracts.cs")
builder = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySnapshotBuilder.cs")
worker = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwayGeometryWorker.cs")
resolver = read(ROOT / "Source/AERISFlightControl/Landing/AERISOperationalRunwayResolver.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
readme = read(ROOT / "README.md")

suite.check("CurrentAxisRegistrationRevision = 2" in contracts,
            "targeted KK/SLE axis-registration revision exists")
suite.check("KK_RUNWAY_AXIS_REGISTRATION" in builder and
            "CurrentAxisRegistrationRevision" in builder,
            "KK/SLE source fingerprint invalidates targeted old axis cache entries")
suite.check("TryRunwaySurfacePca" in worker,
            "worker extracts an independent physical runway-surface axis")
suite.check("TryBestRunwayStripe" in worker and "ScoreRunwayStripe" in worker,
            "axis extraction uses a bounded stripe search with continuity scoring")
suite.check("coefficientOfVariation" in worker and "coverage < 0.70" in worker,
            "dense apron-only or discontinuous diagonal stripes are penalized")
suite.check("TryWeightedPca(bestPoints" in worker,
            "selected runway stripe is refined by physical-point PCA")
for token in ("Taxiway", "Apron", "Platform", "Obstacle", "NaturalSurface",
              "ApproachLight"):
    suite.check(token in worker[worker.find("static bool TryRunwaySurfacePca"):
                                worker.find("static void AddHeadingCandidate")],
                "surface-axis filter excludes semantic: " + token)
suite.check("IndependentSurfaceAxis" in worker,
            "independent surface evidence is explicitly tracked")
suite.check("!physicalAxis.IndependentSurfaceAxis" in worker,
            "certification fails closed without independent surface evidence")
suite.check("surfaceError <= 1.0" in worker,
            "accepted geometry candidate must follow the measured surface axis")
suite.check("axisReferenceError <= 15.0" in worker,
            "runway designator is only a broad sanity gate")
suite.check("legacyRegisteredHeading" in worker and
            "RegisteredHeadingAfterDeg = candidateHeading" in worker,
            "diagnostics distinguish launch-reference heading from corrected mesh heading")
suite.check("SignedAxisDifference(legacyRegisteredHeading, candidateHeading)" in worker,
            "heading correction reports the actual old-to-physical axis rotation")
suite.check("ApplyAxisRegistrationConstraint" in worker and
            "ApplyAbsolutePlacementConstraint" in worker,
            "axis correction precedes launch-position anchoring")
suite.check("LaunchAnchorHeadingDeg" in worker and
            "launchHeadingTelemetryError" in worker,
            "launch heading is a broad sanity reference rather than physical axis truth")
suite.check("snapshot.AbsolutePlacementRequired &&" in resolver and
            "!axis.AxisRegistrationValid" in resolver,
            "main-thread resolver independently rejects invalid KK/SLE axis registration")
suite.check("[RUNWAY_AXIS]" in resolver,
            "field diagnostics expose runway-axis registration")
for token in ("meshRunwayHeadingDeg=", "launchTransformHeadingDeg=",
              "registeredHeadingBeforeDeg=", "registeredHeadingAfterDeg=",
              "headingCorrectionDeg=", "axisReference=LAUNCH_ANCHOR", "axisReferenceErrorDeg=",
              "surfaceAspect=", "surfacePoints=", "axisRegistrationValid="):
    suite.check(token in resolver, "axis log field exists: " + token)
suite.check('Name = "RUNWAY_AXIS_REGISTRATION"' in resolver,
            "certification parameters expose axis-registration state")
suite.check("FlightCtrlState" not in strip_csharp_comments_and_literals(worker + resolver),
            "axis registration remains control-free")
suite.check("MainThrottle" not in strip_csharp_comments_and_literals(worker + resolver),
            "axis registration cannot write throttle")
identity = "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1"
suite.check(identity in build and identity in generated,
            "native build identity includes the axis-registration hotfix")
suite.check("Axis Registration Hotfix 1" in version and
            "KK Runway Absolute Registration Hotfix 1" in version,
            "AVC metadata identifies the axis-registration hotfix")
suite.check("KK Runway Axis Registration Hotfix 1" in readme,
            "README identifies the axis-registration hotfix")

# Numeric mirror of the C# stripe search.  The cloud deliberately includes a
# dense, same-semantic apron that pulls naive PCA and raw support scoring away
# from the true runway heading.  Continuity/uniformity scoring must recover the
# long runway surface rather than the apron diagonal.
def normalize180(value):
    value %= 180.0
    return value + 180.0 if value < 0.0 else value

def angle_error(a, b):
    difference = abs(normalize180(a) - normalize180(b))
    return min(difference, 180.0 - difference)

def make_rectangle(heading, length, width, along_center=0.0,
                   across_center=0.0, along_step=25.0, across_step=10.0,
                   weight=1.5):
    radians = math.radians(heading)
    east, north = math.sin(radians), math.cos(radians)
    normal_east, normal_north = -north, east
    points = []
    along = -length * 0.5
    while along <= length * 0.5 + 1e-9:
        across = -width * 0.5
        while across <= width * 0.5 + 1e-9:
            points.append((along * east + (across + across_center) * normal_east +
                           along_center * east,
                           along * north + (across + across_center) * normal_north +
                           along_center * north,
                           weight))
            across += across_step
        along += along_step
    return points

def weighted_pca(points):
    total = sum(p[2] for p in points)
    mean_e = sum(p[0] * p[2] for p in points) / total
    mean_n = sum(p[1] * p[2] for p in points) / total
    ee = nn = en = 0.0
    for east, north, weight in points:
        de, dn = east - mean_e, north - mean_n
        ee += de * de * weight
        nn += dn * dn * weight
        en += de * dn * weight
    angle = 0.5 * math.atan2(2.0 * en, nn - ee)
    return math.sin(angle), math.cos(angle)

def score_selected(points, heading, stripe_width):
    radians = math.radians(normalize180(heading))
    east, north = math.sin(radians), math.cos(radians)
    normal_east, normal_north = -north, east
    along = [p[0] * east + p[1] * north for p in points]
    across = [p[0] * normal_east + p[1] * normal_north for p in points]
    span = max(along) - min(along)
    width = max(1.0, max(across) - min(across))
    aspect = span / width
    bins = max(16, min(48, int(math.floor(span / max(40.0, stripe_width)))))
    counts = [0] * bins
    safe_span = max(1e-6, span)
    for value in along:
        index = int(math.floor(((value - min(along)) / safe_span) * bins))
        index = max(0, min(bins - 1, index))
        counts[index] += 1
    coverage = sum(1 for count in counts if count >= 2) / float(bins)
    mean = sum(counts) / float(bins)
    variance = sum((count - mean) ** 2 for count in counts) / float(bins)
    coefficient = math.sqrt(max(0.0, variance)) / mean
    ordered = sorted(counts)
    median = ((ordered[bins // 2 - 1] + ordered[bins // 2]) * 0.5
              if bins % 2 == 0 else ordered[bins // 2])
    uniformity = 1.0 / (1.0 + 4.0 * coefficient * coefficient)
    if coverage < 0.70 or span < 100.0 or aspect < 2.5:
        return float("-inf")
    return (span * min(30.0, aspect) * coverage * uniformity *
            (1.0 + math.log(1.0 + median)))

def best_stripe(points, heading, stripe_width):
    radians = math.radians(normalize180(heading))
    east, north = math.sin(radians), math.cos(radians)
    normal_east, normal_north = -north, east
    projected = sorted([(p[0] * east + p[1] * north,
                         p[0] * normal_east + p[1] * normal_north, p)
                        for p in points], key=lambda item: item[1])
    best = None
    left = 0
    # O(n^2) is acceptable in this small deterministic test and mirrors the
    # C# window result rather than its monotonic-queue optimization.
    for right in range(len(projected)):
        while left < right and projected[right][1] - projected[left][1] > stripe_width:
            left += 1
        window = projected[left:right + 1]
        if len(window) < 16:
            continue
        values = [item[0] for item in window]
        span = max(values) - min(values)
        width = max(1.0, window[-1][1] - window[0][1])
        aspect = span / width
        if span < 100.0 or aspect < 2.5:
            continue
        raw = len(window) * span * min(8.0, aspect)
        if best is None or raw > best[0]:
            best = (raw, [item[2] for item in window])
    if best is None:
        return float("-inf"), None
    selected = best[1]
    return score_selected(selected, heading, stripe_width), selected

random.seed(1680)
true_heading = 157.0
legacy_heading = 163.7
cloud = make_rectangle(true_heading, 2500.0, 70.0)
# Dense airport apron attached near one end and rotated differently.  All points
# intentionally carry equivalent weight to model a poorly named combined KK mesh.
cloud.extend(make_rectangle(150.0, 650.0, 500.0, along_center=1000.0,
                            across_center=230.0, along_step=12.5,
                            across_step=12.5))
headings = [normalize180(legacy_heading + offset * 0.5)
            for offset in range(-40, 41)]
best_score = float("-inf")
best_points = None
best_heading = None
for heading in headings:
    score, selected = best_stripe(cloud, heading, 122.5)
    if selected is not None and score > best_score:
        best_score, best_points, best_heading = score, selected, heading
axis_east, axis_north = weighted_pca(best_points)
recovered = normalize180(math.degrees(math.atan2(axis_east, axis_north)))
naive_east, naive_north = weighted_pca(cloud)
naive = normalize180(math.degrees(math.atan2(naive_east, naive_north)))

suite.check(angle_error(legacy_heading, true_heading) > 5.0,
            "synthetic legacy/provider heading is materially wrong")
suite.check(angle_error(naive, true_heading) > 2.0,
            "dense attached apron defeats naive whole-mesh PCA")
suite.check(angle_error(best_heading, true_heading) <= 1.0,
            "continuity-scored stripe search selects the runway direction")
suite.check(angle_error(recovered, true_heading) <= 0.25,
            "PCA refinement recovers the physical runway axis")
suite.check(abs(((recovered - legacy_heading + 90.0) % 180.0) - 90.0) > 5.0,
            "reported axis correction is non-zero for the faulty registration")

for rel in (
    "Docs/CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX_1_v0.18.0.0_ja.md",
    "Docs/ND_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
    "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX1_ja.md",
    "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current axis hotfix document exists: " + rel)

suite.finish()
