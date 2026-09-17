# AERIS54 LAND-R2 Immutable Terrain Corridor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the LAND-R2 bounded-lazy, terrain-only corridor producer that reads accepted immutable Terrain/PTC/Preload DB data, analyzes certified runway directions off-thread, and publishes fail-closed `AERISApproachObstacleSnapshot` values without adding any flight-control authority.

**Architecture:** Add one read-only terrain corridor broker over the accepted preload database/cache and one LAND corridor producer. Main-thread code captures certified runway/body/environment/revision identity and metadata-only tile coverage; shared `AERISPerformanceRuntime` workers perform immutable batch read/decode and pure corridor analysis; main-thread commit rejects stale results and publishes cloned snapshots. Every R2 publication remains terrain-only, so `ObstacleCoverageComplete=false`, `CorridorComplete=false`, `MissedApproachClear=false`, and the existing adaptive planner remains `PENDING`.

**Tech Stack:** C# 7.2 / .NET Framework 4.7.2, KSP 1.12.5, Unity/KSP main-thread runtime, existing `AERISWorkerScheduler`, existing `AERISTerrainPreloadDatabase` V3, Bash audit tooling, Mono/xbuild, reflection-based C# pure test executable.

**Spec:** `docs/superpowers/specs/2026-09-17-aeris54-land-r2-immutable-terrain-corridor-design.md`

## Global Constraints

- Base accepted prerequisite is `agent/aeris54-preload-producer-coherence-accepted` @ `86e7fa0135dfc07016672383e0a93fc3f4dce982`.
- Work only on `agent/aeris54-land-r2-immutable-terrain-corridor-snapshots`.
- Architecture is B: bounded lazy production for every certified runway direction, with ARMED first, selected second, active-body background third, other-body background fourth.
- At most two corridor directions may be in I/O/decode or compute at once; this is an admission bound, not a worker-thread count.
- Normal synchronous PQS sampling is forbidden for LAND-R2.
- Terrain authority is the accepted Terrain/PTC/Preload DB/cache stack, including the accepted producer-coherence effective environment identity.
- Workers may receive only copied/plain data; no `CelestialBody`, `Vessel`, `PQS`, `PQSMod`, `Transform`, `GameObject`, or mutable Unity/KSP runtime object may cross the worker boundary.
- Missing required accepted terrain stays fail-closed; LAND-R2 never generates the missing terrain itself.
- Every R2 snapshot must keep `ObstacleCoverageComplete=false`, `CorridorComplete=false`, and `MissedApproachClear=false`.
- LAND control authority remains `NONE / PILOT`; do not write AP, AA/FBW, PROTECT, throttle, brake, steering, or `FlightCtrlState` state.
- Do not change the accepted ENV4/preload database format, producer-coherence policy, runway-provider logic, ND renderer, or NEW_NAV.
- The user-facing build/install workflow must remain one command and must support both desktop KSP (`$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program`) and laptop KSP (`$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program`).

---

## File Structure

### New production files

- `Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs`
  - Plain-value LAND terrain read contracts.
  - Metadata-only LOD/key selection against accepted preload DB index.
  - Bounded worker submission for `TryLoadBatch`.
  - Deep-cloned immutable tile/result publication back to the main-thread commit callback.
  - No PQS APIs.

- `Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs`
  - Enumerates certified runway directions.
  - Implements B-priority queue and two-direction admission bound.
  - Builds runway-relative query points on the main thread.
  - Submits read phase, then pure analysis phase, through `AERISWorkerScheduler`.
  - Performs deterministic interpolation/signature/terrain-only missed-approach diagnostics.
  - Rejects stale identity/revision results before publication.
  - Stores cloned snapshot dictionary and publication generation.

### New test/audit files

- `Tools/AERIS54_LAND_R2_pure_tests.cs`
  - Reflection-based executable that loads the compiled plugin and verifies internal LAND-R2 pure seams with synthetic values.

- `Tools/aeris54_land_r2_candidate.sh`
  - Static architecture gate, clean build/install, pure test compile/run, runtime log arming and second-pass acceptance audit.

### Existing files modified

- `Source/AERISFlightControl/Landing/AERISApproachModels.cs`
  - Extend obstacle snapshot identity and terrain/obstacle coverage metadata.

- `Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs`
  - Own/expose one corridor read service and read-only active environment/generation identity.

- `Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs`
  - Expose the tile-system corridor read service to bootstrap without exposing preload DB internals.

- `Source/AERISFlightControl/Core/AERISBootstrap.cs`
  - Own/tick/reset producer and rebuild `AERISApproachRegistry` only on airfield DB or corridor publication generation changes.

- `Source/AERISFlightControl/AERISFlightControl.csproj`
  - Explicitly include both new production `.cs` files.

- `Tools/aeris_current_stage.sh`
  - Route LAND-R2 active stage to the candidate runner after implementation.

---

### Task 1: Extend the immutable approach snapshot contract

**Files:**
- Modify: `Source/AERISFlightControl/Landing/AERISApproachModels.cs`
- Create initially: `Tools/AERIS54_LAND_R2_pure_tests.cs`

