#!/usr/bin/env python3
"""AERIS53 Gate 3 targeted regression for GAP3-01.

This is a source-level bounded contract because AERISWindow depends on KSP/Unity IMGUI.
It verifies both safety layers introduced for the finding:
  1) shortcut capture is cancelled whenever its visible OPTIONS lifetime ends;
  2) Emergency Disable is dispatched before the ordinary capture guard.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
window = (root / "Source/AERISFlightControl/UI/AERISWindow.cs").read_text(encoding="utf-8", errors="strict")
bootstrap = (root / "Source/AERISFlightControl/Core/AERISBootstrap.cs").read_text(encoding="utf-8", errors="strict")

checks = []

def check(name, ok):
    checks.append((name, bool(ok)))

# Central reset contract.
clear = window.split("void ClearShortcutCapture()", 1)[1].split("void CancelShortcutCapture", 1)[0]
check("clear resets capture action", "hotkeyCaptureAction=-1" in clear)
check("clear resets primary key", "capturePrimary=KeyCode.None" in clear)
check("clear resets secondary key", "captureSecondary=KeyCode.None" in clear)

# Capture lifetime must end when the UI surface that owns it ends.
check("main-tab change cancels capture", 'CancelShortcutCapture("main tab changed")' in window)
check("SYSTEM-page change cancels capture", 'CancelShortcutCapture("SYSTEM page changed")' in window)
check("Close button cancels capture", 'CancelShortcutCapture("window closed")' in window)
check("toolbar/window hide cancels capture", 'CancelShortcutCapture("window hidden")' in window)
check("scene boundary cancels capture", 'CancelShortcutCapture("scene boundary")' in window)

# Normal explicit capture completion/cancellation uses the centralized state reset.
check("APPLY clears capture state", "ClearShortcutCapture();" in window.split("void DrawShortcutCaptureBox()", 1)[1].split("void ConsumeShortcutCaptureInput()", 1)[0])
check("ESC cancels capture state", 'CancelShortcutCapture("escape key")' in window)

# Safety authority: Emergency Disable must be tested before capture suppresses ordinary shortcuts.
proc = bootstrap.split("void ProcessHotkeys()", 1)[1].split("void ", 1)[0]
emergency = proc.find("IsShortcutTriggered(AERISShortcutAction.EmergencyDisable)")
guard = proc.find("if(window.IsCapturingShortcut) return;")
master = proc.find("IsShortcutTriggered(AERISShortcutAction.ToggleMaster)")
window_toggle = proc.find("IsShortcutTriggered(AERISShortcutAction.ToggleWindow)")
check("Emergency Disable is present", emergency >= 0)
check("capture guard is present", guard >= 0)
check("Emergency Disable precedes capture guard", 0 <= emergency < guard)
check("Toggle MASTER remains behind capture guard", guard < master if master >= 0 else False)
check("Toggle window remains behind capture guard", guard < window_toggle if window_toggle >= 0 else False)
check("initial null guard does not suppress all capture", "window.IsCapturingShortcut) return" not in proc[:max(guard, 0)])

failed = [name for name, ok in checks if not ok]
print("=== AERIS53 GAP3-01 TARGETED REGRESSION ===")
for name, ok in checks:
    print(("PASS" if ok else "FAIL") + ": " + name)
print(f"checks={len(checks)} pass={len(checks)-len(failed)} fail={len(failed)}")
if failed:
    raise SystemExit(1)
