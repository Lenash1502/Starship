Shader "Custom/GlitteringSpaceSkybox"
{
    Properties
    {
        _StarDensity ("Star Density", Range(10, 500)) = 150
        _StarCutoff ("Star Cutoff", Range(0.9, 1.0)) = 0.98
        _StarSize ("Star Size", Range(0.01, 1.0)) = 0.15
        _FlareLength ("Flare Length", Range(0.5, 5.0)) = 3.0
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 3.0
        _TwinkleIntensity ("Twinkle Intensity", Range(0, 1)) = 0.8
    }
    
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 uv : TEXCOORD0;
            };

            float _StarDensity;
            float _StarCutoff;
            float _StarSize;
            float _FlareLength;
            float _TwinkleSpeed;
            float _TwinkleIntensity;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.vertex.xyz; 
                return o;
            }

            // IMPROVED Hash function to eliminate diagonal geometric lines
            float hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 viewDir = normalize(i.uv);
                
                // Scale space into a grid
                float3 pos = viewDir * _StarDensity;
                float3 cell = floor(pos);
                
                float starValue = hash(cell);
                float isStar = step(_StarCutoff, starValue);
                
                // --- NEW: RANDOM JITTER MATH ---
                // Push the star off the dead-center of the grid randomly
                float offsetX = hash(cell + float3(13.0, 1.0, 5.0));
                float offsetY = hash(cell + float3(71.0, 8.0, 2.0));
                float offsetZ = hash(cell + float3(113.0, 4.0, 9.0));
                
                // Keep the offset between 0.2 and 0.8 so the star doesn't clip the cell walls
                float3 randomOffset = float3(offsetX, offsetY, offsetZ) * 0.6 + 0.2;
                float3 cellCenter = cell + randomOffset;
                // -------------------------------

                float3 localPos = abs(pos - cellCenter);
                
                // 1. The bright round core
                float coreDist = length(localPos);
                float core = 1.0 - smoothstep(0.0, _StarSize * 0.3, coreDist);
                
                // 2. The 3D Flare Spikes (Concave Astroid Shape)
                float flareDist = sqrt(localPos.x) + sqrt(localPos.y) + sqrt(localPos.z);
                float flareMax = min(sqrt(_StarSize * _FlareLength), 0.7); 
                float flare = 1.0 - smoothstep(0.0, flareMax, flareDist);
                
                // Combine core and flare
                float sizeGlow = saturate(core + flare * 0.8);
                
                // Twinkle Math
                float timeOffset = hash(cell * 1.5) * 100.0; 
                float twinkle = sin(_Time.y * _TwinkleSpeed + timeOffset);
                twinkle = 1.0 - (_TwinkleIntensity * 0.5) + (twinkle * _TwinkleIntensity * 0.5);

                float brightness = isStar * sizeGlow * twinkle;
                
                return fixed4(brightness, brightness, brightness, 1.0);
            }
            ENDCG
        }
    }
}