**Interfaces:**
- Produces these fields on `AERISApproachObstacleSnapshot` for later tasks:
  - `string BodyName`
  - `string EnvironmentSignature`
  - `long AirfieldDatabaseRevision`
  - `long RunwayGeometryRevision`
  - `long TerrainRequestGeneration`
  - `long TerrainDatabaseGeneration`
  - `bool TerrainCoverageComplete`
  - `bool ObstacleCoverageComplete`
  - `int MinimumTerrainSourceLod`
  - `bool TerrainMissedApproachClear`
- Existing `Clone()` must continue deep-cloning `Samples` and must preserve all new scalar identity fields through `MemberwiseClone()`.

- [ ] **Step 1: Write the failing reflection test for the new snapshot fields**

Create `Tools/AERIS54_LAND_R2_pure_tests.cs` with a small reflection harness. The first test must load `AERISFlightControl.dll`, find `AERISFlightControl.Landing.AERISApproachObstacleSnapshot`, and require every R2 field by exact name:

```csharp
static void RequireField(Type type, string name)
{
    if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) == null)
        Fail(type.FullName + " missing field " + name);
}

static void TestSnapshotContract(Assembly aeris)
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
```

The harness `Main(string[] args)` must accept exactly one DLL path, run named tests, print `AERIS54_LAND_R2_PURE_TESTS=PASS` only when every test succeeds, and return non-zero on failure.

- [ ] **Step 2: Build current branch and run the test to verify it fails**

Desktop command:

```bash
cd "$HOME/AERIS42_R042"
KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
AERIS_PRELOAD_BRANCH="agent/aeris54-land-r2-immutable-terrain-corridor-snapshots" \
  bash Tools/AERIS_preload_build_and_go.sh desktop --no-install
mcs -out:/tmp/AERIS54_LAND_R2_pure_tests.exe Tools/AERIS54_LAND_R2_pure_tests.cs
MONO_PATH="$KSP/KSP_x64_Data/Managed:$KSP/GameData/001_ToolbarControl/Plugins" \
  mono /tmp/AERIS54_LAND_R2_pure_tests.exe \
  Source/AERISFlightControl/bin/Release/AERISFlightControl.dll
```

Expected: test executable exits non-zero with the first missing field, beginning with `BodyName`.

- [ ] **Step 3: Add the R2 identity/coverage fields to `AERISApproachObstacleSnapshot`**

Insert directly after `DirectionStableId` and before signatures/completeness:

```csharp
internal string BodyName = string.Empty;
internal string EnvironmentSignature = string.Empty;
internal long AirfieldDatabaseRevision;
internal long RunwayGeometryRevision;
internal long TerrainRequestGeneration;
internal long TerrainDatabaseGeneration;
internal bool TerrainCoverageComplete;
internal bool ObstacleCoverageComplete;
internal int MinimumTerrainSourceLod = -1;
internal bool TerrainMissedApproachClear;
```

Do not change existing planner behavior and do not change `CorridorComplete` or `MissedApproachClear` semantics.

- [ ] **Step 4: Rebuild and verify the snapshot contract test passes**

Run the same build/test command. Expected final line:

```text
AERIS54_LAND_R2_PURE_TESTS=PASS
```

- [ ] **Step 5: Commit the contract change**

```bash
git add Source/AERISFlightControl/Landing/AERISApproachModels.cs \
        Tools/AERIS54_LAND_R2_pure_tests.cs
git commit -m "AERIS54 add LAND-R2 corridor snapshot identity"
```

---

### Task 2: Add the read-only terrain corridor broker

**Files:**
- Create: `Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs`
- Modify: `Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs`
- Modify: `Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs`
- Modify: `Source/AERISFlightControl/AERISFlightControl.csproj`
- Modify test: `Tools/AERIS54_LAND_R2_pure_tests.cs`

**Interfaces:**

Create these plain-value internal contracts in `AERISTerrainCorridorReadService.cs`:

```csharp
internal sealed class AERISTerrainCorridorQueryPoint
{
    internal int Index;
    internal double LatitudeDeg;
    internal double LongitudeDeg;
    internal double AlongTrackMeters;
    internal double CrossTrackMeters;
    internal bool MissedApproach;
}

internal sealed class AERISTerrainCorridorReadPlan
{
    internal string DirectionStableId = string.Empty;
    internal string BodyName = string.Empty;
    internal double BodyRadiusMeters;
    internal string EnvironmentSignature = string.Empty;
    internal string GameDataHash = string.Empty;
    internal long AirfieldDatabaseRevision;
    internal long RunwayGeometryRevision;
    internal long TerrainRequestGeneration;
    internal long TerrainDatabaseGeneration;
    internal long ProducerGeneration;
    internal AERISTerrainCorridorQueryPoint[] QueryPoints =
        new AERISTerrainCorridorQueryPoint[0];
    internal AERISTerrainTileKey[] TileKeys = new AERISTerrainTileKey[0];
    internal string[] PointTileStableIds = new string[0];
    internal int[] PointSourceLods = new int[0];
    internal bool TerrainCoverageComplete;
}

internal sealed class AERISTerrainCorridorReadResult
{
    internal AERISTerrainCorridorReadPlan Plan;
    internal Dictionary<string, AERISTerrainHeightTile> Tiles =
        new Dictionary<string, AERISTerrainHeightTile>(StringComparer.Ordinal);
    internal bool TerrainCoverageComplete;
    internal string FailureReason = string.Empty;
}
```

