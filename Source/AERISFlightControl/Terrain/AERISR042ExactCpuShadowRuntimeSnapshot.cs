using System;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 4A immutable worker-payload contract.
    //
    // This type is deliberately free of Unity/KSP/runtime-object references.
    // A later main-thread capture builder may populate exactly one accepted
    // pure snapshot family. The worker may receive this object, but may never
    // retain or dereference CelestialBody/PQS/PQSMod/Unity objects.
    //
    // HARD RULES:
    // - body name is diagnostic identity only; it is not certification;
    // - CaptureThreadId records provenance and does not authorize a switch;
    // - R039 Minmus and R041 all-body chain payloads are mutually exclusive;
    // - production authority remains PQS until independent runtime parity gates pass.
    internal sealed class AERISR042ExactCpuShadowRuntimeSnapshot
    {
        internal readonly string BodyName;
        internal readonly AERISR042ExactCpuShadowSourceResolver.CandidateKind Candidate;
        internal readonly string Certificate;
        internal readonly string EnvironmentHash;
        internal readonly string Topology;
        internal readonly int CaptureThreadId;
        internal readonly double PqsRadius;
        internal readonly AERISR039MinmusPureCpuExact.VertexPlanetSnapshot MinmusSnapshot;
        internal readonly AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot HeightChainSnapshot;

        internal AERISR042ExactCpuShadowRuntimeSnapshot(
            string bodyName,
            AERISR042ExactCpuShadowSourceResolver.CandidateKind candidate,
            string certificate,
            string environmentHash,
            string topology,
            int captureThreadId,
            double pqsRadius,
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot minmusSnapshot,
            AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot heightChainSnapshot)
        {
            if (string.IsNullOrEmpty(bodyName))
                throw new ArgumentException("bodyName is required", "bodyName");
            if (candidate == AERISR042ExactCpuShadowSourceResolver.CandidateKind.None)
                throw new ArgumentException("candidate must be certified family", "candidate");
            if (string.IsNullOrEmpty(certificate))
                throw new ArgumentException("certificate is required", "certificate");
            if (captureThreadId <= 0)
                throw new ArgumentOutOfRangeException("captureThreadId");
            if (double.IsNaN(pqsRadius) || double.IsInfinity(pqsRadius) || pqsRadius <= 0.0)
                throw new ArgumentOutOfRangeException("pqsRadius");

            bool hasMinmus = minmusSnapshot != null;
            bool hasHeightChain = heightChainSnapshot != null;
            if (hasMinmus == hasHeightChain)
                throw new ArgumentException(
                    "exactly one pure snapshot family must be present");

            if (candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R039MinmusVertexPlanet &&
                !hasMinmus)
                throw new ArgumentException("R039 candidate requires Minmus snapshot");

            if (candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain &&
                !hasHeightChain)
                throw new ArgumentException("R041 candidate requires height-chain snapshot");

            BodyName = bodyName;
            Candidate = candidate;
            Certificate = certificate;
            EnvironmentHash = environmentHash ?? string.Empty;
            Topology = topology ?? string.Empty;
            CaptureThreadId = captureThreadId;
            PqsRadius = pqsRadius;
            MinmusSnapshot = minmusSnapshot;
            HeightChainSnapshot = heightChainSnapshot;
        }

        internal bool IsStructurallyValid
        {
            get
            {
                if (Candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R039MinmusVertexPlanet)
                    return MinmusSnapshot != null && HeightChainSnapshot == null;

                if (Candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain)
                    return HeightChainSnapshot != null && MinmusSnapshot == null;

                return false;
            }
        }
    }
}
