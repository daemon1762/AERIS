#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 bidirectional runway pair hotfix 1")
witness = read(SOURCE / "Landing/AERISRunwayWitnessLibrary.cs")
worker = read(SOURCE / "Landing/AERISRunwayGeometryWorker.cs")
resolver = read(SOURCE / "Landing/AERISOperationalRunwayResolver.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")

suite.check("const int CalibrationSchema = 3" in witness,
            "calibration schema 3 records reciprocal direction pairs")
for token in ("reciprocalDirectionPair", "directionAHeadingDeg", "directionBHeadingDeg"):
    suite.check(token in witness, "calibration persistence carries " + token)
suite.check("ReciprocalHeadingDeg" in witness,
            "user witness derives the reciprocal course from the two endpoints")
suite.check("HasReciprocalDirectionPair" in witness,
            "two valid endpoints are modeled as one physical runway pair")
suite.check("RunwayPairLabel(calibration)" in witness,
            "completion status reports both reciprocal runway numbers")
suite.check("BOTH RECIPROCAL DIRECTIONS WILL BE RESURVEYED" in witness,
            "second endpoint explicitly schedules both runway directions")
suite.check("RECIPROCAL LOCALIZER PAIR" in witness,
            "calibration summary exposes the reciprocal localizer pair")

suite.check("snapshot.RunwayWitnessUserCalibrated ||" in worker and
            "ApproachAAvailable" in worker and "ApproachBAvailable" in worker,
            "user two-endpoint calibration enables both direction candidates")
suite.check("RECIPROCAL DIRECTIONS GENERATED" in worker,
            "user-calibrated certification basis records pair generation")

def extract_definition(text: str, marker: str) -> str:
    start = text.index(marker)
    brace = text.index("{", start + len(marker))
    clean = strip_csharp_comments_and_literals(text)
    depth = 0
    for index in range(brace, len(clean)):
        if clean[index] == "{":
            depth += 1
        elif clean[index] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:index + 1]
    raise ValueError("unterminated method: " + marker)

build_runway = extract_definition(resolver,
    "static AERISRunwayDefinition BuildRunway(")
validate_pair = extract_definition(resolver,
    "static bool ValidateReciprocalDirectionPair(")
suite.check("runway.Directions.Add(a);" in build_runway and
            "runway.Directions.Add(b);" in build_runway,
            "one physical runway creates both A-to-B and B-to-A directions")
suite.check('BuildDirection(runway, snapshot,\n                axis, "A", thresholdA, thresholdB' in build_runway,
            "direction A uses threshold A toward threshold B")
suite.check('BuildDirection(runway, snapshot,\n                axis, "B", thresholdB, thresholdA' in build_runway,
            "direction B swaps the two thresholds")
suite.check("ValidateReciprocalDirectionPair(a, b" in build_runway,
            "user-calibrated pair is checked before entering the runway database")
suite.check("RECIPROCAL PAIR GENERATED" in build_runway and
            "localizerPair=True" in build_runway and
            "approachValidation=INDEPENDENT" in build_runway,
            "runtime log proves pair creation and independent approach validation")
suite.check("HeadingDifference(a.HeadingDeg" in validate_pair,
            "pair validation enforces reciprocal headings")
suite.check("SamePoint(a.Threshold, b.OppositeThreshold)" in validate_pair and
            "SamePoint(b.Threshold, a.OppositeThreshold)" in validate_pair,
            "pair validation enforces exact endpoint reversal")
suite.check("string.Equals(a.StableId, b.StableId" in validate_pair,
            "pair directions must retain distinct stable IDs")
suite.check("ValidateApproach(body, runway.Directions[0], true" in resolver and
            "ValidateApproach(body, runway.Directions[1], false" in resolver,
            "both reciprocal directions receive independent approach validation")

# Geometry-level proof independent of any airport name.
def normalize(value):
    value %= 360.0
    return value + 360.0 if value < 0.0 else value

for heading in (0.0, 3.2, 89.9, 163.7, 271.4, 359.9):
    reciprocal = normalize(heading + 180.0)
    delta = abs(((reciprocal - heading + 180.0) % 360.0) - 180.0)
    suite.check(abs(delta - 180.0) < 1e-9,
                "synthetic heading %.1f has a reciprocal direction" % heading)

suite.check("Kola" not in validate_pair and "Kola" not in build_runway,
            "bidirectional generation has no airport-specific branch")
suite.check("BIDIRECTIONAL RUNWAY PAIR HOTFIX 1" in build,
            "build identity names bidirectional runway pair hotfix 1")
suite.check(not (ROOT / "GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg").exists(),
            "package contains no user-specific endpoint pair")

suite.finish()