Create `AERISTerrainCorridorReadService` with these exact methods:

```csharp
internal bool TryCapturePlan(
    string directionStableId,
    string bodyName,
    double bodyRadiusMeters,
    string environmentSignature,
    string gameDataHash,
    long airfieldDatabaseRevision,
    long runwayGeometryRevision,
    long producerGeneration,
    IList<AERISTerrainCorridorQueryPoint> queryPoints,
    out AERISTerrainCorridorReadPlan plan,
    out string failureReason);

internal bool SubmitRead(
    AERISTerrainCorridorReadPlan plan,
    AERISRuntimeGenerationStamp stamp,
    Action<AERISTerrainCorridorReadResult> commit);
```

Construction:

```csharp
internal AERISTerrainCorridorReadService(
    AERISTerrainPreloadDatabase database,
    AERISTerrainWarmTileCache warm,
    AERISPerformanceRuntime performance)
```

`TryCapturePlan` is main-thread metadata-only. For each point, probe accepted preload metadata in fidelity order `Land -> Local -> Route -> Far`. Select the first key for which `database.Contains(key)` succeeds. Deduplicate selected tile keys by `StableId`. If no Far-or-better accepted key exists for any point, keep the point mapping empty and set `TerrainCoverageComplete=false`; do not request or generate terrain.

Key creation must reproduce the existing tile-system spatial convention exactly:

```csharp
static AERISTerrainTileKey KeyForPoint(string bodyName, double radius,
    string environment, AERISTerrainTileLod lod, double latitude, double longitude)
{
    double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, radius);
    int latitudeCount = Math.Max(1, (int)Math.Ceiling(180.0 / span));
    int longitudeCount = Math.Max(1, (int)Math.Ceiling(360.0 / span));
    double clampedLat = Math.Max(-90.0, Math.Min(89.999999, latitude));
    double normalizedLon = longitude % 360.0;
    if (normalizedLon > 180.0) normalizedLon -= 360.0;
    if (normalizedLon < -180.0) normalizedLon += 360.0;
    int latIndex = Math.Max(0, Math.Min(latitudeCount - 1,
        (int)Math.Floor((clampedLat + 90.0) / span)));
    int lonIndex = (int)Math.Floor((normalizedLon + 180.0) / span);
    lonIndex %= longitudeCount;
    if (lonIndex < 0) lonIndex += longitudeCount;
    return new AERISTerrainTileKey(bodyName, radius, environment,
        lod, latIndex, lonIndex);
}
```

`SubmitRead` must use `performance.Scheduler.SubmitRequired(AERISRuntimeLane.SafetyLand, ...)`. The worker calls the accepted `database.TryLoadBatch(plan.TileKeys, warm, output, new AERISTerrainPreloadTelemetry(), plan.GameDataHash)`, then deep-clones each returned tile using `CloneImmutable()` before constructing the result. No mutable database/cache collection is returned to Landing code.

- [ ] **Step 1: Extend the pure test harness with broker contract/static-safety tests**

Add reflection checks for the two new types, the two exact method names, and static source scans in the candidate runner later. For now the reflection test must fail because the type does not exist:

```csharp
static void TestReadServiceContract(Assembly aeris)
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
```

- [ ] **Step 2: Run build/pure tests and verify the read-service test fails**

Use the Task 1 desktop build/test command. Expected: `TypeLoadException` for `AERISTerrainCorridorReadService`.

- [ ] **Step 3: Implement the broker and expose it through Terrain**

In `AERISTerrainTileSystem` add:

```csharp
readonly AERISTerrainCorridorReadService corridorReadService;
internal AERISTerrainCorridorReadService CorridorReadService
{
    get { return corridorReadService; }
}
internal string ActiveEnvironmentHash { get { return environmentHash ?? string.Empty; } }
internal string CurrentGameDataHash { get { return cachedGameDataHash ?? string.Empty; } }
internal long TerrainRequestGeneration
{
    get { return preloadDatabase == null ? 0L : preloadDatabase.RequestGeneration; }
}
internal long TerrainDatabaseGeneration
{
    get { return preloadDatabase == null ? 0L : preloadDatabase.DatabaseGeneration; }
}
```

Initialize after `preloadDatabase` and `warm` exist:

```csharp
corridorReadService = new AERISTerrainCorridorReadService(
    preloadDatabase, warm, AERISPerformanceRuntime.Current);
```

Do not use `AERISPerformanceRuntime.Current` if it can be null during construction. Preferred final constructor change is to pass `AERISPerformanceRuntime performanceRuntime` from `AERISTerrainAwareness` / bootstrap into the tile system. If that would widen an existing constructor across unrelated callers, initialize the service lazily from `AERISPerformanceRuntime.Current` on first `SubmitRead` and fail closed with `PERFORMANCE_RUNTIME_UNAVAILABLE` when null. Do not create another scheduler or thread.

