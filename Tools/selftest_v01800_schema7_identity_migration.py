#!/usr/bin/env python3
"""Regression model for provider UUID alias compaction and stable cache identity."""
from __future__ import annotations

import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, CheckSuite, read


def canonical(body: str, uuid: str, site: str, path: str, model: str) -> str:
    body = (body or "").strip()
    site = (site or "").strip()
    path = (path or "").strip().replace("\\", "/")
    model = (model or "").strip()
    stable = bool(site or path or model)
    uuid_fallback = "" if stable else (uuid or "").strip()
    if not any((body, uuid_fallback, site, path, model)):
        return ""
    return "\n".join((body, uuid_fallback, site, path, model))


suite = CheckSuite("v0.18.0.0 failure-hint identity compaction regression")

path = "KSCExtended/TSC/UniversalSpawnPoint.cfg"
old_09 = [canonical("Kerbin", value, "TSC Runway 09", path,
                    "UniversalSpawnPoint") for value in
          ("uuid-old", "uuid-mid", "uuid-new")]
old_27 = [canonical("Kerbin", value, "TSC Runway 27", path,
                    "UniversalSpawnPoint") for value in
          ("uuid-old-27", "uuid-mid-27", "uuid-new-27")]
suite.equal(len(set(old_09)), 1,
            "volatile TSC 09 UUID aliases collapse to one stable identity")
suite.equal(len(set(old_27)), 1,
            "volatile TSC 27 UUID aliases collapse to one stable identity")
suite.check(old_09[0] != old_27[0],
            "opposite TSC runway records remain independently identified")
suite.equal(len(set(old_09 + old_27)), 2,
            "six historical TSC UUID records compact to two failure hints")
suite.check("uuid-new" not in old_09[0],
            "stable provider identity excludes runtime UUID")
suite.check(canonical("Kerbin", "uuid-a", "", "", "") !=
            canonical("Kerbin", "uuid-b", "", "", ""),
            "UUID remains a fallback when no stable provider fields exist")
suite.check(canonical("Kerbin", "", "Site", "path-a", "Model") !=
            canonical("Kerbin", "", "Site", "path-b", "Model"),
            "source path prevents same-site/model collisions")
suite.equal(canonical(" Kerbin ", "ignored", " TSC Runway 09 ",
                      "KSCExtended\\TSC\\UniversalSpawnPoint.cfg ",
                      " UniversalSpawnPoint "), old_09[0],
            "identity normalization trims values and canonicalizes path separators")

helper = read(SOURCE / "Landing" / "AERISProviderIdentity.cs")
cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
builder = read(SOURCE / "Landing" / "AERISRunwaySnapshotBuilder.cs")
contracts = read(SOURCE / "Landing" / "AERISRunwaySurveyContracts.cs")
csproj = read(SOURCE / "AERISFlightControl.csproj")

for token in ("internal static class AERISProviderIdentity",
              "hasStableProviderFields", "uuidFallback",
              "normalizedPath", "Replace('\\\\', '/')"):
    suite.check(token in helper, "shared stable identity contract: " + token)
suite.check('Landing\\AERISProviderIdentity.cs' in csproj,
            "shared provider identity helper is compiled")
suite.check("return AERISProviderIdentity.StableRecordId(record);" in registry,
            "registry failure paths use shared provider identity")
suite.check("return AERISProviderIdentity.StableRecordId(record);" in builder,
            "snapshot certified paths use shared provider identity")
for token in ("CurrentSchemaVersion = 8", "PreviousSchemaVersion = 7",
              "CompatibilitySchemaVersion = 6", "LegacySchemaVersion = 4",
              "canonical identity migration compacted",
              "certifiedAliasesCompacted", "failureAliasesCompacted",
              "PreferCandidate(existing, failure)",
              "AERISProviderIdentity.ComposeStableRecordId(",
              "NormalizeCachedStableIds"):
    suite.check(token in cache, "schema-7 compaction contract: " + token)
suite.check("CurrentAlgorithmVersion = 1710" in contracts,
            "identity migration tracks the current validated survey algorithm")
suite.finish()
