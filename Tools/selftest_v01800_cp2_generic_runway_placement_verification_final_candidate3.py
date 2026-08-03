#!/usr/bin/env python3
from __future__ import annotations
import math
import re
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals, extract_method

suite = CheckSuite("v0.18.0.0 CP2 generic runway placement verification final candidate 3")
source = ROOT / "Source/AERISFlightControl"
models = read(source / "Landing/AERISAirfieldModels.cs")
contracts = read(source / "Landing/AERISRunwaySurveyContracts.cs")
builder = read(source / "Landing/AERISRunwaySnapshotBuilder.cs")
worker = read(source / "Landing/AERISRunwayGeometryWorker.cs")
witness = read(source / "Landing/AERISRunwayWitnessLibrary.cs")
registry = read(source / "Landing/AERISAirfieldRegistry.cs")
window = read(source / "UI/AERISWindow.cs")
nd = read(source / "UI/AERISNavigationDisplay.cs")
settings = read(source / "Settings/AERISSettings.cs")
factory = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")
catalog = read(ROOT / "GameData/AERISFlightControl/Airfields/Defaults/02_Current_Mod_Runway_Survey_Catalog.cfg")
generated = read(source / "Properties/AERISBuildVersion.generated.cs")


def has(text: str, token: str, label: str) -> None:
    suite.check(token in text, label)

has(contracts, "CurrentAlgorithmVersion = 1710", "runway algorithm revision is 1710")
has(contracts, "CurrentRunwayDetectorRevision = 5", "runway detector revision is 5")
for token in ("RunwayUserCalibrationPresent", "RunwayUserCalibrationPending",
              "RunwayPlacementMismatchObserved", "RunwayPlacementObservationDetail"):
    has(contracts, token, "survey contract carries " + token)
has(models, "ObservedPlacementMismatch", "placement mismatch failure state exists")

has(witness, "const int CalibrationSchema = 3", "calibration schema is version 3 with schema 1/2 migration")
has(witness, "schema < 1", "schema 1 remains readable for migration")
has(witness, "schema > CalibrationSchema", "future calibration schema fails closed")
for token in ("placementMismatchObserved", "observedCrossTrackMeters",
              "observedAlongTrackMeters", "observedCorridorGateMeters",
              "placementObservationDetail"):
    has(witness, token, "calibration persistence carries " + token)
has(witness, "calibration.HasStart = false", "observed mismatch invalidates stale threshold A")
has(witness, "calibration.HasEnd = false", "observed mismatch invalidates stale threshold B")
has(witness, "RecordPlacementMismatch", "generic mismatch quarantine writer exists")
has(witness, "USER TWO-POINT CALIBRATION REQUIRED", "mismatch status requires two-point calibration")
has(witness, "CALIBRATION REQUIRES A VESSEL PARKED ON THE PHYSICAL RUNWAY",
    "threshold marking requires a parked runway vessel")
has(witness, "STOP THE VESSEL BEFORE MARKING A RUNWAY THRESHOLD",
    "threshold marking rejects a moving vessel")
has(witness, "RollbackCalibration(calibration, created, backup)",
    "failed calibration persistence rolls in-memory state back")
has(witness, "MatchesAirfieldIdentity", "calibration lookup uses centralized provider identity matching")
has(witness, "string.Equals(value.Body, airfield.Body",
    "fallback calibration identity is body-scoped")
has(witness, '"GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg"',
    "user calibration remains AERIS-owned PluginData")
suite.check(not (ROOT / "GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg").exists(),
            "package does not ship user-specific calibration data")

verify = extract_method(registry, "VerifyRunwayPlacement")
has(verify, "vessel.LandedOrSplashed", "placement check requires a parked ground vessel")
has(verify, "vessel.srfSpeed > 5.0", "placement check rejects a moving vessel")
has(verify, "corridorGate = Math.Max(width * 0.5 + 12.0", "corridor gate includes runway half-width margin")
has(verify, "centerlineUncertainty * 3.0 + 12.0", "corridor gate includes sanitized uncertainty margin")
has(verify, "along >= -endGate", "placement check validates the longitudinal window")
has(verify, "elevationError > elevationGate", "placement check validates runway elevation")
has(verify, "FiniteNumber(rawWidth)", "placement check sanitizes non-finite runway width")
has(verify, "FiniteNumber(bodyRadius)", "placement check sanitizes non-finite body radius")
has(verify, "RecordPlacementMismatch", "generic placement mismatch is persisted")
has(verify, "RequestManualReload", "mismatch quarantine triggers resurvey")
has(verify, "[RUNWAY_PLACEMENT_VERIFY]", "placement verification is logged")
suite.check("Kola" not in verify and "98523c92" not in verify,
            "generic verification method contains no Kola-specific branch")

has(worker, "if (snapshot.RunwayUserCalibrationPending)",
    "pending/mismatch calibration fails closed before geometry certification")
has(worker, "OBSERVED RUNWAY PLACEMENT MISMATCH", "worker explains observed mismatch quarantine")
has(worker, "snapshot.SurveyMethod == AERISRunwaySurveyMethod.ManualRequired",
    "catalog manual-required runway remains fail closed")

kola_block = re.search(r"RunwaySurvey\s*\{[^{}]*providerSiteId\s*=\s*Kola Island[^{}]*\}", catalog, re.S)
suite.check(kola_block is not None, "Kola catalog record exists")
if kola_block:
    suite.check("method = ManualRequired" in kola_block.group(0),
                "known Kola offset is routed to manual calibration")

has(window, "CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY",
    "AIRFIELDS UI exposes generic CHECK HERE action")