In `AERISTerrainAwareness` add:

```csharp
internal AERISTerrainCorridorReadService CorridorReadService
{
    get { return displayTiles == null ? null : displayTiles.CorridorReadService; }
}
```

Add explicit csproj include:

```xml
<Compile Include="Terrain\AERISTerrainCorridorReadService.cs" />
```

- [ ] **Step 4: Run build and pure tests; verify zero errors and broker contract pass**

Expected build: `0 Error(s)` and final pure-test marker `AERIS54_LAND_R2_PURE_TESTS=PASS`.

- [ ] **Step 5: Commit the read broker**

```bash
git add Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs \
        Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs \
        Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs \
        Source/AERISFlightControl/AERISFlightControl.csproj \
        Tools/AERIS54_LAND_R2_pure_tests.cs
git commit -m "AERIS54 add read-only LAND terrain corridor broker"
```

---

### Task 3: Implement pure corridor geometry, interpolation, signature and fail-closed semantics

**Files:**
- Create: `Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs`
- Modify: `Source/AERISFlightControl/AERISFlightControl.csproj`
- Modify test: `Tools/AERIS54_LAND_R2_pure_tests.cs`

**Interfaces:**

Create these plain-value types in the producer file:

```csharp
internal sealed class AERISTerrainCorridorIdentity
{
    internal string DirectionStableId = string.Empty;
    internal string BodyName = string.Empty;
    internal double BodyRadiusMeters;
    internal string EnvironmentSignature = string.Empty;
    internal long AirfieldDatabaseRevision;
    internal long RunwayGeometryRevision;
    internal long TerrainRequestGeneration;
    internal long TerrainDatabaseGeneration;
    internal long ProducerGeneration;
}

internal sealed class AERISTerrainCorridorComputeInput
{
    internal AERISTerrainCorridorIdentity Identity;
    internal double ThresholdLatitudeDeg;
    internal double ThresholdLongitudeDeg;
    internal double ThresholdElevationMeters;
    internal double InboundHeadingDeg;
    internal double MissedApproachHeadingDeg;
    internal double RequiredMissedApproachAltitudeMeters;
    internal AERISApproachPlanningLimits Limits;
    internal AERISTerrainCorridorReadResult ReadResult;
}
```

Expose these pure methods for the reflection harness and for producer internals:

```csharp
internal static int PriorityRank(bool armed, bool selected, bool activeBody);
internal static bool IdentityMatches(
    AERISTerrainCorridorIdentity expected,
    AERISTerrainCorridorIdentity current);
internal static AERISApproachObstacleSnapshot AnalyzePure(
    AERISTerrainCorridorComputeInput input);
```

Priority contract:

```text
ARMED      -> 0
selected   -> 1
activeBody -> 2
otherBody  -> 3
```

`AnalyzePure` must never reference Unity/KSP runtime objects. It must interpolate only inside the single mapped immutable tile for each query point; reject point interpolation when the tile is missing, `SamplingComplete==false`, `Quality<100`, resolution/elevation array is invalid, or the point lies outside the tile bounds. It must append terrain samples with `IsTerrain=true`, `SourceId=tile.Key.StableId`, and copied along/cross-track values.

For a valid complete terrain read:

```csharp
snapshot.TerrainCoverageComplete = true;
snapshot.ObstacleCoverageComplete = false;
snapshot.CorridorComplete = false;
snapshot.MissedApproachClear = false;
snapshot.ObstacleSignature = "R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE";
```

For missing terrain, `TerrainCoverageComplete=false` and the same obstacle/corridor fail-closed values remain.

`TerrainMissedApproachClear` is only a terrain-only diagnostic. Set it true only if every missed-approach query point is valid and all sampled terrain tops are below `RequiredMissedApproachAltitudeMeters`; never copy it into `MissedApproachClear`.

`TerrainSignature` must be deterministic FNV-1A over exact invariant-culture material ordered by query point index:

```text
R2|direction|body|environment|terrainDbGen|pointIndex|tileStableId|sourceLod|elevation_mm|...
```

Use `AERISTerrainHash.Fnv1A64Hex` to produce the final uppercase signature.

- [ ] **Step 1: Add failing pure tests for priority, identity, synthetic interpolation, determinism and fail-closed flags**

Extend the reflection harness with at least these assertions:

```text
PriorityRank(true,true,true) == 0
PriorityRank(false,true,true) == 1
PriorityRank(false,false,true) == 2
PriorityRank(false,false,false) == 3
```

Create synthetic read input with one complete 2x2 tile covering latitude `[0,1]`, longitude `[0,1]`, elevations `[100,200,300,400]`, one query point at `(0.5,0.5)`, and a complete mapping. Invoke `AnalyzePure` twice. Require:

```text
sample count == 1
sample IsTerrain == true
interpolated elevation == 250.0 within 0.001
TerrainSignature identical across both runs
TerrainCoverageComplete == true
ObstacleCoverageComplete == false
CorridorComplete == false
MissedApproachClear == false
ObstacleSignature == R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE
```

