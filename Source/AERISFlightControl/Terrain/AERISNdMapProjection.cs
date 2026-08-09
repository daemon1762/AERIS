using System;
using UnityEngine;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // One immutable ND map transform shared by GPU terrain and GUI symbology.
    // Horizontal ND scale is intentionally 1.30 times vertical scale; therefore a
    // plain normalized-space rotation is geometrically wrong. This snapshot applies
    // heading rotation in metres before the unequal axis scales, or emits the exact
    // scale-corrected matrix for cached N-UP terrain meshes.
    internal struct AERISNdMapProjection
    {
        internal double RadiusMeters;
        internal double CenterX;
        internal double CenterY;
        internal double CenterZ;
        internal double EastX;
        internal double EastY;
        internal double NorthX;
        internal double NorthY;
        internal double NorthZ;
        internal double HorizontalMeters;
        internal double VerticalMeters;
        internal float AnchorGuiV;
        internal float AnchorRenderV;
        internal float HeadingCos;
        internal float HeadingSin;
        internal bool TrackUp;
        internal AERISTerrainRenderTargetOrientation Orientation;

        internal static AERISNdMapProjection Create(CelestialBody body,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float headingDeg, bool trackUp, float anchorGuiV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            double latitudeRad = centerLatitudeDeg * Math.PI / 180.0;
            double longitudeRad = centerLongitudeDeg * Math.PI / 180.0;
            double cosineLatitude = Math.Cos(latitudeRad);
            float radians = trackUp ? headingDeg * Mathf.Deg2Rad : 0f;
            return new AERISNdMapProjection
            {
                RadiusMeters = Math.Max(1000.0, body == null ? 600000.0 : body.Radius),
                CenterX = cosineLatitude * Math.Cos(longitudeRad),
                CenterY = cosineLatitude * Math.Sin(longitudeRad),
                CenterZ = Math.Sin(latitudeRad),
                EastX = -Math.Sin(longitudeRad),
                EastY = Math.Cos(longitudeRad),
                NorthX = -Math.Sin(latitudeRad) * Math.Cos(longitudeRad),
                NorthY = -Math.Sin(latitudeRad) * Math.Sin(longitudeRad),
                NorthZ = Math.Cos(latitudeRad),
                HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30),
                VerticalMeters = Math.Max(1.0, rangeMeters),
                AnchorGuiV = Mathf.Clamp01(anchorGuiV),
                AnchorRenderV = orientation == AERISTerrainRenderTargetOrientation.Flipped ?
                    Mathf.Clamp01(anchorGuiV) : 1f - Mathf.Clamp01(anchorGuiV),
                HeadingCos = Mathf.Cos(radians),
                HeadingSin = Mathf.Sin(radians),
                TrackUp = trackUp,
                Orientation = orientation
            };
        }

        internal void ProjectLatitudeLongitudeToGui(double latitudeDeg,
            double longitudeDeg, out float u, out float v)
        {
            double latitudeRad = latitudeDeg * Math.PI / 180.0;
            double longitudeRad = longitudeDeg * Math.PI / 180.0;
            double cosineLatitude = Math.Cos(latitudeRad);
            ProjectUnitToLocalMeters(cosineLatitude * Math.Cos(longitudeRad),
                cosineLatitude * Math.Sin(longitudeRad), Math.Sin(latitudeRad),
                out double eastMeters, out double northMeters);
            ProjectLocalMetersToGui(eastMeters, northMeters, out u, out v);
        }

        internal void ProjectLocalMetersToGui(double eastMeters, double northMeters,
            out float u, out float v)
        {
            double right = eastMeters;
            double forward = northMeters;
            if (TrackUp)
            {
                right = eastMeters * HeadingCos - northMeters * HeadingSin;
                forward = eastMeters * HeadingSin + northMeters * HeadingCos;
            }
            u = (float)(0.5 + right / HorizontalMeters);
            v = (float)(AnchorGuiV - forward / VerticalMeters);
        }

        internal void UnprojectGuiToLatitudeLongitude(float u, float v,
            out double latitudeDeg, out double longitudeDeg)
        {
            double rightMeters = (u - 0.5) * HorizontalMeters;
            double forwardMeters = (AnchorGuiV - v) * VerticalMeters;
            double eastMeters = rightMeters;
            double northMeters = forwardMeters;
            if (TrackUp)
            {
                eastMeters = rightMeters * HeadingCos +
                    forwardMeters * HeadingSin;
                northMeters = -rightMeters * HeadingSin +
                    forwardMeters * HeadingCos;
            }

            double centerLatitudeRad = Math.Asin(Math.Max(-1.0,
                Math.Min(1.0, CenterZ)));
            double centerLongitudeRad = Math.Atan2(CenterY, CenterX);
            double distanceMeters = Math.Sqrt(eastMeters * eastMeters +
                northMeters * northMeters);
            if (distanceMeters <= 0.000001)
            {
                latitudeDeg = centerLatitudeRad * 180.0 / Math.PI;
                longitudeDeg = NormalizeLongitudeDegrees(
                    centerLongitudeRad * 180.0 / Math.PI);
                return;
            }

            double bearing = Math.Atan2(eastMeters, northMeters);
            double angularDistance = distanceMeters / Math.Max(1000.0, RadiusMeters);
            double sinLatitude = Math.Sin(centerLatitudeRad) *
                Math.Cos(angularDistance) + Math.Cos(centerLatitudeRad) *
                Math.Sin(angularDistance) * Math.Cos(bearing);
            double latitudeRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0,
                sinLatitude)));
            double longitudeRad = centerLongitudeRad + Math.Atan2(
                Math.Sin(bearing) * Math.Sin(angularDistance) *
                    Math.Cos(centerLatitudeRad),
                Math.Cos(angularDistance) - Math.Sin(centerLatitudeRad) *
                    Math.Sin(latitudeRad));
            latitudeDeg = latitudeRad * 180.0 / Math.PI;
            longitudeDeg = NormalizeLongitudeDegrees(longitudeRad * 180.0 / Math.PI);
        }

        static double NormalizeLongitudeDegrees(double value)
        {
            value %= 360.0;
            if (value < -180.0) value += 360.0;
            if (value >= 180.0) value -= 360.0;
            return value;
        }

        internal void ProjectUnitToRenderNUp(double x, double y, double z,
            out float u, out float renderV)
        {
            ProjectUnitToLocalMeters(x, y, z, out double eastMeters,
                out double northMeters);
            u = (float)(0.5 + eastMeters / HorizontalMeters);
            float northNormalized = (float)(northMeters / VerticalMeters);
            renderV = Orientation == AERISTerrainRenderTargetOrientation.Flipped ?
                AnchorRenderV - northNormalized : AnchorRenderV + northNormalized;
        }

        internal Matrix4x4 ResolveScaleCorrectedRenderMatrix()
        {
            if (!TrackUp || Mathf.Abs(HeadingSin) <= 0.0000001f)
                return Matrix4x4.identity;
            float horizontalOverVertical = (float)(HorizontalMeters / VerticalMeters);
            float verticalOverHorizontal = 1f / Math.Max(0.000001f,
                horizontalOverVertical);
            var local = Matrix4x4.identity;
            if (Orientation == AERISTerrainRenderTargetOrientation.Flipped)
            {
                local.m00 = HeadingCos;
                local.m01 = HeadingSin * verticalOverHorizontal;
                local.m10 = -HeadingSin * horizontalOverVertical;
                local.m11 = HeadingCos;
            }
            else
            {
                local.m00 = HeadingCos;
                local.m01 = -HeadingSin * verticalOverHorizontal;
                local.m10 = HeadingSin * horizontalOverVertical;
                local.m11 = HeadingCos;
            }
            Vector3 pivot = new Vector3(0.5f, AnchorRenderV, 0f);
            return Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) *
                local * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        }

        internal void PresentRenderToGui(float renderU, float renderV,
            out float guiU, out float guiV)
        {
            guiU = renderU;
            guiV = Orientation == AERISTerrainRenderTargetOrientation.Flipped ?
                renderV : 1f - renderV;
        }

        void ProjectUnitToLocalMeters(double x, double y, double z,
            out double eastMeters, out double northMeters)
        {
            double eastUnit = x * EastX + y * EastY;
            double northUnit = x * NorthX + y * NorthY + z * NorthZ;
            double radialSquared = Math.Max(0.0,
                eastUnit * eastUnit + northUnit * northUnit);
            double factor;
            if (radialSquared <= 0.18)
            {
                factor = 1.0 + radialSquared * (1.0 / 6.0 +
                    radialSquared * (3.0 / 40.0 + radialSquared * (5.0 / 112.0 +
                    radialSquared * (35.0 / 1152.0 + radialSquared *
                    63.0 / 2816.0))));
            }
            else
            {
                double radial = Math.Sqrt(radialSquared);
                double centerDot = x * CenterX + y * CenterY + z * CenterZ;
                factor = radial <= 1e-12 ? 1.0 :
                    Math.Atan2(radial, centerDot) / radial;
            }
            eastMeters = eastUnit * RadiusMeters * factor;
            northMeters = northUnit * RadiusMeters * factor;
        }
    }

    internal sealed class AERISNdMapLockReference
    {
        internal string StableId = string.Empty;
        internal double LatitudeADeg;
        internal double LongitudeADeg;
        internal double LatitudeBDeg;
        internal double LongitudeBDeg;
    }
}
