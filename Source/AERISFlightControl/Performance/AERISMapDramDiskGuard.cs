using System;

namespace AERISFlightControl.Performance
{
    // Runtime contract monitor for CP2.5 normal map lookups. Producers may perform
    // bounded startup/maintenance I/O, but any guarded synchronous filesystem call
    // reached while this scope is active is a forbidden normal-lookup fallback.
    internal static class AERISMapDramDiskGuard
    {
        [ThreadStatic] static int normalLookupDepth;
        [ThreadStatic] static AERISMapDramCache activeCache;
        [ThreadStatic] static string activeDomain;

        sealed class Scope : IDisposable
        {
            readonly int previousDepth;
            readonly AERISMapDramCache previousCache;
            readonly string previousDomain;
            bool disposed;

            internal Scope(AERISMapDramCache cache, string domain)
            {
                previousDepth = normalLookupDepth;
                previousCache = activeCache;
                previousDomain = activeDomain;
                normalLookupDepth = previousDepth + 1;
                activeCache = cache;
                activeDomain = domain ?? string.Empty;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                normalLookupDepth = previousDepth;
                activeCache = previousCache;
                activeDomain = previousDomain;
            }
        }

        internal static IDisposable EnterNormalLookup(AERISMapDramCache cache,
            string domain)
        {
            return new Scope(cache, domain);
        }

        internal static void BeforeSynchronousDisk(AERISMapDramCache cache,
            string operation)
        {
            if (cache == null) return;
            bool violation = normalLookupDepth > 0 &&
                object.ReferenceEquals(cache, activeCache);
            cache.RecordSynchronousDiskOperation(violation,
                violation ? activeDomain : "MAINTENANCE", operation);
        }
    }
}
