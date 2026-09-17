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

    private static void TestReadServiceContract(Assembly aeris)
    {
        Type t = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorReadService", true);
        if (t.GetMethod("TryCapturePlan",
            BindingFlags.Instance | BindingFlags.NonPublic) == null)
            Fail("TryCapturePlan missing");
        if (t.GetMethod("SubmitRead",
            BindingFlags.Instance | BindingFlags.NonPublic) == null)
            Fail("SubmitRead missing");
    }

    private static object CreateReadService(Assembly aeris, out object database)
    {
        Type databaseType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainPreloadDatabase", true);
        Type mapType = aeris.GetType(
            "AERISFlightControl.Performance.AERISMapDramCache", true);
        ConstructorInfo databaseConstructor = databaseType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new Type[] { typeof(string), typeof(long), mapType }, null);
        string root = Path.Combine(Path.GetTempPath(),
            "aeris54-read-service-test-" + Guid.NewGuid().ToString("N"));
        database = databaseConstructor.Invoke(new object[] { root, long.MaxValue, null });

        Type serviceType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorReadService", true);
        Type warmType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainWarmTileCache", true);
        Type performanceType = aeris.GetType(
            "AERISFlightControl.Performance.AERISPerformanceRuntime", true);
        ConstructorInfo serviceConstructor = serviceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new Type[] { databaseType, warmType, performanceType }, null);
        return serviceConstructor.Invoke(new object[] { database, null, null });
    }

    private static object CreateQueryPoint(Assembly aeris, int index,
        double latitude, double longitude)
    {
        Type pointType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorQueryPoint", true);
        object point = Activator.CreateInstance(pointType, true);
        pointType.GetField("Index", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(point, index);
        pointType.GetField("LatitudeDeg", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(point, latitude);
        pointType.GetField("LongitudeDeg", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(point, longitude);
        return point;
    }

    private static IList CreateQueryList(Assembly aeris, params object[] points)
    {
        Type pointType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorQueryPoint", true);
        IList values = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(pointType));
        for (int i = 0; i < points.Length; i++) values.Add(points[i]);
        return values;
    }

    private static object CapturePlan(Assembly aeris, object service, IList points)
    {
        MethodInfo capture = service.GetType().GetMethod("TryCapturePlan",
            BindingFlags.Instance | BindingFlags.NonPublic);
        object[] args = {
            "direction", "Kerbin", 600000.0, "environment", "game-data",
            11L, 12L, 13L, points, null, null
        };
        if (!(bool)capture.Invoke(service, args))
            Fail("TryCapturePlan failed: " + (args[10] ?? string.Empty));
        return args[9];
    }

    private static void TestReadPlanCopiesQueriesAndMarksMissingCoverage(Assembly aeris)
    {
        object database;
        object service = CreateReadService(aeris, out database);
        object point = CreateQueryPoint(aeris, 7, 12.5, -34.25);
        object plan = CapturePlan(aeris, service, CreateQueryList(aeris, point));
        Type pointType = point.GetType();
        pointType.GetField("LatitudeDeg", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(point, 80.0);

        Type planType = plan.GetType();
        Array copiedPoints = (Array)Field(planType, plan, "QueryPoints");
        if (object.ReferenceEquals(point, copiedPoints.GetValue(0)))
            Fail("QueryPoints did not clone point objects");
        RequireEqual(12.5, Field(pointType, copiedPoints.GetValue(0), "LatitudeDeg"),
            "copied query latitude");
        RequireEqual(false, Field(planType, plan, "TerrainCoverageComplete"),
            "missing terrain coverage");
        RequireEqual(string.Empty,
            ((string[])Field(planType, plan, "PointTileStableIds"))[0],
            "missing point tile mapping");
        RequireEqual(-1, ((int[])Field(planType, plan, "PointSourceLods"))[0],
            "missing point source lod");
    }

    private static object KeyForPoint(object service, object lod,
        double latitude, double longitude)
    {
        MethodInfo keyForPoint = service.GetType().GetMethod("KeyForPoint",
            BindingFlags.Static | BindingFlags.NonPublic);
        return keyForPoint.Invoke(null, new object[] {
            "Kerbin", 600000.0, "environment", lod, latitude, longitude
        });
    }

    private static string StableId(object key)
    {
        return (string)key.GetType().GetProperty("StableId",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(key, null);
    }

    private static void AddAcceptedMetadata(object database, object key, Assembly aeris,
        long generationUtcTicks)
    {
        Type databaseType = database.GetType();
        Type entryType = databaseType.GetNestedType("IndexEntry",
            BindingFlags.NonPublic);
        object entry = Activator.CreateInstance(entryType, true);
        entryType.GetField("StableId", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, StableId(key));
        entryType.GetField("Key", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, key);
        entryType.GetField("ChunkId", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, "chunk");
        entryType.GetField("Quality", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, 100);
        entryType.GetField("GenerationUtcTicks",
            BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, generationUtcTicks);
        Type stateType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainGenerationState", true);
        entryType.GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(entry, Enum.Parse(stateType, "Complete"));
        IDictionary index = (IDictionary)databaseType.GetField("tileIndex",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(database);
        index[StableId(key)] = entry;
    }

    private static void TestReadPlanSelectsLandAndDeduplicatesTiles(Assembly aeris)
    {
        object database;
        object service = CreateReadService(aeris, out database);
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object land = KeyForPoint(service, Enum.Parse(lodType, "Land"), 1.25, 2.5);
        object far = KeyForPoint(service, Enum.Parse(lodType, "Far"), 1.25, 2.5);
        AddAcceptedMetadata(database, far, aeris, 100L);
        AddAcceptedMetadata(database, land, aeris, 101L);
        object plan = CapturePlan(aeris, service, CreateQueryList(aeris,
            CreateQueryPoint(aeris, 0, 1.25, 2.5),
            CreateQueryPoint(aeris, 1, 1.25, 2.5)));

        Type planType = plan.GetType();
        RequireEqual(true, Field(planType, plan, "TerrainCoverageComplete"),
            "accepted terrain coverage");
        Array keys = (Array)Field(planType, plan, "TileKeys");
        RequireEqual(1, keys.Length, "deduplicated tile count");
        string[] mappings = (string[])Field(planType, plan, "PointTileStableIds");
        RequireEqual(StableId(land), mappings[0], "first selected tile");
        RequireEqual(StableId(land), mappings[1], "second selected tile");
        int[] lods = (int[])Field(planType, plan, "PointSourceLods");
        RequireEqual(4, lods[0], "first selected lod");
        RequireEqual(4, lods[1], "second selected lod");
    }

    private static void TestReadPlanFreshnessTracksOnlyRequiredTiles(Assembly aeris)
    {
        object database;
        object service = CreateReadService(aeris, out database);
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object land = KeyForPoint(service, Enum.Parse(lodType, "Land"), 3.0, 4.0);
        AddAcceptedMetadata(database, land, aeris, 200L);
        object plan = CapturePlan(aeris, service, CreateQueryList(aeris,
            CreateQueryPoint(aeris, 0, 3.0, 4.0)));
        Type planType = plan.GetType();
        RequireField(planType, "TileGenerationUtcTicks");
        long[] generations = (long[])Field(planType, plan,
            "TileGenerationUtcTicks");
        RequireEqual(1, generations.Length, "required tile generation count");
        RequireEqual(200L, generations[0], "required tile generation");

        object unrelated = KeyForPoint(service, Enum.Parse(lodType, "Land"),
            30.0, 40.0);
        AddAcceptedMetadata(database, unrelated, aeris, 300L);
        database.GetType().GetField("databaseGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(database, 999L);
        MethodInfo current = service.GetType().GetMethod("IsPlanCurrent",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (current == null) Fail("IsPlanCurrent missing");
        RequireEqual(true, current.Invoke(service, new object[] { plan }),
            "unrelated append plan freshness");

        AddAcceptedMetadata(database, land, aeris, 201L);
        RequireEqual(false, current.Invoke(service, new object[] { plan }),
            "required tile replacement plan freshness");
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
            TestReadServiceContract(aeris);
            TestReadPlanCopiesQueriesAndMarksMissingCoverage(aeris);
            TestReadPlanSelectsLandAndDeduplicatesTiles(aeris);
            TestReadPlanFreshnessTracksOnlyRequiredTiles(aeris);
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
