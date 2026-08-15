Shader "AERIS/ND/ExactVertexProjection"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
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
                output.color = input.color * _Color;
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
