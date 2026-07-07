Shader "InfiniteGrass/Grass"
{
    Properties
    {
        [Header(Main)][Space]
        [MainTexture] _MainTex("Main", 2D) = "white" {}
        
        [Header(Color)][Space]
        _BaseColorTexture("BaseColor Texture", 2D) = "white" {}
        _ColorA("ColorA", Color) = (0,0,0,1)
        _ColorB("ColorB", Color) = (1,1,1,1)
        _AOColor("AO Color", Color) = (0.5,0.5,0.5)

        [Header(Grass Shape)][Space]
        _GrassHeight("Grass Height", Float) = 1
        _GrassHeightRandomness("Grass Height Randomness", Range(0, 1)) = 0.5
        _GrassCurving("Grass Curving", Float) = 0.1
        
        [Space]
        _ExpandDistantGrassWidth("Expand Distant Grass Width", Float) = 1
        _ExpandDistantGrassRange("Expand Distant Grass Range", Vector) = (50, 200, 0, 0)

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            
            Cull Back
            ZTest Less
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "InfiniteGrassCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color       : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            
                half3 _ColorA;
                half3 _ColorB;
                float4 _BaseColorTexture_ST;
                half3 _AOColor;
            
                float _GrassHeight;
                float _GrassCurving;
                float _GrassHeightRandomness;

                float _ExpandDistantGrassWidth;
                float2 _ExpandDistantGrassRange;

                float4 _WindTexture_ST;
                float _WindStrength;
                float2 _WindScroll;

                half _RandomNormal;
            
                StructuredBuffer<float3> _GrassPositions;

            CBUFFER_END

            sampler2D _MainTex;
            sampler2D _BaseColorTexture;
            sampler2D _WindTexture;

            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;
            
            float2 _GrassCenterPos;
            float _GrassDrawDistance;
            float _GrassTextureUpdateThreshold;

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                float3 pivot = _GrassPositions[instanceID];
                
                float2 uv = (pivot.xz - _GrassCenterPos) / (_GrassDrawDistance + _GrassTextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;
                
                // Grass Height
                float grassHeight = _GrassHeight * (1 - random(pivot.x * 230 + pivot.z * 10) * _GrassHeightRandomness);
                
                // Billboard Logic
                float3 cameraTransformForwardWS = -UNITY_MATRIX_V[2].xyz;

                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0, 0));
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                // Direction reconstructed from the slope texture
                float3 slopeDirection = normalize(float3(xSlope, 1 - max(abs(xSlope), abs(zSlope)) * 0.5, zSlope));
                // The original direction is upward
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));

                half3 windTex = tex2Dlod(_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0));
                float2 wind = (windTex.rg * 2 - 1) * _WindStrength * (1-slope.a);

                // Adding wind and multiplying with the Y position to affect the tip only
                bladeDirection.xz += wind * IN.positionOS.y;
                bladeDirection = normalize(bladeDirection);
                
                // This insures that the blade is always facing the camera
                float3 positionOS = bladeDirection * IN.positionOS.y * grassHeight;
                positionOS.xz += IN.positionOS.xz + IN.positionOS.y * IN.positionOS.y * float2(srandom(pivot.x * 851 + pivot.z * 10), srandom(pivot.z * 647 + pivot.x * 10)) * _GrassCurving;
                
                // Adds a bit of curving to grass blade
                // posOS -> posWS
                float3 positionWS = positionOS + pivot;
                // posWS -> posCS
                OUT.positionCS = TransformWorldToHClip(positionWS);
                
                // Color
                half3 baseColor = lerp(_ColorA, _ColorB, tex2Dlod(_BaseColorTexture, float4(TRANSFORM_TEX(pivot.xz, _BaseColorTexture),0,0)).r);
                half3 albedo = lerp(_AOColor, baseColor, IN.positionOS.y);
                float4 color = tex2Dlod(_GrassColorRT, float4(uv, 0, 0));
                albedo = lerp(albedo, color.rgb, color.a);

                // Lighting
                half3 N = normalize(bladeDirection + cameraTransformForwardWS * -0.5 + _RandomNormal * half3(srandom(pivot.x * 314 + pivot.z * 10), 0, srandom(pivot.z * 677 + pivot.x * 10)));
                // The normal vector is just the blade direction tilted a bit towards the camera with a bit of randomness
                half3 V = normalize(_WorldSpaceCameraPos - positionWS);

                float3 lighting = CalculateLighting(albedo, positionWS, N, V, color.a, IN.positionOS.y);
                // I'm also passing the Alpha Channel of the Color Map cause I dont want the blades that are affected with color to receive specular light 
                // The main use of the color map for me is burning the grass and the burned grass should not receive specular light
                
                float fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.color.rgb = MixFog(lighting, fogFactor);
                OUT.uv = IN.uv;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = tex2D(_MainTex, IN.uv);
                color.rgb *= IN.color.rgb;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassDepthNormalsVert
            #pragma fragment GrassDepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "InfiniteGrassCommon.hlsl"
                        
            struct Attributes
            {
                float4 positionOS   : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : NORMAL;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float _GrassHeight;
                float _GrassCurving;
                float _GrassHeightRandomness;

                float _ExpandDistantGrassWidth;
                float2 _ExpandDistantGrassRange;

                float4 _WindTexture_ST;
                float _WindStrength;
                float2 _WindScroll;

                half _RandomNormal;
            
                StructuredBuffer<float3> _GrassPositions;
            CBUFFER_END
            
            sampler2D _WindTexture;

            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;
            
            float2 _GrassCenterPos;
            float _GrassDrawDistance;
            float _GrassTextureUpdateThreshold;
            
            Varyings GrassDepthNormalsVert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                
                float3 pivot = _GrassPositions[instanceID];
                
                float2 uv = (pivot.xz - _GrassCenterPos) / (_GrassDrawDistance + _GrassTextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;
                
                // Grass Height
                float grassHeight = _GrassHeight * (1 - random(pivot.x * 230 + pivot.z * 10) * _GrassHeightRandomness);

                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0, 0));
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                // Direction reconstructed from the slope texture
                float3 slopeDirection = normalize(float3(xSlope, 1 - max(abs(xSlope), abs(zSlope)) * 0.5, zSlope));
                // The original direction is upward
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));

                half3 windTex = tex2Dlod(_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0));
                float2 wind = (windTex.rg * 2 - 1) * _WindStrength * (1-slope.a);

                // Adding wind and multiplying with the Y position to affect the tip only
                bladeDirection.xz += wind * input.positionOS.y;
                bladeDirection = normalize(bladeDirection);
                
                float3 positionOS = bladeDirection * input.positionOS.y * grassHeight;
                positionOS.xz += input.positionOS.xz + input.positionOS.y * input.positionOS.y * float2(srandom(pivot.x * 851 + pivot.z * 10), srandom(pivot.z * 647 + pivot.x * 10)) * _GrassCurving;
                
                // Adds a bit of curving to grass blade
                // posOS -> posWS
                float3 positionWS = positionOS + pivot;
                // posWS -> posCS
                output.positionCS = TransformWorldToHClip(positionWS);

                float3 cameraTransformForwardWS = -UNITY_MATRIX_V[2].xyz;
                half3 normal = normalize(bladeDirection + cameraTransformForwardWS * -0.5 + _RandomNormal * half3(srandom(pivot.x * 314 + pivot.z * 10), 0, srandom(pivot.z * 677 + pivot.x * 10)));
                
                output.normalWS = normal;

                return output;
            }
            
            void GrassDepthNormalsFrag(Varyings input, out half4 outNormalWS : SV_Target0)
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms.
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
                outNormalWS = half4(packedNormalWS, 0.0);
                #else
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(normalWS, 0.0);
                #endif
            }
            
            ENDHLSL
        }
    }
}