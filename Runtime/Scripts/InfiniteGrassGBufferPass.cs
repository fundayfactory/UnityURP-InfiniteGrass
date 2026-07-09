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

            if (_colorLoadActions == null ||
                _colorStoreActions == null ||
                _colorLoadActions.Length != gBuffer.Length ||
                _colorStoreActions.Length != gBuffer.Length)
            {
                _colorLoadActions = new RenderBufferLoadAction[gBuffer.Length];
                _colorStoreActions = new RenderBufferStoreAction[gBuffer.Length];

                for (var i = 0; i < gBuffer.Length; i++)
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
            
            passData.CameraDepthTarget = resourceData.activeDepthTexture;
            
            builder.UseTexture(passData.CameraDepthTarget, AccessFlags.ReadWrite);
            
            passData.PositionBuffers = _infiniteGrassData.PositionBuffers;
            passData.InfiniteGrassData = _infiniteGrassData;
            
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
                        
            if (data.InfiniteGrassData.PositionsFenceValid)
                cmd.WaitOnAsyncGraphicsFence(data.InfiniteGrassData.PositionsFence, SynchronisationStage.VertexProcessing);

            cmd.SetGlobalTexture(ShaderPropertyId.GrassColorRT, data.ColorTexture);
            cmd.SetGlobalTexture(ShaderPropertyId.GrassSlopeRT, data.SlopeTexture);

            if (_colorTargets == null || _colorTargets.Length != data.GBuffer.Length)
                _colorTargets = new RenderTargetIdentifier[data.GBuffer.Length];
            
            for (var i = 0; i < data.GBuffer.Length; i++)
            {
                _colorTargets[i] = data.GBuffer[i];
            }

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
                cmd.EnableShaderKeyword("_LIGHT_LAYERS");
            
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
                cmd.DisableShaderKeyword("_LIGHT_LAYERS");
            
            if (SystemInfo.supportsGraphicsFence)
            {
                data.InfiniteGrassData.GBufferWriteFence = cmd.CreateAsyncGraphicsFence();
                data.InfiniteGrassData.GBufferWriteFenceValid = true;
            }
        }

        private sealed class PassData
        {
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;
            
            public TextureHandle[] GBuffer;
            public TextureHandle CameraDepthTarget;
            public bool HasRenderingLayersTexture;

            public List<GraphicsBuffer> PositionBuffers;
            public InfiniteGrassData InfiniteGrassData;
            
            public RenderBufferLoadAction[] ColorLoadActions;
            public RenderBufferStoreAction[] ColorStoreActions;
        }

        private static class ShaderPropertyId
        {
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
            public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
            public static readonly int GrassRenderingLayerMask = Shader.PropertyToID("_GrassRenderingLayerMask");
        }
    }
    
    internal sealed class InfiniteGrassGBufferSyncPass : ScriptableRenderPass
    {
        private readonly InfiniteGrassData _infiniteGrassData;

        public InfiniteGrassGBufferSyncPass(InfiniteGrassData infiniteGrassData)
        {
            _infiniteGrassData = infiniteGrassData;
        }

        private class PassData
        {
            public InfiniteGrassData InfiniteGrassData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            using var builder = renderGraph.AddUnsafePass<PassData>("Grass GBuffer Sync", out var passData);
            passData.InfiniteGrassData = _infiniteGrassData;
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                if (!data.InfiniteGrassData.GBufferWriteFenceValid)
                    return;

                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                cmd.WaitOnAsyncGraphicsFence(data.InfiniteGrassData.GBufferWriteFence, SynchronisationStage.PixelProcessing);
            });
        }
    }
}