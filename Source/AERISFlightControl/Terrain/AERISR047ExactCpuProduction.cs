using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS47: production promotion for the seven R042-certified Exact CPU bodies.
    //
    // Main thread remains Snapshot / Authority Selection / Commit only. It captures
    // native body-relative directions and immutable map coordinates, but does not call
    // PQS terrain-height callbacks for Exact CPU production samples.
    //
    // GeneralCompute workers evaluate only immutable primitive snapshots and write the
    // resulting float height/land-water payload into the block arrays. Any capture or
    // worker failure disables Exact CPU for that body for the rest of the process and
    // restarts the affected tile on the existing PQS producer.
    internal sealed partial class AERISTerrainBlockPipeline
    {
        readonly HashSet<string> r047ExactCpuDisabledBodies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> r047ExactCpuSelectionLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool R047ShouldUseExactCpuProduction(
            AERISR042ExactCpuShadowSourceResolver.Decision decision,
            AERISR042ExactCpuShadowRuntimeSnapshot snapshot,
            string bodyName)
        {
            if (decision == null || snapshot == null ||
                !snapshot.IsStructurallyValid ||
                !AERISR042ExactCpuShadowSourceResolver.ProducerSwitchEnabled)
                return false;

            if (r047ExactCpuDisabledBodies.Contains(bodyName ?? string.Empty))
                return false;

            return AERISR042ExactCpuShadowSourceResolver.SelectProductionSource(
                decision, true) ==
                AERISR042ExactCpuShadowSourceResolver.ProductionSource.
                    CertifiedExactCpuShadow;
        }

        void R047LogProducerSelection(
            string bodyName,
            AERISR042ExactCpuShadowRuntimeSnapshot snapshot,
            bool enabled)
        {
            string key = (bodyName ?? string.Empty) + "|" +
                (enabled ? "EXACT_CPU" : "PQS");
            if (!r047ExactCpuSelectionLogged.Add(key)) return;

            AERISLogger.Info(
                "[AERIS47][R043_EXACT_CPU_PRODUCER]" +
                "; body=" + SafeR043(bodyName) +
                "; selected=" + (enabled ? "EXACT_CPU" : "PQS") +
                "; candidate=" + (snapshot == null ? "None" :
                    snapshot.Candidate.ToString()) +
                "; producer_switch=" + BoolR043(enabled) +
                "; production_authority=" + (enabled ? "EXACT_CPU" : "PQS") +
                "; db_authority=" + (enabled ? "EXACT_CPU" : "PQS") +
                "; exact_cpu_db_write=" + BoolR043(enabled) +
                "; worker_runtime_object_access=false");
        }

        static bool R047ExactCpuProductionActive(TileState state)
        {
            return state != null && state.R043Ptc != null &&
                state.R043Ptc.ProductionEnabled;
        }

        bool R047TryCaptureExactCpuProductionSample(
            TileState state,
            int local,
            double latitude,
            double longitude)
        {
            if (!R047ExactCpuProductionActive(state)) return false;
            R043PtcTileState shadow = state.R043Ptc;

            try
            {
                if (state.Body == null)
                    throw new InvalidOperationException("BODY_NULL");
                if (shadow.SamplingExpected == null ||
                    shadow.SamplingX == null ||
                    shadow.SamplingY == null ||
                    shadow.SamplingZ == null ||
                    shadow.SamplingU == null ||
                    shadow.SamplingV == null ||
                    shadow.SamplingLatitude == null ||
                    shadow.SamplingLongitude == null ||
                    shadow.SamplingExpectedSourceLatitude == null ||
                    shadow.SamplingExpectedSourceLongitude == null ||
                    shadow.SamplingBoundaryCacheHit == null ||
                    shadow.SamplingActive == null ||
                    local < 0 || local >= shadow.SamplingExpected.Length)
                    throw new InvalidOperationException("SAMPLING_ARRAY_CONTRACT");

                Vector3d inputDirection =
                    state.Body.GetRelSurfaceNVector(latitude, longitude);
                Vector3d direction = inputDirection.normalized;
                if (!FiniteR043(direction.x) ||
                    !FiniteR043(direction.y) ||
                    !FiniteR043(direction.z))
                    throw new InvalidOperationException("DIRECTION_NONFINITE");

                double mapU;
                double mapV;
                ComputeR043StockMapCoords(direction, out mapU, out mapV);

                // Expected is populated with the worker result later. There is no PQS
                // height query on this path.
                shadow.SamplingExpected[local] = 0.0;
                shadow.SamplingX[local] = direction.x;
                shadow.SamplingY[local] = direction.y;
                shadow.SamplingZ[local] = direction.z;
                shadow.SamplingU[local] = mapU;
                shadow.SamplingV[local] = mapV;
                shadow.SamplingLatitude[local] = latitude;
                shadow.SamplingLongitude[local] = longitude;
                shadow.SamplingExpectedSourceLatitude[local] = latitude;
                shadow.SamplingExpectedSourceLongitude[local] = longitude;
                shadow.SamplingBoundaryCacheHit[local] = 0;
                shadow.SamplingActive[local] = 1;
                return true;
            }
            catch (Exception ex)
            {
                R047DisableExactCpuBody(
                    shadow.BodyName,
                    "CAPTURE:" + ex.GetType().Name + ":" + (ex.Message ?? string.Empty));
                shadow.ProductionEnabled = false;
                return false;
            }
        }

        static void R047WriteExactCpuProductionSample(
            R043PtcBlockPayload block,
            int index,
            double actualAsl)
        {
            if (block == null || !block.ProductionEnabled)
                return;
            if (!FiniteR043(actualAsl))
                throw new InvalidOperationException("EXACT_CPU_NONFINITE");
            if (block.ProductionElevation == null ||
                block.ProductionFlags == null ||
                index < 0 ||
                index >= block.ProductionElevation.Length ||
                index >= block.ProductionFlags.Length)
                throw new InvalidOperationException("PRODUCTION_ARRAY_CONTRACT");

            block.ProductionElevation[index] = (float)actualAsl;
            block.ProductionFlags[index] =
                block.HasOcean && actualAsl <= 1.0 ? (byte)2 : (byte)1;
            block.Expected[index] = actualAsl;
            block.ProductionSamples++;
        }

        bool R047HandleExactCpuProductionBlockFailure(
            TileState state,
            R043PtcBlockPayload block)
        {
            if (state == null || state.R043Ptc == null ||
                block == null || !block.ProductionEnabled)
                return false;

            bool failed =
                !string.IsNullOrEmpty(block.Error) ||
                block.ProductionFailures != 0 ||
                block.ProductionSamples !=
                    (block.Active == null ? 0 : CountR047Active(block.Active));
            if (!failed) return false;

            string reason = !string.IsNullOrEmpty(block.Error) ?
                block.Error :
                "PRODUCTION_SAMPLE_COUNT:" +
                block.ProductionSamples.ToString(CultureInfo.InvariantCulture);

            R047DisableExactCpuBody(state.R043Ptc.BodyName, reason);

            // Exact production states are limited to one outstanding block, so this
            // reset cannot race another block from the same tile. Restart the complete
            // tile using the existing PQS producer and preserve the immutable snapshot
            // so PQS-vs-Exact shadow comparison remains available.
            state.R043Ptc.ProductionEnabled = false;
            ResetR047PtcEvidence(state.R043Ptc);
            state.NextBlock = 0;
            state.CompletedBlocks = 0;
            state.Valid = 0;
            state.Minimum = float.PositiveInfinity;
            state.Maximum = float.NegativeInfinity;
            state.LastPublishedPercent = 0;
            state.SamplingBlock = null;
            state.SamplingElevation = null;
            state.SamplingFlags = null;
            state.SamplingAuthorityExact = null;
            state.SamplingR040BCacheHits = null;
            ReleaseR043SamplingBlock(state);
            state.SamplingIndex = 0;
            Array.Clear(state.Elevation, 0, state.Elevation.Length);
            Array.Clear(state.Flags, 0, state.Flags.Length);
            return true;
        }

        void R047DisableExactCpuBody(string bodyName, string reason)
        {
            string normalized = bodyName ?? string.Empty;
            r047ExactCpuDisabledBodies.Add(normalized);
            AERISLogger.Warn(
                "[AERIS47][R043_EXACT_CPU_FALLBACK]" +
                "; body=" + SafeR043(normalized) +
                "; reason=" + SafeR043(reason) +
                "; producer_switch=false" +
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false");
        }

        static int CountR047Active(byte[] values)
        {
            if (values == null) return 0;
            int count = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] != 0) count++;
            return count;
        }

        static void ResetR047PtcEvidence(R043PtcTileState shadow)
        {
            if (shadow == null) return;
            shadow.Samples = 0;
            shadow.Mismatches = 0;
            shadow.NonFinite = 0;
            shadow.BoundaryCacheHitSamples = 0;
            shadow.BoundaryCacheHitMismatches = 0;
            shadow.NonBoundaryCacheHitMismatches = 0;
            shadow.MaxAbsError = 0.0;
            shadow.Blocks = 0;
            shadow.AllWorkersOffMainThread = true;
            shadow.Error = string.Empty;
            shadow.FirstMismatchIndex = -1;
            shadow.FirstExpectedBits = 0;
            shadow.FirstActualBits = 0;
            shadow.FirstExpected = 0.0;
            shadow.FirstActual = 0.0;
            shadow.FirstLatitude = 0.0;
            shadow.FirstLongitude = 0.0;
            shadow.FirstX = 0.0;
            shadow.FirstY = 0.0;
            shadow.FirstZ = 0.0;
            shadow.FirstU = 0.0;
            shadow.FirstV = 0.0;
            shadow.FirstBlockId = -1;
            shadow.FirstGlobalX = -1;
            shadow.FirstGlobalY = -1;
            shadow.FirstBoundary = false;
            shadow.FirstBoundaryCacheHit = false;
            shadow.FirstExpectedSourceLatitude = 0.0;
            shadow.FirstExpectedSourceLongitude = 0.0;
            shadow.ProductionSamples = 0;
            shadow.ProductionFailures = 0;
        }
    }
}
