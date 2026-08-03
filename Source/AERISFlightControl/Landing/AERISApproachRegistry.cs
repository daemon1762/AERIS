using System;
using System.Collections.Generic;

namespace AERISFlightControl.Landing
{
    // Read-only approach procedure registry.  It is intentionally disconnected from
    // AP/NAV control paths in Gate 0; consumers may only inspect/visualize snapshots.
    internal sealed class AERISApproachRegistry
    {
        readonly Dictionary<string, List<AERISApproachProcedure>> byDirection =
            new Dictionary<string, List<AERISApproachProcedure>>(
                StringComparer.OrdinalIgnoreCase);
        long generation;
        string selectedProcedureId = string.Empty;

        internal long Generation { get { return generation; } }
        internal int DirectionCount { get { return byDirection.Count; } }
        internal string Status { get; private set; } = "NOT BUILT";

        internal void Rebuild(IEnumerable<AERISAirfieldDefinition> airfields,
            IDictionary<string, AERISApproachObstacleSnapshot> obstacleSnapshots,
            AERISApproachPlanningLimits limits)
        {
            generation++;
            byDirection.Clear();
            int procedureCount = 0;
            int pendingCount = 0;
            int rejectedCount = 0;
            if (airfields != null)
                foreach (AERISAirfieldDefinition airfield in airfields)
                {
                    if (airfield == null) continue;
                    for (int i = 0; i < airfield.Runways.Count; i++)
                    {
                        AERISRunwayDefinition runway = airfield.Runways[i];
                        if (runway == null) continue;
                        for (int j = 0; j < runway.Directions.Count; j++)
                        {
                            AERISRunwayDirectionDefinition direction = runway.Directions[j];
                            if (direction == null || string.IsNullOrEmpty(direction.StableId))
                                continue;
                            AERISApproachObstacleSnapshot obstacles = null;
                            if (obstacleSnapshots != null)
                                obstacleSnapshots.TryGetValue(direction.StableId,
                                    out obstacles);
                            IList<AERISApproachProcedure> built =
                                AERISAdaptiveApproachPlanner.BuildCandidates(
                                    ExtractPhysicalRunwayId(runway.StableId), runway,
                                    direction, obstacles, limits, generation);
                            var stored = new List<AERISApproachProcedure>();
                            for (int k = 0; k < built.Count; k++)
                            {
                                AERISApproachProcedure procedure = built[k];
                                if (procedure == null) continue;
                                stored.Add(procedure.Clone());
                                procedureCount++;
                                if (procedure.State == AERISApproachProcedureState.Pending)
                                    pendingCount++;
                                if (procedure.State == AERISApproachProcedureState.Rejected)
                                    rejectedCount++;
                            }
                            byDirection[direction.StableId] = stored;
                        }
                    }
                }
            if (!ContainsProcedure(selectedProcedureId)) selectedProcedureId = string.Empty;
            Status = "GEN " + generation + "; " + byDirection.Count +
                " DIRECTION(S); " + procedureCount + " PROCEDURE(S); " +
                pendingCount + " PENDING; " + rejectedCount + " REJECTED";
        }

        internal IList<AERISApproachProcedure> SnapshotForDirection(
            string directionStableId)
        {
            List<AERISApproachProcedure> source;
            if (string.IsNullOrEmpty(directionStableId) ||
                !byDirection.TryGetValue(directionStableId, out source))
                return new List<AERISApproachProcedure>().AsReadOnly();
            var result = new List<AERISApproachProcedure>(source.Count);
            for (int i = 0; i < source.Count; i++) result.Add(source[i].Clone());
            return result.AsReadOnly();
        }

        internal bool Select(string stableProcedureId)
        {
            if (!ContainsProcedure(stableProcedureId)) return false;
            selectedProcedureId = stableProcedureId;
            return true;
        }

        internal AERISApproachProcedure SelectedSnapshot()
        {
            if (string.IsNullOrEmpty(selectedProcedureId)) return null;
            foreach (KeyValuePair<string, List<AERISApproachProcedure>> item in byDirection)
                for (int i = 0; i < item.Value.Count; i++)
                    if (string.Equals(item.Value[i].StableId, selectedProcedureId,
                        StringComparison.OrdinalIgnoreCase))
                        return item.Value[i].Clone();
            return null;
        }

        bool ContainsProcedure(string stableProcedureId)
        {
            if (string.IsNullOrEmpty(stableProcedureId)) return false;
            foreach (KeyValuePair<string, List<AERISApproachProcedure>> item in byDirection)
                for (int i = 0; i < item.Value.Count; i++)
                    if (string.Equals(item.Value[i].StableId, stableProcedureId,
                        StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ExtractPhysicalRunwayId(string stableRunwayId)
        {
            if (string.IsNullOrEmpty(stableRunwayId)) return string.Empty;
            const string marker = "\nPHYSICAL_RUNWAY\n";
            int start = stableRunwayId.IndexOf(marker,
                StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = stableRunwayId.IndexOf('\n', start);
            return end < 0 ? stableRunwayId.Substring(start) :
                stableRunwayId.Substring(start, end - start);
        }
    }
}
