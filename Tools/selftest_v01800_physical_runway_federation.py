#!/usr/bin/env python3
from __future__ import annotations
import math
import re
import sys
sys.dont_write_bytecode = True
from dataclasses import dataclass
from pathlib import Path
from typing import List
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.18.0.0 physical runway federation")
physical = read(SOURCE / "Landing" / "AERISPhysicalRunwayIdentity.cs")
provider = read(SOURCE / "Landing" / "AERISAirfieldProviders.cs")
identity = read(SOURCE / "Landing" / "AERISProviderIdentity.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
catalog = read(SOURCE / "Landing" / "AERISRunwaySurveyCatalog.cs")
csproj = read(SOURCE / "AERISFlightControl.csproj")

for token in (
    "AERISPhysicalRunwayIdentity.Canonicalize",
    "PhysicalRunwayId",
    "ProviderAliases",
    "Complete-link clustering",
    "RunwayNumberHintsCompatible",
    "CompareClustersForIdentity",
    "ResolveClusterRunwayPair",
    "StableClusterDisambiguator",
    "Stable source-authored metadata",
    "MergeRuntimeGeometry",
    "ResolveBodyRadiusMeters",
    "[PHYSICAL_RUNWAY]",
):
    suite.check(token in (physical + provider + registry), "federation contract: " + token)
suite.check("Union(parent" not in physical and "Find(parent" not in physical,
            "transitive union-find bridge merging is absent")
suite.check("|MEMBERS=" not in physical,
            "physical ID never hashes the mutable alias-member set")
suite.check("|PAIR=" in physical and "CanonicalRunwayPairToken" in physical,
            "reciprocal runway pair participates in stable physical identity")
for token in ("CompactPhysicalAliases", "CurrentSchemaVersion = 8",
              "PreviousSchemaVersion = 7", "PHYSICAL_RUNWAY"):
    suite.check(token in cache, "physical cache migration contract: " + token)
suite.check("MatchPhysical" in catalog, "survey catalog resolves provider aliases")
suite.check("Landing\\AERISPhysicalRunwayIdentity.cs" in csproj,
            "physical federation source is compiled")
suite.check("ComposePhysicalRunwayId" in identity and
            "LegacyStableRecordId" in identity,
            "physical and legacy identities coexist during migration")
suite.check("PhysicalRunwayStatus = stagedRegistry.PhysicalRunwayStatus" in registry,
            "physical federation status survives atomic commit")
suite.check("DISC_PHYSICAL_" in registry,
            "discovered database uses one physical-runway airfield identity")

@dataclass(frozen=True)
class R:
    body: str
    name: str
    lat: float
    lon: float
    heading: float
    length: float = 0.0
    has_pos: bool = True
    model: str = ""
    group: str = ""


def tokens(value: str) -> List[str]:
    return re.findall(r"[A-Z0-9]+", value.upper())


def hints(value: str) -> List[int]:
    ts = tokens(value)
    if not any(t in ("RUNWAY", "RWY") for t in ts):
        return []
    return sorted({int(t) for t in ts if t.isdigit() and 1 <= int(t) <= 36})


def site(value: str) -> str:
    ts = tokens(value)
    runway_context = any(t in ("RUNWAY", "RWY") for t in ts)
    out=[]
    for t in ts:
        if t in ("RUNWAY","RWY","AIRFIELD","AIRPORT","LAUNCHSITE"):
            continue
        if runway_context and t.isdigit() and 1 <= int(t) <= 36:
            continue
        out.append(t)
    return "".join(out)


def axis_delta(a: float, b: float) -> float:
    aa=a%180.0; bb=b%180.0
    d=abs(aa-bb)
    return 180.0-d if d>90.0 else d


def distance(a: R,b: R) -> float:
    radius=600000.0
    mean=math.radians((a.lat+b.lat)*0.5)
    north=math.radians(b.lat-a.lat)*radius
    dl=b.lon-a.lon
    while dl>180: dl-=360
    while dl<-180: dl+=360
    east=math.radians(dl)*radius*math.cos(mean)
    return math.hypot(north,east)


def hint_compatible(a: R,b: R) -> bool:
    x=hints(a.name); y=hints(b.name)
    if not x or not y: return True
    return any(abs(i-j) in (0,18) for i in x for j in y)


def compatible(a: R,b: R) -> bool:
    if a.body.upper()!=b.body.upper() or site(a.name)!=site(b.name): return False
    if not hint_compatible(a,b): return False
    if axis_delta(a.heading,b.heading)>8.0: return False
    if a.has_pos and b.has_pos:
        known=max(a.length,b.length)
        allowed=min(3500.0,max(450.0,known*0.72+120.0)) if known>0 else 650.0
        return distance(a,b)<=allowed
    return bool(a.model and b.model and a.model.lower()==b.model.lower()) or \
           bool(a.group and b.group and a.group.lower()==b.group.lower())


def cluster_complete(records: List[R]) -> List[List[R]]:
    result: List[List[R]]=[]
    for record in sorted(records,key=lambda x:(x.body,site(x.name),x.name,x.lat,x.lon)):
        dest=None
        for c in result:
            if all(compatible(record,m) for m in c):
                dest=c; break
        if dest is None:
            dest=[]; result.append(dest)
        dest.append(record)
    return result

# Same physical strip from live PSystem and source-authored SLE aliases.
dull=[R("Kerbin","Dull Spot Runway",0.0,0.0,90,2500),
      R("Kerbin","Dull Spot",0.001,0.0,270,2500)]
suite.equal(len(cluster_complete(dull)),1,"Dull Spot aliases federate")

# Reciprocal endpoint names are aliases of one runway, while adjacent runway
# numbers are not silently collapsed.
tsc_recip=[R("Kerbin","TSC Runway 09",1,1,90,2500),
           R("Kerbin","TSC Runway 27",1.0002,1,270,2500)]
suite.equal(len(cluster_complete(tsc_recip)),1,"TSC reciprocal 09/27 federates")
tsc_parallel=[R("Kerbin","TSC Runway 09",1,1,90,2500),
              R("Kerbin","TSC Runway 10",1.0002,1,100,2500)]
suite.equal(len(cluster_complete(tsc_parallel)),2,
            "different runway-number pairs remain separate")

suite.equal(len(cluster_complete([
    R("Kerbin","Glacier Lake Runway",2,2,30,1800),
    R("Kerbin","Glacier Lake Long Runway",2.0001,2,30,3000)])),2,
    "similarly named but distinct strips remain separate")
suite.equal(len(cluster_complete([
    R("Kerbin","Dull Spot Runway",0,0,90,2500),
    R("Duna","Dull Spot Runway",0,0,90,2500)])),2,
    "body identity prevents cross-body federation")

# A-B and B-C fit, but A-C does not. Complete-link must not bridge all three.
bridge=[R("Kerbin","Bridge Runway",0.0000,0,90,0),
        R("Kerbin","Bridge Runway",0.0350,0,90,0),
        R("Kerbin","Bridge Runway",0.0700,0,90,0)]
# ~366 m per step and ~733 m end-to-end on Kerbin.
suite.equal(len(cluster_complete(bridge)),2,
            "complete-link clustering blocks transitive bridge over-merge")

# Stable identity model: normal physical ID depends on body/site/pair/axis, not
# on how many equivalent provider aliases happened to be visible in a scene.
def pair_token(number: int) -> str:
    reciprocal = number + 18 if number <= 18 else number - 18
    return f"{min(number, reciprocal):02d}-{max(number, reciprocal):02d}"

def base_identity(record: R) -> str:
    hs = hints(record.name)
    pair = "+".join(sorted({pair_token(v) for v in hs})) if hs else "NA"
    axis = int(round(record.heading % 180.0)) % 180
    return f"{record.body.upper()}|{site(record.name)}|PAIR={pair}|AXIS={axis:03d}"

base_single = base_identity(dull[0])
base_with_alias = base_identity(dull[1])
suite.equal(base_single, base_with_alias,
            "alias visibility does not change normal physical runway base identity")
suite.equal(pair_token(9), pair_token(27),
            "reciprocal runway numbers map to one canonical pair token")
suite.check(pair_token(9) != pair_token(10),
            "adjacent runway numbers retain distinct canonical pair tokens")

suite.finish()
