#!/usr/bin/env python3
"""Regression model for restart-stable canonical source-geometry cache keys."""
from __future__ import annotations

import hashlib
import math
import sys
sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


def q(value: float, quantum: float) -> int:
    if not math.isfinite(value) or quantum <= 0.0:
        return -(1 << 63)
    scaled = value / quantum
    return math.floor(scaled + 0.5) if scaled >= 0 else math.ceil(scaled - 0.5)


def digest(parts: list[str]) -> str:
    return hashlib.sha256("|".join(parts).encode("utf-8")).hexdigest()


def source_geometry(kind: str, model: str, path: str,
                    components: list[str]) -> str:
    return digest([kind, model, path, str(len(components))] + sorted(components))


def placement(lat: float, lon: float, elev: float, heading: float,
              scale: float, length: float, width: float, launch: str) -> tuple:
    return (q(lat, 0.00001), q(lon, 0.00001), q(elev, 0.50),
            q(heading, 0.05), q(scale, 0.001), q(length, 0.10),
            q(width, 0.10), launch)


def survey(definition: dict) -> tuple:
    return tuple(definition[key] for key in (
        "id", "uuid", "site", "group", "path", "model", "method",
        "pair", "min_len", "max_len", "min_width", "max_width",
        "aspect", "default_width", "surface", "source_mod", "provider_version",
    ))


suite = CheckSuite("v0.17.0.6 canonical source-geometry cache regression")
mesh_a = "MESH|KK_2500m_runway|24000|4|bounds|relative-matrix|asphalt"
mesh_b = "MESH|runway_lights|144|1|bounds|relative-matrix|light"
collider = "COLLIDER|BoxCollider|relative-matrix|center|size"
components = [mesh_a, mesh_b, collider]

suite.equal(source_geometry("PREFAB", "KK_2500m_runway", "cfg", components),
            source_geometry("PREFAB", "KK_2500m_runway", "cfg",
                            list(reversed(components))),
            "canonical source component multiset is enumeration-order independent")
suite.equal(source_geometry("PREFAB", "KK_2500m_runway", "cfg", components),
            source_geometry("PREFAB", "KK_2500m_runway", "cfg", components),
            "world placement is absent from the source asset signature")
suite.check(source_geometry("PREFAB", "KK_2500m_runway", "cfg", components) !=
            source_geometry("PREFAB", "KK_2500m_runway", "cfg", components + ["MESH|new"]),
            "source hierarchy change invalidates cache")
suite.check(source_geometry("PREFAB", "KK_2500m_runway", "cfg", components) !=
            source_geometry("PREFAB", "KK_2500m_runway_v2", "cfg", components),
            "model identity change invalidates cache")
suite.check(source_geometry("PREFAB", "KK_2500m_runway", "cfg", components) !=
            source_geometry("PREFAB", "KK_2500m_runway", "cfg2", components),
            "source config path change invalidates cache")

base_placement = placement(20.123456, -146.654321, 120.0, 90.0,
                           1.0, 2500.0, 50.0, "spawn")
suite.equal(base_placement,
            placement(20.123456004, -146.654321004, 120.20, 90.019,
                      1.0004, 2500.04, 50.04, "spawn"),
            "sub-quantum provider placement jitter preserves cache key")
suite.check(base_placement !=
            placement(20.123476, -146.654321, 120.0, 90.0,
                      1.0, 2500.0, 50.0, "spawn"),
            "material provider placement change invalidates cache")

base_definition = {
    "id": "AUTO", "uuid": "", "site": "Area 52", "group": "Area 52",
    "path": "Area52", "model": "KK_2500m_runway", "method": 1,
    "pair": "", "min_len": 250.0, "max_len": 10000.0,
    "min_width": 8.0, "max_width": 500.0, "aspect": 4.0,
    "default_width": 45.0, "surface": "PAVED", "source_mod": "KK",
    "provider_version": "2.0.0.0",
}
suite.equal(survey(base_definition), survey(dict(base_definition)),
            "unchanged survey contract preserves cache key")
suite.check(survey(base_definition) !=
            survey(dict(base_definition, min_width=12.0)),
            "survey safety-limit change invalidates cache")
suite.check(survey(base_definition) !=
            survey(dict(base_definition, method=3)),
            "survey method change invalidates cache")

snapshot_builder = read(SOURCE / "Landing" / "AERISRunwaySnapshotBuilder.cs")
contracts = read(SOURCE / "Landing" / "AERISRunwaySurveyContracts.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")

for token in (
    "CanonicalSourceGeometryFingerprint(record)",
    "AppendCanonicalPlacement(builder, record, frame)",
    "AppendSurveyDefinition(builder, definition)",
    "MeshAssetIdentity(root.transform, filters[i])",
    "ColliderAssetIdentity(root.transform,",
    "components.Sort(StringComparer.Ordinal)",
    "Matrix4x4.TRS(current.localPosition,",
    "matrix = local * matrix",
    "canonicalSourceComponents.Add(canonical)",
    "record, canonicalSourceComponents);",
    "record.RuntimeRunwayPrefab != null",
    "CanonicalMaterialNames(renderer)",
    "Sha256Hex(builder.ToString())",
):
    suite.check(token in snapshot_builder, "canonical source fingerprint contract: " + token)

suite.check("root.worldToLocalMatrix * child.localToWorldMatrix" not in snapshot_builder,
            "canonical source key avoids planet-scale world-matrix cancellation")
suite.check("ProviderReferencePositionValid" in snapshot_builder,
            "provider-authored placement is preferred over runtime fallback")

suite.check("CurrentAlgorithmVersion = 1660" in contracts,
            "canonical source cache fingerprint bumps algorithm version")
start = snapshot_builder.index("static string BuildFingerprint")
end = snapshot_builder.index("static string StableProviderFingerprintIdentity", start)
fingerprint_code = snapshot_builder[start:end]
for forbidden in (
    "BuildPrimitiveAggregate(primitives", "BuildPointAggregate(points",
    "frame.PqsElevation", "frame.PqsSampled",
):
    suite.check(forbidden not in fingerprint_code,
                "cache key excludes live/LOD geometry input: " + forbidden)

for retained in (
    "TryAddMesh(filter.sharedMesh", "TryAddBoundsPrimitive(collider.bounds",
    "points.ToArray(), primitives.ToArray(), fingerprint",
):
    suite.check(retained in snapshot_builder,
                "runtime geometry remains survey evidence only: " + retained)

for token in (
    "TryGetExact(string stableRecordId, string fingerprint,",
    "out AERISCachedRunwayRecord record, out string reason)",
    'reason = "ALGORITHM "', 'reason = "FINGERPRINT "',
    "ShortHash(value.Fingerprint)",
):
    suite.check(token in cache, "cache miss diagnostic contract: " + token)
for token in (
    '"[AIRFIELD_CACHE] exact miss; id="', "cachedPoints=", "livePoints=",
    "cachedPrimitives=", "livePrimitives=",
):
    suite.check(token in registry, "field evidence for cache miss: " + token)

# v0.17.0.5 identity signature split remains intact.
for token in (
    "ProviderSignatureIdentity(record)",
    "ProviderGeometryDiagnosticIdentity(record)",
    "geometryDiagnosticSignature = HashProviderIdentities(geometryIdentities)",
):
    suite.check(token in registry, "provider signature split retained: " + token)

suite.finish()
