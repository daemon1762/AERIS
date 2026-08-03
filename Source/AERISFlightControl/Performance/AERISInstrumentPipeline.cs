using System;
using System.Collections.Generic;
using System.Threading;

namespace AERISFlightControl.Performance
{
    // CPU-prepared, renderer-neutral geometry consumed by AERIS/AIFD clients. Alerts,
    // ownership and LAND phase remain discrete canonical fields and are never interpolated.
    public sealed class AERISPreparedDisplayFrame
    {
        public AERISRuntimeGenerationStamp Generation { get; private set; }
        public double BankSine { get; private set; }
        public double BankCosine { get; private set; }
        public double HeadingDeg { get; private set; }
        public double LocalizerDeviationMeters { get; private set; }
        public double GlidePathDeviationMeters { get; private set; }
        public IReadOnlyList<double> PitchLadderDegrees { get; private set; }
        public IReadOnlyList<double> PitchLadderOffsetsDegrees { get; private set; }
        public IReadOnlyList<double> HeadingTicksDegrees { get; private set; }
        public string LateralMode { get; private set; }
        public string VerticalMode { get; private set; }
        public string SpeedMode { get; private set; }
        public string ProtectState { get; private set; }
        public string LandState { get; private set; }

        internal AERISPreparedDisplayFrame(AERISCanonicalSnapshot source,
            double bankSine, double bankCosine, double[] pitchDegrees,
            double[] pitchOffsets, double[] headingTicks)
        {
            Generation = source.Generation;
            BankSine = bankSine;
            BankCosine = bankCosine;
            HeadingDeg = source.HeadingDeg;
            LocalizerDeviationMeters = source.LocalizerDeviationMeters;
            GlidePathDeviationMeters = source.GlidePathDeviationMeters;
            PitchLadderDegrees = Array.AsReadOnly(pitchDegrees);
            PitchLadderOffsetsDegrees = Array.AsReadOnly(pitchOffsets);
            HeadingTicksDegrees = Array.AsReadOnly(headingTicks);
            LateralMode = source.LateralMode;
            VerticalMode = source.VerticalMode;
            SpeedMode = source.SpeedMode;
            ProtectState = source.ProtectState;
            LandState = source.LandState;
        }
    }

    public static class AERISPreparedDisplayFrameApi
    {
        static AERISPreparedDisplayFrame latest;
        internal static void Publish(AERISPreparedDisplayFrame value)
        {
            Volatile.Write(ref latest, value);
        }
        internal static void Clear() { Volatile.Write(ref latest, null); }
        public static bool TryGetLatest(out AERISPreparedDisplayFrame value)
        {
            value = Volatile.Read(ref latest);
            return value != null;
        }
    }

    internal sealed class AERISInstrumentPipeline : IDisposable
    {
        const string JobKey = "instrument-display-preprocess";
        readonly AERISWorkerScheduler scheduler;
        bool disposed;

        internal AERISInstrumentPipeline(AERISWorkerScheduler value)
        {
            scheduler = value;
        }

        internal void Submit(AERISCanonicalSnapshot snapshot)
        {
            if (disposed || scheduler == null || snapshot == null) return;
            AERISCanonicalSnapshot immutable = snapshot;
            // Projection geometry is normal-priority display work. It must never consume
            // the reserved Safety/LAND slot used by future final-approach helpers.
            scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute, JobKey,
                snapshot.Generation, context => Prepare(immutable, context), value =>
                {
                    AERISPreparedDisplayFrame frame = value as AERISPreparedDisplayFrame;
                    if (frame != null) AERISPreparedDisplayFrameApi.Publish(frame);
                });
        }

        static object Prepare(AERISCanonicalSnapshot source,
            AERISRuntimeJobContext context)
        {
            context.ThrowIfStale();
            double bankRadians = source.BankDeg * Math.PI / 180.0;
            const int pitchCount = 13;
            var pitchDegrees = new double[pitchCount];
            var pitchOffsets = new double[pitchCount];
            for (int i = 0; i < pitchCount; i++)
            {
                double value = -30.0 + i * 5.0;
                pitchDegrees[i] = value;
                pitchOffsets[i] = value - source.PitchDeg;
            }
            var headingTicks = new double[15];
            for (int i = 0; i < headingTicks.Length; i++)
            {
                double tick = source.HeadingDeg + (i - 7) * 5.0;
                headingTicks[i] = tick - Math.Floor(tick / 360.0) * 360.0;
            }
            context.ThrowIfStale();
            return new AERISPreparedDisplayFrame(source, Math.Sin(bankRadians),
                Math.Cos(bankRadians), pitchDegrees, pitchOffsets, headingTicks);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (scheduler != null)
                scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, JobKey);
            AERISPreparedDisplayFrameApi.Clear();
        }
    }
}
