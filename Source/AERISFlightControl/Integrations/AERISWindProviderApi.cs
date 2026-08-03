using System;
using System.Threading;

namespace AERISFlightControl.Integration
{
    // Provider-neutral primitive wind query. External adapters can bind without exposing
    // Vessel, CelestialBody or Unity objects across the integration boundary.
    public struct AERISWindQuery
    {
        public string BodyName;
        public double UniversalTime;
        public double LatitudeDeg;
        public double LongitudeDeg;
        public double AltitudeAslMeters;
    }

    public struct AERISWindSample
    {
        public bool Valid;
        public double EastMetersPerSecond;
        public double NorthMetersPerSecond;
        public double UpMetersPerSecond;
        public string Status;
    }

    public interface IAERISWindProvider
    {
        string ProviderName { get; }
        bool TryGetWind(AERISWindQuery query, out AERISWindSample sample);
    }

    public static class AERISWindProviderApi
    {
        static IAERISWindProvider provider;

        public static void Bind(IAERISWindProvider value)
        {
            Volatile.Write(ref provider, value);
        }

        public static void Unbind(IAERISWindProvider value)
        {
            Interlocked.CompareExchange(ref provider, null, value);
        }

        public static string ProviderName
        {
            get
            {
                IAERISWindProvider current = Volatile.Read(ref provider);
                if (current == null) return string.Empty;
                try { return current.ProviderName ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        internal static bool TrySample(AERISWindQuery query,
            out AERISWindSample sample, out string providerName)
        {
            sample = new AERISWindSample();
            providerName = string.Empty;
            IAERISWindProvider current = Volatile.Read(ref provider);
            if (current == null) return false;
            try
            {
                providerName = current.ProviderName ?? string.Empty;
                if (!current.TryGetWind(query, out sample) || !sample.Valid ||
                    !Finite(sample.EastMetersPerSecond) ||
                    !Finite(sample.NorthMetersPerSecond) ||
                    !Finite(sample.UpMetersPerSecond))
                {
                    sample = new AERISWindSample();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                sample = new AERISWindSample
                {
                    Valid = false,
                    Status = ex.GetType().Name
                };
                return false;
            }
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
