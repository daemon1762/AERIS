Shader "AERIS/ND/ExactVertexProjection"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _AerisTerrainSemanticMode ("AERIS terrain semantic mode", Float) = 0
        [HideInInspector] _AerisTerrainDisplayMode ("AERIS terrain display mode", Float) = 0
        [HideInInspector] _AerisTerrainPreset ("AERIS terrain colour preset", Float) = 0
        [HideInInspector] _AerisAircraftAltitudeMeters ("AERIS aircraft altitude", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float3 geographicUnit : TEXCOORD1;
                // x=elevation metres, y=raw shade byte 0..255, z=1 land / 0 water.
                float3 terrainSemantic : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            float4 _Color;
            float4 _AerisCenter;
            float4 _AerisEast;
            float4 _AerisNorth;
            float _AerisRadiusMeters;
            float _AerisHorizontalMeters;
            float _AerisVerticalMeters;
            float _AerisAnchorRenderV;
            float _AerisOrientationSign;
            float _AerisTerrainSemanticMode;
            float _AerisTerrainDisplayMode;
            float _AerisTerrainPreset;
            float _AerisAircraftAltitudeMeters;

            float4 AerisByteColour(float r, float g, float b)
            {
                return float4(r, g, b, 255.0) / 255.0;
            }

            float4 AerisLerpByte(float4 a, float4 b, float t)
            {
                float4 raw = lerp(a, b, saturate(t));
                return floor(raw * 255.0 + 0.5) / 255.0;
            }

            float4 AerisGradient(float t, float4 a, float4 b, float4 c,
                float4 d, float4 e)
            {
                if (t <= 0.25) return AerisLerpByte(a, b, t * 4.0);
                if (t <= 0.50) return AerisLerpByte(b, c, (t - 0.25) * 4.0);
                if (t <= 0.75) return AerisLerpByte(c, d, (t - 0.50) * 4.0);
                return AerisLerpByte(d, e, (t - 0.75) * 4.0);
            }

            float4 AerisRelativeColour(float clearance, int preset)
            {
                if (clearance <= 30.0)
                {
                    if (preset == 1) return AerisByteColour(190, 45, 210);
                    return AerisByteColour(224, 31, 20);
                }
                if (clearance <= 300.0)
                {
                    if (preset == 2) return AerisByteColour(242, 235, 225);
                    return AerisByteColour(235, 184, 20);
                }
                if (clearance <= 600.0)
                {
                    if (preset == 1) return AerisByteColour(35, 105, 210);
                    if (preset == 3) return AerisByteColour(70, 235, 70);
                    return AerisByteColour(51, 122, 41);
                }
                if (preset == 1) return AerisByteColour(15, 35, 75);
                if (preset == 3) return AerisByteColour(12, 72, 24);
                return AerisByteColour(26, 61, 31);
            }

            float4 AerisTopographicColour(float elevation, int preset)
            {
                float t = saturate((elevation + 500.0) / 12500.0);
                if (preset == 1)
                    return AerisGradient(t, AerisByteColour(25,55,105),
                        AerisByteColour(45,110,175), AerisByteColour(225,175,70),
                        AerisByteColour(150,105,85), AerisByteColour(245,245,245));
                if (preset == 2)
                    return AerisGradient(t, AerisByteColour(25,70,48),
                        AerisByteColour(70,135,75), AerisByteColour(160,150,80),
                        AerisByteColour(125,90,75), AerisByteColour(245,245,245));
                if (preset == 3)
                    return AerisGradient(t, AerisByteColour(5,35,15),
                        AerisByteColour(40,150,40), AerisByteColour(255,220,40),
                        AerisByteColour(160,70,30), AerisByteColour(255,255,255));
                return AerisGradient(t, AerisByteColour(18,65,35),
                    AerisByteColour(55,125,55), AerisByteColour(150,145,70),
                    AerisByteColour(120,85,65), AerisByteColour(235,235,235));
            }

            float4 AerisApplyShade(float4 colour, float shadeByte, bool relativeMode)
            {
                float raw = clamp(shadeByte / 227.0, 0.82, 1.04);
                float blend = relativeMode ? 0.30 : 0.55;
                float factor = lerp(1.0, raw, blend);
                factor = relativeMode ? clamp(factor, 0.94, 1.02) :
                    clamp(factor, 0.88, 1.035);
                float3 bytes = floor(colour.rgb * 255.0 * factor + 0.5);
                return float4(clamp(bytes, 0.0, 255.0) / 255.0, colour.a);
            }

            float4 AerisTerrainColour(float3 semantic)
            {
                int preset = (int)floor(_AerisTerrainPreset + 0.5);
                if (semantic.z < 0.5)
                {
                    if (preset == 1) return AerisByteColour(0, 20, 70);
                    return AerisByteColour(8, 52, 118);
                }
                // AERIS25_DYNAMIC_COLOUR_MODE_SPLIT: REL and TOPO are uniform-mode
                // exclusive.  Preserve the exact existing equations while preventing
                // the unused colour path from becoming per-vertex work.
                if (_AerisTerrainDisplayMode > 0.5)
                    return AerisApplyShade(
                        AerisRelativeColour(_AerisAircraftAltitudeMeters - semantic.x, preset),
                        semantic.y, true);
                return AerisApplyShade(AerisTopographicColour(semantic.x, preset),
                    semantic.y, false);
            }

            float AerisAngularScale(float3 geographicUnit,
                float eastUnit, float northUnit)
            {
                float radialSquared = max(0.0,
                    eastUnit * eastUnit + northUnit * northUnit);
                if (radialSquared <= 0.18)
                {
                    // Byte-for-formula equivalent of AERISNdMapProjection's accepted
                    // small-angle series.  Keeping the exact coefficient sequence avoids
                    // a second cartographic authority between runway/symbology and terrain.
                    return 1.0 + radialSquared * (1.0 / 6.0 +
                        radialSquared * (3.0 / 40.0 +
                        radialSquared * (5.0 / 112.0 +
                        radialSquared * (35.0 / 1152.0 +
                        radialSquared * (63.0 / 2816.0)))));
                }

                float radial = sqrt(radialSquared);
                float centerDot = dot(geographicUnit, _AerisCenter.xyz);
                return radial <= 1.0e-12 ? 1.0 :
                    atan2(radial, centerDot) / radial;
            }

            v2f vert(appdata input)
            {
                v2f output;
                float3 geographicUnit = input.geographicUnit;
                float eastUnit = dot(geographicUnit, _AerisEast.xyz);
                float northUnit = dot(geographicUnit, _AerisNorth.xyz);
                float factor = AerisAngularScale(geographicUnit,
                    eastUnit, northUnit);
                float eastMeters = eastUnit * _AerisRadiusMeters * factor;
                float northMeters = northUnit * _AerisRadiusMeters * factor;

                float u = 0.5 + eastMeters /
                    max(1.0, _AerisHorizontalMeters);
                float renderV = _AerisAnchorRenderV +
                    _AerisOrientationSign * northMeters /
                    max(1.0, _AerisVerticalMeters);

                // DrawMeshNow supplies the existing scale-corrected TRACK-UP map matrix;
                // GL.LoadOrtho supplies the same render-target projection as the CPU path.
                output.vertex = UnityObjectToClipPos(float4(u, renderV, 0.0, 1.0));
                output.color = _AerisTerrainSemanticMode > 0.5 ?
                    AerisTerrainColour(input.terrainSemantic) : input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return input.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
