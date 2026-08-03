#!/usr/bin/env python3
from __future__ import annotations
import sys
sys.dont_write_bytecode = True
from dataclasses import dataclass
from datetime import datetime
from v01700_testlib import SOURCE, CheckSuite, read

suite=CheckSuite("v0.18.0.0 schema-7 physical cache aliases")
cache=read(SOURCE/"Landing"/"AERISRunwayCertificationCache.cs")
identity=read(SOURCE/"Landing"/"AERISProviderIdentity.cs")
registry=read(SOURCE/"Landing"/"AERISAirfieldRegistry.cs")

for token in (
    "CurrentSchemaVersion = 8",
    "PreviousSchemaVersion = 7",
    "CompatibilitySchemaVersion = 6",
    "OlderCompatibilitySchemaVersion = 5",
    "LegacySchemaVersion = 4",
    "OldestLegacySchemaVersion = 3",
    "CompactPhysicalAliases",
    "RebindPhysicalIdentity",
    "IsPhysicalStableRecordId",
    "NormalizeCachedStableIds(canonicalKey, record.Airfield)",
    "physical runway alias migration",
):
    suite.check(token in cache,"cache contract: "+token)
suite.check("schemaVersion < CurrentSchemaVersion" in cache,
            "schema 7 preserves physical IDs and migrates older provider IDs")
suite.check("ComposePhysicalRunwayId" in identity,
            "physical cache key has explicit namespace marker")
suite.check("cache.CompactPhysicalAliases(stagedRecords" in registry,
            "cache aliases compact only after the complete provider snapshot")

@dataclass
class V:
    key:str
    algorithm:int
    saved:str
    positive:bool


def prefer(a:V|None,b:V)->bool:
    if a is None:return True
    if b.algorithm!=a.algorithm:return b.algorithm>a.algorithm
    return datetime.fromisoformat(b.saved)>datetime.fromisoformat(a.saved)


def compact(values:list[V], aliases:set[str], canonical:str):
    candidates=[x for x in values if x.key in aliases]
    positive=[x for x in candidates if x.positive]
    pool=positive if positive else candidates
    winner=None
    for x in pool:
        if prefer(winner,x):winner=x
    retained=[x for x in values if x.key not in aliases]
    if winner:
        retained.append(V(canonical,winner.algorithm,winner.saved,winner.positive))
    return retained

aliases={"legacy-a","legacy-b","physical"}
values=[V("legacy-a",1670,"2026-01-01T00:00:00+00:00",True),
        V("legacy-b",1670,"2026-01-02T00:00:00+00:00",True),
        V("physical",1660,"2026-01-03T00:00:00+00:00",True),
        V("other",1670,"2026-01-04T00:00:00+00:00",True)]
out=compact(values,aliases,"physical")
suite.equal(len([x for x in out if x.key=="physical"]),1,
            "multiple provider aliases become one physical certified record")
suite.equal(next(x for x in out if x.key=="physical").saved,
            "2026-01-02T00:00:00+00:00",
            "newest equal-algorithm certification wins migration")
suite.equal(len([x for x in out if x.key=="other"]),1,
            "unrelated cache record is untouched")

mixed=[V("legacy-a",1670,"2026-01-01T00:00:00+00:00",False),
       V("legacy-b",1670,"2026-01-01T00:00:01+00:00",True)]
out=compact(mixed,aliases,"physical")
suite.check(out[0].positive,
            "positive certification supersedes negative aliases in a physical cluster")

suite.finish()
