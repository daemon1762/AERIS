#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import SOURCE, CheckSuite, read

suite = CheckSuite("v0.16.3.0 first-gate improvements")
addon = read(SOURCE / "Integrations" / "AddonIntegration.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")

for token, label in (
    ("AppDomain.CurrentDomain.GetAssemblies()", "APP loaded-assembly detection"),
    ('string.Equals(name, "AutoPropPitch"', "APP exact assembly-name detection"),
    ('name.StartsWith("AutoPropPitch."', "APP namespaced assembly detection"),
    ("IsInstalled = ids.Length > 0 || assemblyLoaded", "APP install and provider states are separated"),
    ("IsApiReady = ids.Length > 0", "APP API readiness is provider-based"),
    ("Installed / propulsion provider API not registered", "APP installed/API-not-ready state is explicit"),
): suite.check(token in addon, label)

for token, label in (
    ("dlcDefinedCount", "DLC defined count"),
    ("dlcDetectedCount", "DLC detected count"),
    ('dlcDetectedCount + "/" +', "DLC detected/defined display"),
    ("if (airfields[i].ProviderDetected) dlcDetectedCount++", "runtime detection drives DLC detected count"),
): suite.check(token in registry, label)
suite.finish()
