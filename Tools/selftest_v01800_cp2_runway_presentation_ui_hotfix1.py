#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 runway presentation UI hotfix 1")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")

suite.check("plot.y + plot.height * Mathf.Clamp01(anchorV)" in nd,
            "off-scale runway pointer originates from the aircraft map anchor")
suite.check("Vector2 origin = plot.center" not in nd,
            "legacy plot-center pointer origin is removed")
suite.check("FormatDistance(distanceMeters)" in nd,
            "off-scale pointer is labelled with current runway distance")
suite.check("CurrentRunwayDistanceMeters" in nd,
            "off-scale distance is recomputed from the current map center")
suite.check("APP " in nd and "ApproachButtonLabel" in nd,
            "approach-direction selector is present")
suite.check("previewDirectionIndex ==" in nd and
            "runway.DirectionASelectableIndex" in nd and
            "runway.DirectionBSelectableIndex" in nd,
            "direction selector toggles only the two ends of the preview runway")
arm = nd.find('new GUIContent("ARM"')
center = nd.find('new GUIContent(compact ? "CTR" : "CENTER"')
direction = nd.find('new GUIContent(directionLabel')
select = nd.find('new GUIContent(compact ? "SEL" : "SELECT"')
clear = nd.find('new GUIContent(compact ? "CLR" : "CLEAR"')
suite.check(-1 not in (arm, center, direction, select, clear) and
            arm < center < direction < select < clear,
            "bottom control order is ARM CENTER APPROACH SELECT CLEAR")
suite.check("available / weightTotal" in nd and "panel.width" in nd,
            "button widths are derived from available panel width")
suite.check('"CLR SEL"' not in nd,
            "old top-right CLR SEL button is removed")
suite.check("ClearSelectionFromNd" in nd and
            'core.Landing.Disarm("ND selection cleared")' in nd,
            "CLEAR disarms LAND and clears the selection")
identity = "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1"
suite.check(identity in build and identity in generated,
            "current build identity is consistent")
suite.finish()
