using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;
using AERISFlightControl.UI;

namespace AERISFlightControl.Terrain
{
    // OH REV3.5 R017: observation-only detector for ND-local front-presentation stalls.
    // It never writes renderer/control state. A stall requires an old committed FRONT plus
    // real pending presentation demand; a stationary retained FRONT is not classified.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class AERISR017NdPresentationStallObserver : MonoBehaviour
    {
        const string Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER";
        const string R025Variant = "AERIS30_REV3_5_SALBUTAMOL_SULFATE_R025_RESIDUAL_STALL_ATTRIBUTION_OBSERVER";
        const string R025LogPrefix = "[OH_REV3_5_R025_STALL_ATTRIBUTION]";
        const string LogPrefix = "[OH_REV3_5_R017_ND_PRESENT_STALL]";
        const float SampleIntervalSeconds = 0.10f;
        const float StallThresholdSeconds = 0.25f;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static readonly FieldInfo BootstrapFlightInstrumentField =
            typeof(AERISBootstrap).GetField("flightInstrument", PrivateInstance);
        static readonly FieldInfo FlightInstrumentNavigationDisplayField =
            typeof(AERISFlightInstrument).GetField("navigationDisplay", PrivateInstance);
        static readonly FieldInfo NavigationDisplayRendererField =
            typeof(AERISNavigationDisplay).GetField("terrainTileRenderer", PrivateInstance);

        static readonly FieldInfo FrontBufferValidField = RendererField("frontBufferValid");
        static readonly FieldInfo FrontCommittedRealtimeField = RendererField("frontCommittedRealtime");
        static readonly FieldInfo LastFrontBufferPresentedField = RendererField("lastFrontBufferPresented");
        static readonly FieldInfo LastFrontBufferLatchedField = RendererField("lastFrontBufferLatched");
        static readonly FieldInfo FrontContentRevisionField = RendererField("frontContentRevision");
        static readonly FieldInfo GpuContentRevisionField = RendererField("gpuContentRevision");
        static readonly FieldInfo FrontBufferSwapsField = RendererField("frontBufferSwaps");
        static readonly FieldInfo BackRenderFramesField = RendererField("backRenderFrames");
        static readonly FieldInfo BlockedIncompleteSwapsField = RendererField("blockedIncompleteSwaps");
        static readonly FieldInfo SkippedBackRenderFramesField = RendererField("skippedBackRenderFrames");
        static readonly FieldInfo MotionRefreshesField = RendererField("operationHealthMotionRefreshes");
        static readonly FieldInfo ContentTicksField = RendererField("operationHealthContentTicks");
        static readonly FieldInfo ContentCapturesField = RendererField("operationHealthContentCaptures");
        static readonly FieldInfo ResolveCallsField = RendererField("operationHealthResolveCalls");
        static readonly FieldInfo R014PublicationSerialField = RendererField("rev35R014PublicationSerial");
        static readonly FieldInfo R014ReconciledSerialField = RendererField("rev35R014ReconciledPublicationSerial");
        static readonly FieldInfo R014FullReconcileField = RendererField("operationHealthRev35R014FullReconciles");
        static readonly FieldInfo R014PublicationEventsField = RendererField("operationHealthRev35R014PublicationEvents");

        // R025 observation-only pipeline attribution.
        static readonly FieldInfo MainCommitBacklogField =
            RendererField("operationHealthMainCommitBacklog");
        static readonly FieldInfo PendingEntryCommitField =
            RendererField("pendingEntryCommit");

        static readonly FieldInfo R006MissingPendingField =
            RendererField("operationHealthRev35R006FoundationMissingPending");
        static readonly FieldInfo R006MissingRenderReadyField =
            RendererField("operationHealthRev35R006FoundationMissingRenderReady");
        static readonly FieldInfo R006MissingUpstreamField =
            RendererField("operationHealthRev35R006FoundationMissingUpstream");

        static readonly FieldInfo R018VisibleRequiredFarField =
            RendererField("operationHealthRev35R018VisibleRequiredFar");
        static readonly FieldInfo R018VisibleReadyFarField =
            RendererField("operationHealthRev35R018VisibleReadyFar");
        static readonly FieldInfo R018VisibleCoverageField =
            RendererField("operationHealthRev35R018VisibleCoverage");
        static readonly FieldInfo R019VisibleDeficitField =
            RendererField("operationHealthRev35R019VisibleDeficit");

        static readonly FieldInfo ContentReadyFarField =
            RendererField("contentReadyFar");
        static readonly FieldInfo R008GeometryReconcilePendingField =
            RendererField("rev35R008GeometryReconcilePending");

        static readonly FieldInfo FrontViewGenerationField =
            RendererField("frontViewGeneration");
        static readonly FieldInfo FrontTerrainGenerationField =
            RendererField("frontTerrainGeneration");
        static readonly FieldInfo ContentTerrainGenerationField =
            RendererField("contentTerrainGeneration");
        static readonly FieldInfo ContentVisibleField =
            RendererField("contentVisible");

        static readonly FieldInfo RasterizerField =
            RendererField("rasterizer");

        static readonly FieldInfo R024VisibleReadyChecksField =
            RendererField("operationHealthRev35R024VisibleReadyChecks");
        static readonly FieldInfo R024HiddenPendingPreemptionsField =
            RendererField("operationHealthRev35R024HiddenPendingPreemptions");
        static readonly FieldInfo R024FinalizeProtectedField =
            RendererField("operationHealthRev35R024LateFinalizeProtected");

        static readonly FieldInfo R017BlockedRenderedFalseField =
            RendererField("operationHealthRev35R017BlockedRenderedFalse");
        static readonly FieldInfo R017BlockedFoundationFlagField =
            RendererField("operationHealthRev35R017BlockedFoundationFlag");
        static readonly FieldInfo R017BlockedCoverageField =
            RendererField("operationHealthRev35R017BlockedCoverage");
        static readonly FieldInfo R017BlockedReadyFarField =
            RendererField("operationHealthRev35R017BlockedReadyFar");
        static readonly FieldInfo R017CadenceSkipField =
            RendererField("operationHealthRev35R017CadenceSkips");

        AERISBootstrap core;
        AERISNavigationDisplay navigationDisplay;
        AERISTerrainGpuTileRenderer renderer;
        float nextSampleRealtime;
        bool bindingWarningLogged;
        bool baselineValid;
        bool stallActive;
        float stallStartedRealtime;
        float stallMaxFrontAge;

        long lastSwapCount;
        long swapBaselineBackRender;
        long swapBaselineBlocked;
        long swapBaselineSkipped;
        long swapBaselineMotion;
        long swapBaselineContentTicks;
        long swapBaselineContentCaptures;
        long swapBaselineResolve;
        long swapBaselineR014FullReconcile;
        long swapBaselineR014Publications;
        long swapBaselineRenderedFalse;
        long swapBaselineFoundationFlag;
        long swapBaselineCoverage;
        long swapBaselineReadyFar;
        long swapBaselineCadenceSkip;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            nextSampleRealtime = 0f;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSampleRealtime) return;
            nextSampleRealtime = now + SampleIntervalSeconds;

