// Transparent hologram used for the hover preview. Edges brighten so the silhouette of the part
// reads clearly even when it overlaps the hull behind it.
Shader "ShipBuilder/Ghost"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.35, 0.85, 1.0, 0.3)
        _RimColor ("Rim Color", Color) = (0.6, 0.95, 1.0, 1.0)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RimColor;
                float _RimPower;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positions.positionCS;
                output.normalWS = normals.normalWS;
                output.viewWS = GetWorldSpaceViewDir(positions.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(input.viewWS);

                float rim = 1.0 - saturate(dot(normal, view));
                rim = pow(rim, _RimPower);

                half3 rgb = lerp(_Color.rgb, _RimColor.rgb, rim);
                half alpha = saturate(_Color.a + rim * _RimColor.a);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
