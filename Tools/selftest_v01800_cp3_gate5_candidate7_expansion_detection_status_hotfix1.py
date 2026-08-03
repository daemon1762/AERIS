#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 7 Expansion Detection / DLC Runtime Status Hotfix 1')
exp=read(SOURCE/'Landing/AERISExpansionStatus.cs')
reg=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
ui=read(SOURCE/'UI/AERISWindow.cs')
proj=read(SOURCE/'AERISFlightControl.csproj')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
for name,text in (('expansion status',exp),('registry',reg),('ui',ui)):
 c=strip_csharp_comments_and_literals(text)
 suite.check(c.count('{')==c.count('}'),name+' braces balanced')
 suite.check(c.count('(')==c.count(')'),name+' parens balanced')
suite.check('Landing\\AERISExpansionStatus.cs' in proj,'expansion status source is compiled')
suite.check('ThreadPool.QueueUserWorkItem' in exp,'expansion disk detection runs on background ThreadPool')
suite.check('Directory.Exists(Path.Combine(expansionRoot, "MakingHistory"))' in exp,'Making History installation path is detected')
suite.check('Directory.Exists(Path.Combine(expansionRoot, "Serenity"))' in exp,'Breaking Ground Serenity installation path is detected')
suite.check('AssemblyLoader.loadedAssemblies' in exp,'session-loaded expansion state is detected in memory')
suite.check('squadexpansion/makinghistory' in exp.lower() and 'makinghistory' in exp.lower(),'Making History loaded marker supported')
suite.check('squadexpansion/serenity' in exp.lower() and 'breakingground' in exp.lower() and 'serenity' in exp.lower(),'Breaking Ground loaded markers supported')
suite.check('INSTALLED / RESTART REQUIRED' in exp,'installed-but-not-loaded session is explicit')
suite.check('SAVE-LOCKED / NOT EXPOSED' in exp,'Making History runway runtime/save exposure is separate from DLC install state')
suite.check('DESSERT AIRFIELD AVAILABLE' in exp,'runtime-exposed Dessert Airfield state is explicit')
suite.check('EXPANSIONS: MH ' in exp and ' | BG ' in exp,'both DLC products are independently surfaced')
suite.check('AERISExpansionStatus.RequestRefresh();' in reg,'registry refresh triggers expansion status refresh')
suite.check('internal string ExpansionStatus' in reg and 'internal string DlcRunwayStatus' in reg,'registry exposes expansion and DLC runway status separately')
suite.check(' | DLC RWY " + PresentationAirfieldCount(AERISAirfieldSource.Dlc)' in reg,'source summary labels DLC count specifically as presentation-visible runways')
suite.check('WrappedAirfieldLabel(registry.ExpansionStatus);' not in ui and 'ExpansionStatus' in reg,
            'successor removes expansion debug display while registry state remains available')
suite.check('WrappedAirfieldLabel(registry.DlcRunwayStatus);' not in ui and 'DlcRunwayStatus' in reg,
            'successor removes DLC runtime debug display while registry state remains available')
# Main-thread safety: directory checks must exist only inside the ThreadPool callback, not UI/registry.
suite.check('Directory.Exists' not in ui and 'Directory.Exists' not in reg,'AIRFIELDS UI/registry contain no new synchronous expansion disk check')
# Certification authority must remain Candidate 5 policy.
suite.check('return record != null && record.Source == AERISAirfieldSource.Stock;' in reg,
            'DLC install detection does not grant automatic certification authority')
suite.check('record.Source == AERISAirfieldSource.Stock' in reg,'base-game Stock remains sole automatic provider authority')
# Candidate 6 baseline must remain packaged.
suite.check((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg').is_file(),'field-verified Candidate 6 default runway baseline remains packaged')
expected='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"'
suite.check(expected in version and expected in build,'Candidate 7 tab/build identity exact')
suite.check('Candidate 7 Expansion Detection / DLC Runtime Status Hotfix 1' in avc,'Candidate 7 AVC identity')
suite.check('Candidate 7' in read(ROOT/'README.md') and 'Making History' in read(ROOT/'README.md') and 'Breaking Ground' in read(ROOT/'README.md'),'README documents expansion-state split')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE7_EXPANSION_DETECTION_DLC_RUNTIME_STATUS_HOTFIX1.txt').is_file(),'Candidate 7 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE7_EXPANSION_DETECTION_STATUS_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 7 runtime test card present')
suite.finish()
