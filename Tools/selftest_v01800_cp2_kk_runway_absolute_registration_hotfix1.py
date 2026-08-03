#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 KK runway absolute registration hotfix 1")
contracts = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySurveyContracts.cs")
builder = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySnapshotBuilder.cs")
worker = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwayGeometryWorker.cs")
resolver = read(ROOT / "Source/AERISFlightControl/Landing/AERISOperationalRunwayResolver.cs")
models = read(ROOT / "Source/AERISFlightControl/Landing/AERISAirfieldModels.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")

suite.check("CurrentAlgorithmVersion = 1710" in contracts,
            "global runway certification algorithm is current")
suite.check("CurrentAbsolutePlacementRevision = 2" in contracts,
            "targeted KK/SLE absolute-placement revision advances for axis registration")
suite.check("KK_ABSOLUTE_PLACEMENT" in builder and
            "CurrentAbsolutePlacementRevision" in builder,
            "KK/SLE source fingerprint invalidates only targeted old cache entries")
suite.check("AbsolutePlacementRequired" in contracts and
            "AbsolutePlacementConstraintAvailable" in contracts,
            "required and available absolute-placement states are distinct")
suite.check("RuntimeInstanceTransform.position" in builder and
            "ProviderReferenceOriginUsed" in builder,
            "placed static instance is used as the independent survey origin")
suite.check("RuntimeLaunchPosition" in builder and "LaunchAnchorEastMeters" in builder,
            "launch transform is retained as an independent absolute anchor")
suite.check("LaunchAnchorHeadingDeg" in builder and
            "ProviderReferenceToLaunchMeters" in builder,
            "launch heading and provider-to-launch separation are captured")
suite.check("record.Source == AERISAirfieldSource.KerbalKonstructs" in builder and
            "record.Source == AERISAirfieldSource.StockLaunchsitesExpansion" in builder,
            "absolute placement is targeted to KK and SLE providers")
suite.check("AbsolutePlacementInvalid" in models,
            "dedicated fail-closed certification code exists")
suite.check("snapshot.AbsolutePlacementRequired &&" in worker and
            "!snapshot.AbsolutePlacementConstraintAvailable" in worker,
            "missing required launch anchor is rejected before certification")
suite.check("ApplyAbsolutePlacementConstraint" in worker,
            "worker applies absolute placement before accepting a runway candidate")
suite.check("launchHeadingTelemetryError" in worker and
            "headingValid" not in worker[worker.find("static bool ApplyAbsolutePlacementConstraint"):worker.find("static bool ApplyAxisRegistrationConstraint")],
            "launch heading is telemetry only and cannot certify the physical runway axis")
suite.check("candidate.PhysicalStartMeters - alongMargin" in worker and
            "candidate.PhysicalEndMeters + alongMargin" in worker,
            "launch anchor must lie within the physical longitudinal runway interval")
suite.check("maximumCorrection = Math.Max(75.0, candidate.WidthMeters * 1.25)" in worker,
            "absolute lateral correction is bounded")
suite.check("candidate.CenterEast += normalEast * crossTrack" in worker and
            "candidate.CenterNorth += normalNorth * crossTrack" in worker,
            "only the measured centerline cross-track position is translated")
suite.check("candidate.PhysicalStartMeters" not in worker[worker.find("candidate.CenterEast +="):worker.find("candidate.AbsolutePlacementDetail =", worker.find("candidate.CenterEast +="))],
            "absolute placement correction does not rewrite physical longitudinal endpoints")
suite.check("LaunchCrossTrackBeforeMeters" in worker and
            "LaunchCrossTrackAfterMeters" in worker and
            "LaunchAlongTrackMeters" in worker,
            "absolute placement diagnostics are preserved on the candidate")
suite.check("case AERISRunwayFailureCode.AbsolutePlacementInvalid: return 100" in worker,
            "absolute placement failure has strongest rejection priority")
suite.check("snapshot.AbsolutePlacementRequired &&" in resolver and
            "!axis.AbsolutePlacementValid" in resolver,
            "main-thread resolver independently refuses invalid absolute placement")
suite.check("[RUNWAY_PLACEMENT]" in resolver,
            "field log exposes absolute registration evidence")
for token in ("providerToLaunchM=", "launchCrossBeforeM=", "launchCrossAfterM=",
              "launchAlongM=", "launchHeadingTelemetryErrorDeg=", "correctionM=",
              "absolutePlacementValid="):
    suite.check(token in resolver, "placement log field exists: " + token)
suite.check('Name = "ABSOLUTE_PLACEMENT"' in resolver,
            "certification exposes absolute placement state")
suite.check('Name = "LAUNCH_CROSS_TRACK"' in resolver,
            "certification exposes pre-correction launch cross-track")
suite.check("FlightCtrlState" not in strip_csharp_comments_and_literals(worker + resolver),
            "absolute registration logic remains control-free")
suite.check("MainThrottle" not in strip_csharp_comments_and_literals(worker + resolver),
            "absolute registration logic cannot write throttle")
identity = "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1"
suite.check(identity in build and identity in generated,
            "build identity is consistent")

# Independent numeric examples of the bounded cross-track-only translation.
def constrain(axis_e, axis_n, center_e, center_n, launch_e, launch_n):
    normal_e, normal_n = -axis_n, axis_e
    center_across = center_e * normal_e + center_n * normal_n
    launch_across = launch_e * normal_e + launch_n * normal_n
    cross = launch_across - center_across
    return (center_e + normal_e * cross,
            center_n + normal_n * cross,
            cross)

e, n, cross = constrain(0.0, 1.0, 20.0, 1000.0, 0.0, 100.0)
suite.check(abs(cross - 20.0) < 1e-9 and abs(e) < 1e-9 and abs(n - 1000.0) < 1e-9,
            "north-south runway lateral offset is corrected without longitudinal motion")
e, n, cross = constrain(1.0, 0.0, 500.0, -15.0, 50.0, 0.0)
suite.check(abs(cross - 15.0) < 1e-9 and abs(e - 500.0) < 1e-9 and abs(n) < 1e-9,
            "east-west runway lateral offset is corrected without longitudinal motion")
# The corrected centerline and launch point have identical across coordinates.
axis_e, axis_n = math.sin(math.radians(34.0)), math.cos(math.radians(34.0))
e, n, _ = constrain(axis_e, axis_n, 31.0, -7.0, -2.0, 5.0)
normal_e, normal_n = -axis_n, axis_e
suite.check(abs((e * normal_e + n * normal_n) -
                (-2.0 * normal_e + 5.0 * normal_n)) < 1e-9,
            "oblique runway correction places launch anchor on the measured centerline")

for rel in (
    "Docs/ND_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
    "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX1_ja.md",
    "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix document exists: " + rel)

suite.finish()
