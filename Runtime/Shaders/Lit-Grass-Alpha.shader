Shader "InfiniteGrass/Grass Alpha"
{
    Properties
    {
        [Header(Main)][Space]
        [MainTexture] _MainTex("Main", 2D) = "white" {}
        _AlphaCut0ff("Alpha Cutoff", Range(0, 1)) = 0.5
        
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
        Tags {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
            "IgnoreProjector"="True"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Cull Back
            ZTest Lequal
            AlphaToMask On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
            #include "InfiniteGrassCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                half3 color         : TEXCOORD0;
                float2 uv           : TEXCOORD1;
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
                half _AlphaCut0ff;
            
                StructuredBuffer<float3> _GrassPositions;
            CBUFFER_END
            
            TEXTURE2D(_MainTex);            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BaseColorTexture);   SAMPLER(sampler_BaseColorTexture);
            TEXTURE2D(_WindTexture);        SAMPLER(sampler_WindTexture);

            TEXTURE2D(_GrassColorRT);       SAMPLER(sampler_GrassColorRT);
            TEXTURE2D(_GrassSlopeRT);       SAMPLER(sampler_GrassSlopeRT);
            
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

                float4 slope = SAMPLE_TEXTURE2D_LOD(_GrassSlopeRT, sampler_GrassSlopeRT, float4(uv, 0, 0), 0);
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                // Direction reconstructed from the slope texture
                float3 slopeDirection = normalize(float3(xSlope, 1 - max(abs(xSlope), abs(zSlope)) * 0.5, zSlope));
                // The original direction is upward
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));

                half3 windTex = SAMPLE_TEXTURE2D_LOD(_WindTexture, sampler_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0), 0);
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
                half3 baseColor = lerp(_ColorA, _ColorB, SAMPLE_TEXTURE2D_LOD(_BaseColorTexture, sampler_BaseColorTexture, float4(TRANSFORM_TEX(pivot.xz, _BaseColorTexture),0,0), 0).r);
                half3 albedo = lerp(_AOColor, baseColor, IN.positionOS.y);
                float4 color = SAMPLE_TEXTURE2D_LOD(_GrassColorRT, sampler_GrassColorRT, float4(uv, 0, 0), 0);
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
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(color.a - _AlphaCut0ff);
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
            
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "InfiniteGrassCommon.hlsl"
                        
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 texcoord     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : NORMAL;
                float2 uv           : TEXCOORD1;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            
                float _GrassHeight;
                float _GrassCurving;
                float _GrassHeightRandomness;

                float _ExpandDistantGrassWidth;
                float2 _ExpandDistantGrassRange;

                float4 _WindTexture_ST;
                float _WindStrength;
                float2 _WindScroll;

                half _RandomNormal;
                half _AlphaCut0ff;
            
                StructuredBuffer<float3> _GrassPositions;
            CBUFFER_END
            
            TEXTURE2D(_MainTex);            SAMPLER(sampler_MainTex);
            TEXTURE2D(_WindTexture);        SAMPLER(sampler_WindTexture);

            TEXTURE2D(_GrassColorRT);       SAMPLER(sampler_GrassColorRT);
            TEXTURE2D(_GrassSlopeRT);       SAMPLER(sampler_GrassSlopeRT);
            
            float2 _GrassCenterPos;
            float _GrassDrawDistance;
            float _GrassTextureUpdateThreshold;
            uint _GrassRenderingLayerMask;
            
            Varyings GrassDepthNormalsVert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                
                float3 pivot = _GrassPositions[instanceID];
                
                float2 uv = (pivot.xz - _GrassCenterPos) / (_GrassDrawDistance + _GrassTextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;
                
                // Grass Height
                float grassHeight = _GrassHeight * (1 - random(pivot.x * 230 + pivot.z * 10) * _GrassHeightRandomness);

                float4 slope = SAMPLE_TEXTURE2D_LOD(_GrassSlopeRT, sampler_GrassSlopeRT, float4(uv, 0, 0), 0);
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                // Direction reconstructed from the slope texture
                float3 slopeDirection = normalize(float3(xSlope, 1 - max(abs(xSlope), abs(zSlope)) * 0.5, zSlope));
                // The original direction is upward
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));

                half3 windTex = SAMPLE_TEXTURE2D_LOD(_WindTexture, sampler_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0), 0);
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
                output.uv = input.texcoord;

                return output;
            }
            
            void GrassDepthNormalsFrag(Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(color.a - _AlphaCut0ff);
                
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(normalWS, 0.0);
                
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = _GrassRenderingLayerMask & _RenderingLayerMaxInt;
            #endif
            }
            
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }
            
            ZWrite On
            ZTest LEqual
            Cull Back
            
            Stencil
            {
                Ref 33
                ReadMask 0
                WriteMask 96
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore
            
            #pragma vertex GBufferPassVertex
            #pragma fragment GBufferPassFragment
            
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl"
            #include "InfiniteGrassCommon.hlsl"
            
            struct AttributesCustom
            {
                float4 positionOS   : POSITION;
                float2 texcoord     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsCustom
            {
                float4 positionCS   : SV_POSITION;
                half3 normalWS      : NORMAL;
                half3 color         : TEXCOORD0;
                float2 uv           : TEXCOORD1;
                float3 positionWS   : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
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
                half _AlphaCut0ff;
            
                StructuredBuffer<float3> _GrassPositions;
            CBUFFER_END
            
            TEXTURE2D(_MainTex);            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BaseColorTexture);   SAMPLER(sampler_BaseColorTexture);
            TEXTURE2D(_WindTexture);        SAMPLER(sampler_WindTexture);

            TEXTURE2D(_GrassColorRT);       SAMPLER(sampler_GrassColorRT);
            TEXTURE2D(_GrassSlopeRT);       SAMPLER(sampler_GrassSlopeRT);
            
            float2 _GrassCenterPos;
            float _GrassDrawDistance;
            float _GrassTextureUpdateThreshold;
            uint _GrassRenderingLayerMask;

            VaryingsCustom GBufferPassVertex(AttributesCustom input, uint instanceID : SV_InstanceID)
            {
                VaryingsCustom output = (VaryingsCustom)0;
                
                float3 pivot = _GrassPositions[instanceID];
                
                float2 uv = (pivot.xz - _GrassCenterPos) / (_GrassDrawDistance + _GrassTextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;
                
                // Grass Height
                float grassHeight = _GrassHeight * (1 - random(pivot.x * 230 + pivot.z * 10) * _GrassHeightRandomness);

                float4 slope = SAMPLE_TEXTURE2D_LOD(_GrassSlopeRT, sampler_GrassSlopeRT, float4(uv, 0, 0), 0);
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                // Direction reconstructed from the slope texture
                float3 slopeDirection = normalize(float3(xSlope, 1 - max(abs(xSlope), abs(zSlope)) * 0.5, zSlope));
                // The original direction is upward
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));

                half3 windTex = SAMPLE_TEXTURE2D_LOD(_WindTexture, sampler_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0), 0);
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

                half3 baseColor = lerp(_ColorA, _ColorB, SAMPLE_TEXTURE2D_LOD(_BaseColorTexture, sampler_BaseColorTexture, float4(TRANSFORM_TEX(pivot.xz, _BaseColorTexture),0,0), 0).r);
                half3 albedo = lerp(_AOColor, baseColor, input.positionOS.y);
                float4 color = SAMPLE_TEXTURE2D_LOD(_GrassColorRT, sampler_GrassColorRT, float4(uv, 0, 0), 0);
                albedo = lerp(albedo, color.rgb, color.a);
                
                output.color = albedo;
                output.uv = input.texcoord;
                output.normalWS = normal;
                output.positionWS = positionWS;
                
                OUTPUT_SH4(positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(positionWS), output.vertexSH, output.probeOcclusion);

                return output;
            }
            
            void InitializeBakedGIDataCustom(VaryingsCustom input, inout InputData inputData)
            {
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
            #elif defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(inputData.positionWS),
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    inputData.positionCS.xy,
                    input.probeOcclusion,
                    inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif
            }
            
            GBufferFragOutput GBufferPassFragment(VaryingsCustom input)
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _AlphaCut0ff);
                
                albedo.rgb *= input.color;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.occlusion = 1;
                surfaceData.alpha = albedo.a;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                // inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);

                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
                #endif
                
                InitializeBakedGIDataCustom(input, inputData);

                BRDFData brdfData;
                InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
                
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);

                half3 color = GlobalIllumination(brdfData, (BRDFData)0, 0,
                                                          inputData.bakedGI, surfaceData.occlusion, inputData.positionWS,
                                                          inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);

                GBufferFragOutput output = PackGBuffersBRDFData(brdfData, inputData, surfaceData.smoothness, surfaceData.emission + color, surfaceData.occlusion);
                
            #if defined(_WRITE_RENDERING_LAYERS) || defined(_LIGHT_LAYERS)
                output.meshRenderingLayers = _GrassRenderingLayerMask & _RenderingLayerMaxInt;
            #endif

                return output;
            }
                        
            ENDHLSL
        }
    }
}