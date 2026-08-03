#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 calibration round-trip hotfix 1")
path = SOURCE / "Landing" / "AERISRunwayWitnessLibrary.cs"
suite.check(path.is_file(), "runway witness library source exists")
source = read(path)

def extract_definition(marker: str) -> str:
    start = source.index(marker)
    brace = source.index("{", start + len(marker))
    clean = strip_csharp_comments_and_literals(source)
    depth = 0
    for index in range(brace, len(clean)):
        if clean[index] == "{":
            depth += 1
        elif clean[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace:index + 1]
    raise ValueError("unterminated method: " + marker)

load = extract_definition("void LoadUserCalibrations()")
save = extract_definition("bool SaveUserCalibrations(out string error)")
validator = extract_definition("static void ValidateCalibrationDocument(")
resolver = extract_definition("static ConfigNode ResolveCalibrationRoot(ConfigNode loaded)")

suite.check("ConfigNode loaded = ConfigNode.Load(path);" in load,
            "calibration load retains the raw ConfigNode file root")
suite.check("ConfigNode root = ResolveCalibrationRoot(loaded);" in load,
            "calibration load resolves all ConfigNode round-trip shapes")
suite.check("string.Equals(root.name" not in load,
            "load no longer rejects generic direct-value roots")

suite.check("ValidateCalibrationDocument(ConfigNode.Load(temporary)" in save,
            "temporary file is fully read back through the shared validator")
suite.check("ValidateCalibrationDocument(ConfigNode.Load(path)" in save,
            "committed file is read back after the atomic move")
suite.check("if (File.Exists(path)) File.Delete(path);" in save and
            "if (File.Exists(backup)) File.Move(backup, path);" in save,
            "failed committed readback restores the previous calibration file")
suite.check("committedReadback=True" in save,
            "successful persistence emits committed readback evidence")
suite.check('AERISLogger.Warn("[RUNWAY_CALIBRATION] " + error)' in save,
            "calibration save failures are written to the runtime log")

suite.check("verifiedRecords.Length != expectedCount" in validator,
            "validator requires exact calibration record count")
suite.check("reciprocalDirectionPair" in validator,
            "validator checks the reciprocal direction pair declaration")
suite.check("directionAHeadingDeg" in validator and "directionBHeadingDeg" in validator,
            "validator checks both persisted direction headings")
suite.check("reciprocal direction pair validation failed" in validator,
            "invalid two-direction records fail closed")

suite.check('const string name = "AERIS_USER_RUNWAY_CALIBRATIONS";' in resolver,
            "resolver owns the calibration root identity")
suite.check("string.Equals(loaded.name, name" in resolver,
            "resolver accepts a named loaded root")
suite.check("loaded.GetNode(name)" in resolver,
            "resolver accepts a named child under a generic file root")
suite.check('loaded.GetValue("schema")' in resolver,
            "resolver accepts direct values on a generic file root")
suite.check("string.IsNullOrEmpty(schema) ? null : loaded" in resolver,
            "generic roots without calibration schema fail closed")

build = read(SOURCE / "Properties" / "AERISBuildVersion.generated.cs")
suite.check("CALIBRATION ROUND-TRIP HOTFIX 1" in build,
            "build identity names calibration round-trip hotfix 1")
suite.check("Kola" not in resolver and "Kola" not in save and "Kola" not in validator,
            "persistence hotfix introduces no airport-specific branch")
suite.check(not (ROOT / "GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg").exists(),
            "package still excludes user-owned runway calibration data")

suite.finish()
