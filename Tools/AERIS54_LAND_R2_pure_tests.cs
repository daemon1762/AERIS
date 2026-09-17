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

    private static void RequireNear(double expected, double actual, double tolerance,
        string name)
    {
        if (Math.Abs(expected - actual) > tolerance)
            Fail(name + " expected " + expected + " but was " + actual);
    }

    private static void SetField(Type type, object value, string name, object fieldValue)
    {
        FieldInfo field = type.GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) Fail(type.FullName + " missing field " + name);
        field.SetValue(value, fieldValue);
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
        return CreateReadService(aeris, null, out database);
    }

    private static object CreateReadService(Assembly aeris, object warm,
        out object database)
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
        return serviceConstructor.Invoke(new object[] { database, warm, null });
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

    private static void TestReadResultRejectsStaleWarmGeneration(Assembly aeris)
    {
        Type warmType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainWarmTileCache", true);
        object warm = Activator.CreateInstance(warmType,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { 8L * 1024L * 1024L }, null);
        object database;
        object service = CreateReadService(aeris, warm, out database);
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object land = KeyForPoint(service, Enum.Parse(lodType, "Land"), 5.0, 6.0);
        AddAcceptedMetadata(database, land, aeris, 401L);
        object plan = CapturePlan(aeris, service, CreateQueryList(aeris,
            CreateQueryPoint(aeris, 0, 5.0, 6.0)));

        Type tileType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainHeightTile", true);
        object tile = Activator.CreateInstance(tileType, true);
        tileType.GetField("Key", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, land);
        tileType.GetField("Resolution", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, 2);
        tileType.GetField("Elevation", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, new float[] { 10f, 10f, 10f, 10f });
        tileType.GetField("Flags", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, new byte[] { 0, 0, 0, 0 });
        tileType.GetField("CreatedUtcTicks", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, 400L);
        tileType.GetField("Quality", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, 100);
        tileType.GetField("SamplingComplete", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tile, true);

        Type codecType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainPreloadCodec", true);
        Type codecIdType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCodecId", true);
        object encoded = codecType.GetMethod("Encode",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new object[] { tile, "environment", "game-data", 0L,
                Enum.Parse(codecIdType, "Raw") });
        warmType.GetMethod("Put", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(warm, new object[] { encoded, 2 });

        Type keyType = land.GetType();
        Type keysType = typeof(List<>).MakeGenericType(keyType);
        IList keys = (IList)Activator.CreateInstance(keysType);
        keys.Add(land);
        Type outputType = typeof(Dictionary<,>).MakeGenericType(typeof(string), tileType);
        IDictionary loaded = (IDictionary)Activator.CreateInstance(outputType);
        MethodInfo load = database.GetType().GetMethod("TryLoadBatch",
            BindingFlags.Instance | BindingFlags.NonPublic);
        bool loadedAny = (bool)load.Invoke(database,
            new object[] { keys, warm, loaded, null, "game-data" });
        RequireEqual(true, loadedAny, "stale warm payload loaded");
        object decoded = loaded[StableId(land)];
        RequireEqual(400L, Field(tileType, decoded, "CreatedUtcTicks"),
            "stale warm decoded generation");

        MethodInfo buildResult = service.GetType().GetMethod("BuildReadResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (buildResult == null) Fail("BuildReadResult missing");
        object result = buildResult.Invoke(null,
            new object[] { plan, loaded, loadedAny });
        Type resultType = result.GetType();
        IDictionary resultTiles = (IDictionary)Field(resultType, result, "Tiles");
        RequireEqual(0, resultTiles.Count, "stale warm result tile count");
        RequireEqual(false, Field(resultType, result, "TerrainCoverageComplete"),
            "stale warm result coverage");
        RequireEqual("TERRAIN_TILE_GENERATION_MISMATCH",
            Field(resultType, result, "FailureReason"),
            "stale warm failure reason");
    }

    private sealed class PointSpec
    {
        internal int Index;
        internal double Latitude;
        internal double Longitude;
        internal double Along;
        internal double Cross;
        internal bool Missed;
    }

    private static object CreateIdentity(Assembly aeris)
    {
        Type type = aeris.GetType(
            "AERISFlightControl.Landing.AERISTerrainCorridorIdentity", true);
        object identity = Activator.CreateInstance(type, true);
        SetField(type, identity, "DirectionStableId", "direction");
        SetField(type, identity, "BodyName", "Kerbin");
        SetField(type, identity, "BodyRadiusMeters", 600000.0);
        SetField(type, identity, "EnvironmentSignature", "environment");
        SetField(type, identity, "AirfieldDatabaseRevision", 11L);
        SetField(type, identity, "RunwayGeometryRevision", 12L);
        SetField(type, identity, "TerrainRequestGeneration", 13L);
        SetField(type, identity, "TerrainDatabaseGeneration", 14L);
        SetField(type, identity, "ProducerGeneration", 15L);
        return identity;
    }

    private static object CreateSyntheticKey(Assembly aeris, string body,
        object lod, int latitudeIndex, int longitudeIndex)
    {
        Type keyType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileKey", true);
        ConstructorInfo constructor = keyType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new Type[] { typeof(string), typeof(double), typeof(string),
                lod.GetType(), typeof(int), typeof(int) }, null);
        return constructor.Invoke(new object[] {
            body, 600000.0, "environment", lod, latitudeIndex, longitudeIndex
        });
    }

    private static object CreateSyntheticTile(Assembly aeris, object key,
        int resolution, float[] elevations, int quality, bool complete,
        double south, double north, double west, double east)
    {
        Type tileType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainHeightTile", true);
        object tile = Activator.CreateInstance(tileType, true);
        SetField(tileType, tile, "Key", key);
        SetField(tileType, tile, "Resolution", resolution);
        SetField(tileType, tile, "SouthLatitudeDeg", south);
        SetField(tileType, tile, "NorthLatitudeDeg", north);
        SetField(tileType, tile, "WestLongitudeDeg", west);
        SetField(tileType, tile, "EastLongitudeDeg", east);
        SetField(tileType, tile, "Elevation", elevations);
        SetField(tileType, tile, "Quality", quality);
        SetField(tileType, tile, "SamplingComplete", complete);
        return tile;
    }

    private static IDictionary CreateTileDictionary(Assembly aeris,
        params object[] tiles)
    {
        Type tileType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainHeightTile", true);
        IDictionary values = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), tileType));
        for (int i = 0; i < tiles.Length; i++)
        {
            object key = Field(tileType, tiles[i], "Key");
            values[StableId(key)] = tiles[i];
        }
        return values;
    }

    private static object CreateAnalysisInput(Assembly aeris, PointSpec[] specs,
        string[] mappings, int[] sourceLods, IDictionary tiles,
        bool readCoverageComplete, double requiredMissedAltitude)
    {
        Type pointType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorQueryPoint", true);
        Array points = Array.CreateInstance(pointType, specs.Length);
        for (int i = 0; i < specs.Length; i++)
        {
            object point = Activator.CreateInstance(pointType, true);
            SetField(pointType, point, "Index", specs[i].Index);
            SetField(pointType, point, "LatitudeDeg", specs[i].Latitude);
            SetField(pointType, point, "LongitudeDeg", specs[i].Longitude);
            SetField(pointType, point, "AlongTrackMeters", specs[i].Along);
            SetField(pointType, point, "CrossTrackMeters", specs[i].Cross);
            SetField(pointType, point, "MissedApproach", specs[i].Missed);
            points.SetValue(point, i);
        }

        Type planType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorReadPlan", true);
        object plan = Activator.CreateInstance(planType, true);
        SetField(planType, plan, "QueryPoints", points);
        SetField(planType, plan, "PointTileStableIds", mappings);
        SetField(planType, plan, "PointSourceLods", sourceLods);
        SetField(planType, plan, "TerrainCoverageComplete", readCoverageComplete);

        Type resultType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainCorridorReadResult", true);
        object result = Activator.CreateInstance(resultType, true);
        SetField(resultType, result, "Plan", plan);
        SetField(resultType, result, "Tiles", tiles);
        SetField(resultType, result, "TerrainCoverageComplete",
            readCoverageComplete);

        Type inputType = aeris.GetType(
            "AERISFlightControl.Landing.AERISTerrainCorridorComputeInput", true);
        object input = Activator.CreateInstance(inputType, true);
        SetField(inputType, input, "Identity", CreateIdentity(aeris));
        SetField(inputType, input, "ThresholdLatitudeDeg", 0.0);
        SetField(inputType, input, "ThresholdLongitudeDeg", 0.0);
        SetField(inputType, input, "ThresholdElevationMeters", 70.0);
        SetField(inputType, input, "InboundHeadingDeg", 90.0);
        SetField(inputType, input, "MissedApproachHeadingDeg", 90.0);
        SetField(inputType, input, "RequiredMissedApproachAltitudeMeters",
            requiredMissedAltitude);
        SetField(inputType, input, "Limits", Activator.CreateInstance(aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachPlanningLimits", true), true));
        SetField(inputType, input, "ReadResult", result);
        return input;
    }

    private static object Analyze(Assembly aeris, object input)
    {
        Type producer = aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachTerrainCorridorProducer", true);
        MethodInfo method = producer.GetMethod("AnalyzePure",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null) Fail("AnalyzePure missing");
        return method.Invoke(null, new object[] { input });
    }

    private static PointSpec Point(int index, double latitude, double longitude,
        double along, double cross, bool missed)
    {
        return new PointSpec {
            Index = index, Latitude = latitude, Longitude = longitude,
            Along = along, Cross = cross, Missed = missed
        };
    }

    private static void TestProducerPriorityAndIdentity(Assembly aeris)
    {
        Type producer = aeris.GetType(
            "AERISFlightControl.Landing.AERISApproachTerrainCorridorProducer", true);
        MethodInfo priority = producer.GetMethod("PriorityRank",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (priority == null) Fail("PriorityRank missing");
        RequireEqual(0, priority.Invoke(null, new object[] { true, true, true }),
            "armed priority");
        RequireEqual(1, priority.Invoke(null, new object[] { false, true, true }),
            "selected priority");
        RequireEqual(2, priority.Invoke(null, new object[] { false, false, true }),
            "active body priority");
        RequireEqual(3, priority.Invoke(null, new object[] { false, false, false }),
            "other body priority");

        MethodInfo matches = producer.GetMethod("IdentityMatches",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (matches == null) Fail("IdentityMatches missing");
        object expected = CreateIdentity(aeris);
        object current = CreateIdentity(aeris);
        RequireEqual(true, matches.Invoke(null, new object[] { expected, current }),
            "equal identity");
        Type identityType = expected.GetType();
        SetField(identityType, current, "EnvironmentSignature", "changed");
        RequireEqual(false, matches.Invoke(null, new object[] { expected, current }),
            "environment identity mismatch");
        current = CreateIdentity(aeris);
        SetField(identityType, current, "RunwayGeometryRevision", 99L);
        RequireEqual(false, matches.Invoke(null, new object[] { expected, current }),
            "runway identity mismatch");
        current = CreateIdentity(aeris);
        SetField(identityType, current, "TerrainRequestGeneration", 99L);
        RequireEqual(false, matches.Invoke(null, new object[] { expected, current }),
            "terrain request identity mismatch");
        RequireEqual(false, matches.Invoke(null, new object[] { expected, null }),
            "null current identity mismatch");
    }

    private static void TestSyntheticInterpolationSignatureAndFailClosedFlags(
        Assembly aeris)
    {
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object key = CreateSyntheticKey(aeris, "Kerbin",
            Enum.Parse(lodType, "Land"), 0, 0);
        float[] elevations = { 100f, 200f, 300f, 400f };
        object tile = CreateSyntheticTile(aeris, key, 2, elevations, 100, true,
            0.0, 1.0, 0.0, 1.0);
        string stableId = StableId(key);
        PointSpec point = Point(0, 0.5, 0.5, 123.0, -7.0, false);
        IDictionary tiles = CreateTileDictionary(aeris, tile);
        string[] mappings = { stableId };
        int[] lods = { 4 };
        object input = CreateAnalysisInput(aeris, new PointSpec[] { point },
            mappings, lods, tiles, true, 300.0);
        object first = Analyze(aeris, input);
        object second = Analyze(aeris, input);
        Type snapshotType = first.GetType();
        IList samples = (IList)Field(snapshotType, first, "Samples");
        RequireEqual(1, samples.Count, "interpolated sample count");
        Type sampleType = samples[0].GetType();
        RequireEqual(true, Field(sampleType, samples[0], "IsTerrain"),
            "interpolated sample terrain flag");
        RequireEqual(stableId, Field(sampleType, samples[0], "SourceId"),
            "interpolated sample source");
        RequireNear(250.0, (double)Field(sampleType, samples[0],
            "TopElevationMeters"), 0.001, "bilinear center elevation");
        RequireNear(123.0, (double)Field(sampleType, samples[0],
            "AlongTrackMeters"), 0.001, "copied along track");
        RequireNear(-7.0, (double)Field(sampleType, samples[0],
            "CrossTrackMeters"), 0.001, "copied cross track");
        RequireEqual(Field(snapshotType, first, "TerrainSignature"),
            Field(snapshotType, second, "TerrainSignature"),
            "deterministic terrain signature");
        RequireEqual("1C621A8DC906F4CB",
            Field(snapshotType, first, "TerrainSignature"),
            "exact invariant terrain signature material");
        if (string.IsNullOrEmpty((string)Field(snapshotType, first,
            "TerrainSignature"))) Fail("terrain signature empty");
        RequireEqual(true, Field(snapshotType, first, "TerrainCoverageComplete"),
            "complete terrain coverage");
        RequireEqual(false, Field(snapshotType, first, "ObstacleCoverageComplete"),
            "fail-closed obstacle coverage");
        RequireEqual(false, Field(snapshotType, first, "CorridorComplete"),
            "fail-closed corridor");
        RequireEqual(false, Field(snapshotType, first, "MissedApproachClear"),
            "fail-closed missed approach");
        RequireEqual("R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE",
            Field(snapshotType, first, "ObstacleSignature"),
            "terrain-only obstacle signature");
        RequireEqual(4, Field(snapshotType, first, "MinimumTerrainSourceLod"),
            "best observed terrain lod");
        RequireEqual(false, Field(snapshotType, first,
            "TerrainMissedApproachClear"), "no missed-approach points diagnostic");

        RequireEqual(4, elevations.Length, "input elevation length unchanged");
        RequireNear(100.0, elevations[0], 0.001, "input elevation unchanged");
        RequireEqual(stableId, mappings[0], "input mapping unchanged");
        RequireEqual(4, lods[0], "input source lod unchanged");
        RequireEqual(1, tiles.Count, "input tiles unchanged");
        Type inputType = input.GetType();
        object readResult = Field(inputType, input, "ReadResult");
        object plan = Field(readResult.GetType(), readResult, "Plan");
        Array inputPoints = (Array)Field(plan.GetType(), plan, "QueryPoints");
        object inputPoint = inputPoints.GetValue(0);
        RequireNear(0.5, (double)Field(inputPoint.GetType(), inputPoint,
            "LatitudeDeg"), 0.0, "input query latitude unchanged");
        RequireNear(123.0, (double)Field(inputPoint.GetType(), inputPoint,
            "AlongTrackMeters"), 0.0, "input query along track unchanged");
    }

    private static void TestEdgeInterpolationAndIndexOrderedSignature(Assembly aeris)
    {
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object key = CreateSyntheticKey(aeris, "Kerbin",
            Enum.Parse(lodType, "Land"), 0, 0);
        object tile = CreateSyntheticTile(aeris, key, 2,
            new float[] { 100f, 200f, 300f, 400f }, 100, true,
            0.0, 1.0, 0.0, 1.0);
        string stableId = StableId(key);
        IDictionary tiles = CreateTileDictionary(aeris, tile);
        PointSpec low = Point(0, 0.0, 0.0, 1.0, 2.0, false);
        PointSpec high = Point(1, 1.0, 1.0, 3.0, 4.0, false);
        object ordered = Analyze(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { low, high }, new string[] { stableId, stableId },
            new int[] { 1, 4 }, tiles, true, 500.0));
        object reversed = Analyze(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { high, low }, new string[] { stableId, stableId },
            new int[] { 4, 1 }, tiles, true, 500.0));
        Type snapshotType = ordered.GetType();
        IList samples = (IList)Field(snapshotType, ordered, "Samples");
        RequireNear(100.0, (double)Field(samples[0].GetType(), samples[0],
            "TopElevationMeters"), 0.001, "south-west edge elevation");
        RequireNear(400.0, (double)Field(samples[1].GetType(), samples[1],
            "TopElevationMeters"), 0.001, "north-east edge elevation");
        RequireEqual(Field(snapshotType, ordered, "TerrainSignature"),
            Field(snapshotType, reversed, "TerrainSignature"),
            "query-index ordered signature");
        RequireEqual(4, Field(snapshotType, ordered, "MinimumTerrainSourceLod"),
            "maximum numeric best observed lod");
    }

    private static void RequireIncompleteAnalysis(Assembly aeris, object input,
        string name)
    {
        object snapshot = Analyze(aeris, input);
        Type type = snapshot.GetType();
        RequireEqual(false, Field(type, snapshot, "TerrainCoverageComplete"),
            name + " terrain coverage");
        RequireEqual(false, Field(type, snapshot, "CorridorComplete"),
            name + " corridor coverage");
        RequireEqual(false, Field(type, snapshot, "TerrainMissedApproachClear"),
            name + " missed approach diagnostic");
        RequireEqual(-1, Field(type, snapshot, "MinimumTerrainSourceLod"),
            name + " source lod");
        RequireEqual(0, ((IList)Field(type, snapshot, "Samples")).Count,
            name + " sample count");
    }

    private static void TestInvalidAndMissingTilesFailClosed(Assembly aeris)
    {
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object key = CreateSyntheticKey(aeris, "Kerbin",
            Enum.Parse(lodType, "Land"), 0, 0);
        string stableId = StableId(key);
        PointSpec point = Point(0, 0.5, 0.5, 0.0, 0.0, false);
        RequireIncompleteAnalysis(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { point }, new string[] { string.Empty },
            new int[] { -1 }, CreateTileDictionary(aeris), false, 300.0),
            "missing mapping");
        RequireIncompleteAnalysis(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { point }, new string[] { stableId },
            new int[] { 4 }, CreateTileDictionary(aeris), true, 300.0),
            "missing tile");

        object[] invalidTiles = {
            CreateSyntheticTile(aeris, key, 1, new float[] { 100f }, 100, true,
                0.0, 1.0, 0.0, 1.0),
            CreateSyntheticTile(aeris, key, 2, new float[] { 100f, 200f, 300f },
                100, true, 0.0, 1.0, 0.0, 1.0),
            CreateSyntheticTile(aeris, key, 2,
                new float[] { 100f, 200f, 300f, 400f }, 99, true,
                0.0, 1.0, 0.0, 1.0),
            CreateSyntheticTile(aeris, key, 2,
                new float[] { 100f, 200f, 300f, 400f }, 100, false,
                0.0, 1.0, 0.0, 1.0)
        };
        string[] names = { "invalid resolution", "short elevation array",
            "insufficient quality", "incomplete sampling" };
        for (int i = 0; i < invalidTiles.Length; i++)
            RequireIncompleteAnalysis(aeris, CreateAnalysisInput(aeris,
                new PointSpec[] { point }, new string[] { stableId },
                new int[] { 4 }, CreateTileDictionary(aeris, invalidTiles[i]),
                true, 300.0), names[i]);

        object validTile = CreateSyntheticTile(aeris, key, 2,
            new float[] { 100f, 200f, 300f, 400f }, 100, true,
            0.0, 1.0, 0.0, 1.0);
        RequireIncompleteAnalysis(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { Point(0, 1.01, 0.5, 0.0, 0.0, false) },
            new string[] { stableId }, new int[] { 4 },
            CreateTileDictionary(aeris, validTile), true, 300.0),
            "point outside mapped tile");
    }

    private static void TestMissedApproachTerrainDiagnostic(Assembly aeris)
    {
        Type lodType = aeris.GetType(
            "AERISFlightControl.Terrain.AERISTerrainTileLod", true);
        object key = CreateSyntheticKey(aeris, "Kerbin",
            Enum.Parse(lodType, "Land"), 0, 0);
        object tile = CreateSyntheticTile(aeris, key, 2,
            new float[] { 100f, 100f, 100f, 100f }, 100, true,
            0.0, 1.0, 0.0, 1.0);
        string stableId = StableId(key);
        PointSpec missed = Point(0, 0.5, 0.5, -100.0, 0.0, true);
        object clear = Analyze(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { missed }, new string[] { stableId },
            new int[] { 4 }, CreateTileDictionary(aeris, tile), true, 101.0));
        Type snapshotType = clear.GetType();
        RequireEqual(true, Field(snapshotType, clear,
            "TerrainMissedApproachClear"), "terrain-only missed approach clear");
        RequireEqual(false, Field(snapshotType, clear, "MissedApproachClear"),
            "operational missed approach remains fail-closed");

        object blocked = Analyze(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { missed }, new string[] { stableId },
            new int[] { 4 }, CreateTileDictionary(aeris, tile), true, 100.0));
        RequireEqual(false, Field(snapshotType, blocked,
            "TerrainMissedApproachClear"), "equal terrain top is not below limit");

        object missing = Analyze(aeris, CreateAnalysisInput(aeris,
            new PointSpec[] { missed }, new string[] { string.Empty },
            new int[] { -1 }, CreateTileDictionary(aeris), false, 101.0));
        RequireEqual(false, Field(snapshotType, missing,
            "TerrainMissedApproachClear"), "missing missed terrain fails closed");
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
            TestReadResultRejectsStaleWarmGeneration(aeris);
            TestProducerPriorityAndIdentity(aeris);
            TestSyntheticInterpolationSignatureAndFailClosedFlags(aeris);
            TestEdgeInterpolationAndIndexOrderedSignature(aeris);
            TestInvalidAndMissingTilesFailClosed(aeris);
            TestMissedApproachTerrainDiagnostic(aeris);
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