Create a second input with the point mapping missing. Require `TerrainCoverageComplete==false` and `CorridorComplete==false`.

Create two identities differing only in `EnvironmentSignature`; require `IdentityMatches(...)==false`. Repeat for runway geometry revision and terrain request generation.

- [ ] **Step 2: Run the test and verify it fails because producer/pure methods are absent**

Use the same build/reflection command. Expected non-zero exit before implementation.

- [ ] **Step 3: Implement the pure producer helpers and model-safe interpolation**

Implement bilinear interpolation using tile bounds and row-major `Elevation[y * Resolution + x]`. Clamp exact north/east edge to the last cell without reading past the array. Never interpolate across two tiles.

Generate only terrain samples in R2. Set `MinimumTerrainSourceLod` to the numerically smallest/highest-fidelity LOD actually used by the snapshot according to the existing enum values only after explicitly defining the summary rule in code; because enum values increase toward higher fidelity, store the maximum numeric used LOD as the best fidelity seen and name the comment accordingly. If no sample was valid, store `-1`.

Add csproj include:

```xml
<Compile Include="Landing\AERISApproachTerrainCorridorProducer.cs" />
```

- [ ] **Step 4: Rebuild and verify every pure test passes**

Expected final marker:

```text
AERIS54_LAND_R2_PURE_TESTS=PASS
```

- [ ] **Step 5: Commit the pure compute layer**

```bash
git add Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs \
        Source/AERISFlightControl/AERISFlightControl.csproj \
        Tools/AERIS54_LAND_R2_pure_tests.cs
git commit -m "AERIS54 add pure LAND-R2 terrain corridor analysis"
```

---

### Task 4: Implement bounded-lazy B scheduling and main-thread publication

**Files:**
- Modify: `Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs`
- Modify test: `Tools/AERIS54_LAND_R2_pure_tests.cs`

**Interfaces:**

Producer constructor:

```csharp
internal AERISApproachTerrainCorridorProducer(
    AERISAirfieldRegistry airfields,
    AERISLandingFoundation landing,
    AERISTerrainCorridorReadService reads,
    AERISPerformanceRuntime performance,
    AERISApproachPlanningLimits limits)
```

Runtime API:

```csharp
internal long PublicationGeneration { get; }
internal int InFlightCount { get; }
internal void Tick(string activeBodyName);
internal void Reset(string reason);
internal IDictionary<string, AERISApproachObstacleSnapshot> SnapshotDictionary();
```

The producer keeps at most two direction jobs admitted. A direction job counts as in-flight from successful read submission until either incomplete-read commit/pending state or compute commit/rejection.

Direction identity is captured on the main thread from cloned/certified registry values. Body radius is resolved on the main thread from `FlightGlobals.Bodies` by body name. That `CelestialBody` is used only to copy `Radius`; it is never stored in request/worker objects.

Build the final-approach query grid as follows:

1. Along-track baseline from `0` through `limits.MaximumCaptureDistanceMeters` at `AERISTerrainTileFormat.NominalCellMeters(Far)` spacing plus the exact end point.
2. Cross-track offsets `-limits.CorridorHalfWidthMeters`, `0`, `+limits.CorridorHalfWidthMeters`.
3. Convert local runway-relative offsets to geodetic coordinates on the main thread using only copied radius/heading/threshold values.
4. After baseline metadata capture, if the best source LOD for a baseline point is Route/Local/Land, insert intermediate along-track points for that adjacent segment using `max(64.0, NominalCellMeters(sourceLod))` spacing and recapture metadata. This is the bounded source-aware refinement rule; never refine below 64 m in R2.
5. Build missed-approach centerline points from threshold to `limits.MaximumCaptureDistanceMeters` using the same source-aware refinement but `MissedApproach=true` and `direction.MissedApproachHeadingDeg`.

Queue ordering must sort by:

```csharp
PriorityRank(isArmedDirection, isSelectedDirection, isActiveBody),
DirectionStableId ordinal-ignore-case
```

Do not requeue an incomplete direction every frame. Record its last attempted identity tuple and retry only when terrain request/database generation changes, runway/airfield identity changes, or it becomes selected/ARMED after previously being background.

Read commit behavior:

- Rebuild current identity on main thread.
- If stale, decrement in-flight, log one bounded `STALE_REJECT`, do not compute/publish.
- If read incomplete, publish a partial diagnostic snapshot only if `AnalyzePure` can produce samples; always keep `CorridorComplete=false`; mark direction pending and decrement in-flight.
- If read ready, submit compute through `SubmitRequired(AERISRuntimeLane.SafetyLand, ...)` with only copied input.

Compute commit behavior:

- Rebuild current identity again.
- Reject stale result before touching published dictionary.
- Clone snapshot into dictionary.
- Increment `PublicationGeneration` only when the material signature/identity differs from the previous published snapshot.
- Decrement in-flight exactly once.

