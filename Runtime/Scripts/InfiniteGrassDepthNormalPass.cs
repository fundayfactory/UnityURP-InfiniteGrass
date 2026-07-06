using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    internal sealed class InfiniteGrassDepthNormalPass : ScriptableRenderPass
    {
        private static RenderTargetIdentifier[] _colorTargets;
        private static readonly RenderBufferLoadAction[] _colorLoadActions = { RenderBufferLoadAction.Load, RenderBufferLoadAction.Load };
        private static readonly RenderBufferStoreAction[] _colorStoreActions = { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store };
        
        private readonly InfiniteGrassData _infiniteGrassData;
            
        public InfiniteGrassDepthNormalPass(InfiniteGrassData infiniteGrassData)
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
            
            using var builder = renderGraph.AddUnsafePass<PassData>("Grass Depth Normal Pass", out var passData);
            passData.ColorTexture = renderGraph.ImportTexture(_infiniteGrassData.ColorRT);
            passData.SlopeTexture = renderGraph.ImportTexture(_infiniteGrassData.SlopeRT);
            
            builder.UseTexture(passData.ColorTexture);
            builder.UseTexture(passData.SlopeTexture);
            
            var resourceData = frameContext.Get<UniversalResourceData>();
            passData.CameraNormalsTexture = resourceData.cameraNormalsTexture;
            passData.CameraDepthTarget = resourceData.activeDepthTexture;
            
            builder.UseTexture(passData.CameraNormalsTexture, AccessFlags.Write);
            builder.UseTexture(passData.CameraDepthTarget, AccessFlags.Write);
            
            passData.RenderingLayersTexture = resourceData.renderingLayersTexture;
            passData.HasRenderingLayersTexture = resourceData.renderingLayersTexture.IsValid();
 
            if (passData.HasRenderingLayersTexture)
                builder.UseTexture(passData.RenderingLayersTexture, AccessFlags.Write);
            
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

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }
        
        private static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            cmd.SetGlobalTexture(ShaderPropertyId.GrassColorRT, data.ColorTexture);
            cmd.SetGlobalTexture(ShaderPropertyId.GrassSlopeRT, data.SlopeTexture);
            
            if (data.HasRenderingLayersTexture)
            {
                _colorTargets ??= new RenderTargetIdentifier[2];
                _colorTargets[0] = data.CameraNormalsTexture;
                _colorTargets[1] = data.RenderingLayersTexture;
 
                var binding = new RenderTargetBinding(
                    _colorTargets,
                    _colorLoadActions,
                    _colorStoreActions,
                    data.CameraDepthTarget,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store
                );
 
                cmd.SetRenderTarget(binding);
            }
            else
            {
                cmd.SetRenderTarget(data.CameraNormalsTexture, data.CameraDepthTarget);
            }
            
            if (data.HasRenderingLayersTexture)
                cmd.EnableShaderKeyword("_WRITE_RENDERING_LAYERS");

            for (var i = 0; i < data.PositionBuffers.Count; i++)
            {
                var settings = InfiniteGrassUtility.Settings[i];
                var posBuffer = data.PositionBuffers[i];
                cmd.SetGlobalBuffer(ShaderPropertyId.GrassPositions, posBuffer);
                cmd.SetGlobalInt(ShaderPropertyId.GrassRenderingLayerMask, settings.renderingLayerMask);
                cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.ArgsBuffers[i], 4);
                    
                cmd.DrawMeshInstancedIndirect(InfiniteGrassUtility.Meshes[i], 0, InfiniteGrassUtility.Materials[i], InfiniteGrassStaticConfig.DepthNormalPassIndex, InfiniteGrassUtility.ArgsBuffers[i], 0);
            }
            
            if (data.HasRenderingLayersTexture)
                cmd.DisableShaderKeyword("_WRITE_RENDERING_LAYERS");
        }

        private sealed class PassData
        {
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;
                
            public TextureHandle CameraNormalsTexture;
            public TextureHandle CameraDepthTarget;
            public TextureHandle RenderingLayersTexture;
            public bool HasRenderingLayersTexture;

            public List<GraphicsBuffer> PositionBuffers;
        }

        private static class ShaderPropertyId
        {
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
            public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
            public static readonly int GrassRenderingLayerMask = Shader.PropertyToID("_GrassRenderingLayerMask");
        }
    }
}