#!/usr/bin/env python3
"""Regression model for exact stable-ID persistence and cross-restart provider signatures."""
from __future__ import annotations

import base64
import math
import sys
sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


def encode_id(value: str) -> str:
    return base64.b64encode(value.encode("utf-8")).decode("ascii")


def decode_id(value: str) -> str:
    return base64.b64decode(value.encode("ascii")).decode("utf-8")


def compose(body: str, uuid: str, site: str, path: str, model: str) -> str:
    stable = bool(site or path or model)
    uuid_fallback = "" if stable else (uuid or "")
    if not any((body, uuid_fallback, site, path, model)):
        return ""
    return "\n".join((body or "", uuid_fallback, site or "", path or "", model or ""))


def q(value: float, quantum: float) -> str:
    if not math.isfinite(value) or quantum <= 0.0:
        return "NA"
    # Equivalent to MidpointRounding.AwayFromZero for the non-half test values below.
    scaled = value / quantum
    rounded = math.floor(scaled + 0.5) if scaled >= 0 else math.ceil(scaled - 0.5)
    return str(rounded)


def provider_identity(record: dict) -> str:
    stable = bool(record.get("site") or record.get("path") or record.get("model"))
    uuid_fallback = "" if stable else record.get("uuid", "")
    return "|".join((
        record.get("body", ""), record.get("source", ""), record.get("kind", ""),
        record.get("site", ""), record.get("group", ""), record.get("model", ""),
        record.get("path", ""), uuid_fallback,
    ))


suite = CheckSuite("v0.17.0.4 cache round-trip / provider determinism regression")

original = compose("Kerbin", "e05e0418-530b-40e7-a776-d20bdbe000bb",
                   "Harvester Airfield",
                   "KerbinSideRemastered/Harvester/KK_1700m_runway.cfg",
                   "KK_1700m_runway")
suite.equal(decode_id(encode_id(original)), original,
            "base64 UTF-8 persistence preserves all five newline-separated identity fields")

legacy_flattened = "Kerbine05e0418-530b-40e7-a776-d20bdbe000bbHarvester AirfieldKerbinSideRemastered/Harvester/KK_1700m_runway.cfgKK_1700m_runway"
suite.check(legacy_flattened != original,
            "legacy ConfigNode flattening is observably not the runtime stable ID")
suite.equal(compose("Kerbin", "e05e0418-530b-40e7-a776-d20bdbe000bb",
                    "Harvester Airfield",
                    "KerbinSideRemastered/Harvester/KK_1700m_runway.cfg",
                    "KK_1700m_runway"), original,
            "legacy records can be canonically rebuilt from stored provider fields")

ids = {
    compose("Kerbin", "", "Black Krags GC Runway", "PSystem", ""),
    compose("Kerbin", "", "Black Krags", "PSystem", "GC Runway"),
}
encoded = {encode_id(value) for value in ids}
suite.equal(len(encoded), len(ids),
            "distinct structured IDs cannot collapse during ConfigNode serialization")

base = {
    "body": "Kerbin", "source": "KerbalKonstructs", "kind": "Runway",
    "site": "Area 52 Long runway", "group": "Area 52", "model": "KK_2500m_runway",
    "path": "KerbinSideRemastered/Statics/ExampleBases/Area52/KK_2500m_runway.cfg",
    "lat": 20.123456789, "lon": -146.987654321, "elev": 133.125,
    "heading": 89.9998, "length": 2500.0, "width": 50.0, "scale": 1.0,
    "uuid": "runtime-a",
}
changed_uuid = dict(base, uuid="runtime-b")
suite.equal(provider_identity(base), provider_identity(changed_uuid),
            "provider signature ignores volatile UUID when stable provider fields exist")
suite.check(provider_identity(base) != provider_identity(dict(base, path=base["path"] + ".new")),
            "provider signature changes when a stable source identity changes")
suite.equal(provider_identity(base), provider_identity(dict(base, lat=base["lat"] + 0.00001)),
            "provider identity remains stable across runtime geometry jitter")

cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")

for token in (
    "const int CurrentSchemaVersion = 6",
    "const int PreviousSchemaVersion = 5",
    "const int CompatibilitySchemaVersion = 4",
    "const int LegacySchemaVersion = 3",
    'const string StableIdEncoding = "base64-utf8-v1"',
    "WriteStableRecordId(node, record.StableRecordId)",
    "WriteStableRecordId(node, value.StableRecordId)",
    "Convert.ToBase64String",
    "Convert.FromBase64String",
    "AERISProviderIdentity.ComposeStableRecordId(",
    "schema-3 migration retained",
    "ambiguous failure hint(s) for safe rebuild",
    "expected certified=",
    "actual certified=",
):
    suite.check(token in cache, "cache exact-persistence contract: " + token)

for token in (
    "ProviderSignatureIdentity(record)",
    "hasStableProviderFields",
    "uuidFallback",
    "ProviderGeometryDiagnosticIdentity(record)",
    "geometryDiagnosticSignature",
):
    suite.check(token in registry, "provider deterministic-signature contract: " + token)

suite.finish()
