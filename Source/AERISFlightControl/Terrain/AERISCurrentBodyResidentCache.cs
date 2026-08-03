using System;
using System.Collections.Generic;
using System.Threading;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // CP3 state contract. Gate 3.1 keeps Global/Far as the sole background-populated
    // current-body base. Route/Local remain legal on-demand exact bridge payloads and
    // adaptive presentation refinement is reconstructed from that base.
    internal enum AERISResidentTileState
    {
        Indexed = 0,
        SsdReady = 1,
        Decoded = 2,
        RamResident = 3,
        RenderReady = 4,
        GpuReady = 5
    }

    [Flags]
    internal enum AERISResidentPinReason
    {
        None = 0,
        GlobalFoundation = 1,
        Viewport = 2,
        ForwardCorridor = 4,
        Runway = 8,
        RenderPreparation = 16
    }

    internal enum AERISResidentEvictionReason
    {
        Budget = 0,
        BodyTransition = 1,
        DatabaseGenerationChanged = 2,
        SceneReset = 3,
        Explicit = 4,
        Shutdown = 5
    }

    internal enum AERISResidentCommitResult
    {
        Committed = 0,
        InvalidPayload = 1,
        StaleScope = 2,
        InvalidTransition = 3,
        BudgetRejected = 4
    }

    // A worker must capture this token before it starts any asynchronous stage.
    // Every commit validates all generations and the complete tile identity again.
    internal struct AERISResidentCommitToken : IEquatable<AERISResidentCommitToken>
    {
        internal readonly string StableId;
        internal readonly string BodyName;
        internal readonly long BodyRadiusMillimetres;
        internal readonly string EnvironmentHash;
        internal readonly AERISTerrainTileLod Lod;
        internal readonly long ScopeGeneration;
        internal readonly long BodyGeneration;
        internal readonly long DatabaseGeneration;

        internal AERISResidentCommitToken(AERISTerrainTileKey key,
            long scopeGeneration, long bodyGeneration, long databaseGeneration)
        {
            StableId = key.StableId;
            BodyName = key.BodyName ?? string.Empty;
            BodyRadiusMillimetres = key.BodyRadiusMillimetres;
            EnvironmentHash = key.EnvironmentHash ?? string.Empty;
            Lod = key.Lod;
            ScopeGeneration = scopeGeneration;
            BodyGeneration = bodyGeneration;
            DatabaseGeneration = databaseGeneration;
        }

        internal bool IsEmpty { get { return string.IsNullOrEmpty(StableId); } }

        public bool Equals(AERISResidentCommitToken other)
        {
            return ScopeGeneration == other.ScopeGeneration &&
                BodyGeneration == other.BodyGeneration &&
                DatabaseGeneration == other.DatabaseGeneration &&
                BodyRadiusMillimetres == other.BodyRadiusMillimetres &&
                Lod == other.Lod &&
                string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
                string.Equals(BodyName, other.BodyName, StringComparison.Ordinal) &&
                string.Equals(EnvironmentHash, other.EnvironmentHash,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AERISResidentCommitToken &&
                Equals((AERISResidentCommitToken)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StableId == null ? 0 : StableId.GetHashCode();
                hash = hash * 397 ^ ScopeGeneration.GetHashCode();
                hash = hash * 397 ^ BodyGeneration.GetHashCode();
                hash = hash * 397 ^ DatabaseGeneration.GetHashCode();
                return hash;
            }
        }
    }

    internal sealed class AERISCurrentBodyResidentTelemetrySnapshot
    {
        internal bool Active;
        internal string ActiveBody = string.Empty;
        internal long ActiveBodyRadiusMillimetres;
        internal string ActiveEnvironmentHash = string.Empty;
        internal long ScopeGeneration;
        internal long BodyGeneration;
        internal long DatabaseGeneration;
        internal long RamBytes;
        internal long RamBudgetBytes;
        internal long OverBudgetBytes;
        internal int EntryCount;
        internal int IndexedCount;
        internal int SsdReadyCount;
        internal int DecodedCount;
        internal int RamResidentCount;
        internal int RenderReadyCount;
        internal int GpuReadyCount;
        internal int GlobalCount;
        internal int FarCount;
        internal int RouteCount;
        internal int LocalCount;
        internal int PinnedEntryCount;
        internal int PinLeaseCount;
        internal long Registrations;
        internal long StatePromotions;
        internal long RamCommits;
        internal long CacheHits;
        internal long CacheMisses;
        internal long Evictions;
        internal long BudgetEvictions;
        internal long BodyTransitions;
        internal long DatabaseGenerationTransitions;
        internal long SceneResets;
        internal long StaleCommitRejects;
        internal long ForeignBodyRejects;
        internal long BudgetRejects;
        internal long InvalidTransitionRejects;
        internal long AsyncDecodeSubmissions;
        internal long AsyncDecodeSuccesses;
        internal long AsyncDecodeFailures;
        internal long GlobalBudgetRejects;
        internal long FarBudgetRejects;
        internal long RouteBudgetRejects;
        internal long LocalBudgetRejects;
        internal long GlobalBudgetEvictions;
        internal long FarBudgetEvictions;
        internal long RouteBudgetEvictions;
        internal long LocalBudgetEvictions;
        internal string LastCause = string.Empty;
        internal string Status = "INACTIVE";
    }

    internal sealed class AERISResidentPinLease : IDisposable
    {
        AERISCurrentBodyResidentCache owner;
        readonly string stableId;
        readonly long scopeGeneration;
        readonly AERISResidentPinReason reason;

        internal AERISResidentPinLease(AERISCurrentBodyResidentCache owner,
            string stableId, long scopeGeneration, AERISResidentPinReason reason)
        {
            this.owner = owner;
            this.stableId = stableId ?? string.Empty;
            this.scopeGeneration = scopeGeneration;
            this.reason = reason;
        }

        public void Dispose()
        {
            AERISCurrentBodyResidentCache current =
                Interlocked.Exchange(ref owner, null);
            if (current != null)
                current.ReleasePin(stableId, scopeGeneration, reason);
        }
    }

    // Separate owner from AERISMapDramCache. This class never performs disk I/O and
    // never publishes metadata into Map DRAM. Gate 2 worker jobs transfer decoded
    // current-body payload ownership through the generation-checked methods below.
    internal sealed class AERISCurrentBodyResidentCache : IDisposable
    {
        sealed class Entry
        {
            internal AERISTerrainTileKey Key;
            internal AERISResidentTileState State;
            internal long ScopeGeneration;
            internal long BodyGeneration;
            internal long DatabaseGeneration;
            internal long StoredBytes;
            internal long RamBytes;
            internal long LastAccessSequence;
            internal long LastStateChangeUtcTicks;
            internal AERISTerrainHeightTile ResidentTile;
            internal LinkedListNode<string> LruNode;
            internal readonly Dictionary<AERISResidentPinReason, int> Pins =
                new Dictionary<AERISResidentPinReason, int>();
            internal int PinCount;
        }

        readonly object sync = new object();
        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly LinkedList<string> lru = new LinkedList<string>();

        bool active;
        bool disposed;
        string activeBody = string.Empty;
        long activeBodyRadiusMillimetres;
        string activeEnvironmentHash = string.Empty;
        long scopeGeneration = 1L;
        long bodyGeneration = 1L;
        long databaseGeneration;
        long accessSequence;
        long ramBytes;
        long ramBudgetBytes;
        long registrations;
        long statePromotions;
        long ramCommits;
        long cacheHits;
        long cacheMisses;
        long evictions;
        long budgetEvictions;
        long bodyTransitions;
        long databaseGenerationTransitions;
        long sceneResets;
        long staleCommitRejects;
        long foreignBodyRejects;
        long budgetRejects;
        long invalidTransitionRejects;
        long asyncDecodeSubmissions;
        long asyncDecodeSuccesses;
        long asyncDecodeFailures;
        long globalBudgetRejects;
        long farBudgetRejects;
        long routeBudgetRejects;
        long localBudgetRejects;
        long globalBudgetEvictions;
        long farBudgetEvictions;
        long routeBudgetEvictions;
        long localBudgetEvictions;
        int pinLeaseCount;
        string lastCause = string.Empty;
        string status = "INACTIVE";

        internal AERISCurrentBodyResidentCache(long ramBudgetBytes)
        {
            this.ramBudgetBytes = NormalizeBudget(ramBudgetBytes);
        }

        internal bool Active { get { lock (sync) return active && !disposed; } }
        internal long RamBytes { get { lock (sync) return ramBytes; } }
        internal long RamBudgetBytes { get { lock (sync) return ramBudgetBytes; } }
        internal long ScopeGeneration { get { lock (sync) return scopeGeneration; } }
        internal long BodyGeneration { get { lock (sync) return bodyGeneration; } }
        internal long DatabaseGeneration { get { lock (sync) return databaseGeneration; } }
        internal string ActiveBody { get { lock (sync) return activeBody; } }

        internal void SetRamBudget(long bytes, string cause)
        {
            lock (sync)
            {
                if (disposed) return;
                ramBudgetBytes = NormalizeBudget(bytes);
                lastCause = cause ?? string.Empty;
                EnforceBudgetLocked(null);
                UpdateStatusLocked();
            }
        }

        // Returns true when a new body/database scope was created. A database epoch
        // change invalidates all in-flight commits even when the celestial body did not.
        internal bool BeginBody(string bodyName, double bodyRadiusMeters,
            string environmentHash, long nextDatabaseGeneration, string cause)
        {
            string normalizedBody = bodyName ?? string.Empty;
            string normalizedEnvironment = environmentHash ?? string.Empty;
            long radiusMillimetres = (long)Math.Round(
                Math.Max(0.0, bodyRadiusMeters) * 1000.0);
            long normalizedDatabaseGeneration = Math.Max(0L, nextDatabaseGeneration);

            lock (sync)
            {
                if (disposed) return false;
                bool hadIdentity = !string.IsNullOrEmpty(activeBody) ||
                    activeBodyRadiusMillimetres > 0L ||
                    !string.IsNullOrEmpty(activeEnvironmentHash);
                bool sameIdentity =
                    string.Equals(activeBody, normalizedBody, StringComparison.Ordinal) &&
                    activeBodyRadiusMillimetres == radiusMillimetres &&
                    string.Equals(activeEnvironmentHash, normalizedEnvironment,
                        StringComparison.Ordinal);
                bool sameDatabase = sameIdentity &&
                    databaseGeneration == normalizedDatabaseGeneration;
                if (sameDatabase) return false;

                AERISResidentEvictionReason reason = sameIdentity ?
                    AERISResidentEvictionReason.DatabaseGenerationChanged :
                    AERISResidentEvictionReason.BodyTransition;
                ClearEntriesLocked(reason);
                scopeGeneration++;
                if (!sameIdentity)
                {
                    bodyGeneration++;
                    if (hadIdentity) bodyTransitions++;
                }
                else databaseGenerationTransitions++;

                // Empty environment identity is fail-closed. Solid-surface bodies
                // receive a non-empty fingerprint after GameData hashing; gas giants
                // and unavailable PQS environments keep the contract inactive.
                active = !string.IsNullOrEmpty(normalizedBody) &&
                    radiusMillimetres > 0L &&
                    !string.IsNullOrEmpty(normalizedEnvironment);
                activeBody = normalizedBody;
                activeBodyRadiusMillimetres = radiusMillimetres;
                activeEnvironmentHash = normalizedEnvironment;
                databaseGeneration = normalizedDatabaseGeneration;
                lastCause = cause ?? string.Empty;
                status = active ? "CURRENT BODY CONTRACT READY" : "INACTIVE";
                AERISLogger.Info("[CP3_RESIDENT] scope=" + scopeGeneration +
                    " bodyGen=" + bodyGeneration + " dbGen=" + databaseGeneration +
                    " body=" + (string.IsNullOrEmpty(activeBody) ? "<none>" : activeBody) +
                    " active=" + (active ? "1" : "0") +
                    " reason=" + (lastCause.Length == 0 ? reason.ToString() : lastCause) +
                    " payloadRoute=ASYNC_DECODE_RAM_RESIDENT");
                return true;
            }
        }

        internal void Reset(string cause)
        {
            lock (sync)
            {
                if (disposed) return;
                ClearEntriesLocked(AERISResidentEvictionReason.SceneReset);
                active = false;
                activeBody = string.Empty;
                activeBodyRadiusMillimetres = 0L;
                activeEnvironmentHash = string.Empty;
                databaseGeneration = 0L;
                scopeGeneration++;
                bodyGeneration++;
                sceneResets++;
                lastCause = cause ?? string.Empty;
                status = "INACTIVE — SCENE RESET";
            }
        }

        internal bool RegisterIndexed(AERISTerrainTileKey key,
            long entryDatabaseGeneration, long storedBytes,
            out AERISResidentCommitToken token)
        {
            token = default(AERISResidentCommitToken);
            lock (sync)
            {
                if (!ValidateKeyForCurrentScopeLocked(key,
                    entryDatabaseGeneration, false)) return false;

                Entry entry;
                if (!entries.TryGetValue(key.StableId, out entry) || entry == null)
                {
                    entry = new Entry
                    {
                        Key = key,
                        State = AERISResidentTileState.Indexed,
                        ScopeGeneration = scopeGeneration,
                        BodyGeneration = bodyGeneration,
                        DatabaseGeneration = databaseGeneration,
                        StoredBytes = Math.Max(0L, storedBytes),
                        LastStateChangeUtcTicks = DateTime.UtcNow.Ticks
                    };
                    entry.LruNode = lru.AddLast(key.StableId);
                    entries[key.StableId] = entry;
                    registrations++;
                }
                else
                {
                    entry.StoredBytes = Math.Max(0L, storedBytes);
                    TouchLocked(entry);
                }
                token = TokenForLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        // Atomically registers an indexed payload and promotes it to SSD READY before
        // a shared-scheduler worker begins I/O. Append-only manifest commits do not
        // invalidate this scope; the caller supplies the database request epoch.
        internal bool TryPrepareSsdDecode(AERISTerrainTileKey key,
            long entryDatabaseGeneration, long storedBytes,
            out AERISResidentCommitToken token, out bool alreadyResident)
        {
            token = default(AERISResidentCommitToken);
            alreadyResident = false;
            if (!IsGate3ResidencyLod(key.Lod)) return false;
            lock (sync)
            {
                if (!ValidateKeyForCurrentScopeLocked(key,
                    entryDatabaseGeneration, false)) return false;

                Entry entry;
                if (!entries.TryGetValue(key.StableId, out entry) || entry == null)
                {
                    entry = new Entry
                    {
                        Key = key,
                        State = AERISResidentTileState.Indexed,
                        ScopeGeneration = scopeGeneration,
                        BodyGeneration = bodyGeneration,
                        DatabaseGeneration = databaseGeneration,
                        StoredBytes = Math.Max(0L, storedBytes),
                        LastStateChangeUtcTicks = DateTime.UtcNow.Ticks
                    };
                    entry.LruNode = lru.AddLast(key.StableId);
                    entries[key.StableId] = entry;
                    registrations++;
                }
                else
                {
                    entry.StoredBytes = Math.Max(0L, storedBytes);
                }

                if ((int)entry.State >= (int)AERISResidentTileState.RamResident &&
                    entry.ResidentTile != null)
                {
                    alreadyResident = true;
                    cacheHits++;
                    token = TokenForLocked(entry);
                    TouchLocked(entry);
                    UpdateStatusLocked();
                    return true;
                }

                if (entry.State == AERISResidentTileState.Indexed)
                {
                    entry.State = AERISResidentTileState.SsdReady;
                    entry.LastStateChangeUtcTicks = DateTime.UtcNow.Ticks;
                    statePromotions++;
                }
                else if (entry.State != AERISResidentTileState.SsdReady &&
                    entry.State != AERISResidentTileState.Decoded)
                {
                    invalidTransitionRejects++;
                    return false;
                }

                asyncDecodeSubmissions++;
                token = TokenForLocked(entry);
                TouchLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        internal bool TryCaptureCommitToken(AERISTerrainTileKey key,
            out AERISResidentCommitToken token)
        {
            token = default(AERISResidentCommitToken);
            lock (sync)
            {
                Entry entry;
                if (!ValidateKeyForCurrentScopeLocked(key, databaseGeneration, true) ||
                    !entries.TryGetValue(key.StableId, out entry) || entry == null)
                {
                    cacheMisses++;
                    return false;
                }
                token = TokenForLocked(entry);
                TouchLocked(entry);
                return true;
            }
        }

        internal bool TryMarkSsdReady(AERISResidentCommitToken token)
        {
            return TryPromoteMetadataState(token, AERISResidentTileState.SsdReady);
        }

        internal bool TryMarkDecoded(AERISResidentCommitToken token)
        {
            return TryPromoteMetadataState(token, AERISResidentTileState.Decoded);
        }

        internal void RecordDecodeFailure(AERISResidentCommitToken token,
            string cause)
        {
            lock (sync)
            {
                Entry entry;
                if (!ValidateTokenLocked(token, out entry)) return;
                asyncDecodeFailures++;
                lastCause = cause ?? string.Empty;
                TouchLocked(entry);
                UpdateStatusLocked();
            }
        }

        bool TryPromoteMetadataState(AERISResidentCommitToken token,
            AERISResidentTileState next)
        {
            lock (sync)
            {
                Entry entry;
                if (!ValidateTokenLocked(token, out entry)) return false;
                if (next != AERISResidentTileState.SsdReady &&
                    next != AERISResidentTileState.Decoded)
                {
                    invalidTransitionRejects++;
                    return false;
                }
                if ((int)next < (int)entry.State ||
                    (int)next > (int)entry.State + 1)
                {
                    invalidTransitionRejects++;
                    return false;
                }
                if (entry.State == next) return true;
                entry.State = next;
                entry.LastStateChangeUtcTicks = DateTime.UtcNow.Ticks;
                statePromotions++;
                TouchLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        // Ownership transfer contract for Gate 2. The caller must not mutate tile after
        // a successful commit. The transient viewport cache shares the immutable payload.
        internal bool TryCommitRamResident(AERISResidentCommitToken token,
            AERISTerrainHeightTile tile)
        {
            AERISResidentCommitResult result;
            return TryCommitRamResident(token, tile, out result);
        }

        internal bool TryCommitRamResident(AERISResidentCommitToken token,
            AERISTerrainHeightTile tile, out AERISResidentCommitResult result)
        {
            result = AERISResidentCommitResult.InvalidPayload;
            if (tile == null || !IsGate3ResidencyLod(tile.Key.Lod)) return false;
            lock (sync)
            {
                Entry entry;
                if (!ValidateTokenLocked(token, out entry))
                {
                    result = AERISResidentCommitResult.StaleScope;
                    return false;
                }
                if (!tile.Key.Equals(entry.Key) ||
                    entry.State != AERISResidentTileState.Decoded)
                {
                    invalidTransitionRejects++;
                    result = AERISResidentCommitResult.InvalidTransition;
                    return false;
                }
                long incomingBytes = Math.Max(0L, tile.EstimatedBytes);
                long previousBytes = Math.Max(0L, entry.RamBytes);
                long requiredBytes = Math.Max(0L, incomingBytes - previousBytes);
                bool pinnedCommit = entry.PinCount > 0;
                if (!MakeRoomLocked(requiredBytes, entry.Key.StableId, pinnedCommit,
                    entry.Key.Lod))
                {
                    budgetRejects++;
                    IncrementBudgetRejectLocked(entry.Key.Lod);
                    lastCause = "RAM BUDGET ADMISSION — " +
                        entry.Key.Lod.ToString().ToUpperInvariant();
                    result = AERISResidentCommitResult.BudgetRejected;
                    UpdateStatusLocked();
                    return false;
                }

                ramBytes = Math.Max(0L, ramBytes - previousBytes) + incomingBytes;
                entry.ResidentTile = tile;
                entry.RamBytes = incomingBytes;
                entry.State = AERISResidentTileState.RamResident;
                entry.LastStateChangeUtcTicks = DateTime.UtcNow.Ticks;
                statePromotions++;
                ramCommits++;
                asyncDecodeSuccesses++;
                TouchLocked(entry);
                EnforceBudgetLocked(entry.Key.StableId);
                result = AERISResidentCommitResult.Committed;
                UpdateStatusLocked();
                return true;
            }
        }

        // Gate 4A presentation-state contract. Render-ready data is CPU-owned immutable
        // height/topology data; GPU-ready means corresponding Unity resources currently
        // exist. Demotion never destroys the RAM-resident source payload.
        internal bool TryMarkRenderReady(AERISResidentCommitToken token)
        {
            return TryPromotePresentationState(token, AERISResidentTileState.RenderReady);
        }

        internal bool TryMarkGpuReady(AERISResidentCommitToken token)
        {
            return TryPromotePresentationState(token, AERISResidentTileState.GpuReady);
        }

        bool TryPromotePresentationState(AERISResidentCommitToken token,
            AERISResidentTileState next)
        {
            lock (sync)
            {
                Entry entry;
                if (!ValidateTokenLocked(token, out entry)) return false;
                if (next != AERISResidentTileState.RenderReady &&
                    next != AERISResidentTileState.GpuReady ||
                    (int)entry.State < (int)AERISResidentTileState.RamResident ||
                    (int)next > (int)entry.State + 1)
                {
                    invalidTransitionRejects++;
                    return false;
                }
                if ((int)entry.State >= (int)next) return true;
                entry.State = next;
                entry.LastStateChangeUtcTicks = DateTime.UtcNow.Ticks;
                statePromotions++;
                TouchLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        internal bool TryDemotePresentationState(AERISResidentCommitToken token,
            AERISResidentTileState target)
        {
            lock (sync)
            {
                Entry entry;
                if (!ValidateTokenLocked(token, out entry)) return false;
                if (target != AERISResidentTileState.RamResident &&
                    target != AERISResidentTileState.RenderReady ||
                    (int)entry.State < (int)target ||
                    (int)entry.State < (int)AERISResidentTileState.RamResident)
                {
                    invalidTransitionRejects++;
                    return false;
                }
                if (entry.State == target) return true;
                entry.State = target;
                entry.LastStateChangeUtcTicks = DateTime.UtcNow.Ticks;
                TouchLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        internal bool TryGetRamResident(AERISTerrainTileKey key,
            out AERISTerrainHeightTile tile,
            out AERISResidentCommitToken token)
        {
            tile = null;
            token = default(AERISResidentCommitToken);
            lock (sync)
            {
                Entry entry;
                if (!ValidateKeyForCurrentScopeLocked(key, databaseGeneration, true) ||
                    !entries.TryGetValue(key.StableId, out entry) || entry == null ||
                    (int)entry.State < (int)AERISResidentTileState.RamResident ||
                    entry.ResidentTile == null)
                {
                    cacheMisses++;
                    return false;
                }
                cacheHits++;
                tile = entry.ResidentTile;
                token = TokenForLocked(entry);
                TouchLocked(entry);
                return true;
            }
        }

        internal bool TryPin(AERISTerrainTileKey key,
            AERISResidentPinReason reason, out AERISResidentPinLease lease)
        {
            lease = null;
            if (!SinglePinReason(reason)) return false;
            lock (sync)
            {
                Entry entry;
                if (!ValidateKeyForCurrentScopeLocked(key, databaseGeneration, true) ||
                    !entries.TryGetValue(key.StableId, out entry) || entry == null)
                    return false;
                AddPinLocked(entry, reason);
                lease = new AERISResidentPinLease(this, key.StableId,
                    scopeGeneration, reason);
                TouchLocked(entry);
                UpdateStatusLocked();
                return true;
            }
        }

        internal bool Evict(AERISTerrainTileKey key, string cause)
        {
            lock (sync)
            {
                Entry entry;
                if (!entries.TryGetValue(key.StableId, out entry) || entry == null ||
                    entry.PinCount > 0) return false;
                RemoveEntryLocked(entry, AERISResidentEvictionReason.Explicit);
                lastCause = cause ?? string.Empty;
                UpdateStatusLocked();
                return true;
            }
        }

        internal AERISCurrentBodyResidentTelemetrySnapshot SnapshotTelemetry()
        {
            lock (sync)
            {
                var snapshot = new AERISCurrentBodyResidentTelemetrySnapshot
                {
                    Active = active && !disposed,
                    ActiveBody = activeBody,
                    ActiveBodyRadiusMillimetres = activeBodyRadiusMillimetres,
                    ActiveEnvironmentHash = activeEnvironmentHash,
                    ScopeGeneration = scopeGeneration,
                    BodyGeneration = bodyGeneration,
                    DatabaseGeneration = databaseGeneration,
                    RamBytes = ramBytes,
                    RamBudgetBytes = ramBudgetBytes,
                    OverBudgetBytes = Math.Max(0L, ramBytes - ramBudgetBytes),
                    EntryCount = entries.Count,
                    PinLeaseCount = pinLeaseCount,
                    Registrations = registrations,
                    StatePromotions = statePromotions,
                    RamCommits = ramCommits,
                    CacheHits = cacheHits,
                    CacheMisses = cacheMisses,
                    Evictions = evictions,
                    BudgetEvictions = budgetEvictions,
                    BodyTransitions = bodyTransitions,
                    DatabaseGenerationTransitions = databaseGenerationTransitions,
                    SceneResets = sceneResets,
                    StaleCommitRejects = staleCommitRejects,
                    ForeignBodyRejects = foreignBodyRejects,
                    BudgetRejects = budgetRejects,
                    InvalidTransitionRejects = invalidTransitionRejects,
                    AsyncDecodeSubmissions = asyncDecodeSubmissions,
                    AsyncDecodeSuccesses = asyncDecodeSuccesses,
                    AsyncDecodeFailures = asyncDecodeFailures,
                    GlobalBudgetRejects = globalBudgetRejects,
                    FarBudgetRejects = farBudgetRejects,
                    RouteBudgetRejects = routeBudgetRejects,
                    LocalBudgetRejects = localBudgetRejects,
                    GlobalBudgetEvictions = globalBudgetEvictions,
                    FarBudgetEvictions = farBudgetEvictions,
                    RouteBudgetEvictions = routeBudgetEvictions,
                    LocalBudgetEvictions = localBudgetEvictions,
                    LastCause = lastCause,
                    Status = status
                };
                foreach (Entry entry in entries.Values)
                {
                    if (entry == null) continue;
                    switch (entry.State)
                    {
                        case AERISResidentTileState.Indexed: snapshot.IndexedCount++; break;
                        case AERISResidentTileState.SsdReady: snapshot.SsdReadyCount++; break;
                        case AERISResidentTileState.Decoded: snapshot.DecodedCount++; break;
                        case AERISResidentTileState.RamResident: snapshot.RamResidentCount++; break;
                        case AERISResidentTileState.RenderReady: snapshot.RenderReadyCount++; break;
                        case AERISResidentTileState.GpuReady: snapshot.GpuReadyCount++; break;
                    }
                    if ((int)entry.State >= (int)AERISResidentTileState.RamResident &&
                        entry.ResidentTile != null)
                    {
                        switch (entry.Key.Lod)
                        {
                            case AERISTerrainTileLod.Global: snapshot.GlobalCount++; break;
                            case AERISTerrainTileLod.Far: snapshot.FarCount++; break;
                            case AERISTerrainTileLod.Route: snapshot.RouteCount++; break;
                            case AERISTerrainTileLod.Local: snapshot.LocalCount++; break;
                        }
                    }
                    if (entry.PinCount > 0) snapshot.PinnedEntryCount++;
                }
                return snapshot;
            }
        }

        internal void ReleasePin(string stableId, long leaseScopeGeneration,
            AERISResidentPinReason reason)
        {
            lock (sync)
            {
                if (disposed || leaseScopeGeneration != scopeGeneration) return;
                Entry entry;
                if (!entries.TryGetValue(stableId ?? string.Empty, out entry) ||
                    entry == null) return;
                int count;
                if (!entry.Pins.TryGetValue(reason, out count) || count <= 0) return;
                if (count == 1) entry.Pins.Remove(reason);
                else entry.Pins[reason] = count - 1;
                entry.PinCount = Math.Max(0, entry.PinCount - 1);
                pinLeaseCount = Math.Max(0, pinLeaseCount - 1);
                EnforceBudgetLocked(null);
                UpdateStatusLocked();
            }
        }

        bool ValidateKeyForCurrentScopeLocked(AERISTerrainTileKey key,
            long entryDatabaseGeneration, bool countStale)
        {
            if (disposed || !active) return false;
            bool sameBody = string.Equals(key.BodyName, activeBody,
                StringComparison.Ordinal) &&
                key.BodyRadiusMillimetres == activeBodyRadiusMillimetres &&
                string.Equals(key.EnvironmentHash, activeEnvironmentHash,
                    StringComparison.Ordinal);
            if (!sameBody)
            {
                foreignBodyRejects++;
                return false;
            }
            if (entryDatabaseGeneration != databaseGeneration)
            {
                if (countStale) staleCommitRejects++;
                return false;
            }
            return true;
        }

        bool ValidateTokenLocked(AERISResidentCommitToken token, out Entry entry)
        {
            entry = null;
            if (disposed || !active || token.IsEmpty) return false;
            if (token.ScopeGeneration != scopeGeneration ||
                token.BodyGeneration != bodyGeneration ||
                token.DatabaseGeneration != databaseGeneration)
            {
                staleCommitRejects++;
                return false;
            }
            if (!string.Equals(token.BodyName, activeBody, StringComparison.Ordinal) ||
                token.BodyRadiusMillimetres != activeBodyRadiusMillimetres ||
                !string.Equals(token.EnvironmentHash, activeEnvironmentHash,
                    StringComparison.Ordinal))
            {
                foreignBodyRejects++;
                return false;
            }
            if (!entries.TryGetValue(token.StableId, out entry) || entry == null ||
                entry.ScopeGeneration != scopeGeneration ||
                entry.BodyGeneration != bodyGeneration ||
                entry.DatabaseGeneration != databaseGeneration)
            {
                staleCommitRejects++;
                entry = null;
                return false;
            }
            // A StableId lookup is not accepted as sufficient authority. Re-check the
            // complete immutable tile identity so a malformed or stale worker token
            // cannot alias a different LOD/body/environment entry.
            if (!string.Equals(entry.Key.StableId, token.StableId,
                    StringComparison.Ordinal) ||
                !string.Equals(entry.Key.BodyName, token.BodyName,
                    StringComparison.Ordinal) ||
                entry.Key.BodyRadiusMillimetres != token.BodyRadiusMillimetres ||
                !string.Equals(entry.Key.EnvironmentHash, token.EnvironmentHash,
                    StringComparison.Ordinal) ||
                entry.Key.Lod != token.Lod)
            {
                staleCommitRejects++;
                entry = null;
                return false;
            }
            return true;
        }

        AERISResidentCommitToken TokenForLocked(Entry entry)
        {
            return new AERISResidentCommitToken(entry.Key, scopeGeneration,
                bodyGeneration, databaseGeneration);
        }

        void AddPinLocked(Entry entry, AERISResidentPinReason reason)
        {
            if (entry == null || !SinglePinReason(reason)) return;
            int count;
            entry.Pins.TryGetValue(reason, out count);
            entry.Pins[reason] = count + 1;
            entry.PinCount++;
            pinLeaseCount++;
        }

        static bool SinglePinReason(AERISResidentPinReason reason)
        {
            int value = (int)reason;
            return value > 0 && (value & (value - 1)) == 0;
        }

        bool MakeRoomLocked(long requiredBytes, string protectedStableId,
            bool allowPinnedOverBudget, AERISTerrainTileLod incomingLod)
        {
            requiredBytes = Math.Max(0L, requiredBytes);
            if (requiredBytes == 0L) return true;
            if (ramBudgetBytes <= 0L) return allowPinnedOverBudget;
            while (ramBytes + requiredBytes > ramBudgetBytes)
            {
                Entry candidate = FindEvictionCandidateLocked(protectedStableId,
                    ResidencyPriority(incomingLod));
                if (candidate == null) return allowPinnedOverBudget;
                RemoveEntryLocked(candidate, AERISResidentEvictionReason.Budget);
            }
            return true;
        }

        void EnforceBudgetLocked(string protectedStableId)
        {
            while (ramBytes > ramBudgetBytes)
            {
                Entry candidate = FindEvictionCandidateLocked(protectedStableId,
                    int.MaxValue);
                if (candidate == null) break;
                RemoveEntryLocked(candidate, AERISResidentEvictionReason.Budget);
            }
        }

        Entry FindEvictionCandidateLocked(string protectedStableId,
            int maximumResidencyPriority)
        {
            Entry selected = null;
            int selectedPriority = int.MaxValue;
            LinkedListNode<string> node = lru.First;
            while (node != null)
            {
                Entry entry;
                if (entries.TryGetValue(node.Value, out entry) && entry != null &&
                    entry.RamBytes > 0L && entry.PinCount == 0 &&
                    !string.Equals(entry.Key.StableId, protectedStableId,
                        StringComparison.Ordinal))
                {
                    int priority = ResidencyPriority(entry.Key.Lod);
                    if (priority <= maximumResidencyPriority &&
                        priority < selectedPriority)
                    {
                        selected = entry;
                        selectedPriority = priority;
                        if (selectedPriority <= 1) break;
                    }
                }
                node = node.Next;
            }
            return selected;
        }

        void TouchLocked(Entry entry)
        {
            if (entry == null) return;
            entry.LastAccessSequence = ++accessSequence;
            if (entry.LruNode == null)
                entry.LruNode = lru.AddLast(entry.Key.StableId);
            else
            {
                lru.Remove(entry.LruNode);
                lru.AddLast(entry.LruNode);
            }
        }

        void RemoveEntryLocked(Entry entry, AERISResidentEvictionReason reason)
        {
            if (entry == null) return;
            entries.Remove(entry.Key.StableId);
            if (entry.LruNode != null) lru.Remove(entry.LruNode);
            ramBytes = Math.Max(0L, ramBytes - Math.Max(0L, entry.RamBytes));
            pinLeaseCount = Math.Max(0, pinLeaseCount - Math.Max(0, entry.PinCount));
            entry.Pins.Clear();
            entry.PinCount = 0;
            entry.ResidentTile = null;
            entry.RamBytes = 0L;
            evictions++;
            if (reason == AERISResidentEvictionReason.Budget)
            {
                budgetEvictions++;
                IncrementBudgetEvictionLocked(entry.Key.Lod);
            }
        }

        void ClearEntriesLocked(AERISResidentEvictionReason reason)
        {
            if (entries.Count == 0)
            {
                lru.Clear();
                ramBytes = 0L;
                pinLeaseCount = 0;
                return;
            }
            var values = new List<Entry>(entries.Values);
            for (int i = 0; i < values.Count; i++)
                RemoveEntryLocked(values[i], reason);
            entries.Clear();
            lru.Clear();
            ramBytes = 0L;
            pinLeaseCount = 0;
        }

        void UpdateStatusLocked()
        {
            if (disposed) status = "DISPOSED";
            else if (!active) status = "INACTIVE";
            else if (ramBytes > ramBudgetBytes) status = "PINNED OVER BUDGET";
            else if (localBudgetRejects > 0L ||
                routeBudgetRejects > 0L || farBudgetRejects > 0L ||
                globalBudgetRejects > 0L)
                status = "GATE 4A RENDER-READY RESIDENT — BUDGET DEGRADED";
            else if (ramBytes > 0L) status = "GATE 4A RENDER-READY / GPU-READY ACTIVE";
            else status = "GATE 3.1 ASYNC FAR BASE DECODE READY";
        }

        static bool IsGate3ResidencyLod(AERISTerrainTileLod lod)
        {
            return lod == AERISTerrainTileLod.Global ||
                lod == AERISTerrainTileLod.Far ||
                lod == AERISTerrainTileLod.Route ||
                lod == AERISTerrainTileLod.Local;
        }

        // Higher values are protected first. Under pressure the cache degrades in
        // Local -> Route -> Far -> Global order, preserving the coarse foundation.
        static int ResidencyPriority(AERISTerrainTileLod lod)
        {
            switch (lod)
            {
                case AERISTerrainTileLod.Global: return 4;
                case AERISTerrainTileLod.Far: return 3;
                case AERISTerrainTileLod.Route: return 2;
                case AERISTerrainTileLod.Local: return 1;
                default: return 0;
            }
        }

        void IncrementBudgetRejectLocked(AERISTerrainTileLod lod)
        {
            switch (lod)
            {
                case AERISTerrainTileLod.Global: globalBudgetRejects++; break;
                case AERISTerrainTileLod.Far: farBudgetRejects++; break;
                case AERISTerrainTileLod.Route: routeBudgetRejects++; break;
                case AERISTerrainTileLod.Local: localBudgetRejects++; break;
            }
        }

        void IncrementBudgetEvictionLocked(AERISTerrainTileLod lod)
        {
            switch (lod)
            {
                case AERISTerrainTileLod.Global: globalBudgetEvictions++; break;
                case AERISTerrainTileLod.Far: farBudgetEvictions++; break;
                case AERISTerrainTileLod.Route: routeBudgetEvictions++; break;
                case AERISTerrainTileLod.Local: localBudgetEvictions++; break;
            }
        }

        static long NormalizeBudget(long bytes)
        {
            if (bytes <= 0L) return 0L;
            return Math.Min(16L * 1024L * 1024L * 1024L,
                Math.Max(16L * 1024L * 1024L, bytes));
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                ClearEntriesLocked(AERISResidentEvictionReason.Shutdown);
                disposed = true;
                active = false;
                activeBody = string.Empty;
                activeBodyRadiusMillimetres = 0L;
                activeEnvironmentHash = string.Empty;
                databaseGeneration = 0L;
                scopeGeneration++;
                bodyGeneration++;
                lastCause = "SHUTDOWN";
                status = "DISPOSED";
            }
        }
    }
}
