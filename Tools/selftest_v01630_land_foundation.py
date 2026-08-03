#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
import re
from v01630_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.16.3.0 independent LAND first gate")
models = read(SOURCE / "Landing" / "AERISAirfieldModels.cs")
parser = read(SOURCE / "Landing" / "AERISAirfieldConfigParser.cs")
providers = read(SOURCE / "Landing" / "AERISAirfieldProviders.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
landing = read(SOURCE / "Landing" / "AERISLandingFoundation.cs")
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
fdi = read(SOURCE / "UI" / "AERISFlightInstrument.cs")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Airfields" / "Defaults" /
           "01_Stock_DLC_Foundation.cfg")

for token in ('AERISAirfieldSource', 'AERISFacilityKind', 'AERISAirfieldValidation',
              'AERISRunwayDirectionDefinition', 'CanArmFoundation'):
    suite.check(token in models, f"registry model exists: {token}")
for token in ('ConfigNode.Load', 'GreatCircleDistanceMeters', 'InitialBearingDeg',
              'double.IsNaN', 'double.IsInfinity'):
    suite.check(token in parser, f"CFG parser guard exists: {token}")

suite.check('KerbalKonstructs.Core.LaunchSiteManager' in providers,
            "KSP-RO KK LaunchSiteManager is discovered by reflection")
suite.check('"AllLaunchSites", "allLaunchSites"' in providers,
            "KK public and array launch-site surfaces are supported")
suite.check('waterlaunch' in providers and 'AERISFacilityKind.Harbour' in providers,
            "KK water launches are classified as non-runway harbour facilities")
suite.check('StockLaunchsitesExpansion' in providers and 'LooksLikeSle' in providers,
            "SLE source is identified")
suite.check('KerbalKonstructs.dll' not in read(SOURCE / "AERISFlightControl.csproj"),
            "KK remains an optional runtime integration")

kk_pos = registry.find('AERISKerbalKonstructsProvider.Collect')
ksp_pos = registry.find('AERISKspFacilityProvider.Collect')
suite.check(kk_pos >= 0 and ksp_pos > kk_pos,
            "KK is collected before PSystemSetup to prevent SLE misclassification")
suite.check('AERISAirfieldValidation.DiscoveryOnly' in registry,
            "unknown provider geometry remains discovery-only")
suite.check('RUNWAY GEOMETRY REQUIRED' in registry,
            "unvalidated runway reason is visible")

for token in ('ControlText { get { return "PILOT"; } }',
              'internal string LocalizerText',
              'internal string GlidePathText',
              'HeadingMatchesGeometry',
              'Observation.OnApproachSide',
              'RUNWAY GEOMETRY NOT FOUNDATION-VALIDATED',
              'AIRBORNE FIXED-WING FLIGHT REQUIRED',
              'FIXED-WING PLANE VESSEL TYPE REQUIRED',
              'vesselType != "PLANE"',
              'CAPTURE LOGIC NOT ENABLED IN FIRST GATE',
              'no AP or FlightCtrlState authority granted'):
    suite.check(token in landing, f"safe LAND boundary exists: {token}")
landing_code = strip_csharp_comments_and_literals(landing)
for forbidden in ('FlightCtrlState', '.SetArmed(', 'ApplyAaNative', 'MainThrottle',
                  'mainThrottle', 'wheelThrottle', 'wheelSteer'):
    suite.check(forbidden not in landing_code, f"LAND observation layer is command-free: {forbidden}")

suite.check('Airfields=new AERISAirfieldRegistry(settings)' in bootstrap,
            "Airfield Registry is owned by bootstrap")
suite.check('Landing=new AERISLandingFoundation(Airfields)' in bootstrap,
            "LAND foundation is owned by bootstrap")
suite.check('Landing.Tick(FlightGlobals.ActiveVessel,Attitude)' in bootstrap,
            "LAND geometry observation updates in flight")
arm = re.search(r'internal bool ArmLanding\(out string error\)\s*\{(?P<body>.*?)\n  \}', bootstrap, re.S)
suite.check(arm is not None, "bootstrap LAND ARM wrapper exists")
if arm is not None:
    body = arm.group('body')
    suite.check(body.find('error="LAND ARM unavailable"') >= 0 and
                body.find('if(Landing==null)return false;') > body.find('error="LAND ARM unavailable"') and
                body.find('Landing.TryArm(FlightGlobals.ActiveVessel,out error)') >
                body.find('if(Landing==null)return false;'),
                "LAND ARM out error is definitely assigned before every return path")
suite.check('Landing.ResetForSceneTransition(reason)' in bootstrap,
            "LAND ARM resets on scene/vessel boundary")
suite.check('Airfields.RefreshDynamicProviders()' in bootstrap,
            "dynamic providers refresh after startup and scene entry")

for token in ('LAND — INDEPENDENT AIRFIELD REGISTRY / FIRST GATE',
              'ARM LAND — OBSERVE', 'CONTROL REMAINS PILOT',
              'REFRESH PROVIDERS', 'RUNWAY GEOMETRY REQUIRED', 'SURVEY:',
              'No AP, throttle, brake, airbrake, Ground Assist or FlightCtrlState command'):
    suite.check(token in window, f"LAND UI contract visible: {token}")
for token in ('DrawLandingPlan', 'DrawLandingProfile', '"XTK "', '"GS "', '"PILOT  ARM"',
              'settings.NavigationDisplayTrackUp', 'GUI.matrix = previousMatrix',
              'finally { GUI.EndGroup(); }'):
    suite.check(token in nd, f"ND first-gate display feature exists: {token}")
suite.check('core.Landing != null && core.Landing.AutoDisplayDemand' in fdi,
            "ND AUTO display still follows LAND ARM")

for key in ('LandSelectedAirfieldId', 'LandSelectedDirectionId',
            'LandSectionExpanded'):
    suite.check(key in settings, f"LAND setting exists: {key}")
for key in ('landSelectedAirfieldId', 'landSelectedDirectionId',
            'landSectionExpanded'):
    suite.check(key in read(ROOT / "GameData" / "AERISFlightControl" / "Config" /
                            "AERISSettings.cfg"), f"default LAND setting exists: {key}")

code = re.sub(r'//.*', '', cfg)
suite.equal(code.count('{'), code.count('}'), "Airfield CFG braces are balanced")
suite.check('id = KSC_MAIN' in cfg and 'id = ISLAND_AIRFIELD' in cfg,
            "KSC and Island foundation definitions exist")
suite.check(cfg.count('validation = FoundationValidated') == 2,
            "only KSC and Island are foundation-validated")
suite.check(cfg.count('id = RWY_09') == 2 and cfg.count('id = RWY_27') == 2,
            "KSC and Island each define both runway directions")
suite.check('id = DLC_DESSERT_AIRFIELD' in cfg and 'validation = DiscoveryOnly' in cfg,
            "Dessert Airfield is runtime-supported but unvalidated")
suite.check('id = DLC_WOOMERANG' in cfg and 'facilityType = LaunchPad' in cfg,
            "Woomerang is explicitly non-runway")
suite.check('LandShowAllFacilities' not in settings and 'landShowAllFacilities' not in settings,
            "obsolete all-facilities LAND setting is removed")
suite.check('ALL FACILITIES' not in window,
            "LAND UI no longer offers non-runway facilities")
suite.check('if(airfield.FacilityKind!=AERISFacilityKind.Runway)continue;' in window,
            "LAND selector is hard-filtered to fixed-wing runways")
suite.finish()
