// RETIRED IN CP3 GATE 4A.
// This file is deliberately excluded from AERISFlightControl.csproj. It preserves
// historical audit evidence for the removed CPU terrain-raster boundary; no runtime
// type in this file is compiled or used for ND presentation.
namespace AERISFlightControl.Terrain
{
    internal static class AERISTerrainRasterWorkerRetiredGuard
    {
        internal sealed class RetiredRequest
        {
            internal double AircraftAltitudeAslMeters;
        }

        internal static bool HistoricalInputGuards(long byteCountLong,
            RetiredRequest request, double neighbourElevation)
        {
            if (byteCountLong > 64L * 1024L * 1024L) return false;
            if (request == null || !Finite(request.AircraftAltitudeAslMeters)) return false;
            return Finite(neighbourElevation);
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
