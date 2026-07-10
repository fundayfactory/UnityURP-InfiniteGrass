using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    internal sealed class InfiniteGrassGBufferPass : ScriptableRenderPass
    {
        private readonly InfiniteGrassData _infiniteGrassData;

        private RenderBufferLoadAction[] _colorLoadActions;
        private RenderBufferStoreAction[] _colorStoreActions;
        
        private static RenderTargetIdentifier[] _colorTargets;
            
        public InfiniteGrassGBufferPass(InfiniteGrassData infiniteGrassData)
        {
            _infiniteGrassData = infiniteGrassData;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var universalRenderingData = frameContext.Get<UniversalRenderingData>();
            if (universalRenderingData.renderingMode != RenderingMode.Deferred && universalRenderingData.renderingMode != RenderingMode.DeferredPlus)
                return;
            
            if (InfiniteGrassUtility.ArgsBuffers.Count == 0)
                return;
            
            _infiniteGrassData.EnsureRTHandles();
            
            using var builder = renderGraph.AddUnsafePass<PassData>("Grass GBuffer Pass", out var passData);
            passData.ColorTexture = renderGraph.ImportTexture(_infiniteGrassData.ColorRT);
            passData.SlopeTexture = renderGraph.ImportTexture(_infiniteGrassData.SlopeRT);
            
            builder.UseTexture(passData.ColorTexture);
            builder.UseTexture(passData.SlopeTexture);
            
            var resourceData = frameContext.Get<UniversalResourceData>();
            var gBuffer = resourceData.gBuffer;
            
            if (gBuffer == null || gBuffer.Length == 0 || !gBuffer[0].IsValid())
                return;
            
            passData.RenderingLayersTexture = resourceData.renderingLayersTexture;
            passData.HasRenderingLayersTexture = resourceData.renderingLayersTexture.IsValid();
 
            if (passData.HasRenderingLayersTexture)
                builder.UseTexture(passData.RenderingLayersTexture, AccessFlags.ReadWrite);
            
            var targetLength = gBuffer.Length + (passData.HasRenderingLayersTexture ? 1 : 0);

            if (_colorLoadActions == null ||
                _colorStoreActions == null ||
                _colorLoadActions.Length != targetLength ||
                _colorStoreActions.Length != targetLength)
            {
                _colorLoadActions = new RenderBufferLoadAction[targetLength];
                _colorStoreActions = new RenderBufferStoreAction[targetLength];

                for (var i = 0; i < targetLength; i++)
                {
                    _colorLoadActions[i] = RenderBufferLoadAction.Load;
                    _colorStoreActions[i] = RenderBufferStoreAction.Store;
                }
            }
                        
            passData.GBuffer = gBuffer;
            passData.ColorLoadActions = _colorLoadActions;
            passData.ColorStoreActions = _colorStoreActions;
            
            for (var i = 0; i < gBuffer.Length; i++)
            {
                if (gBuffer[i].IsValid())
                    builder.UseTexture(gBuffer[i], AccessFlags.ReadWrite);
            }
            
            var dBuffer = resourceData.dBuffer;
            for (var i = 0; i < dBuffer.Length; i++)
            {
                if (dBuffer[i].IsValid())
                    builder.UseTexture(dBuffer[i]);
            }
            
            builder.UseAllGlobalTextures(true);
            // builder.UseGlobalTexture(ShaderPropertyId.MainLightShadowmapID);
            // builder.UseGlobalTexture(ShaderPropertyId.AdditionalLightsShadowmapID);
            // builder.UseGlobalTexture(ShaderPropertyId.ScreenSpaceShadowmapID); 
            
            passData.CameraDepthTarget = resourceData.activeDepthTexture;
            
            builder.UseTexture(passData.CameraDepthTarget, AccessFlags.ReadWrite);
            
            passData.PositionBuffers = _infiniteGrassData.PositionBuffers;
            
            for (var i = 0; i < passData.PositionBuffers.Count; i++)
            {
                var posBufferHandle = renderGraph.ImportBuffer(passData.PositionBuffers[i]);
                builder.UseBuffer(posBufferHandle, AccessFlags.Read);
            }

            for (var i = 0; i < InfiniteGrassUtility.ArgsBuffers.Count; i++)
            {
                var argsBufferHandle = renderGraph.ImportBuffer(InfiniteGrassUtility.ArgsBuffers[i]);
                builder.UseBuffer(argsBufferHandle, AccessFlags.ReadWrite);
            }
            
            passData.HasRenderingLayersTexture = resourceData.renderingLayersTexture.IsValid();

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }
        
        private static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            if (data.GBuffer == null || data.GBuffer.Length == 0)
                return;
            
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            cmd.SetGlobalTexture(ShaderPropertyId.GrassColorRT, data.ColorTexture);
            cmd.SetGlobalTexture(ShaderPropertyId.GrassSlopeRT, data.SlopeTexture);
            
            var targetLength = data.GBuffer.Length + (data.HasRenderingLayersTexture ? 1 : 0);

            if (_colorTargets == null || _colorTargets.Length != targetLength)
                _colorTargets = new RenderTargetIdentifier[targetLength];
            
            for (var i = 0; i < data.GBuffer.Length; i++)
            {
                _colorTargets[i] = data.GBuffer[i];
            }

            if (data.HasRenderingLayersTexture)
                _colorTargets[^1] = data.RenderingLayersTexture;

            var binding = new RenderTargetBinding(
                _colorTargets,
                data.ColorLoadActions,
                data.ColorStoreActions,
                data.CameraDepthTarget,
                RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store
            );
 
            cmd.SetRenderTarget(binding);
            
            if (data.HasRenderingLayersTexture)
                cmd.EnableShaderKeyword("_WRITE_RENDERING_LAYERS");
            
            for (var i = 0; i < data.PositionBuffers.Count; i++)
            {
                var settings = InfiniteGrassUtility.Settings[i];
                var posBuffer = data.PositionBuffers[i];
                cmd.SetGlobalBuffer(ShaderPropertyId.GrassPositions, posBuffer);
                cmd.SetGlobalInt(ShaderPropertyId.GrassRenderingLayerMask, settings.renderingLayerMask);
                cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.ArgsBuffers[i], 4);
                    
                cmd.DrawMeshInstancedIndirect(InfiniteGrassUtility.Meshes[i], 0, InfiniteGrassUtility.Materials[i], InfiniteGrassStaticConfig.GBufferPassIndex, InfiniteGrassUtility.ArgsBuffers[i], 0);
            }
                        
            if (data.HasRenderingLayersTexture)
                cmd.DisableShaderKeyword("_WRITE_RENDERING_LAYERS");
        }

        private sealed class PassData
        {
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;
            
            public TextureHandle[] GBuffer;
            public TextureHandle CameraDepthTarget;
            public TextureHandle RenderingLayersTexture;
            public bool HasRenderingLayersTexture;

            public List<GraphicsBuffer> PositionBuffers;
            public RenderBufferLoadAction[] ColorLoadActions;
            public RenderBufferStoreAction[] ColorStoreActions;
        }

        private static class ShaderPropertyId
        {
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
            public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
            public static readonly int GrassRenderingLayerMask = Shader.PropertyToID("_GrassRenderingLayerMask");
            
            public static readonly int MainLightShadowmapID = Shader.PropertyToID("_MainLightShadowmapTexture");
            public static readonly int AdditionalLightsShadowmapID = Shader.PropertyToID("_AdditionalLightsShadowmapTexture");
            public static readonly int ScreenSpaceShadowmapID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
        }
    }
}