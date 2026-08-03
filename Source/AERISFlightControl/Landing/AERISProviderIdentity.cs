using System;

namespace AERISFlightControl.Landing
{
    // Stable cache identity shared by snapshot capture, registry failure paths,
    // provider federation and cache migration.  A v0.18 physical-runway identity
    // supersedes provider-specific aliases only after conservative clustering.
    internal static class AERISProviderIdentity
    {
        internal static string StableRecordId(AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            if (!string.IsNullOrEmpty(record.PhysicalRunwayId))
                return ComposePhysicalRunwayId(record.Body, record.PhysicalRunwayId);
            return LegacyStableRecordId(record);
        }

        internal static string LegacyStableRecordId(AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            return ComposeStableRecordId(record.Body, record.ProviderUuid,
                record.ProviderSiteId, record.SourcePath, record.ModelName);
        }

        internal static string ComposePhysicalRunwayId(string body,
            string physicalRunwayId)
        {
            string normalizedBody = Normalize(body, false);
            string normalizedPhysical = Normalize(physicalRunwayId, false);
            if (string.IsNullOrEmpty(normalizedBody) &&
                string.IsNullOrEmpty(normalizedPhysical)) return string.Empty;
            return normalizedBody + "\nPHYSICAL_RUNWAY\n" + normalizedPhysical;
        }

        internal static string ComposeStableRecordId(string body,
            string providerUuid, string providerSiteId, string sourcePath,
            string modelName)
        {
            string normalizedBody = Normalize(body, false);
            string normalizedSite = Normalize(providerSiteId, false);
            string normalizedPath = Normalize(sourcePath, true);
            string normalizedModel = Normalize(modelName, false);
            bool hasStableProviderFields =
                !string.IsNullOrEmpty(normalizedSite) ||
                !string.IsNullOrEmpty(normalizedPath) ||
                !string.IsNullOrEmpty(normalizedModel);
            string uuidFallback = hasStableProviderFields ? string.Empty :
                Normalize(providerUuid, false);
            if (string.IsNullOrEmpty(normalizedBody) &&
                string.IsNullOrEmpty(uuidFallback) &&
                string.IsNullOrEmpty(normalizedSite) &&
                string.IsNullOrEmpty(normalizedPath) &&
                string.IsNullOrEmpty(normalizedModel)) return string.Empty;
            return normalizedBody + "\n" + uuidFallback + "\n" +
                normalizedSite + "\n" + normalizedPath + "\n" +
                normalizedModel;
        }

        static string Normalize(string value, bool path)
        {
            string result = (value ?? string.Empty).Trim();
            return path ? result.Replace('\\', '/') : result;
        }
    }
}
