#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.16.3.0 common-system structural regressions")
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
csproj = read(SOURCE / "AERISFlightControl.csproj")
archive = read(SOURCE / "Recording" / "AERISFlightDataArchive.cs")
recorder = read(SOURCE / "Recording" / "AERISFlightDataRecorder.cs")
manager_v2 = read(SOURCE / "Core" / "AERISExternalAutomationManager.V2.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")

required_compile = (
    'AA\\Modules\\StandardFlyByWire.cs',
    'Autopilot\\AERISBankDirector.cs', 'Autopilot\\AERISHdgDirector.cs',
    'Autopilot\\AERISPitchDirector.cs', 'Autopilot\\AERISVerticalSpeedDirector.cs',
    'Autopilot\\AERISAltitudeDirector.cs', 'Autopilot\\AERISAccelerationDirector.cs',
    'Autopilot\\AERISVelocityDirector.cs', 'Autopilot\\AERISAutoTakeoffDirector.cs',
    'Autopilot\\AERISSpeedAirbrakeController.cs',
    'Protect\\GroundStabilityProtection.cs', 'Protect\\ProtectTelemetry.cs',
    'Recording\\AERISFlightDataRecorder.cs', 'Recording\\AERISFlightDataArchive.cs',
    'API\\AERISGroundAssistApi.cs', 'API\\AERISPropulsionApi.cs',
    'API\\AERISExtensionApi.cs', 'API\\AERISExternalAutomationApi.cs',
)
for value in required_compile:
    suite.check(value in csproj, f"common subsystem compiled: {value}")

for token, label in (
    ('OnAutopilotUpdate += OnAutopilotInput', "pre-AA control hook retained"),
    ('Hdg.ApplyAaNativeYawRateDemand', "HDG native yaw transport retained"),
    ('Pitch.ApplyAaNativePitchRateDemand', "PITCH native rate transport retained"),
    ('Acceleration.ApplyAaNativeThrottleDemand', "ACC native throttle transport retained"),
    ('Bank.ApplyAaNativeRollRateDemand', "BANK native roll transport retained"),
    ('AutoTakeoff.ApplyAaNativeTakeoffDemand', "Auto Takeoff transport retained"),
    ('GroundStability.ApplyAaNativeGroundDemand', "Ground Stability transport retained"),
    ('ExternalAutomation.ApplyV2ControlDemands', "external automation control boundary retained"),
    ('ClearExternalControlDemands()', "scene/vessel demand clearing retained"),
    ('GroundStability.UpdateGroundState', "ground sensor refresh retained"),
):
    suite.check(token in bootstrap, label)

for token, label in (
    ('System.IO.Compression', "managed cross-platform compression namespace retained"),
    ('ZipArchive', "managed ZIP writer retained"),
    ('CompressionLevel.Optimal', "optimal compression retained"),
    ('finalPath + ".tmp"', "temporary archive suffix retained"),
    ('VerifyArchive(temporaryPath', "temporary ZIP is verified before commit"),
    ('File.Move(temporaryPath, finalPath)', "verified ZIP is atomically finalized"),
    ('TryDeleteSource(folder', "raw folder deletion occurs only after archive success"),
    ('raw flight data retained', "archive failure retains raw data"),
    ('ThreadPool.QueueUserWorkItem', "archive work remains backgrounded"),
):
    suite.check(token in archive, label)

for token, label in (
    ('SampleBankDiagnostics', "BANK FDR retained"),
    ('SampleHeadingDiagnostics', "HDG FDR retained"),
    ('SampleVerticalSpeedDiagnostics', "V/S FDR retained"),
    ('SampleAltitudeDiagnostics', "ALT FDR retained"),
    ('SampleAccelerationDiagnostics', "ACC FDR retained"),
    ('SampleVelocityDiagnostics', "VEL FDR retained"),
    ('SampleGroundTakeoffDiagnostics', "ground/takeoff FDR retained"),
    ('AERISFlightDataArchive.QueueArchive', "FDR archive handoff retained"),
):
    suite.check(token in recorder, label)

for cap in ('SetpointGuidance', 'GroundPropulsionTest', 'AutoTakeoff', 'LearningCorridor',
            'EnvelopeSurvey', 'AntiStallEvent', 'ControlAuthorityTelemetry', 'GroundAssistStop'):
    suite.check(f'AddCapability(values, AERISAutomationCapability.{cap})' in manager_v2,
                f"external capability retained: {cap}")

suite.check('LATERAL — NORMAL AP' in window, "normal lateral UI retained")
suite.check('VERTICAL — NORMAL AP' in window, "normal vertical UI retained")
suite.check('SPEED — NORMAL AP' in window, "normal speed UI retained")
suite.finish()
