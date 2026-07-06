using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    internal sealed class InfiniteGrassRenderPass : ScriptableRenderPass
    {
        private readonly InfiniteGrassData _infiniteGrassData;
        private readonly MaterialPropertyBlock _propertyBlock;
            
        public InfiniteGrassRenderPass(InfiniteGrassData infiniteGrassData)
        {
            _infiniteGrassData = infiniteGrassData;
            _propertyBlock = new MaterialPropertyBlock();
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var universalRenderingData = frameContext.Get<UniversalRenderingData>();
            if (universalRenderingData.renderingMode == RenderingMode.Deferred || universalRenderingData.renderingMode == RenderingMode.DeferredPlus)
                return;
            
            if (InfiniteGrassUtility.ArgsBuffers.Count == 0)
                return;
            
            _infiniteGrassData.EnsureRTHandles();

            using var builder = renderGraph.AddUnsafePass<PassData>("Grass Forward Pass", out var passData);
            passData.ColorTexture = renderGraph.ImportTexture(_infiniteGrassData.ColorRT);
            passData.SlopeTexture = renderGraph.ImportTexture(_infiniteGrassData.SlopeRT);
            
            builder.UseTexture(passData.ColorTexture);
            builder.UseTexture(passData.SlopeTexture);
            
            var resourceData = frameContext.Get<UniversalResourceData>();
            passData.CameraColorTarget = resourceData.activeColorTexture;
            passData.CameraDepthTarget = resourceData.activeDepthTexture;
            
            builder.UseTexture(passData.CameraColorTarget, AccessFlags.Write);
            builder.UseTexture(passData.CameraDepthTarget, AccessFlags.Write);

            passData.PositionBuffers = _infiniteGrassData.PositionBuffers;
            passData.PropertyBlock = _propertyBlock;
            
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

            // Restore camera render target and matrices
            cmd.SetRenderTarget(data.CameraColorTarget, data.CameraDepthTarget);
                
            // --- Set lighting data ---
            var mpb = data.PropertyBlock;
            mpb.Clear();
            mpb.SetVector(ShaderPropertyId.UnityLightData, new Vector4(1, 16, 1, 0));

            var shc = new SHCoefficients(RenderSettings.ambientProbe);
            mpb.SetVector(ShaderPropertyId.UnitySHAr, shc.SHAr);
            mpb.SetVector(ShaderPropertyId.UnitySHAg, shc.SHAg);
            mpb.SetVector(ShaderPropertyId.UnitySHAb, shc.SHAb);
            mpb.SetVector(ShaderPropertyId.UnitySHBr, shc.SHBr);
            mpb.SetVector(ShaderPropertyId.UnitySHBg, shc.SHBg);
            mpb.SetVector(ShaderPropertyId.UnitySHBb, shc.SHBb);
            mpb.SetVector(ShaderPropertyId.UnitySHC, shc.SHC);
            mpb.SetVector(ShaderPropertyId.UnityProbesOcclusion, shc.ProbesOcclusion);

            // --- Draw ---
            for (var i = 0; i < data.PositionBuffers.Count; i++)
            {
                var posBuffer = data.PositionBuffers[i];
                cmd.SetGlobalBuffer(ShaderPropertyId.GrassPositions, posBuffer);
                cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.ArgsBuffers[i], 4);
                    
                cmd.DrawMeshInstancedIndirect(InfiniteGrassUtility.Meshes[i], 0, InfiniteGrassUtility.Materials[i], InfiniteGrassStaticConfig.ForwardPassIndex, InfiniteGrassUtility.ArgsBuffers[i], 0, mpb);
            }
        }

        private sealed class PassData
        {
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;
                
            public TextureHandle CameraColorTarget;
            public TextureHandle CameraDepthTarget;

            public List<GraphicsBuffer> PositionBuffers;

            public MaterialPropertyBlock PropertyBlock;
        }

        private static class ShaderPropertyId
        {
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
            public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
                
            public static readonly int UnityLightData = Shader.PropertyToID("unity_LightData");
                
            public static readonly int UnitySHAr = Shader.PropertyToID("unity_SHAr");
            public static readonly int UnitySHAg = Shader.PropertyToID("unity_SHAg");
            public static readonly int UnitySHAb = Shader.PropertyToID("unity_SHAb");
            public static readonly int UnitySHBr = Shader.PropertyToID("unity_SHBr");
            public static readonly int UnitySHBg = Shader.PropertyToID("unity_SHBg");
            public static readonly int UnitySHBb = Shader.PropertyToID("unity_SHBb");
            public static readonly int UnitySHC = Shader.PropertyToID("unity_SHC");
            public static readonly int UnityProbesOcclusion = Shader.PropertyToID("unity_ProbesOcclusion");
        }
    }
}