#!/usr/bin/env python3
"""Regression model for restart-stable provider identity and diagnostic geometry signatures."""
from __future__ import annotations

import math
import sys
sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


def q(value: float, quantum: float) -> str:
    if not math.isfinite(value) or quantum <= 0.0:
        return "NA"
    scaled = value / quantum
    rounded = math.floor(scaled + 0.5) if scaled >= 0 else math.ceil(scaled - 0.5)
    return str(rounded)


def identity(record: dict) -> str:
    stable = bool(record.get("site") or record.get("path") or record.get("model"))
    uuid_fallback = "" if stable else record.get("uuid", "")
    return "|".join((
        record.get("body", ""), record.get("source", ""), record.get("kind", ""),
        record.get("site", ""), record.get("group", ""), record.get("model", ""),
        record.get("path", ""), uuid_fallback,
    ))


def geometry(record: dict) -> str:
    return "|".join((
        identity(record),
        q(record.get("lat", 0.0), 0.000001), q(record.get("lon", 0.0), 0.000001),
        q(record.get("elev", 0.0), 0.1), q(record.get("heading", 0.0), 0.01),
        q(record.get("length", 0.0), 0.1), q(record.get("width", 0.0), 0.1),
        q(record.get("scale", 1.0), 0.001),
    ))




def qint(value: float, quantum: float) -> int:
    if not math.isfinite(value) or quantum <= 0.0:
        return -(1 << 63)
    scaled = value / quantum
    return math.floor(scaled + 0.5) if scaled >= 0 else math.ceil(scaled - 0.5)


def hash_long(hash_value: int, value: int) -> int:
    bits = value & 0xFFFFFFFFFFFFFFFF
    for _ in range(8):
        hash_value ^= bits & 0xFF
        hash_value = (hash_value * 1099511628211) & 0xFFFFFFFFFFFFFFFF
        bits >>= 8
    return hash_value


def point_hash(point: tuple[float, float, float, float, int, int]) -> int:
    value = 1469598103934665603
    for number, quantum in zip(point[:4], (0.05, 0.05, 0.05, 0.01)):
        value = hash_long(value, qint(number, quantum))
    value = hash_long(value, point[4])
    value = hash_long(value, point[5])
    return value


def aggregate(points: list[tuple[float, float, float, float, int, int]]) -> tuple[int, int, int]:
    xor = total = mix = 0
    for point in points:
        value = point_hash(point)
        xor ^= value
        total = (total + value) & 0xFFFFFFFFFFFFFFFF
        mix = (mix + value * (value | 1)) & 0xFFFFFFFFFFFFFFFF
    return xor, total, mix

suite = CheckSuite("v0.17.0.5 provider identity / geometry signature split regression")
base = {
    "body": "Kerbin", "source": "KerbalKonstructs", "kind": "Runway",
    "site": "Area 52 Long runway", "group": "Area 52", "model": "KK_2500m_runway",
    "path": "KerbinSideRemastered/Statics/ExampleBases/Area52/KK_2500m_runway.cfg",
    "lat": 20.123456789, "lon": -146.987654321, "elev": 133.125,
    "heading": 89.9998, "length": 2500.0, "width": 50.0, "scale": 1.0,
    "uuid": "runtime-a",
}

suite.equal(identity(base), identity(dict(base, uuid="runtime-b")),
            "stable provider identity ignores volatile runtime UUID")
suite.equal(identity(base), identity(dict(base, lat=base["lat"] + 0.01,
                                          elev=base["elev"] + 5.0,
                                          heading=base["heading"] + 0.25)),
            "restart-stable identity excludes transform-derived geometry")
suite.check(geometry(base) != geometry(dict(base, lat=base["lat"] + 0.01)),
            "diagnostic geometry signature still reports meaningful position changes")
suite.check(identity(base) != identity(dict(base, path=base["path"] + ".new")),
            "identity signature changes for stable source identity changes")

fallback = dict(base, site="", group="", model="", path="", uuid="runtime-a")
suite.check(identity(fallback) != identity(dict(fallback, uuid="runtime-b")),
            "UUID remains the fallback only when no stable provider identity exists")

points = [
    (1.001, 2.001, 0.001, 1.0, 1, 2),
    (-4.999, 8.001, 0.049, 1.5, 4, 8),
    (12.499, -1.001, 0.101, 1.0, 2, 16),
]
suite.equal(aggregate(points), aggregate(list(reversed(points))),
            "canonical point fingerprint is independent of Unity component order")
jittered = [(e + 0.004, n - 0.004, u + 0.004, w, semantic, method)
            for e, n, u, w, semantic, method in points]
suite.equal(aggregate(points), aggregate(jittered),
            "sub-quantum transform jitter preserves the cache fingerprint")
changed = list(points)
changed[0] = (changed[0][0] + 0.10,) + changed[0][1:]
suite.check(aggregate(points) != aggregate(changed),
            "material ten-centimetre geometry change invalidates the cache fingerprint")

registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
for token in (
    "ProviderSignatureIdentity(record)",
    "ProviderGeometryDiagnosticIdentity(record)",
    "HashProviderIdentities(identities)",
    "geometryDiagnosticSignature = HashProviderIdentities(geometryIdentities)",
    '"; geometrySignature=" +',
    "excludes runtime geometry",
):
    suite.check(token in registry, "provider signature split contract: " + token)

contracts = read(SOURCE / "Landing" / "AERISRunwaySurveyContracts.cs")
snapshot_builder = read(SOURCE / "Landing" / "AERISRunwaySnapshotBuilder.cs")
for token in (
    "StableProviderFingerprintIdentity(record)",
    "BuildPrimitiveAggregate(primitives",
    "BuildPointAggregate(points",
    "AccumulateFingerprint(hash",
    "QuantizedFingerprintValue(value, quantum)",
):
    suite.check(token in snapshot_builder,
                "canonical cache fingerprint contract: " + token)
suite.check("CurrentAlgorithmVersion = 1650" in contracts,
            "canonical cache fingerprint bumps the survey algorithm version")
for forbidden in (
    "Append(builder, record.ProviderUuid);",
    "Append(builder, value.SourceGroup);",
    "int pointStep = Math.Max(1, points.Count / 128)",
):
    suite.check(forbidden not in snapshot_builder,
                "cache fingerprint excludes volatile/order-sensitive input: " + forbidden)

identity_start = registry.index("static string ProviderSignatureIdentity")
identity_end = registry.index("static string ProviderGeometryDiagnosticIdentity")
identity_code = registry[identity_start:identity_end]
for forbidden in (
    "LatitudeDeg", "LongitudeDeg", "ElevationMeters", "OrientationHeadingDeg",
    "DeclaredLengthMeters", "DeclaredWidthMeters", "RuntimeModelScale",
    "QuantizedSignatureNumber",
):
    suite.check(forbidden not in identity_code,
                "identity signature excludes runtime geometry: " + forbidden)

suite.finish()
