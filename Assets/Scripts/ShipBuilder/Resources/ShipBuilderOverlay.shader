// Flat colour that ignores the depth buffer, so a hard point marker tucked inside the hull is
// still visible and still clickable.
Shader "ShipBuilder/Overlay"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.8, 1.0, 0.55)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Always
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
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_Color);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
