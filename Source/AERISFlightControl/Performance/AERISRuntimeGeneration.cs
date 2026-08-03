using System;

namespace AERISFlightControl.Performance
{
    // Plain-value identity carried by every asynchronous job and result.  No Unity or
    // KSP object reference crosses the worker boundary.
    public struct AERISRuntimeGenerationStamp : IEquatable<AERISRuntimeGenerationStamp>
    {
        public readonly long SceneGeneration;
        public readonly long VesselPersistentId;
        public readonly long VesselInstanceGeneration;
        public readonly long BodyId;
        public readonly long ControlPointRevision;
        public readonly long DockingSeparationRevision;
        public readonly long RunwayDatabaseRevision;
        public readonly long RunwaySelectionRevision;
        public readonly long FlightPlanRevision;
        public readonly long DisplayLayoutRevision;
        public readonly long Sequence;
        public readonly long MonotonicTimestamp;

        internal AERISRuntimeGenerationStamp(long scene, long vesselId,
            long vesselInstance, long body, long controlPoint, long docking,
            long runwayDatabase, long runwaySelection, long flightPlan,
            long displayLayout, long sequence, long timestamp)
        {
            SceneGeneration = scene;
            VesselPersistentId = vesselId;
            VesselInstanceGeneration = vesselInstance;
            BodyId = body;
            ControlPointRevision = controlPoint;
            DockingSeparationRevision = docking;
            RunwayDatabaseRevision = runwayDatabase;
            RunwaySelectionRevision = runwaySelection;
            FlightPlanRevision = flightPlan;
            DisplayLayoutRevision = displayLayout;
            Sequence = sequence;
            MonotonicTimestamp = timestamp;
        }

        public bool Equals(AERISRuntimeGenerationStamp other)
        {
            // Sequence/timestamp identify a sample, but commit compatibility is governed
            // by the state generations.  A newer snapshot in the same state may replace
            // an older latest-wins request without invalidating its identity domain.
            return SceneGeneration == other.SceneGeneration &&
                VesselPersistentId == other.VesselPersistentId &&
                VesselInstanceGeneration == other.VesselInstanceGeneration &&
                BodyId == other.BodyId && ControlPointRevision == other.ControlPointRevision &&
                DockingSeparationRevision == other.DockingSeparationRevision &&
                RunwayDatabaseRevision == other.RunwayDatabaseRevision &&
                RunwaySelectionRevision == other.RunwaySelectionRevision &&
                FlightPlanRevision == other.FlightPlanRevision &&
                DisplayLayoutRevision == other.DisplayLayoutRevision;
        }

        public override bool Equals(object obj)
        {
            return obj is AERISRuntimeGenerationStamp &&
                Equals((AERISRuntimeGenerationStamp)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)SceneGeneration;
                hash = hash * 397 ^ VesselPersistentId.GetHashCode();
                hash = hash * 397 ^ VesselInstanceGeneration.GetHashCode();
                hash = hash * 397 ^ BodyId.GetHashCode();
                hash = hash * 397 ^ ControlPointRevision.GetHashCode();
                hash = hash * 397 ^ DockingSeparationRevision.GetHashCode();
                hash = hash * 397 ^ RunwayDatabaseRevision.GetHashCode();
                hash = hash * 397 ^ RunwaySelectionRevision.GetHashCode();
                hash = hash * 397 ^ FlightPlanRevision.GetHashCode();
                hash = hash * 397 ^ DisplayLayoutRevision.GetHashCode();
                return hash;
            }
        }
    }

    internal sealed class AERISGenerationRegistry
    {
        readonly object sync = new object();
        long scene = 1L;
        long vesselId;
        long vesselInstance = 1L;
        long body;
        long controlPoint;
        long docking;
        long runwayDatabase;
        long runwaySelection;
        long flightPlan;
        long displayLayout;
        long sequence;

        internal AERISRuntimeGenerationStamp Capture(long timestamp)
        {
            lock (sync)
            {
                return new AERISRuntimeGenerationStamp(scene, vesselId, vesselInstance,
                    body, controlPoint, docking, runwayDatabase, runwaySelection,
                    flightPlan, displayLayout, ++sequence, timestamp);
            }
        }

        internal bool Matches(AERISRuntimeGenerationStamp value)
        {
            lock (sync)
            {
                return scene == value.SceneGeneration && vesselId == value.VesselPersistentId &&
                    vesselInstance == value.VesselInstanceGeneration && body == value.BodyId &&
                    controlPoint == value.ControlPointRevision &&
                    docking == value.DockingSeparationRevision &&
                    runwayDatabase == value.RunwayDatabaseRevision &&
                    runwaySelection == value.RunwaySelectionRevision &&
                    flightPlan == value.FlightPlanRevision &&
                    displayLayout == value.DisplayLayoutRevision;
            }
        }

        internal void SceneChanged()
        {
            lock (sync) { scene++; vesselInstance++; sequence = 0L; }
        }

        internal void VesselChanged(long persistentId, long bodyId)
        {
            lock (sync)
            {
                vesselId = persistentId;
                body = bodyId;
                vesselInstance++;
                controlPoint++;
                docking++;
                sequence = 0L;
            }
        }

        internal void UpdateRunway(long databaseRevision, long selectionRevision)
        {
            lock (sync)
            {
                runwayDatabase = Math.Max(0L, databaseRevision);
                runwaySelection = Math.Max(0L, selectionRevision);
            }
        }

        internal void ControlPointChanged() { lock (sync) controlPoint++; }
        internal void DockingChanged() { lock (sync) docking++; }
        internal void FlightPlanChanged() { lock (sync) flightPlan++; }
        internal void DisplayLayoutChanged() { lock (sync) displayLayout++; }
    }
}
