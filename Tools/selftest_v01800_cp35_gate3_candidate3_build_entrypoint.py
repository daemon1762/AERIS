#!/usr/bin/env python3
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 3 Candidate 3 build entrypoint')
build=read(ROOT/'build_ubuntu.sh'); version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
identity='DEV CP3.5 GATE 3 — SPARSE HI-RES / PALETTE V2 / ARCHIVE RETENTION CANDIDATE 3'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated identity exact')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build-generated identity exact')
quick='run_v01800_cp35_gate3_candidate3_prebuild.py'
suite.check(quick in build,'normal build invokes Candidate 3 lightweight prebuild')
active='\n'.join(line for line in build.splitlines() if line.strip() and not line.lstrip().startswith('#'))
suite.check('SOURCE_MANIFEST_SHA256.txt' not in active and 'MANIFEST_SHA256.txt' not in active,'normal build still excludes full manifest hashing')
suite.check('xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj' in build,'xbuild remains compile authority')
suite.check(build.index(quick)<build.index('xbuild /p:Configuration=Release'),'prebuild runs before native compile')
suite.check('cp -f bin/Release/AERISFlightControl.dll' in build,'DLL install path retained')
suite.finish()
