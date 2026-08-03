#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 runway map lock hotfix 2 compile hotfix 1")
projection = read(ROOT / "Source/AERISFlightControl/Terrain/AERISNdMapProjection.cs")
settings = read(ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")

suite.check("using AERISFlightControl.Settings;" in projection,
            "projection imports Settings namespace containing render orientation enum")
suite.check("internal enum AERISTerrainRenderTargetOrientation" in settings,
            "render target orientation enum remains defined in Settings namespace")
suite.check("internal AERISTerrainRenderTargetOrientation Orientation;" in projection and
            "AERISTerrainRenderTargetOrientation orientation" in projection,
            "projection references the imported orientation type")
suite.check('<Compile Include="Terrain\\AERISNdMapProjection.cs" />' in csproj and
            '<Compile Include="Settings\\AERISSettings.cs" />' in csproj,
            "both dependent source files are compiled by xbuild")
identity = "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1"
suite.check(identity in build and identity in generated,
            "compile hotfix build identity is consistent")
suite.finish()
