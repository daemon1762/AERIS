#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import ROOT, SOURCE, CheckSuite, all_text, read, source_files, strip_csharp_comments_and_literals

suite = CheckSuite("v0.16.3.0 legacy NAV removal")
source_text = all_text(source_files(".cs"))
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
flightplans = read(SOURCE / "FlightPlans" / "AERISFlightPlanLibrary.cs")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
settings_cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Config" / "AERISSettings.cfg")
api = read(SOURCE / "API" / "AERISExternalAutomationApi.cs")
manager = read(SOURCE / "Core" / "AERISExternalAutomationManager.cs")
manager_v2 = read(SOURCE / "Core" / "AERISExternalAutomationManager.V2.cs")
recorder = read(SOURCE / "Recording" / "AERISFlightDataRecorder.cs")
landing = read(SOURCE / "Landing" / "AERISLandingFoundation.cs")

suite.check('internal bool SetNavigationArm(bool armed,out string error)' in bootstrap,
            "safe NAV ARM rejection boundary exists")
nav_block = bootstrap[bootstrap.find('SetNavigationArm'):bootstrap.find('SetNavigationArm') + 600]
suite.check('return false;' in nav_block, "NAV ARM always rejects")
suite.check('navigation ARM rejected: legacy NAV remains removed' in bootstrap,
            "NAV ARM rejection is logged")
suite.check('NAV ARM  UNAVAILABLE' in window and 'GUI.enabled=false;' in window,
            "NAV ARM UI is visibly disabled")
suite.check('Selecting a plan cannot arm AP modes or write FlightCtrlState.' in window,
            "flight-plan library remains data-only")
for token in ('FlightCtrlState', 'SetArmed(', 'AERISBankDirector', 'AERISHdgDirector',
              'AERISVerticalSpeedDirector', 'AERISVelocityDirector'):
    suite.check(token not in flightplans, f"flight-plan library has no control dependency: {token}")
suite.check('Directory.GetFiles(directory, "*.cfg", SearchOption.AllDirectories)' in flightplans,
            "existing CFG library remains readable")
suite.check('DATA ONLY' in flightplans, "flight-plan library reports data-only status")

for token in ('TryStartNavLanding', 'TryGetCompatibleNavPlans',
              'AERISNavLandingRequest', 'AERISNavPlanDescriptor'):
    suite.check(token not in api + manager + manager_v2, f"legacy automation entry removed: {token}")
suite.check('AddCapability(values, AERISAutomationCapability.Navigation)' not in manager_v2,
            "Navigation capability is not advertised")
suite.check('AddCapability(values, AERISAutomationCapability.AutoLanding)' not in manager_v2,
            "AutoLanding capability is not advertised")
suite.check('Navigation = 1' in api and 'AutoLanding = 2' in api,
            "Contract v2 numeric slots remain reserved")

for token in ('ApNav', 'apNav', 'showFlightInstrumentWithoutApNav'):
    suite.check(token not in settings + settings_cfg, f"legacy setting removed: {token}")
for token in ('SampleNavDiagnostics', 'fdr_nav_diagnostics', 'navDiagnosticsWriter',
              'hdg_managed_yaw_active'):
    suite.check(token not in recorder, f"legacy recorder surface removed: {token}")

landing_code = strip_csharp_comments_and_literals(landing)
for forbidden in ('FlightCtrlState', '.SetArmed(', 'ApplyAaNative', 'MainThrottle',
                  '.pitch', '.roll', '.yaw', 'GroundStability.', 'SpeedAirbrake.'):
    suite.check(forbidden not in landing_code, f"LAND first gate has no control dependency: {forbidden}")

for token in (
    'Hdg.Update(vessel, Attitude, Bank, Protect, Master, normalApExecutionPermitted)',
    'Altitude.Update(vessel, Attitude, VerticalSpeed, Master, normalApExecutionPermitted)',
    'VerticalSpeed.Update(vessel, Attitude, Pitch, Master, normalApExecutionPermitted)',
    'Velocity.Update(vessel, Attitude, Acceleration, Master, normalApExecutionPermitted)',
    'Acceleration.Update(vessel, Attitude, Master, normalApExecutionPermitted)',
):
    suite.check(token in bootstrap, f"normal AP update retained: {token.split('(')[0]}")

for token in ('AERISNavDirector', 'AERISRouteSpeedPlanner', 'AERISTrajectoryPrimitives'):
    suite.check(token not in source_text, f"legacy NAV class absent: {token}")
suite.finish()
