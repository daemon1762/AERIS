using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class AERIS54LandR2PureTests
{
    private static void Fail(string message)
    {
        throw new InvalidOperationException(message);
    }

    private static void RequireField(Type type, string name)
    {
        if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) == null)
            Fail(type.FullName + " missing field " + name);
    }

    private static object Field(Type type, object value, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    private static void RequireEqual(object expected, object actual, string name)
    {
        if (!object.Equals(expected, actual)) Fail(name + " expected " + expected + " but was " + actual);
    }

    private static void TestSnapshotContract(Assembly aeris)
    {
        Type t = aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachObstacleSnapshot", true);
        string[] names = {
            "BodyName", "EnvironmentSignature", "AirfieldDatabaseRevision",
            "RunwayGeometryRevision", "TerrainRequestGeneration",
            "TerrainDatabaseGeneration", "TerrainCoverageComplete",
            "ObstacleCoverageComplete", "MinimumTerrainSourceLod",
            "TerrainMissedApproachClear"
        };
        for (int i = 0; i < names.Length; i++) RequireField(t, names[i]);
    }

    private static void TestSnapshotDefaultsAndClone(Assembly aeris)
    {
        Type snapshotType = aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachObstacleSnapshot", true);
        object snapshot = Activator.CreateInstance(snapshotType, true);
        RequireEqual(string.Empty, Field(snapshotType, snapshot, "BodyName"), "BodyName default");
        RequireEqual(string.Empty, Field(snapshotType, snapshot, "EnvironmentSignature"), "EnvironmentSignature default");
        RequireEqual(-1, Field(snapshotType, snapshot, "MinimumTerrainSourceLod"), "MinimumTerrainSourceLod default");
        RequireEqual(false, Field(snapshotType, snapshot, "TerrainCoverageComplete"), "TerrainCoverageComplete default");
        RequireEqual(false, Field(snapshotType, snapshot, "ObstacleCoverageComplete"), "ObstacleCoverageComplete default");
        RequireEqual(false, Field(snapshotType, snapshot, "TerrainMissedApproachClear"), "TerrainMissedApproachClear default");

        string[] scalarNames = {
            "BodyName", "EnvironmentSignature", "AirfieldDatabaseRevision",
            "RunwayGeometryRevision", "TerrainRequestGeneration",
            "TerrainDatabaseGeneration", "TerrainCoverageComplete",
            "ObstacleCoverageComplete", "MinimumTerrainSourceLod",
            "TerrainMissedApproachClear"
        };
        object[] scalarValues = {
            "body", "environment", 11L, 12L, 13L, 14L, true, true, 7, true
        };
        for (int i = 0; i < scalarNames.Length; i++)
            snapshotType.GetField(scalarNames[i], BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(snapshot, scalarValues[i]);

        Type sampleType = aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachObstacleSample", true);
        object sample = Activator.CreateInstance(sampleType, true);
        Type samplesType = typeof(List<>).MakeGenericType(sampleType);
        IList samples = (IList)Activator.CreateInstance(samplesType);
        samples.Add(sample);
        snapshotType.GetField("Samples", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(snapshot, samples);

        object clone = snapshotType.GetMethod("Clone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(snapshot, null);
        for (int i = 0; i < scalarNames.Length; i++)
            RequireEqual(scalarValues[i], Field(snapshotType, clone, scalarNames[i]), scalarNames[i] + " clone");
        IList clonedSamples = (IList)Field(snapshotType, clone, "Samples");
        if (object.ReferenceEquals(samples, clonedSamples) || object.ReferenceEquals(sample, clonedSamples[0]))
            Fail("Samples clone is not deep");
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) Fail("usage: AERIS54_LAND_R2_pure_tests.exe <AERISFlightControl.dll>");
            if (!File.Exists(args[0])) Fail("missing assembly " + args[0]);
            string managedDirectory = Environment.GetEnvironmentVariable("AERIS_KSP_MANAGED");
            if (!string.IsNullOrEmpty(managedDirectory))
            {
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs resolveArgs)
                {
                    string name = new AssemblyName(resolveArgs.Name).Name + ".dll";
                    string candidate = Path.Combine(managedDirectory, name);
                    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                };
            }
            Assembly aeris = Assembly.LoadFrom(args[0]);
            TestSnapshotContract(aeris);
            TestSnapshotDefaultsAndClone(aeris);
            Console.WriteLine("AERIS54_LAND_R2_PURE_TESTS=PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