has(window, "MARK A", "AIRFIELDS UI retains threshold A calibration")
has(window, "MARK B", "AIRFIELDS UI retains threshold B calibration")
has(window, "CLEAR", "AIRFIELDS UI can clear a calibration/quarantine")
suite.check(
    "ResolveNavigationDirectionPair" in nd and
    "candidate.HasCertifiedGeometry" in nd and
    "registry.EffectiveState(candidate)" in nd and
    "AERISRunwayCertificationState.Certified" in nd and
    "bool provisionalRunway = false;" in nd,
    "ND only receives certified non-provisional runway geometry")

for token in ("Cp2" + "RunwaySurveyDebugVisible",
              "cp2" + "RunwaySurveyDebugVisible",
              "Cp2" + "SurveyDebugDetail",
              "RUNWAY_" + "DEBUG_CANDIDATE",
              "CP2_" + "RUNWAY_DEBUG",
              "Cp2" + "DebugAirfields",
              "cp2" + "DebugAirfields",
              "ResolveCp2" + "DebugRunways",
              "CP2" + "DBG_"):
    combined = "\n".join([models, contracts, builder, worker, witness, registry,
                           window, nd, settings, factory])
    suite.check(token not in combined, "CP2 runway debug runtime token removed: " + token)

has(generated, "GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3",
    "build identity names final candidate 3")

# Constructor arity regression: all snapshot calls must match the constructor.
def parenthesized(text: str, open_index: int) -> str:
    clean = strip_csharp_comments_and_literals(text)
    depth = 0
    for i in range(open_index, len(clean)):
        c = clean[i]
        if c == "(": depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[open_index + 1:i]
    raise ValueError("unterminated parentheses")

def split_top_level(value: str) -> list[str]:
    clean = strip_csharp_comments_and_literals(value)
    out, start = [], 0
    p = b = c = 0
    for i, ch in enumerate(clean):
        if ch == "(": p += 1
        elif ch == ")": p -= 1
        elif ch == "[": b += 1
        elif ch == "]": b -= 1
        elif ch == "{": c += 1
        elif ch == "}": c -= 1
        elif ch == "," and p == b == c == 0:
            out.append(value[start:i].strip()); start = i + 1
    tail = value[start:].strip()
    if tail: out.append(tail)
    return out

ctor_marker = "internal AERISRunwaySurveySnapshot("
ctor_open = contracts.index(ctor_marker) + len(ctor_marker) - 1
ctor_count = len(split_top_level(parenthesized(contracts, ctor_open)))
call_counts = []
pos = 0
marker = "new AERISRunwaySurveySnapshot("
while True:
    found = builder.find(marker, pos)
    if found < 0: break
    open_idx = found + len(marker) - 1
    call_counts.append(len(split_top_level(parenthesized(builder, open_idx))))
    pos = open_idx + 1
suite.check(len(call_counts) == 2, "two runway snapshot constructor calls are present")
for index, count in enumerate(call_counts):
    suite.equal(count, ctor_count, "snapshot constructor call %d arity matches" % (index + 1))

# Lightweight C# delimiter balance over all source files.
for path in sorted(source.rglob("*.cs")):
    clean = strip_csharp_comments_and_literals(read(path))
    suite.check(clean.count("{") == clean.count("}"),
                "C# brace balance: " + str(path.relative_to(ROOT)))

# Replay the observed Kola geometry without relying on its name in production code.
def norm_lon(value: float) -> float:
    while value > 180.0: value -= 360.0
    while value < -180.0: value += 360.0
    return value

def distance(a, b, radius=600000.0):
    lat1, lat2 = math.radians(a[0]), math.radians(b[0])
    dlat = lat2 - lat1
    dlon = math.radians(norm_lon(b[1] - a[1]))
    h = math.sin(dlat / 2.0) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin(dlon / 2.0) ** 2
    return 2.0 * radius * math.asin(min(1.0, math.sqrt(max(0.0, h))))

def bearing(a, b):
    lat1, lat2 = math.radians(a[0]), math.radians(b[0])
    dlon = math.radians(norm_lon(b[1] - a[1]))
    y = math.sin(dlon) * math.cos(lat2)
    x = math.cos(lat1) * math.sin(lat2) - math.sin(lat1) * math.cos(lat2) * math.cos(dlon)
    return math.degrees(math.atan2(y, x)) % 360.0

def placement(a, b, point):
    d = distance(a, point)
    delta = math.radians(((bearing(a, point) - bearing(a, b) + 180.0) % 360.0) - 180.0)
    return d * math.cos(delta), d * math.sin(delta), distance(a, b)

a = (-4.0460519614916342, -72.142698408004165)
b = (-4.2760802241856384, -72.075185095331548)
v = (-4.1754577, -72.1147287)
along, cross, length = placement(a, b, v)
gate = max(77.742763235022636 * 0.5 + 12.0, 0.75 * 3.0 + 12.0)
suite.check(-max(100.0, 77.742763235022636 * 1.5) <= along <= length + max(100.0, 77.742763235022636 * 1.5),
            "observed Kola vessel position is inside the longitudinal check window")
suite.check(abs(cross) > gate, "observed Kola position exceeds the generic corridor gate",
            "cross=%.2f gate=%.2f" % (abs(cross), gate))

# A second anonymous synthetic runway proves the math is not tied to Kola data.
sa, sb = (0.0, 0.0), (0.0, 0.01)
_, synthetic_cross, _ = placement(sa, sb, (0.003, 0.005))
suite.check(abs(synthetic_cross) > 22.0,
            "anonymous second-airport offset is also detectable")
_, centered_cross, _ = placement(sa, sb, (0.0, 0.005))
suite.check(abs(centered_cross) < 1.0,
            "anonymous centerline position passes the same generic math")

suite.finish()
