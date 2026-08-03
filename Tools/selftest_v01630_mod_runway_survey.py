#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from v01630_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.16.3.0 MOD runway survey / ILS runway-only")
models = read(SOURCE / "Landing" / "AERISAirfieldModels.cs")
catalog_cs = read(SOURCE / "Landing" / "AERISRunwaySurveyCatalog.cs")
provider = read(SOURCE / "Landing" / "AERISAirfieldProviders.cs")
resolver = read(SOURCE / "Landing" / "AERISModRunwaySurveyResolver.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
csproj = read(SOURCE / "AERISFlightControl.csproj")
cfg_path = ROOT / "GameData" / "AERISFlightControl" / "Airfields" / "Defaults" / "02_Current_Mod_Runway_Survey_Catalog.cfg"
cfg = read(cfg_path)

suite.check(cfg_path.is_file(), "current MOD runway survey catalog exists")
suite.equal(len(re.findall(r'^\s*RunwaySurvey\s*$', cfg, re.M)), 41,
            "catalog contains 41 current runway records")
suite.equal(cfg.count('method = StaticBounds'), 34,
            "34 straight runway records use runtime bounds")
suite.equal(cfg.count('method = PairedThresholds'), 2,
            "two TSC threshold records use paired geometry")
suite.equal(cfg.count('method = ManualRequired'), 5,
            "five ambiguous/X/spawn records require manual geometry")
suite.check('TSC_MAIN_RUNWAY' in cfg and 'TSC Runway 09' in cfg and 'TSC Runway 27' in cfg,
            "TSC opposite thresholds are explicitly paired")
for name in ('Top Secret Area 15', 'Dununda', 'Meeda Naval Air Station',
             'Area 52 X-Runway', 'Lake Dermal Runway'):
    block = cfg[cfg.find('providerSiteId = ' + name):]
    block = block[:block.find('    }')]
    suite.check('method = ManualRequired' in block,
                f"ambiguous runway is inhibited: {name}")

for token in ('AERISRunwaySurveyMethod', 'StaticBounds', 'PairedThresholds',
              'RuntimeSurveyValidated', 'MinimumAspectRatio'):
    suite.check(token in models, f"survey model exists: {token}")
for token in ('ConfigNode.Load', 'GetNodes("RunwaySurvey")', 'ProviderUuid',
              'ProviderSiteId', 'SourcePathContains', 'ModelName', 'duplicate catalog id ignored'):
    suite.check(token in catalog_cs, f"catalog parser guard exists: {token}")
suite.check('return !string.IsNullOrEmpty(ProviderUuid) || !string.IsNullOrEmpty(ProviderSiteId)' in models,
            "catalog entries require an exact provider identity")

for token in ('RuntimeLaunchTransform', 'RuntimeRunwayObject', 'RuntimeRunwayPrefab',
              'RuntimeLaunchFrameValid', 'ProviderCategory', 'LaunchPadTransform',
              'ResolveGameObject', 'KerbalKonstructs.Core.LaunchSiteManager'):
    suite.check(token in provider, f"optional KK runtime survey input exists: {token}")
suite.check('KerbalKonstructs.dll' not in csproj,
            "runway survey keeps KK as an optional reflection-only dependency")
suite.check('Never call StaticInstance.mesh here' in provider and
            'GetFieldValue(instance' in provider and '"_mesh"' in provider,
            "registry scan does not activate every KK static")

for token in ('TryMeasureStaticBounds', 'GetComponentsInChildren<Collider>(true)',
              'GetComponentsInChildren<Renderer>(true)', 'GetComponentsInChildren<MeshFilter>(true)',
              'AccumulatePrefabMeshBounds', 'collider.isTrigger', 'ProjectBounds', 'MinimumLengthMeters', 'MaximumLengthMeters',
              'MinimumWidthMeters', 'MaximumWidthMeters', 'MinimumAspectRatio',
              'RUNWAY SHAPE AMBIGUOUS / MANUAL GEOMETRY REQUIRED',
              'TryMeasurePairedThresholds', 'records.Count != 2',
              'SurveyGeometryValid = true', 'RUNTIME BOUNDS SURVEYED'):
    suite.check(token in resolver, f"fail-safe runtime survey rule exists: {token}")
resolver_code = strip_csharp_comments_and_literals(resolver)
for forbidden in ('FlightCtrlState', 'MainThrottle', 'SetArmed(', 'ApplyAaNative'):
    suite.check(forbidden not in resolver_code,
                f"survey resolver has no flight-control authority: {forbidden}")

for token in ('AERISRunwaySurveyCatalog.Load(files)',
              'AERISModRunwaySurveyResolver.Apply(records, surveyCatalog',
              'AERISAirfieldValidation.RuntimeSurveyValidated',
              'ApplySurveyGeometry', 'BuildSurveyDirection',
              'RUNWAY GEOMETRY REQUIRED', 'RunwaySurveyStatus'):
    suite.check(token in registry, f"registry survey integration exists: {token}")
suite.check('if (airfield.Validation == AERISAirfieldValidation.DiscoveryOnly)' in registry,
            "runtime survey does not downgrade configured validation")

suite.check('ALL FACILITIES' not in window and 'LandShowAllFacilities' not in window,
            "non-runway visibility toggle is removed")
suite.check('if(airfield.FacilityKind!=AERISFacilityKind.Runway)continue;' in window,
            "LAND/ILS selector shows runways only")
suite.check('Select a runway.' in window and 'Selected runway:' in window,
            "LAND selection wording is runway-only")
suite.check('SURVEY:' in window, "runtime survey summary is visible")

repaint = nd[nd.find('if (repaint)'):nd.find('DrawMapControls', nd.find('if (repaint)'))]
suite.check('if (!landActive)' in repaint and 'DrawAirfieldSymbols' in repaint,
            "facility symbols are drawn only outside active LAND/ILS")
suite.check('if (!landActive || overlay)' in repaint and 'else DrawCleanBackground(plan);' in repaint,
            "LAND overlay keeps terrain while LAND focus is clean")
suite.check('DrawLandingPlan' in repaint and 'DrawLandingProfile' in repaint,
            "selected runway/LOC/GS plan and profile remain")
suite.check('DrawThreatTerrainOnly' not in nd,
            "FOCUS removes all background terrain instead of retaining unrelated map detail")

suite.finish()
