#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def check(cond,label,detail=""):
    global failures
    if cond:
        print("[PASS] "+label)
    else:
        failures += 1
        print("[FAIL] "+label+(" :: "+detail if detail else ""))

failures=0
print("[AERIS] CP3.75 Candidate 1 Pure ND Rebase static authority test")

# Gate A: build/source identity.
version=(ROOT/"Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs").read_text(encoding="utf-8")
build=(ROOT/"build_ubuntu.sh").read_text(encoding="utf-8")
avc=(ROOT/"GameData/AERISFlightControl/AERISFlightControl.version").read_text(encoding="utf-8")
identity='DEV CP3.75 — PURE ND REBASE CANDIDATE 1'
check(identity in version,"CP3.75 Candidate1 generated identity")
check("DEV CP3.75 PURE ND REBASE CANDIDATE 1" in build,"CP3.75 Candidate1 build identity")
check("CP3.75 Pure ND Rebase Candidate 1" in avc,"CP3.75 Candidate1 AVC identity")

# Gate B1: exact Candidate14 ND/supply authority.
expected={'Source/AERISFlightControl/AERISFlightControl.csproj': '31d26fb35afc69d91b507ed7c17874211cd289acf8d3ef6147b77e0d1e685eed', 'Source/AERISFlightControl/Performance/AERISMapDramCache.cs': '43e4d6326bc37e0b4baac0af9cf08d11a1737bee8c8532de6e90de4f7ff64abf', 'Source/AERISFlightControl/Performance/AERISNavigationDisplayPipeline.cs': '9c631354b13e2316e5e31f0dc736073f675ba1ff97aedcdcb69bd6c4a4123d28', 'Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs': 'a627b306da19c3cb19f2a1e700a210b7ed04f07775c3230998babbec45d06b46', 'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs': '2c9fc6067e7a5c8fbf4e67e58e706e4e70ac548f2dfe6acf2eb503992b9aa5cc', 'Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs': '5a638af4d2895adfcb2748162a79e7f4442b136b524acdb3ea80f20b0c1a73c4', 'Source/AERISFlightControl/Terrain/AERISPredictiveForwardCorridor.cs': '9492adcaf59a56e4cffe482d1202e14b77f6f199d55efe9dd453f6e96f051a2a', 'Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs': 'c9a5a867edba4576ebff192e3505cd53e62564560949c80e644163745ea279ab', 'Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs': '3a699c1cf9a440ae6e826367788ca706f483b49ceaf54d6ad52a8f7df0dd43c0', 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs': 'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032', 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs': '7bba788ead43026dd4cd83e7cb6fb1e9ddcf3da796195116f8b97b8d95b0f0d5', 'Source/AERISFlightControl/Terrain/AERISTerrainLandDetailActivationPolicy.cs': '98d771b41c12b2d2f12157b326b49750610e45e50ad0f18d9f6ea66f4c55fb9d', 'Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs': '76c96e96784ce3e6dc2f93708f84fc9374a05c0c721aacc5f25dd962b40bbeac', 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs': '0127585f13b0da4e9676eeb55525f0195da964b019535b9d48327163cb9880e2', 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs': '378d4b068c1ad97ba83b44c4fb0d09a76ef9e49868e45f5c28a88e660ccba59a', 'Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs': '7790977cd845c58767a70f193db3efbfc573812706466b477846b06447440f86', 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs': '2b05d95cf6e958d769d6f6b0ed1fe41b91a1cd5b4d966c996a3aaf0d25c61add', 'Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs': '6afedb91ee15194da10d3cc105d6ab5e329a2552739cf0146cb5591e597473b2', 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs': '70c0239aea848293458ff8f6faccddab86f85ef1cfbdf132bfea05d1658168fe'}
for rel,want in expected.items():
    path=ROOT/rel
    got=sha(path) if path.is_file() else "MISSING"
    check(got==want,"Candidate14 exact rebase: "+rel,got)

# Gate B2: protected non-ND files stay exact to AERIS20 current baseline.
baseline=ROOT/"Evidence/PROTECTED_NON_ND_HASH_BASELINE.txt"
check(baseline.is_file(),"protected non-ND baseline exists")
if baseline.is_file():
    for line in baseline.read_text(encoding="utf-8").splitlines():
        m=re.match(r"^([0-9a-f]{64})  (.+)$",line)
        if not m: continue
        want,rel=m.groups()
        path=ROOT/rel
        got=sha(path) if path.is_file() else "MISSING"
        check(got==want,"protected non-ND unchanged: "+rel,got)

# Mixed-file merge boundaries.
settings=(ROOT/"Source/AERISFlightControl/Settings/AERISSettings.cs").read_text(encoding="utf-8")
config=(ROOT/"GameData/AERISFlightControl/Config/AERISSettings.cfg").read_text(encoding="utf-8")
bootstrap=(ROOT/"Source/AERISFlightControl/Core/AERISBootstrap.cs").read_text(encoding="utf-8")
window=(ROOT/"Source/AERISFlightControl/UI/AERISWindow.cs").read_text(encoding="utf-8")
archive=(ROOT/"Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs").read_text(encoding="utf-8")
check("Land = 4" in settings and "TerrainLandRuntimeQualityEnabled" in settings,
      "Candidate14 hidden LAND terrain capability restored")
check("CurrentTerrainQualityModelRevision = 2" in settings,
      "Candidate14 terrain quality model revision restored")
check("flightDataArchiveLimit = 10" in config,
      "later FDR/CVR archive limit default preserved")
check("FlightDataArchiveLimit = 10" in settings and "NormalizeFlightDataArchiveLimit" in settings,
      "later FDR/CVR retention settings preserved")
check("AERISFlightDataArchive.ConfigureRetention(settings.FlightDataArchiveLimit)" in bootstrap,
      "later FDR/CVR retention bootstrap preserved")
check("demand-gated LAND microtiles" in bootstrap,
      "Candidate14 terrain startup authority restored")
check("FlightArchiveLimitLabels" in window and "DrawFlightDataArchiveLimitSelector()" in window,
      "later FDR/CVR archive UI preserved")
check("settings.TerrainQualityMode==AERISTerrainQualityMode.Land?-1" in window and
      'new string[]{"AUTO","LOW","MEDIUM","HIGH"}' in window,
      "Candidate14 terrain quality selector semantics restored")
check("VerifiedMarkerSuffix" in archive and "PruneVerifiedArchives" in archive,
      "later verified-archive retention implementation preserved")
check((ROOT/"Source/AERISFlightControl/Terrain/AERISTerrainLandDetailActivationPolicy.cs").is_file(),
      "Candidate14 LAND activation policy restored")
csproj=(ROOT/"Source/AERISFlightControl/AERISFlightControl.csproj").read_text(encoding="utf-8")
check('Terrain\\AERISTerrainLandDetailActivationPolicy.cs' in csproj,
      "LAND activation policy compiled")

# Explicitly quarantine rejected CP3.5 active identity from build product.
check("UiCheckpoint = \"DEV CP3.5 GATE 4" not in version,
      "rejected CP3.5 Gate4 is not active generated identity")
check("run_v01800_cp375_candidate1_prebuild.py" in build,
      "build entrypoint runs CP3.75 Candidate1 prebuild")

if failures:
    print("[AERIS] CP3.75 Candidate1 static authority FAIL: %d failure(s)" % failures)
    raise SystemExit(1)
print("[AERIS] CP3.75 Candidate1 static authority PASS")
