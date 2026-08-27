// The clickable blob at a hard point: a camera facing circle, drawn either solid (an open socket)
// or as a hollow ring (a socket that already holds a part), with an optional outline hugging its rim.
//
// Deliberately draws on top of everything (ZTest Always). Hard points sit right on - and often just
// inside - the hull surface, so depth testing here would silently swallow perfectly visible sockets.
// Which markers belong to the far side of the ship is decided on the CPU instead, by the builder
// casting a ray from each socket toward the camera; that runs against the same colliders as picking,
// so what you can see and what you can click never disagree.
Shader "ShipBuilder/Marker"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.8, 1.0, 0.55)
        _Fill ("Fill", Range(0, 1)) = 1
        _InnerRadius ("Ring Inner Radius", Range(0, 1)) = 0.68
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Marker"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Always
            ZWrite Off
            Cull Off
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
                float2 local : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _OutlineColor;
                float _Fill;
                float _InnerRadius;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Billboard in view space: the quad is rebuilt around the object origin facing the
                // camera, so the marker reads as a circle from every angle without the CPU having
                // to reorient it every frame.
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 centerWS = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
                float scale = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));

                float3 centerVS = TransformWorldToView(centerWS);
                float3 positionVS = centerVS + float3(input.positionOS.xy * scale, 0.0);

                output.positionHCS = TransformWViewToHClip(positionVS);
                output.local = input.positionOS.xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float distanceFromCenter = length(input.local) * 2.0;
                float edge = max(fwidth(distanceFromCenter), 1e-5);

                float disc = 1.0 - smoothstep(1.0 - edge, 1.0, distanceFromCenter);
                float hole = smoothstep(_InnerRadius - edge, _InnerRadius + edge, distanceFromCenter);

                // _Fill 1 keeps the whole disc, _Fill 0 keeps only the rim.
                float shape = disc * lerp(hole, 1.0, _Fill);

                // A band hugging the outer rim. Gated on the width so a marker that wants no
                // outline gets exactly none rather than a hairline at the edge.
                float outlineStart = 1.0 - _OutlineWidth;
                float band = disc * smoothstep(outlineStart - edge, outlineStart + edge, distanceFromCenter);
                band *= step(0.0001, _OutlineWidth) * _OutlineColor.a;

                half3 rgb = lerp(_Color.rgb, _OutlineColor.rgb, band);
                half alpha = max(_Color.a * shape, band);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
