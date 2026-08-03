using System;
using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    // Registration-only convenience utility. Available only in Sandbox flight scenes.
    // The target is never synthesized from runway thresholds: the authoritative target
    // is the provider's own launch/spawn transform for the registered runway site.
    internal static class AERISSandboxNativeSpawnWarpUtility
    {
        internal static bool Available
        {
            get
            {
                return HighLogic.LoadedSceneIsFlight && HighLogic.CurrentGame != null &&
                    HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
            }
        }

        internal static bool ShouldShow(AERISAirfieldDefinition airfield)
        {
            return Available && airfield != null && airfield.ProviderDetected &&
                (airfield.Source == AERISAirfieldSource.KerbalKonstructs ||
                 airfield.Source == AERISAirfieldSource.StockLaunchsitesExpansion);
        }

        internal static bool CanWarp(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, out string reason)
        {
            reason = string.Empty;
            if (!Available)
            {
                reason = "SANDBOX ONLY";
                return false;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.mainBody == null)
            {
                reason = "NO ACTIVE VESSEL";
                return false;
            }
            if (airfield == null || runway == null)
            {
                reason = "AIRFIELD/RUNWAY MISSING";
                return false;
            }
            if (airfield.Source != AERISAirfieldSource.KerbalKonstructs &&
                airfield.Source != AERISAirfieldSource.StockLaunchsitesExpansion)
            {
                reason = "NO MOD-NATIVE SPAWN PROVIDER";
                return false;
            }
            if (!airfield.ProviderDetected)
            {
                reason = "PROVIDER NOT PRESENT";
                return false;
            }
            if (!string.Equals(airfield.Body, vessel.mainBody.bodyName,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = "RUNWAY IS ON " + (airfield.Body ?? "ANOTHER BODY");
                return false;
            }
            if (vessel.vesselTransform == null || vessel.ReferenceTransform == null)
            {
                reason = "VESSEL TRANSFORM NOT READY";
                return false;
            }
            if (string.IsNullOrEmpty(runway.ProviderSiteId) &&
                string.IsNullOrEmpty(runway.ProviderUuid) &&
                string.IsNullOrEmpty(airfield.ProviderStableRecordId))
            {
                reason = "RUNWAY PROVIDER IDENTITY MISSING";
                return false;
            }
            return true;
        }

        const float WarpCooldownSeconds = 12f;
        const double EaseGravityMultiplier = 0.05;
        static float nextWarpAllowedRealtime;

        internal static bool TryWarp(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, out string message)
        {
            string reason;
            if (!CanWarp(airfield, runway, out reason))
            {
                message = "WARP REFUSED — " + reason;
                return false;
            }
            if (Time.realtimeSinceStartup < nextWarpAllowedRealtime)
            {
                message = "WARP REFUSED — STOCK PHYSICS EASING ACTIVE";
                return false;
            }

            AERISProviderFacilityRecord record;
            Vector3 spawnPosition;
            Vector3 spawnForward;
            string providerStatus;
            if (!TryResolveNativeSpawn(airfield, runway, out record,
                out spawnPosition, out spawnForward, out providerStatus))
            {
                message = "WARP REFUSED — MOD NATIVE SPAWN NOT AVAILABLE";
                AERISLogger.Warn("[AIRFIELDS/NATIVE_SPAWN_WARP] " + message +
                    "; airfield=" + (airfield.DisplayName ?? string.Empty) +
                    "; runway=" + (runway.DisplayName ?? string.Empty) +
                    "; provider=" + providerStatus);
                return false;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel.mainBody;
            if (record.RuntimeBody != null && record.RuntimeBody != body)
            {
                message = "WARP REFUSED — NATIVE SPAWN BODY MISMATCH";
                return false;
            }
            if (FlightGlobals.fetch == null)
            {
                message = "WARP REFUSED — FLIGHT GLOBALS NOT READY";
                return false;
            }

            double latitude = body.GetLatitude((Vector3d)spawnPosition);
            double longitude = body.GetLongitude((Vector3d)spawnPosition);
            double nativeAltitudeAsl = body.GetAltitude((Vector3d)spawnPosition);
            double terrainAltitudeAsl;
            if (!AERISOperationalRunwayResolver.TryTerrainSample(body, latitude, longitude,
                out terrainAltitudeAsl))
            {
                message = "WARP REFUSED — TERRAIN ALTITUDE UNAVAILABLE";
                return false;
            }
            double spawnAltitudeAgl = nativeAltitudeAsl - terrainAltitudeAsl;
            if (double.IsNaN(spawnAltitudeAgl) || double.IsInfinity(spawnAltitudeAgl) ||
                spawnAltitudeAgl < -100.0 || spawnAltitudeAgl > 500.0)
            {
                message = "WARP REFUSED — MOD NATIVE SPAWN ALTITUDE INVALID";
                return false;
            }
            // FlightGlobals.SetVesselPosition expects a surface-relative altitude when
            // easeToSurface is enabled. The provider transform is world/ASL authority,
            // so convert its ASL to AGL instead of accidentally adding body terrain
            // elevation a second time. Keep a tiny clearance for easing/gear settling.
            spawnAltitudeAgl = Math.Max(2.0, spawnAltitudeAgl);
            double inclinationDeg;
            double headingDeg;
            if (!TryResolveNativeAttitude(body, spawnPosition, spawnForward,
                out inclinationDeg, out headingDeg))
            {
                message = "WARP REFUSED — NATIVE SPAWN ORIENTATION INVALID";
                return false;
            }

            int bodyIndex = FlightGlobals.Bodies.IndexOf(body);
            if (bodyIndex < 0)
            {
                message = "WARP REFUSED — BODY INDEX NOT AVAILABLE";
                return false;
            }

            // Safety Hotfix 2: direct relocation of an unpacked multi-part vessel can
            // leave it above the provider anchor with ordinary gravity and produce a
            // destructive impact on the next physics frames. Delegate the relocation to
            // KSP's own Set Position transport and enable its physics easing. The MOD's
            // live LaunchPadTransform remains the horizontal/heading/ASL authority. Its
            // ASL is converted to surface-relative AGL before KSP performs the move and
            // gentle surface settling; this prevents terrain elevation from being added twice.
            if (TimeWarp.CurrentRateIndex != 0) TimeWarp.SetRate(0, true);
            nextWarpAllowedRealtime = Time.realtimeSinceStartup + WarpCooldownSeconds;
            try
            {
                FlightGlobals.fetch.SetVesselPosition(bodyIndex, latitude, longitude,
                    spawnAltitudeAgl, inclinationDeg, headingDeg, true, true,
                    EaseGravityMultiplier);
            }
            catch (Exception ex)
            {
                nextWarpAllowedRealtime = 0f;
                message = "WARP FAILED — STOCK SET POSITION ERROR";
                AERISLogger.Error("[AIRFIELDS/NATIVE_SPAWN_WARP] " + message +
                    "; " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }

            message = "WARPING TO MOD NATIVE SPAWN — STOCK PHYSICS EASING";
            AERISLogger.Info("[AIRFIELDS/NATIVE_SPAWN_WARP] " + message +
                "; source=" + record.Source +
                "; site=" + (record.ProviderSiteId ?? string.Empty) +
                "; launchTransform=" + (record.LaunchPadTransform ?? string.Empty) +
                "; lat=" + latitude.ToString("0.000000") +
                "; lon=" + longitude.ToString("0.000000") +
                "; native_alt_asl=" + nativeAltitudeAsl.ToString("0.0") +
                "; terrain_alt_asl=" + terrainAltitudeAsl.ToString("0.0") +
                "; target_agl=" + spawnAltitudeAgl.ToString("0.0") +
                "; inclination=" + inclinationDeg.ToString("0.00") +
                "; heading=" + headingDeg.ToString("0.00") +
                "; ease_gravity=" + EaseGravityMultiplier.ToString("0.00"));
            return true;
        }

        static bool TryResolveNativeSpawn(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, out AERISProviderFacilityRecord selected,
            out Vector3 position, out Vector3 forward, out string providerStatus)
        {
            selected = null;
            position = Vector3.zero;
            forward = Vector3.forward;
            providerStatus = "NOT SCANNED";

            var records = new List<AERISProviderFacilityRecord>();
            string kkStatus;
            string kspStatus;
            AERISKerbalKonstructsProvider.Collect(records, out kkStatus);
            AERISKspFacilityProvider.Collect(records, out kspStatus);
            providerStatus = "KK=" + kkStatus + " / KSP=" + kspStatus;

            AERISPhysicalRunwayMergeSummary summary;
            records = AERISPhysicalRunwayIdentity.Canonicalize(records, out summary);

            int bestScore = int.MinValue;
            for (int i = 0; i < records.Count; i++)
            {
                AERISProviderFacilityRecord record = records[i];
                if (record == null ||
                    (record.Source != AERISAirfieldSource.KerbalKonstructs &&
                     record.Source != AERISAirfieldSource.StockLaunchsitesExpansion))
                    continue;
                if (!string.Equals(record.Body, airfield.Body,
                    StringComparison.OrdinalIgnoreCase)) continue;

                int score = MatchScore(airfield, runway, record);
                if (score <= 0 || score < bestScore) continue;

                Vector3 candidatePosition;
                Vector3 candidateForward;
                if (!TryGetCurrentLaunchFrame(record, out candidatePosition,
                    out candidateForward)) continue;

                selected = record;
                position = candidatePosition;
                forward = candidateForward;
                bestScore = score;
            }
            return selected != null;
        }

        static int MatchScore(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, AERISProviderFacilityRecord record)
        {
            int score = 0;
            string stable = AERISProviderIdentity.StableRecordId(record);
            if (!string.IsNullOrEmpty(airfield.ProviderStableRecordId) &&
                string.Equals(airfield.ProviderStableRecordId, stable,
                    StringComparison.OrdinalIgnoreCase)) score += 100;
            if (!string.IsNullOrEmpty(runway.ProviderUuid) &&
                !string.IsNullOrEmpty(record.ProviderUuid) &&
                string.Equals(runway.ProviderUuid, record.ProviderUuid,
                    StringComparison.OrdinalIgnoreCase)) score += 80;
            if (!string.IsNullOrEmpty(runway.ProviderSiteId) &&
                !string.IsNullOrEmpty(record.ProviderSiteId) &&
                string.Equals(runway.ProviderSiteId, record.ProviderSiteId,
                    StringComparison.OrdinalIgnoreCase)) score += 70;
            if (!string.IsNullOrEmpty(airfield.ProviderUuid) &&
                !string.IsNullOrEmpty(record.ProviderUuid) &&
                string.Equals(airfield.ProviderUuid, record.ProviderUuid,
                    StringComparison.OrdinalIgnoreCase)) score += 40;
            if (!string.IsNullOrEmpty(airfield.ProviderSiteId) &&
                !string.IsNullOrEmpty(record.ProviderSiteId) &&
                string.Equals(airfield.ProviderSiteId, record.ProviderSiteId,
                    StringComparison.OrdinalIgnoreCase)) score += 30;
            if (!string.IsNullOrEmpty(airfield.ProviderGroup) &&
                !string.IsNullOrEmpty(record.ProviderGroup) &&
                string.Equals(airfield.ProviderGroup, record.ProviderGroup,
                    StringComparison.OrdinalIgnoreCase)) score += 10;
            return score;
        }

        static bool TryGetCurrentLaunchFrame(AERISProviderFacilityRecord record,
            out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.forward;
            if (record == null) return false;

            // Prefer the live provider transform. This tracks KSP FloatingOrigin and is
            // the closest representation of the location the mod itself uses to spawn.
            Transform live = record.RuntimeLaunchTransform;
            if (live != null)
            {
                position = live.position;
                forward = live.forward.normalized;
                return Finite(position) && Finite(forward) &&
                    forward.sqrMagnitude > 0.5f;
            }

            // KK may expose only the instance transform plus the prefab launch transform.
            // Reconstruct the current world frame exactly as the provider does; never
            // fall back to runway thresholds or AERIS-calculated runway geometry.
            Transform instance = record.RuntimeInstanceTransform;
            GameObject prefab = record.RuntimeRunwayPrefab;
            Transform prefabLaunch = record.RuntimePrefabLaunchTransform;
            if (instance == null || prefab == null || prefabLaunch == null) return false;
            try
            {
                Transform prefabRoot = prefab.transform;
                Vector3 localPosition = prefabRoot.InverseTransformPoint(
                    prefabLaunch.position) * record.RuntimeModelScale;
                Vector3 localForward = prefabRoot.InverseTransformDirection(
                    prefabLaunch.forward).normalized;
                position = instance.TransformPoint(localPosition);
                forward = instance.TransformDirection(localForward).normalized;
                return Finite(position) && Finite(forward) &&
                    forward.sqrMagnitude > 0.5f;
            }
            catch
            {
                return false;
            }
        }

        static bool TryResolveNativeAttitude(CelestialBody body,
            Vector3 spawnPosition, Vector3 spawnForward, out double inclinationDeg,
            out double headingDeg)
        {
            inclinationDeg = 0.0;
            headingDeg = 0.0;
            if (body == null || !Finite(spawnPosition) || !Finite(spawnForward) ||
                spawnForward.sqrMagnitude < 0.5f) return false;

            double latitude = body.GetLatitude((Vector3d)spawnPosition);
            double longitude = body.GetLongitude((Vector3d)spawnPosition);
            double altitude = body.GetAltitude((Vector3d)spawnPosition);
            if (double.IsNaN(latitude) || double.IsInfinity(latitude) ||
                double.IsNaN(longitude) || double.IsInfinity(longitude) ||
                double.IsNaN(altitude) || double.IsInfinity(altitude)) return false;

            Vector3 up = ((Vector3)body.GetSurfaceNVector(latitude, longitude)).normalized;
            if (!Finite(up) || up.sqrMagnitude < 0.5f) return false;
            Vector3 forward = spawnForward.normalized;
            Vector3 tangentForward = Vector3.ProjectOnPlane(forward, up).normalized;
            if (!Finite(tangentForward) || tangentForward.sqrMagnitude < 0.5f) return false;

            const double delta = 0.0001;
            Vector3 northPoint = (Vector3)body.GetWorldSurfacePosition(
                Math.Min(89.9999, latitude + delta), longitude, altitude);
            Vector3 eastPoint = (Vector3)body.GetWorldSurfacePosition(latitude,
                AERISAirfieldConfigParser.NormalizeLongitude(longitude + delta), altitude);
            Vector3 north = Vector3.ProjectOnPlane(northPoint - spawnPosition, up).normalized;
            Vector3 east = Vector3.ProjectOnPlane(eastPoint - spawnPosition, up).normalized;
            if (!Finite(north) || !Finite(east) || north.sqrMagnitude < 0.5f ||
                east.sqrMagnitude < 0.5f) return false;
            east = Vector3.Cross(north, up).normalized;
            north = Vector3.Cross(up, east).normalized;

            headingDeg = Math.Atan2(Vector3.Dot(tangentForward, east),
                Vector3.Dot(tangentForward, north)) * 180.0 / Math.PI;
            if (headingDeg < 0.0) headingDeg += 360.0;
            double vertical = Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(forward, up)));
            inclinationDeg = Math.Asin(vertical) * 180.0 / Math.PI;
            return !(double.IsNaN(headingDeg) || double.IsInfinity(headingDeg) ||
                double.IsNaN(inclinationDeg) || double.IsInfinity(inclinationDeg));
        }

        static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
