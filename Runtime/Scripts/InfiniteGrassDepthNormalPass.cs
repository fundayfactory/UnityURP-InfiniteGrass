using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    internal sealed class InfiniteGrassDepthNormalPass : ScriptableRenderPass
    {
        private readonly InfiniteGrassData _infiniteGrassData;
            
        public InfiniteGrassDepthNormalPass(InfiniteGrassData infiniteGrassData)
        {
            _infiniteGrassData = infiniteGrassData;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
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
            
            passData.PositionBuffers = _infiniteGrassData.PositionBuffers;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }
        
        private static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            cmd.SetGlobalTexture(ShaderPropertyId.GrassColorRT, data.ColorTexture);
            cmd.SetGlobalTexture(ShaderPropertyId.GrassSlopeRT, data.SlopeTexture);
            
            cmd.SetRenderTarget(data.CameraNormalsTexture, data.CameraDepthTarget);

            // --- Draw ---
            for (var i = 0; i < data.PositionBuffers.Count; i++)
            {
                var settings = InfiniteGrassUtility.Settings[i];
                var posBuffer = data.PositionBuffers[i];
                cmd.SetGlobalBuffer(ShaderPropertyId.GrassPositions, posBuffer);
                cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.ArgsBuffers[i], 4);
                    
                if (settings.previewVisibleGrassCount)
                    cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.Buffers[i], 0);
                    
                cmd.DrawMeshInstancedIndirect(InfiniteGrassUtility.Meshes[i], 0, InfiniteGrassUtility.Materials[i], 1, InfiniteGrassUtility.ArgsBuffers[i], 0);
            }
        }

        private sealed class PassData
        {
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;
                
            public TextureHandle CameraNormalsTexture;
            public TextureHandle CameraDepthTarget;

            public List<ComputeBuffer> PositionBuffers;
        }

        private static class ShaderPropertyId
        {
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
            public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
        }
    }
}