#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 Generic Runway Placement FC3 Compile Hotfix 1")
path = SOURCE / "Landing" / "AERISAirfieldRegistry.cs"
suite.check(path.is_file(), "airfield registry source exists")
source = read(path)
start = source.index("internal bool VerifyRunwayPlacement")
end = source.index("static double InitialBearing", start)
method = source[start:end]

declaration = "string stored = string.Empty;"
short_circuit = "witnessLibrary == null || !witnessLibrary.RecordPlacementMismatch("

suite.check(declaration in method,
            "quarantine save detail is definitely assigned before short-circuit evaluation")
suite.check(method.find(declaration) < method.find(short_circuit),
            "stored initialization precedes witness-library short-circuit")
suite.check("out stored" in method,
            "quarantine persistence still returns its detail through stored")
suite.check("string stored;" not in method,
            "uninitialized stored declaration cannot regress")
suite.check("string.IsNullOrEmpty(stored)" in method,
            "save-failure fallback still safely consumes stored")
suite.check("PLACEMENT MISMATCH DETECTED BUT QUARANTINE SAVE FAILED" in method,
            "existing fail-closed fallback message remains intact")
suite.check("RecordPlacementMismatch" in method and "RequestManualReload();" in method,
            "placement mismatch persistence and resurvey request remain intact")
suite.check("Kola" not in method and "98523c92" not in method,
            "compile hotfix introduces no airport-specific production branch")

suite.finish()