`Reset(reason)` increments `producerGeneration`, clears queued/in-flight bookkeeping and publications, and increments `PublicationGeneration` only if there were published snapshots. Scheduler jobs from older producer generation become stale through `IdentityMatches` even if scheduler generation itself has not changed.

- [ ] **Step 1: Add failing tests for deterministic priority and stale identity rejection**

The Task 3 priority/identity tests already exercise the pure ranking. Extend the harness to require the producer type to expose `PublicationGeneration`, `InFlightCount`, `Tick`, `Reset`, and `SnapshotDictionary`.

Add a source-level assertion in the later candidate script that the producer contains:

```text
const int MaximumInFlightDirections = 2
```

and uses `SubmitRequired` with `AERISRuntimeLane.SafetyLand` for both asynchronous phases.

- [ ] **Step 2: Run tests and verify the runtime producer API test fails**

Expected non-zero reflection test because scheduling/publication members do not yet exist.

- [ ] **Step 3: Implement bounded-lazy scheduling/publication and structured logs**

Add bounded logs under exactly `[AERIS54][LAND_R2]` with events:

```text
QUEUE
READ_READY
READ_INCOMPLETE
WORKER_COMPLETE
STALE_REJECT
PUBLISH
DIRECTION_PENDING
RESET
SUMMARY
```

Every event must include `direction=`, `body=`, `environment=` (prefix is acceptable), `producer_generation=`, and a reason where applicable. `SUMMARY` must include `in_flight=`, `published=`, `pending=`, and `authority=NONE_PILOT`.

- [ ] **Step 4: Rebuild and run pure tests**

Expected `0 Error(s)` and `AERIS54_LAND_R2_PURE_TESTS=PASS`.

- [ ] **Step 5: Commit bounded-lazy producer behavior**

```bash
git add Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs \
        Tools/AERIS54_LAND_R2_pure_tests.cs
git commit -m "AERIS54 add bounded lazy LAND-R2 corridor producer"
```

---

### Task 5: Wire producer lifecycle into bootstrap and Approach Registry

**Files:**
- Modify: `Source/AERISFlightControl/Core/AERISBootstrap.cs`
- Modify test: `Tools/AERIS54_LAND_R2_pure_tests.cs`

**Interfaces:**

Add bootstrap state:

```csharp
AERISApproachTerrainCorridorProducer approachTerrainCorridors;
long lastApproachTerrainPublicationGeneration = -1L;
long lastApproachTerrainDatabaseRevision = -1L;
```

After `Terrain` exists in `Start()` construct:

```csharp
approachTerrainCorridors = new AERISApproachTerrainCorridorProducer(
    Airfields, Landing,
    Terrain == null ? null : Terrain.CorridorReadService,
    Performance, approachPlanningLimits);
```

During the existing main-thread runtime update after `Airfields` and `Terrain` have ticked, call:

```csharp
if (approachTerrainCorridors != null)
    approachTerrainCorridors.Tick(
        FlightGlobals.ActiveVessel == null ||
        FlightGlobals.ActiveVessel.mainBody == null ? string.Empty :
        FlightGlobals.ActiveVessel.mainBody.name);
```

Replace the R1-only null snapshot rebuild gate with generation-aware rebuild:

```csharp
long corridorGeneration = approachTerrainCorridors == null ? 0L :
    approachTerrainCorridors.PublicationGeneration;
bool approachDbChanged = database != lastApproachTerrainDatabaseRevision;
bool corridorChanged = corridorGeneration !=
    lastApproachTerrainPublicationGeneration;
if ((approachDbChanged || corridorChanged) && Approaches != null)
{
    IDictionary<string, AERISApproachObstacleSnapshot> snapshots =
        approachTerrainCorridors == null ? null :
        approachTerrainCorridors.SnapshotDictionary();
    Approaches.Rebuild(Airfields.Airfields, snapshots, approachPlanningLimits);
    lastApproachTerrainDatabaseRevision = database;
    lastApproachTerrainPublicationGeneration = corridorGeneration;
}
```

Keep `Performance.UpdateRunwayRevisions(database, selection)` unchanged.

In `ResetAutomationState`, add a safe reset step before or adjacent to Terrain/Landing reset:

```csharp
SafeResetStep(()=>{
    if (approachTerrainCorridors != null)
        approachTerrainCorridors.Reset(reason);
}, "LAND-R2 terrain corridor producer", reason);
```

Do not disarm/rearm LAND from producer code and do not alter control state.

- [ ] **Step 1: Add failing static/reflection integration checks**

Require bootstrap assembly type to contain the `approachTerrainCorridors` field. Add candidate-script static checks later for `SnapshotDictionary()` passed into `Approaches.Rebuild` and for producer reset.

- [ ] **Step 2: Run pure test and verify bootstrap integration check fails**

Expected non-zero reflection test because field is absent.

- [ ] **Step 3: Implement bootstrap ownership, tick, reset, and registry rebuild**

Preserve existing airfield database/selection revision update logic. Do not rebuild Approach Registry every frame; only database or publication generation changes trigger it.

- [ ] **Step 4: Build and run pure tests**

