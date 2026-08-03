#!/usr/bin/env python3
"""Regression model for strict sub-metre provider-frame cache compatibility."""
from __future__ import annotations

import math
import sys
sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


def horizontal(lat_a: float, lon_a: float, lat_b: float, lon_b: float,
               radius: float = 600000.0) -> float:
    radians = math.pi / 180.0
    mean = (lat_a + lat_b) * 0.5 * radians
    north = (lat_b - lat_a) * radians * radius
    delta = lon_b - lon_a
    while delta > 180.0:
        delta -= 360.0
    while delta < -180.0:
        delta += 360.0
    east = delta * radians * radius * math.cos(mean)
    return math.hypot(north, east)


def compatible(source_a: str, source_b: str, points_a: int, points_b: int,
               primitives_a: int, primitives_b: int, collider_a: bool,
               collider_b: bool, lat_a: float, lon_a: float, elev_a: float,
               heading_a: float, scale_a: float, lat_b: float, lon_b: float,
               elev_b: float, heading_b: float, scale_b: float) -> bool:
    heading = abs(heading_a - heading_b) % 360.0
    heading = 360.0 - heading if heading > 180.0 else heading
    return (
        source_a.lower() == source_b.lower()
        and points_a == points_b
        and primitives_a == primitives_b
        and collider_a == collider_b
        and horizontal(lat_a, lon_a, lat_b, lon_b) <= 0.50
        and abs(elev_a - elev_b) <= 0.10
        and heading <= 0.02
        and abs(scale_a - scale_b) <= 0.0005
    )


suite = CheckSuite("v0.17.0.7 sub-metre provider-frame cache regression")

# Exact field evidence from the v0.17.0.6 flight-scene regeneration.
dull_a = (63.803240814807836, -172.67325367587458, 422.80746752338018)
dull_b = (63.80323984199557, -172.67326151866419, 422.78428790799808)
mahi_a = (-49.766594932351708, -120.82684109550206, 63.205645062727854)
mahi_b = (-49.766596330778135, -120.82683972219067, 63.185458638705313)

suite.check(horizontal(dull_a[0], dull_a[1], dull_b[0], dull_b[1]) < 0.50,
            "Dull Spot provider-frame rebuild is inside the 0.50 m gate")
suite.check(horizontal(mahi_a[0], mahi_a[1], mahi_b[0], mahi_b[1]) < 0.50,
            "Mahi provider-frame rebuild is inside the 0.50 m gate")
suite.check(compatible("source", "SOURCE", 12123, 12123, 15, 15, True, True,
                       *dull_a, 0.0, 1.0, *dull_b, 0.0, 1.0),
            "Dull Spot reuses cache only under identical source/geometry contracts")
suite.check(compatible("source", "source", 8531, 8531, 15, 15, True, True,
                       *mahi_a, 0.0, 1.0, *mahi_b, 0.0, 1.0),
            "Mahi reuses cache only under identical source/geometry contracts")
suite.check(not compatible("source-a", "source-b", 12123, 12123, 15, 15,
                           True, True, *dull_a, 0.0, 1.0, *dull_b, 0.0, 1.0),
            "source/model/config change always rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12124, 15, 15,
                           True, True, *dull_a, 0.0, 1.0, *dull_b, 0.0, 1.0),
            "point-count change rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 16,
                           True, True, *dull_a, 0.0, 1.0, *dull_b, 0.0, 1.0),
            "primitive-count change rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 15,
                           True, False, *dull_a, 0.0, 1.0, *dull_b, 0.0, 1.0),
            "collider-readability change rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 15,
                           True, True, *dull_a, 0.0, 1.0,
                           dull_b[0] + 0.00010, dull_b[1], dull_b[2], 0.0, 1.0),
            "material horizontal relocation rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 15,
                           True, True, *dull_a, 0.0, 1.0,
                           dull_b[0], dull_b[1], dull_b[2] + 0.14, 0.0, 1.0),
            "vertical relocation above 0.10 m rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 15,
                           True, True, *dull_a, 0.0, 1.0,
                           dull_b[0], dull_b[1], dull_b[2], 0.021, 1.0),
            "heading change above 0.02 degrees rejects compatibility")
suite.check(not compatible("source", "source", 12123, 12123, 15, 15,
                           True, True, *dull_a, 0.0, 1.0,
                           dull_b[0], dull_b[1], dull_b[2], 0.0, 1.0006),
            "model-scale change rejects compatibility")

contracts = read(SOURCE / "Landing" / "AERISRunwaySurveyContracts.cs")
builder = read(SOURCE / "Landing" / "AERISRunwaySnapshotBuilder.cs")
cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")

suite.check("CurrentAlgorithmVersion = 1670" in contracts,
            "compatibility contract bumps the survey algorithm version")
for token in ("SourceFingerprint", "string sourceFingerprint, string inputFingerprint"):
    suite.check(token in contracts, "snapshot carries strict source identity: " + token)
for token in ("BuildSourceFingerprint(record", "AppendSurveyDefinition(builder, definition)",
              "AppendCanonicalPlacement(builder, record, frame)"):
    suite.check(token in builder, "source/full fingerprint split: " + token)
for token in ("CurrentSchemaVersion = 6", "PreviousSchemaVersion = 5",
              "TryGetSubMetreCompatible", "MaximumHorizontalMeters = 0.50",
              "MaximumVerticalMeters = 0.10", "MaximumHeadingDegrees = 0.02",
              "MaximumScaleDelta = 0.0005", "HorizontalDistanceMeters",
              "GEOMETRY SHAPE/COUNT CHANGED", "sourceFingerprint"):
    suite.check(token in cache, "strict compatibility cache contract: " + token)
for token in ("sub-metre compatible hit", "TryGetSubMetreCompatible(snapshot, record",
              "CERTIFIED CACHE HIT — SUB-METRE PROVIDER FRAME COMPATIBILITY"):
    suite.check(token in registry, "registry compatibility commit contract: " + token)

suite.finish()
