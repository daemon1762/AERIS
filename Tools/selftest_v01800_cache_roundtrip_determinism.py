#!/usr/bin/env python3
"""Gate-0 regression for exact cache persistence and physical-runway IDs."""
from __future__ import annotations
import base64
import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, CheckSuite, read


def encode_id(value: str) -> str:
    return base64.b64encode(value.encode("utf-8")).decode("ascii")


def decode_id(value: str) -> str:
    return base64.b64decode(value.encode("ascii")).decode("utf-8")


suite = CheckSuite("v0.18.0.0 Gate-0 cache round-trip determinism")
cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
identity = read(SOURCE / "Landing" / "AERISProviderIdentity.cs")
physical = read(SOURCE / "Landing" / "AERISPhysicalRunwayIdentity.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")

physical_id = "Kerbin\nPHYSICAL_RUNWAY\nPRWY_0123456789ABCDEF"
suite.equal(decode_id(encode_id(physical_id)), physical_id,
            "base64 UTF-8 persistence preserves physical runway namespace and newlines")
suite.check("\nPHYSICAL_RUNWAY\n" in physical_id,
            "physical cache key has an explicit non-provider namespace")

for token in (
    "const int CurrentSchemaVersion = 8",
    "const int PreviousSchemaVersion = 7",
    "const int CompatibilitySchemaVersion = 6",
    "const int OlderCompatibilitySchemaVersion = 5",
    "const int LegacySchemaVersion = 4",
    'const string StableIdEncoding = "base64-utf8-v1"',
    "WriteStableRecordId(node, record.StableRecordId)",
    "WriteStableRecordId(node, value.StableRecordId)",
    "Convert.ToBase64String",
    "Convert.FromBase64String",
    "CompactPhysicalAliases",
    "RebindPhysicalIdentity",
    "NormalizeCachedStableIds(canonicalKey, record.Airfield)",
    "expected certified=",
    "actual certified=",
):
    suite.check(token in cache, "cache exact-persistence contract: " + token)

suite.check("IsPhysicalStableRecordId" in cache and
            "schemaVersion < CurrentSchemaVersion" in cache,
            "schema 7 preserves canonical physical IDs while older IDs migrate")
suite.check("ComposePhysicalRunwayId" in identity and
            "LegacyStableRecordId" in identity,
            "physical and legacy provider identities coexist only for migration")
suite.check("|MEMBERS=" not in physical,
            "physical identity never hashes the mutable provider-alias member set")
suite.check("|PAIR=" in physical and "StableClusterDisambiguator" in physical,
            "duplicate runway identity uses stable pair/location/source disambiguation")
suite.check("ProviderSignatureIdentity(record)" in registry and
            "record.PhysicalRunwayId" in registry,
            "provider signature publishes canonical physical identity")
suite.check("ProviderGeometryDiagnosticIdentity(record)" in registry and
            "geometryDiagnosticSignature" in registry,
            "runtime geometry remains a separate diagnostic signature")

suite.finish()