Expected zero errors and pure tests PASS.

- [ ] **Step 5: Commit lifecycle integration**

```bash
git add Source/AERISFlightControl/Core/AERISBootstrap.cs \
        Tools/AERIS54_LAND_R2_pure_tests.cs
git commit -m "AERIS54 wire LAND-R2 terrain snapshots into approach registry"
```

---

### Task 6: Add the static/build/pure/runtime candidate gate

**Files:**
- Create: `Tools/aeris54_land_r2_candidate.sh`
- Modify: `Tools/aeris_current_stage.sh`

**Interfaces:**

`Tools/aeris54_land_r2_candidate.sh <KSP root>` must use the same two-pass arming model as the producer-coherence runner:

- first invocation on a new HEAD: static audit -> clean release build -> install -> pure tests -> store HEAD/DLL SHA/log offset -> `WAITING_FOR_RUNTIME`;
- user launches KSP, waits for airfield/preload/LAND-R2 activity, selects a runway, arms LAND when safe/appropriate, disarms, exits normally;
- second invocation on unchanged HEAD/DLL: audit only the log segment after the armed offset.

Static gates must require:

```bash
! grep -Eq 'FlightCtrlState|PQS\.GetSurfaceHeight|GetSurfaceHeight|PQSMod|\.pqs\b' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs \
    Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs

grep -Fq 'MaximumInFlightDirections = 2' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
grep -Fq 'AERISRuntimeLane.SafetyLand' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
grep -Fq 'TryLoadBatch' \
    Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs
grep -Fq 'R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
grep -Fq 'CorridorComplete = false' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
grep -Fq 'MissedApproachClear = false' \
    Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
```

The build command must be:

```bash
AERIS_PRELOAD_BRANCH="agent/aeris54-land-r2-immutable-terrain-corridor-snapshots" \
  bash Tools/AERIS_preload_build_and_go.sh desktop
```

or laptop according to KSP root, and the pure harness must be compiled/run immediately after build/install.

Runtime second-pass counters must include:

```text
queue_count
read_ready_count
read_incomplete_count
worker_complete_count
publish_count
stale_reject_count
pending_count
summary_count
max_observed_in_flight
corridor_complete_true_count
missed_clear_true_count
pqs_forbidden_marker_count
env4_db_write_suppressed_count
suspected_exceptions
```

Candidate PASS requires:

```text
queue_count >= 1
read_ready_count >= 1
worker_complete_count >= 1
publish_count >= 1
pending_count >= 1
summary_count >= 1
max_observed_in_flight <= 2
corridor_complete_true_count == 0
missed_clear_true_count == 0
pqs_forbidden_marker_count == 0
env4_db_write_suppressed_count == 0
suspected_exceptions == 0
```

`stale_reject_count` is not mandatory in the first natural runtime pass because a deterministic stale test exists in the pure harness; if it appears naturally, report it. Runtime must also show at least one `[AERIS54][LAND_R2]` event for the currently selected/ARMED direction before a background direction at the same scheduling opportunity. The runner should print evidence lines when this cannot be proven automatically rather than silently claiming priority PASS.

Final candidate marker:

```text
AERIS54_LAND_R2_VERDICT=PASS_CANDIDATE
AERIS_CURRENT_STAGE=LAND_R2_CANDIDATE_PASS
```

- [ ] **Step 1: Write the candidate runner so it fails against any missing gate**

Use exact branch/base checks from `aeris54_land_r2_start_audit.sh`, preserve the accepted producer-coherence base ancestry check, and reject dirty trees.

- [ ] **Step 2: Run candidate runner once and fix only audit-script defects until static/build/pure gates pass**

Desktop:

```bash
bash Tools/aeris54_land_r2_candidate.sh \
  "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

Laptop:

```bash
bash Tools/aeris54_land_r2_candidate.sh \
  "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

Expected first-pass end:

```text
AERIS54_LAND_R2=ARMED
AERIS_CURRENT_STAGE=WAITING_FOR_RUNTIME
```

- [ ] **Step 3: Route `aeris_current_stage.sh` to the candidate runner**

Set stage text to `LAND-R2-IMMUTABLE-TERRAIN-CORRIDOR-SNAPSHOTS-CANDIDATE` and execute `Tools/aeris54_land_r2_candidate.sh "$KSP"`.

- [ ] **Step 4: Commit the candidate gate**

```bash
git add Tools/aeris54_land_r2_candidate.sh Tools/aeris_current_stage.sh
git commit -m "AERIS54 add LAND-R2 candidate audit runner"
```

---

### Task 7: Runtime acceptance pass and freeze-ready evidence

**Files:**
- Runtime evidence only on first pass.
- Modify after evidence: `Docs/AERIS54_LAND_R2_IMMUTABLE_TERRAIN_CORRIDOR_SNAPSHOTS.md`
- Modify after evidence: `docs/superpowers/specs/2026-09-17-aeris54-land-r2-immutable-terrain-corridor-design.md` only if the implementation intentionally diverged from the approved design; otherwise leave the spec unchanged.

**Interfaces:**
- Produces one candidate commit/DLL pair with reproducible runtime evidence.

