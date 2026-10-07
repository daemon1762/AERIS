# AERIS53 R-AA2-04 — legacy NeoGUI reference retention

Assessment date: 2026-10-08 JST.
Inspected baseline: `f85959ad330cb553640da9ddfceb5c1ad97b626a`.

## Disposition

**Not applicable to the current AERIS build; no production patch.**

The retained upstream `AA/GUI/NeoGUIController.cs` has a reference-retention
risk: `findModules()` returns when the active vessel or its module map is
unavailable without clearing cached module references, and replaces a
reference only when a corresponding module is found. Several property
setters use the cache without first rediscovering modules. These observations
describe the legacy source, not a reproduced current-game failure.

The current project does not compile this source. The current host does
not instantiate it, so this route cannot affect ordinary vessel switching
or UI operation in the candidate built by the supplied helper. Editing or
reactivating that legacy UI is outside the requested minimal bug-fix scope.

## Evidence

- `Source/AERISFlightControl/AERISFlightControl.csproj` lists its compile
  inputs explicitly. `AA/GUI/NeoGUIController.cs` is absent, with no wildcard
  Compile entries. The project imports the standard Microsoft C# targets.
- An XML-based compile-input inspection found no reference to
  `NeoGUIController` in any listed production source. Repository source
  searches found only the class definition and constructor in the excluded
  file, rather than a caller or factory registration.
- `AA/AtmosphereAutopilot.cs` explicitly states that legacy AA UI is excluded.
  Its startup does not construct a NeoGUI controller. Runtime module
  discovery enumerates the executing assembly's sealed AutopilotModule
  subclasses; this excluded controller cannot be introduced by that path.
- The R-AA2-03 desktop Release compile command reported by the user likewise
  omitted `NeoGUIController.cs`. That installed candidate passed its 171
  checks and the user subsequently reported ordinary flight without problems.
  The flight report is supporting context, not proof of this exclusion;
  the project inputs establish it.
- The existing historical
  `Tools/selftest_v01800_cp3_gate5_candidate9_ui_stability_telemetry_hotfix1.py`
  describes the legacy AA GUI as retained heritage unreachable from the
  current host. This investigation inspected that statement, without
  running or claiming a pass for the entire historical suite.

## Boundaries

No Source, test, build/install helper, DLL or settings change is made for
R-AA2-04. No game launch, new audit feature or reinstall is required for
this documentation-only disposition. The inactive file is retained with
its upstream attribution and license.

The legacy code has not been repaired or declared safe for future reuse.
If NeoGUI is intentionally enabled in a future build, reassess its cached
references and setter behavior before enabling it. This disposition does
not assert that all other active AERIS UI paths are free of similar issues.