            if (!ResolveTargets())
            {
                if (!bindingWarningLogged && HighLogic.LoadedSceneIsFlight)
                {
                    bindingWarningLogged = true;
                    AERISLogger.Warn(LogPrefix + " target unavailable; observer owns no presentation authority.");
                }
                return;
            }
            bindingWarningLogged = false;

            if (!HighLogic.LoadedSceneIsFlight)
            {
                baselineValid = false;
                stallActive = false;
                return;
            }
            Sample(now);
        }

        bool ResolveTargets()
        {
            if (!BindingsAvailable()) return false;
            if (core == null) core = FindObjectOfType<AERISBootstrap>();
            if (core == null) return false;
            object flightInstrument = BootstrapFlightInstrumentField.GetValue(core);
            if (flightInstrument == null)
            {
                navigationDisplay = null;
                renderer = null;
                baselineValid = false;
                stallActive = false;
                return false;
            }
            AERISNavigationDisplay currentNavigation =
                FlightInstrumentNavigationDisplayField.GetValue(flightInstrument) as AERISNavigationDisplay;
            AERISTerrainGpuTileRenderer currentRenderer = currentNavigation == null ? null :
                NavigationDisplayRendererField.GetValue(currentNavigation) as AERISTerrainGpuTileRenderer;
            if (!ReferenceEquals(currentRenderer, renderer))
            {
                navigationDisplay = currentNavigation;
                renderer = currentRenderer;
                baselineValid = false;
                stallActive = false;
            }
            else navigationDisplay = currentNavigation;
            return navigationDisplay != null && renderer != null;
        }

        void Sample(float now)
        {
            bool frontValid = ReadBool(FrontBufferValidField, renderer);
            bool frontPresented = ReadBool(LastFrontBufferPresentedField, renderer);
            bool frontLatched = ReadBool(LastFrontBufferLatchedField, renderer);
            float frontCommittedRealtime = ReadFloat(FrontCommittedRealtimeField, renderer);
            long frontContentRevision = ReadLong(FrontContentRevisionField, renderer);
            long gpuContentRevision = ReadLong(GpuContentRevisionField, renderer);
            long swaps = ReadLong(FrontBufferSwapsField, renderer);
            long backRender = ReadLong(BackRenderFramesField, renderer);
            long blocked = ReadLong(BlockedIncompleteSwapsField, renderer);
            long skipped = ReadLong(SkippedBackRenderFramesField, renderer);
            long motion = ReadLong(MotionRefreshesField, renderer);
            long contentTicks = ReadLong(ContentTicksField, renderer);
            long contentCaptures = ReadLong(ContentCapturesField, renderer);
            long resolveCalls = ReadLong(ResolveCallsField, renderer);
            long pubSerial = ReadLong(R014PublicationSerialField, renderer);
            long reconciledSerial = ReadLong(R014ReconciledSerialField, renderer);
            long fullReconcile = ReadLong(R014FullReconcileField, renderer);
            long publications = ReadLong(R014PublicationEventsField, renderer);
            long renderedFalse = ReadLong(R017BlockedRenderedFalseField, renderer);
            long foundationFlag = ReadLong(R017BlockedFoundationFlagField, renderer);
            long coverage = ReadLong(R017BlockedCoverageField, renderer);
            long readyFar = ReadLong(R017BlockedReadyFarField, renderer);
            long cadenceSkip = ReadLong(R017CadenceSkipField, renderer);

            if (!baselineValid)
            {
                ResetSwapBaseline(swaps, backRender, blocked, skipped, motion, contentTicks,
                    contentCaptures, resolveCalls, fullReconcile, publications, renderedFalse,
                    foundationFlag, coverage, readyFar, cadenceSkip);
                baselineValid = true;
                return;
            }

            if (swaps != lastSwapCount)
            {
                if (stallActive)
                    EmitStallEnd(now, swaps, backRender, blocked, skipped, motion, contentTicks,
                        contentCaptures, resolveCalls, pubSerial, reconciledSerial, fullReconcile,
                        publications, renderedFalse, foundationFlag, coverage, readyFar, cadenceSkip,
                        "FRONT_SWAP");
                ResetSwapBaseline(swaps, backRender, blocked, skipped, motion, contentTicks,
                    contentCaptures, resolveCalls, fullReconcile, publications, renderedFalse,
                    foundationFlag, coverage, readyFar, cadenceSkip);
                return;
            }

            float frontAge = frontCommittedRealtime > 0f ? Math.Max(0f, now - frontCommittedRealtime) : 0f;
            long blockedSinceSwap = NonNegativeDelta(blocked, swapBaselineBlocked);
            long skippedSinceSwap = NonNegativeDelta(skipped, swapBaselineSkipped);
            long motionSinceSwap = NonNegativeDelta(motion, swapBaselineMotion);
            bool revisionMismatch = frontContentRevision != gpuContentRevision;
            bool publicationPending = pubSerial != reconciledSerial;
            bool requestedReady = renderer.RequestedViewReady;
            bool demandPending = blockedSinceSwap > 0 || skippedSinceSwap > 0 ||
                motionSinceSwap > 0 || revisionMismatch || publicationPending || !requestedReady;
            bool stalled = frontValid && frontPresented && frontLatched && demandPending &&
                frontAge >= StallThresholdSeconds;

            if (stalled)
            {
                if (!stallActive)
                {
                    stallActive = true;
                    stallStartedRealtime = now;
                    stallMaxFrontAge = frontAge;
                    EmitStallStart(now, frontAge, backRender, blocked, skipped, motion, contentTicks,
                        contentCaptures, resolveCalls, pubSerial, reconciledSerial, fullReconcile,
                        publications, renderedFalse, foundationFlag, coverage, readyFar, cadenceSkip,
                        revisionMismatch, requestedReady);
                }
                else if (frontAge > stallMaxFrontAge) stallMaxFrontAge = frontAge;
            }
            else if (stallActive)
            {
                EmitStallEnd(now, swaps, backRender, blocked, skipped, motion, contentTicks,
                    contentCaptures, resolveCalls, pubSerial, reconciledSerial, fullReconcile,
                    publications, renderedFalse, foundationFlag, coverage, readyFar, cadenceSkip,
                    "DEMAND_CLEARED");
                stallActive = false;
            }
        }

        void EmitStallStart(float now, float frontAge, long backRender, long blocked,
            long skipped, long motion, long contentTicks, long contentCaptures, long resolveCalls,
            long pubSerial, long reconciledSerial, long fullReconcile, long publications,
            long renderedFalse, long foundationFlag, long coverage, long readyFar, long cadenceSkip,
            bool revisionMismatch, bool requestedReady)
        {
            EmitR025Attribution("START");
            AERISLogger.Info(LogPrefix + " START variant=" + Variant +
                "; front_age_s=" + frontAge.ToString("F3", CultureInfo.InvariantCulture) +
                "; back_since_swap=" + NonNegativeDelta(backRender, swapBaselineBackRender) +
                "; blocked_since_swap=" + NonNegativeDelta(blocked, swapBaselineBlocked) +
                "; skipped_since_swap=" + NonNegativeDelta(skipped, swapBaselineSkipped) +
                "; motion_since_swap=" + NonNegativeDelta(motion, swapBaselineMotion) +
                "; content_tick_since_swap=" + NonNegativeDelta(contentTicks, swapBaselineContentTicks) +
                "; content_capture_since_swap=" + NonNegativeDelta(contentCaptures, swapBaselineContentCaptures) +
                "; resolve_since_swap=" + NonNegativeDelta(resolveCalls, swapBaselineResolve) +
                "; r014_full_reconcile_since_swap=" + NonNegativeDelta(fullReconcile, swapBaselineR014FullReconcile) +
                "; r014_publications_since_swap=" + NonNegativeDelta(publications, swapBaselineR014Publications) +
                "; blocked_rendered_false=" + NonNegativeDelta(renderedFalse, swapBaselineRenderedFalse) +
                "; blocked_foundation_flag=" + NonNegativeDelta(foundationFlag, swapBaselineFoundationFlag) +
                "; blocked_coverage=" + NonNegativeDelta(coverage, swapBaselineCoverage) +
                "; blocked_ready_far=" + NonNegativeDelta(readyFar, swapBaselineReadyFar) +
                "; cadence_skip=" + NonNegativeDelta(cadenceSkip, swapBaselineCadenceSkip) +
                "; revision_mismatch=" + (revisionMismatch ? "1" : "0") +
                "; requested_view_ready=" + (requestedReady ? "1" : "0") +
                "; publication_pending=" + (pubSerial != reconciledSerial ? "1" : "0"));
        }

        void EmitStallEnd(float now, long swaps, long backRender, long blocked, long skipped,
            long motion, long contentTicks, long contentCaptures, long resolveCalls, long pubSerial,
            long reconciledSerial, long fullReconcile, long publications, long renderedFalse,
            long foundationFlag, long coverage, long readyFar, long cadenceSkip, string reason)
        {
            EmitR025Attribution("END");
            float duration = Math.Max(0f, now - stallStartedRealtime);
            AERISLogger.Info(LogPrefix + " END reason=" + reason +
                "; duration_s=" + duration.ToString("F3", CultureInfo.InvariantCulture) +
                "; max_front_age_s=" + stallMaxFrontAge.ToString("F3", CultureInfo.InvariantCulture) +
                "; swaps=" + swaps +
                "; back_since_swap=" + NonNegativeDelta(backRender, swapBaselineBackRender) +
                "; blocked_since_swap=" + NonNegativeDelta(blocked, swapBaselineBlocked) +
                "; skipped_since_swap=" + NonNegativeDelta(skipped, swapBaselineSkipped) +
                "; motion_since_swap=" + NonNegativeDelta(motion, swapBaselineMotion) +
                "; content_tick_since_swap=" + NonNegativeDelta(contentTicks, swapBaselineContentTicks) +
                "; content_capture_since_swap=" + NonNegativeDelta(contentCaptures, swapBaselineContentCaptures) +
                "; resolve_since_swap=" + NonNegativeDelta(resolveCalls, swapBaselineResolve) +
                "; r014_full_reconcile_since_swap=" + NonNegativeDelta(fullReconcile, swapBaselineR014FullReconcile) +
                "; r014_publications_since_swap=" + NonNegativeDelta(publications, swapBaselineR014Publications) +
                "; blocked_rendered_false=" + NonNegativeDelta(renderedFalse, swapBaselineRenderedFalse) +
                "; blocked_foundation_flag=" + NonNegativeDelta(foundationFlag, swapBaselineFoundationFlag) +
                "; blocked_coverage=" + NonNegativeDelta(coverage, swapBaselineCoverage) +
                "; blocked_ready_far=" + NonNegativeDelta(readyFar, swapBaselineReadyFar) +
                "; cadence_skip=" + NonNegativeDelta(cadenceSkip, swapBaselineCadenceSkip) +
                "; publication_pending=" + (pubSerial != reconciledSerial ? "1" : "0"));
            stallActive = false;
        }

        void EmitR025Attribution(string phase)
        {
            try
            {
                AERISLogger.Info(R025LogPrefix + " " + phase + R025Snapshot());
            }
            catch (Exception ex)
            {
                // Observer failure must never influence ND authority.
                AERISLogger.Warn(R025LogPrefix +
                    " observer_error=" + ex.GetType().Name);
            }
        }

        string R025Snapshot()
        {
            int missingPending = ReadInt(R006MissingPendingField, renderer);
            int missingRenderReady =
                ReadInt(R006MissingRenderReadyField, renderer);
            int missingUpstream = ReadInt(R006MissingUpstreamField, renderer);

            int visibleRequired =
                ReadInt(R018VisibleRequiredFarField, renderer);
            int visibleReady =
                ReadInt(R018VisibleReadyFarField, renderer);
            float visibleCoverage =
                ReadFloat(R018VisibleCoverageField, renderer);
            int visibleDeficit =
                ReadInt(R019VisibleDeficitField, renderer);

            int contentReadyFar = ReadInt(ContentReadyFarField, renderer);
            int mainBacklog = ReadInt(MainCommitBacklogField, renderer);
            bool geometryReconcile =
                ReadBool(R008GeometryReconcilePendingField, renderer);

            long frontViewGeneration =
                ReadLong(FrontViewGenerationField, renderer);
            long frontTerrainGeneration =
                ReadLong(FrontTerrainGenerationField, renderer);
            long contentTerrainGeneration =
                ReadLong(ContentTerrainGenerationField, renderer);

            long contentViewGeneration = -1L;
            object contentVisibleObject =
                ContentVisibleField.GetValue(renderer);
            AERISTerrainVisibleTileSet visible =
                contentVisibleObject as AERISTerrainVisibleTileSet;
            if (visible != null)
                contentViewGeneration = visible.ViewGeneration;

            int rasterPending = -1;
            int rasterCompleted = -1;
            object rasterObject = RasterizerField.GetValue(renderer);
            AERISTerrainGpuTileRasterizer raster =
                rasterObject as AERISTerrainGpuTileRasterizer;
            if (raster != null)
            {
                rasterPending = raster.PendingCount;
                rasterCompleted = raster.CompletedCount;
            }

            object pendingObject = PendingEntryCommitField.GetValue(renderer);
            string pendingStage = "NONE";
            if (pendingObject != null)
            {
                FieldInfo stageField = pendingObject.GetType().GetField(
                    "Stage", PrivateInstance);
                if (stageField != null)
                {
                    object stage = stageField.GetValue(pendingObject);
                    if (stage != null)
                        pendingStage = stage.ToString();
                }
                else
                {
                    pendingStage = "UNKNOWN";
                }
            }

            return
                "; variant=" + R025Variant +
                "; missing_pending=" + missingPending +
                "; missing_render_ready=" + missingRenderReady +
                "; missing_upstream=" + missingUpstream +
                "; visible_required_far=" + visibleRequired +
                "; visible_ready_far=" + visibleReady +
                "; visible_coverage=" +
                    visibleCoverage.ToString("F3",
                        CultureInfo.InvariantCulture) +
                "; visible_deficit=" + visibleDeficit +
                "; content_ready_far=" + contentReadyFar +
                "; main_commit_backlog=" + mainBacklog +
                "; pending_commit_stage=" + pendingStage +
                "; raster_pending=" + rasterPending +
                "; raster_completed=" + rasterCompleted +
                "; geometry_reconcile_pending=" +
                    (geometryReconcile ? "1" : "0") +
                "; requested_view_ready=" +
                    (renderer.RequestedViewReady ? "1" : "0") +
                "; front_view_generation=" + frontViewGeneration +
                "; content_view_generation=" + contentViewGeneration +
                "; front_terrain_generation=" + frontTerrainGeneration +
                "; content_terrain_generation=" + contentTerrainGeneration +
                "; r024_visible_ready_checks=" +
                    ReadLong(R024VisibleReadyChecksField, renderer) +
                "; r024_hidden_preempt=" +
                    ReadLong(R024HiddenPendingPreemptionsField, renderer) +
                "; r024_finalize_protected=" +
                    ReadLong(R024FinalizeProtectedField, renderer);
        }

        void ResetSwapBaseline(long swaps, long backRender, long blocked, long skipped, long motion,
            long contentTicks, long contentCaptures, long resolveCalls, long fullReconcile,
            long publications, long renderedFalse, long foundationFlag, long coverage,
            long readyFar, long cadenceSkip)
        {
            lastSwapCount = swaps;
            swapBaselineBackRender = backRender;
            swapBaselineBlocked = blocked;
            swapBaselineSkipped = skipped;
            swapBaselineMotion = motion;
            swapBaselineContentTicks = contentTicks;
            swapBaselineContentCaptures = contentCaptures;
            swapBaselineResolve = resolveCalls;
            swapBaselineR014FullReconcile = fullReconcile;
            swapBaselineR014Publications = publications;
            swapBaselineRenderedFalse = renderedFalse;
            swapBaselineFoundationFlag = foundationFlag;
            swapBaselineCoverage = coverage;
            swapBaselineReadyFar = readyFar;
            swapBaselineCadenceSkip = cadenceSkip;
            stallActive = false;
            stallMaxFrontAge = 0f;
        }

        static long NonNegativeDelta(long current, long previous)
        {
            return current >= previous ? current - previous : 0L;
        }

        static bool BindingsAvailable()
        {
            return BootstrapFlightInstrumentField != null &&
                FlightInstrumentNavigationDisplayField != null &&
                NavigationDisplayRendererField != null &&
                FrontBufferValidField != null && FrontCommittedRealtimeField != null &&
                LastFrontBufferPresentedField != null && LastFrontBufferLatchedField != null &&
                FrontContentRevisionField != null && GpuContentRevisionField != null &&
                FrontBufferSwapsField != null && BackRenderFramesField != null &&
                BlockedIncompleteSwapsField != null && SkippedBackRenderFramesField != null &&
                MotionRefreshesField != null && ContentTicksField != null &&
                ContentCapturesField != null && ResolveCallsField != null &&
                R014PublicationSerialField != null && R014ReconciledSerialField != null &&
                R014FullReconcileField != null && R014PublicationEventsField != null &&
                R017BlockedRenderedFalseField != null && R017BlockedFoundationFlagField != null &&
                R017BlockedCoverageField != null && R017BlockedReadyFarField != null &&
                R017CadenceSkipField != null &&
                MainCommitBacklogField != null &&
                PendingEntryCommitField != null &&
                R006MissingPendingField != null &&
                R006MissingRenderReadyField != null &&
                R006MissingUpstreamField != null &&
                R018VisibleRequiredFarField != null &&
                R018VisibleReadyFarField != null &&
                R018VisibleCoverageField != null &&
                R019VisibleDeficitField != null &&
                ContentReadyFarField != null &&
                R008GeometryReconcilePendingField != null &&
                FrontViewGenerationField != null &&
                FrontTerrainGenerationField != null &&
                ContentTerrainGenerationField != null &&
                ContentVisibleField != null &&
                RasterizerField != null &&
                R024VisibleReadyChecksField != null &&
                R024HiddenPendingPreemptionsField != null &&
                R024FinalizeProtectedField != null;
        }

        static FieldInfo RendererField(string name)
        {
            return typeof(AERISTerrainGpuTileRenderer).GetField(name, PrivateInstance);
        }

        static long ReadLong(FieldInfo field, object target)
        {
            object value = field.GetValue(target);
            return value is long ? (long)value : 0L;
        }

        static int ReadInt(FieldInfo field, object target)
        {
            object value = field.GetValue(target);
            return value is int ? (int)value : 0;
        }

        static float ReadFloat(FieldInfo field, object target)
        {
            object value = field.GetValue(target);
            return value is float ? (float)value : 0f;
        }

        static bool ReadBool(FieldInfo field, object target)
        {
            object value = field.GetValue(target);
            return value is bool && (bool)value;
        }
    }
}