- [ ] **Step 1: Sync/build/arm on desktop**

```bash
cd "$HOME/AERIS42_R042"
git status --short
git push origin agent/aeris54-land-r2-immutable-terrain-corridor-snapshots
bash Tools/aeris_current_stage.sh \
  "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

Expected: clean branch, static gates PASS, build `0 Error(s)`, pure tests PASS, install verify PASS, runtime audit armed.

- [ ] **Step 2: Perform one controlled KSP runtime pass**

Human actions:

```text
1. Launch KSP with the accepted runway-mod baseline enabled.
2. Reach main menu and allow preload/airfield startup work to settle.
3. Enter a normal fixed-wing flight on Kerbin.
4. Select a certified runway direction.
5. Observe LAND-R2 queue/read/publish logs/status for the selected direction.
6. When safe and normal for the test flight, ARM LAND once so ARMED priority is exercised.
7. Keep control with the pilot; LAND-R2 must not command the aircraft.
8. Disarm LAND.
9. Return to main menu and exit normally.
```

No forced terrain deletion, DB deletion, or PQS regeneration is permitted for this acceptance pass.

- [ ] **Step 3: Run the same stage command again for runtime audit**

```bash
bash Tools/aeris_current_stage.sh \
  "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

Expected candidate verdict:

```text
AERIS54_LAND_R2_VERDICT=PASS_CANDIDATE
AERIS_CURRENT_STAGE=LAND_R2_CANDIDATE_PASS
```

If the only missing evidence is an intentionally incomplete terrain case, use an existing certified direction/body whose accepted DB coverage is naturally incomplete. Do not delete or corrupt terrain data to manufacture the case. The pure missing-coverage test remains the deterministic fail-closed proof.

- [ ] **Step 4: Record exact acceptance evidence in the LAND-R2 doc**

Append exact branch HEAD, DLL SHA256, runtime counters, selected/ARMED direction IDs used, at least one `PUBLISH` line, at least one `DIRECTION_PENDING` or pure missing-coverage proof line, and the zero-control/zero-PQS/zero-ENV4-regression counters.

The acceptance block must conclude only with facts demonstrated by the audit, for example:

```text
LAND-R2 runtime candidate = PASS
terrain authority = ACCEPTED_PRELOAD_PTC_DB_CACHE
normal synchronous PQS = 0 observed / statically forbidden
maximum corridor in-flight = <=2
terrain-only CorridorComplete=true = 0
terrain-only MissedApproachClear=true = 0
LAND control authority = NONE / PILOT
```

- [ ] **Step 5: Run final clean verification before any ACCEPTED branch/tag is created**

```bash
cd "$HOME/AERIS42_R042"
git status --short
AERIS_PRELOAD_BRANCH="agent/aeris54-land-r2-immutable-terrain-corridor-snapshots" \
  bash Tools/AERIS_preload_build_and_go.sh desktop --no-install
bash Tools/aeris54_land_r2_candidate.sh \
  "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

Do not claim LAND-R2 accepted until this final clean verification and the recorded runtime evidence agree on HEAD and DLL SHA.

- [ ] **Step 6: Commit acceptance evidence**

```bash
git add Docs/AERIS54_LAND_R2_IMMUTABLE_TERRAIN_CORRIDOR_SNAPSHOTS.md
git commit -m "AERIS54 record LAND-R2 candidate runtime evidence"
```

At this point the branch is freeze-ready; acceptance branch creation is a separate explicit decision after review.

---

## Self-Review Results

### Spec coverage

- B-priority bounded lazy scheduling: Tasks 4 and 6.
- Accepted DB/cache only, no normal PQS: Tasks 2 and 6.
- Worker plain-value boundary: Tasks 2, 3, and 6.
- Source-aware corridor sampling: Task 4.
- Immutable read/decode and deep clones: Task 2.
- Identity tuple and stale rejection: Tasks 3 and 4.
- Terrain-only snapshot semantics: Tasks 1 and 3.
- Main-thread publication/registry integration: Tasks 4 and 5.
- Reset/invalidation: Tasks 4 and 5.
- Structured observability: Tasks 4 and 6.
- Static/pure tests: Tasks 1 through 6.
- Runtime acceptance: Task 7.
- No control authority / no NEW_NAV / no unrelated refactors: Global Constraints and Tasks 5-7.

### Placeholder scan

The plan contains no `TBD`, `TODO`, “implement later”, unspecified error-handling steps, or unnamed test requirements. Every task names exact files, interfaces, commands, expected failures, expected passes, and commit boundaries.

### Type consistency

The plan consistently uses:

```text
AERISTerrainCorridorReadService
AERISTerrainCorridorQueryPoint
AERISTerrainCorridorReadPlan
AERISTerrainCorridorReadResult
AERISTerrainCorridorIdentity
AERISTerrainCorridorComputeInput
AERISApproachTerrainCorridorProducer
```

and consistently carries `DirectionStableId`, body/environment identity, airfield/runway revision, terrain request/database generation, and producer generation from main-thread capture through stale-check and publication.