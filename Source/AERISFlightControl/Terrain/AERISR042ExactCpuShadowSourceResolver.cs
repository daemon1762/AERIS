using System;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 3 source resolver contract.
    //
    // This layer does NOT switch production terrain away from PQS. It only converts
    // a live main-thread CelestialBody reference into an immutable primitive decision
    // describing whether the body is a candidate for an already accepted exact CPU
    // shadow family. A later runtime-snapshot gate must still certify the concrete
    // modifier topology/configuration before any producer switch can be considered.
    //
    // HARD RULES:
    // - no CelestialBody/PQS/runtime object is retained in Decision;
    // - body name alone never authorizes production shadow output;
    // - unsupported/unknown/failed snapshot cases fall back to PQS;
    // - producer switch stays disabled in this phase.
    internal static class AERISR042ExactCpuShadowSourceResolver
    {
        internal enum CandidateKind
        {
            None = 0,
            R039MinmusVertexPlanet = 1,
            R041HeightModifierChain = 2
        }

        internal enum ProductionSource
        {
            PqsFallback = 0,
            CertifiedExactCpuShadow = 1
        }

        internal sealed class Decision
        {
            internal readonly string BodyName;
            internal readonly CandidateKind Candidate;
            internal readonly string Certificate;
            internal readonly bool RuntimeSnapshotCertificationRequired;
            internal readonly string FallbackReason;

            internal Decision(
                string bodyName,
                CandidateKind candidate,
                string certificate,
                bool runtimeSnapshotCertificationRequired,
                string fallbackReason)
            {
                BodyName = bodyName ?? string.Empty;
                Candidate = candidate;
                Certificate = certificate ?? string.Empty;
                RuntimeSnapshotCertificationRequired =
                    runtimeSnapshotCertificationRequired;
                FallbackReason = fallbackReason ?? string.Empty;
            }

            internal bool IsCandidate
            {
                get { return Candidate != CandidateKind.None; }
            }
        }

        // AERIS47 promotion gate: R042 snapshot/runtime certification, R043 live-preload
        // integration, AERIS46 natural boundary provenance repair and ENV3 persistence
        // have passed. Only the seven explicitly certified bodies may select Exact CPU;
        // every other body remains fail-closed on PQS.
        internal const bool ProducerSwitchEnabled = true;

        internal const int AcceptedCandidateBodyCount = 7;
        internal const int R039CandidateBodyCount = 1;
        internal const int R041CandidateBodyCount = 6;

        internal static Decision ResolveCandidate(CelestialBody body)
        {
            if (body == null)
                return Fallback(string.Empty, "BODY_NULL");

            string bodyName = body.name ?? string.Empty;
            if (string.IsNullOrEmpty(bodyName))
                return Fallback(string.Empty, "BODY_NAME_EMPTY");

            if (string.Equals(bodyName, "Minmus", StringComparison.OrdinalIgnoreCase))
            {
                return new Decision(
                    bodyName,
                    CandidateKind.R039MinmusVertexPlanet,
                    "R039_MINMUS_PURE_CPU_EXACT_ACCEPTED",
                    true,
                    string.Empty);
            }

            if (IsR041HeightChainBody(bodyName))
            {
                return new Decision(
                    bodyName,
                    CandidateKind.R041HeightModifierChain,
                    "R041_ALLBODY_HEIGHT_CHAIN_BIT_EXACT_16950_PLUS_TERRAINALTITUDE_3360",
                    true,
                    string.Empty);
            }

            return Fallback(bodyName, "BODY_NOT_IN_ACCEPTED_EXACT_SET");
        }

        internal static ProductionSource SelectProductionSource(
            Decision decision,
            bool runtimeSnapshotCertified)
        {
            // Name/candidate classification is never sufficient by itself. Even after
            // snapshot certification, Phase 3 keeps authority on PQS until the explicit
            // producer-switch gate is promoted in a later phase.
            if (decision == null || !decision.IsCandidate)
                return ProductionSource.PqsFallback;
            if (!runtimeSnapshotCertified)
                return ProductionSource.PqsFallback;
            if (!ProducerSwitchEnabled)
                return ProductionSource.PqsFallback;
            return ProductionSource.CertifiedExactCpuShadow;
        }

        internal static bool IsR041HeightChainBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return false;
            return
                string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bodyName, "Eve", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bodyName, "Duna", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bodyName, "Dres", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bodyName, "Moho", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bodyName, "Eeloo", StringComparison.OrdinalIgnoreCase);
        }

        static Decision Fallback(string bodyName, string reason)
        {
            return new Decision(
                bodyName,
                CandidateKind.None,
                string.Empty,
                false,
                reason);
        }
    }
}
