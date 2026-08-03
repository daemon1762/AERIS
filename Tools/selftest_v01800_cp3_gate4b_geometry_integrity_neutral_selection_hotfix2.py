#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite("v0.18.0.0 CP3 Gate4B Geometry Integrity Neutral Selection Hotfix2")
r=read(SOURCE/"Terrain/AERISTerrainGpuTileRenderer.cs")
a=read(SOURCE/"Landing/AERISAirfieldRegistry.cs")
s=read(SOURCE/"Settings/AERISSettings.cs")
g=read(SOURCE/"Properties/AERISBuildVersion.generated.cs")
b=read(ROOT/"build_ubuntu.sh")
for name,text in (("renderer",r),("airfield",a),("settings",s)):
 c=strip_csharp_comments_and_literals(text); suite.check(c.count("{")==c.count("}"),name+" braces balanced"); suite.check(c.count("(")==c.count(")"),name+" parens balanced")
suite.check("float historySurfaceRangeMeters = rangeMeters;" in r,"visible GPU authority uses exact requested range")
suite.check("system.CaptureVisible(centerLatitudeDeg,\n                centerLongitudeDeg, rangeMeters" in r,"tile ownership captured for exact visible projection")
suite.check("bool present = TryPresentReprojectedFront" not in r,"rejected temporal reprojection is not presentation authority")
suite.check(r.count("TryPresentReprojectedFront(")==1,"temporal reprojection remains definition-only quarantine")
suite.check("PresentFrontDirect(plot, orientation);" in r,"GPU FRONT is presented directly")
suite.check("RenderBackBuffer(tiles, projection, mapRotation" in r,"recovery BACK uses exact current projection")
suite.check("centerLongitudeDeg, rangeMeters, rangeMeters" in r,"committed FRONT records exact visible range")
suite.check("cpu_terrain_draw=0" in r,"CPU terrain draw remains hard zero")
suite.check("internal bool LandSelectionExplicitlyCleared = true;" in s,"selection default is explicitly neutral")
suite.check("LandSelectionExplicitlyCleared = true;\n            LandSectionExpanded" in s,"reset defaults preserve neutral selection")
suite.check("if (!startupComplete)\n                ResetSelectionForStartup();" in a,"first registry commit always resets selection")
suite.check("startup neutral; airport=NONE; runway=NONE" in a,"startup neutral state is logged")
restore=a[a.index("void RestoreSelection"):a.index("void PersistSelection")]
suite.check("if (string.IsNullOrEmpty(airfieldId)) return;" in restore,"empty saved airport never auto-selects")
suite.check("string.Equals(values[i].StableId, airfieldId" in restore and "SelectedAirfieldIndex = i;" in restore,"only explicit matching airport may restore")
suite.check("SelectedDirectionIndex = 0" not in restore,"restore has no first-runway fallback")
suite.check("if (SelectedAirfieldIndex < 0)" not in restore,"restore has no first-airfield fallback")
ui='UiCheckpoint = "DEV CP3 GATE 4B — ATTR GEOMETRY INTEGRITY & NEUTRAL SELECTION HOTFIX 2"'
suite.check(ui in g and ui in b,"tab/build identity updated")
suite.finish()
