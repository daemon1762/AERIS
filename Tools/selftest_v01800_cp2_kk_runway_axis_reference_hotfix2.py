#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 KK runway axis reference hotfix 2")
contracts = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySurveyContracts.cs")
builder = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySnapshotBuilder.cs")
worker = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwayGeometryWorker.cs")
resolver = read(ROOT / "Source/AERISFlightControl/Landing/AERISOperationalRunwayResolver.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
readme = read(ROOT / "README.md")

suite.check("CurrentAxisRegistrationRevision = 2" in contracts,
            "axis reference correction invalidates Hotfix 1 failure cache entries")
suite.check("KK_RUNWAY_AXIS_REGISTRATION" in builder and
            "CurrentAxisRegistrationRevision" in builder,
            "targeted KK/SLE source fingerprint includes axis revision 2")
start = worker.find("static bool ApplyAxisRegistrationConstraint")
end = worker.find("static void ReRegisterCandidateToPhysicalAxis", start)
axis = worker[start:end]
suite.check(start >= 0 and end > start,
            "axis-registration implementation slice is present")
suite.check("registeredReferenceHeading = Finite(snapshot.LaunchAnchorHeadingDeg)" in axis,
            "launch/spawn transform supplies the independent world-space axis reference")
suite.check("AngleDifference180(measuredHeading," in axis and
            "registeredReferenceHeading" in axis,
            "physical pavement axis is compared with the launch reference")
suite.check("axisReferenceError <= 15.0" in axis,
            "launch reference remains a broad fail-closed sanity gate")
suite.check("snapshot.DeclaredHeadingDeg" not in axis,
            "KK static model orientation cannot reject internally rotated runway meshes")
suite.check("ReRegisterCandidateToPhysicalAxis" in axis and
            "if (!surfaceAgreement)" in axis,
            "measured pavement stripe remains authoritative")
suite.check("axisReference=LAUNCH_ANCHOR" in worker and
            "axisReferenceErrorDeg=" in resolver,
            "runtime diagnostics identify the corrected reference source")
suite.check("AxisReferenceErrorDeg" in contracts and
            "RunwayDesignatorErrorDeg" not in contracts,
            "telemetry field no longer mislabels launch-axis agreement as designator error")

# Regression mirror from AERISFlightControl(16).zip. Dundard's Edge has an
# internally rotated runway mesh near 133.57 degrees while the KK static
# orientation is 0 degrees. Hotfix 1 compared those values and rejected it.
def axis_error(a, b):
    delta = (a - b) % 180.0
    return min(delta, 180.0 - delta)

mesh_heading = 133.5728
static_orientation = 0.0
launch_heading = 133.5728
suite.check(axis_error(mesh_heading, static_orientation) > 15.0,
            "Hotfix 1 static-orientation gate reproduces the field rejection")
suite.check(axis_error(mesh_heading, launch_heading) <= 0.05,
            "launch-reference gate accepts the measured Dundard runway axis")
suite.check(axis_error(mesh_heading + 180.0, launch_heading) <= 0.05,
            "reciprocal runway direction remains axis-equivalent")

for mesh, launch in ((163.69, 343.69), (130.62, 310.62),
                     (137.65, 317.65), (140.88, 320.88),
                     (136.14, 316.14), (179.28, 359.26)):
    suite.check(axis_error(mesh, launch) <= 0.10,
                "representative KK/SLE mesh/launch reciprocal pair passes: %.2f/%.2f" %
                (mesh, launch))

identity = "AXIS REFERENCE HOTFIX 2"
suite.check(identity in build and identity in generated,
            "native build identity exposes axis reference hotfix 2")
suite.check("Axis Reference Hotfix 2" in version,
            "AVC metadata exposes axis reference hotfix 2")
suite.check("KK Runway Axis Reference Hotfix 2" in readme,
            "README identifies the current runtime regression fix")
suite.check("FlightCtrlState" not in strip_csharp_comments_and_literals(axis + resolver),
            "axis reference correction remains control-free")
suite.check("MainThrottle" not in strip_csharp_comments_and_literals(axis + resolver),
            "axis reference correction cannot command throttle")

for rel in (
    "Docs/CP2_KK_RUNWAY_AXIS_REFERENCE_HOTFIX_2_v0.18.0.0_ja.md",
    "Docs/ND_CP2_KK_RUNWAY_AXIS_REFERENCE_HOTFIX_2_TEST_CARD_v0.18.0.0_ja.md",
    "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl16_CP2_AXIS_REFERENCE_HOTFIX2.txt",
    "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REFERENCE_HOTFIX2.txt",
    "ACCEPTANCE_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REFERENCE_HOTFIX2.txt"):
    suite.check((ROOT / rel).is_file(), "axis reference hotfix evidence exists: " + rel)

suite.finish()
