#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "Source/AERISFlightControl/Terrain/AERISR041AllBodyPqsCapabilityCensusObserver.cs"
CSPROJ = ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj"

errors = []

def require(text, token, where):
    if token not in text:
        errors.append(f"missing {token!r} in {where}")

def forbid(text, token, where):
    if token in text:
        errors.append(f"forbidden {token!r} in {where}")

if not SRC.is_file():
    errors.append(f"missing source: {SRC}")
    source = ""
else:
    source = SRC.read_text(encoding="utf-8")

if not CSPROJ.is_file():
    errors.append(f"missing project: {CSPROJ}")
    project = ""
else:
    project = CSPROJ.read_text(encoding="utf-8")

for token in (
    "AERIS38_R041A_ALL_BODY_PQS_CAPABILITY_DEPENDENCY_CENSUS",
    "[KSPAddon(KSPAddon.Startup.MainMenu, false)]",
    "[R041A][ALL_BODY_PQS_CENSUS_BEGIN]",
    "[R041A][BODY]",
    "[R041A][MODIFIER]",
    "[R041A][ALL_BODY_PQS_CENSUS_COMPLETE]",
    'const double ExactToleranceMeters = 1E-08;',
    'const string ProducerSwitch = "false";',
    'const string DbAuthority = "PQS";',
    'const string ProductionAuthority = "PQS";',
    '"; db_write=false"',
    '"; preload_mutation=false"',
    '"; worker_runtime_object_access=false"',
    "FlightGlobals.Bodies",
    "body.pqsController",
    "BindingFlags.Instance",
    "Capability.PureCpuReady",
    "Capability.Snapshottable",
    "Capability.UnsupportedFormula",
    "Capability.RuntimeDependent",
    "Capability.NoPqs",
    "R039_R040B_MINMUS_EXACT_FAMILY_ACCEPTED",
):
    require(source, token, SRC.name)

require(
    project,
    '<Compile Include="Terrain\\AERISR041AllBodyPqsCapabilityCensusObserver.cs" />',
    CSPROJ.name,
)

# This observer is a census only. Direct terrain/DB mutation or worker scheduling is
# out of scope and must not silently enter R041A.
for token in (
    "ThreadPool.",
    "Task.Run",
    "performance.Enqueue",
    "AERISWorkerScheduler",
    ".SetSurfaceHeight",
    ".SetState(",
    ".Save(",
    "File.Write",
    "Directory.CreateDirectory",
    "producer_switch=true",
    "db_write=true",
    "preload_mutation=true",
):
    forbid(source, token, SRC.name)

# The only PQS-related operation permitted is read-only discovery/census. Explicitly
# guard common mutation/control spellings.
for token in (
    "pqsController =",
    ".enabled =",
    ".modEnabled =",
    ".order =",
    "Destroy(",
    "AddComponent",
    "RemoveModifier",
    "AddModifier",
):
    forbid(source, token, SRC.name)

if errors:
    print("=== AERIS38 R041A STATIC VERIFY ===")
    for err in errors:
        print("FAIL:", err)
    print("[AERIS38 R041A STATIC VERIFY] FAIL")
    sys.exit(1)

print("=== AERIS38 R041A STATIC VERIFY ===")
print(f"source={SRC.relative_to(ROOT)}")
print(f"project={CSPROJ.relative_to(ROOT)}")
print("mode=READ_ONLY_CAPABILITY_CENSUS")
print("terrain_tolerance_m=1E-08")
print("production_authority=PQS")
print("db_authority=PQS")
print("producer_switch=false")
print("db_write=false")
print("preload_mutation=false")
print("worker_runtime_object_access=false")
print("[AERIS38 R041A STATIC VERIFY] PASS")
