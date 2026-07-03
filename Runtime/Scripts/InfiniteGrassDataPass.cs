using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    internal sealed class InfiniteGrassDataPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> MaskShaderTag = new() { new ShaderTagId("GrassMask") };
        private static readonly List<ShaderTagId> ColorShaderTag = new() { new ShaderTagId("GrassColor") };
        private static readonly List<ShaderTagId> SlopeShaderTag = new() { new ShaderTagId("GrassSlope") };

        private readonly InfiniteGrassData _infiniteGrassData;
        private readonly LayerMask _heightMapLayer;
        private readonly Material _heightMapMat;
        private readonly ComputeShader _computeShader;
        private readonly float _fullDensityDistance;
        private readonly float _drawDistance;
        private readonly float _textureUpdateThreshold;
        private readonly bool _enableLodLevels;
        private readonly List<ShaderTagId> _shaderTagsList = new();
            
        public InfiniteGrassDataPass(
            InfiniteGrassData infiniteGrassData,
            LayerMask heightMapLayer,
            Material heightMapMat,
            ComputeShader computeShader,
            float fullDensityDistance,
            float drawDistance,
            float textureUpdateThreshold,
            bool enableLodLevels)
        {
            _infiniteGrassData = infiniteGrassData;
            _heightMapLayer = heightMapLayer;
            _computeShader = computeShader;
            _fullDensityDistance = fullDensityDistance;
            _drawDistance = drawDistance;
            _textureUpdateThreshold = textureUpdateThreshold;
            _enableLodLevels = enableLodLevels;
            _heightMapMat = heightMapMat;

            _shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
            _shaderTagsList.Add(new ShaderTagId("UniversalForward"));
            _shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var cameraData = frameContext.Get<UniversalCameraData>();
            var mainCamera = Camera.main;

            if (mainCamera == null)
                mainCamera = cameraData.camera;
            
            if (InfiniteGrassUtility.ArgsBuffers.Count == 0 || mainCamera == null)
                return;
            
            _infiniteGrassData.EnsureRTHandles();
            var renderingData = frameContext.Get<UniversalRenderingData>();
            var lightData = frameContext.Get<UniversalLightData>();
            
            var cameraPos = mainCamera.transform.position;
            var cameraBounds = InfiniteGrassUtility.CalculateCameraBounds(mainCamera, _drawDistance);
            var centerPos = new Vector2(Mathf.Floor(cameraPos.x / _textureUpdateThreshold) * _textureUpdateThreshold, Mathf.Floor(cameraPos.z / _textureUpdateThreshold) * _textureUpdateThreshold);
            
            Shader.SetGlobalVector(ShaderPropertyId.GrassCenterPos, centerPos);
            Shader.SetGlobalFloat(ShaderPropertyId.GrassDrawDistance, _drawDistance);
            Shader.SetGlobalFloat(ShaderPropertyId.GrassTextureUpdateThreshold, _textureUpdateThreshold);
                
            var viewMatrix = Matrix4x4.TRS(new Vector3(centerPos.x, cameraBounds.max.y, centerPos.y), Quaternion.LookRotation(-Vector3.up), new Vector3(1, 1, -1)).inverse;
            var projectionMatrix = Matrix4x4.Ortho(-(_drawDistance + _textureUpdateThreshold), _drawDistance + _textureUpdateThreshold, -(_drawDistance + _textureUpdateThreshold), _drawDistance + _textureUpdateThreshold, 0, cameraBounds.size.y);

            // --- Build renderer lists ---
            // Height map: override material on heightMapLayer objects
            var heightDrawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagsList, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            _heightMapMat.SetVector(ShaderPropertyId.BoundsYMinMax, new Vector2(cameraBounds.min.y, cameraBounds.max.y));
            heightDrawSettings.overrideMaterial = _heightMapMat;
            var heightFilterSettings = new FilteringSettings(RenderQueueRange.all, _heightMapLayer);

            // Mask pass
            var maskDrawSettings = RenderingUtils.CreateDrawingSettings(MaskShaderTag, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var maskFilterSettings = new FilteringSettings(RenderQueueRange.all);

            // Color pass
            var colorDrawSettings = RenderingUtils.CreateDrawingSettings(ColorShaderTag, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var colorFilterSettings = new FilteringSettings(RenderQueueRange.all);

            // Slope pass
            var slopeDrawSettings = RenderingUtils.CreateDrawingSettings(SlopeShaderTag, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var slopeFilterSettings = new FilteringSettings(RenderQueueRange.all);
            var resourceData = frameContext.Get<UniversalResourceData>();

            using var builder = renderGraph.AddUnsafePass<PassData>("Grass Data Pass", out var passData);
            passData.HeightTexture = renderGraph.ImportTexture(_infiniteGrassData.HeightRT);
            passData.HeightDepthTexture = renderGraph.ImportTexture(_infiniteGrassData.HeightDepthRT);
            passData.MaskTexture = renderGraph.ImportTexture(_infiniteGrassData.MaskRT);
            passData.ColorTexture = renderGraph.ImportTexture(_infiniteGrassData.ColorRT);
            passData.SlopeTexture = renderGraph.ImportTexture(_infiniteGrassData.SlopeRT);

            builder.UseTexture(passData.HeightTexture, AccessFlags.Write);
            builder.UseTexture(passData.HeightDepthTexture, AccessFlags.Write);
            builder.UseTexture(passData.MaskTexture, AccessFlags.Write);
            builder.UseTexture(passData.ColorTexture, AccessFlags.Write);
            builder.UseTexture(passData.SlopeTexture, AccessFlags.Write);
                
            passData.CameraColorTarget = resourceData.activeColorTexture;
            passData.CameraDepthTarget = resourceData.activeDepthTexture;
            
            builder.UseTexture(passData.CameraColorTarget, AccessFlags.Write);
            builder.UseTexture(passData.CameraDepthTarget, AccessFlags.Write);

            passData.HeightMapRendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, heightDrawSettings, heightFilterSettings));
            passData.MaskRendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, maskDrawSettings, maskFilterSettings));
            passData.ColorRendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, colorDrawSettings, colorFilterSettings));
            passData.SlopeRendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, slopeDrawSettings, slopeFilterSettings));
                
            builder.UseRendererList(passData.HeightMapRendererList);
            builder.UseRendererList(passData.MaskRendererList);
            builder.UseRendererList(passData.ColorRendererList);
            builder.UseRendererList(passData.SlopeRendererList);

            passData.HeightRTHandle = _infiniteGrassData.HeightRT;
            passData.MaskRTHandle = _infiniteGrassData.MaskRT;
                    
            passData.ComputeShader = _computeShader;
            passData.ViewMatrix = viewMatrix;
            passData.ProjectionMatrix = projectionMatrix;
            passData.OriginalViewMatrix = cameraData.GetViewMatrix();
            passData.OriginalProjectionMatrix = cameraData.GetProjectionMatrix();
            passData.CenterPos = centerPos;
            passData.CameraBounds = cameraBounds;
            passData.FullDensityDistance = _fullDensityDistance;
            passData.DrawDistance = _drawDistance;
            passData.TextureUpdateThreshold = _textureUpdateThreshold;
            passData.CameraVpMatrix = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
            passData.CameraPosition = cameraPos;
            passData.PositionBuffers = _infiniteGrassData.PositionBuffers;
            passData.QualityScale = _infiniteGrassData.QualityScale;
            passData.EnableLodLevels = _enableLodLevels;
            
            for (var i = 0; i < passData.PositionBuffers.Count; i++)
            {
                var posBufferHandle = renderGraph.ImportBuffer(passData.PositionBuffers[i]);
                builder.UseBuffer(posBufferHandle, AccessFlags.Write);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }

        private static void SetAllViewProjMatrices(CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
        {
            // SetViewProjectionMatrices sets the built-in cbuffer matrices
            cmd.SetViewProjectionMatrices(view, proj);

            // Also set the SRP global keyword variants that URP HLSL shaders
            // and UnityCG.cginc macros may resolve to on some platforms.
            var gpuProj = GL.GetGPUProjectionMatrix(proj, true);
            var vp = gpuProj * view;
            cmd.SetGlobalMatrix(ShaderPropertyId.ViewMatrix, view);
            cmd.SetGlobalMatrix(ShaderPropertyId.InverseViewMatrix, view.inverse);
            cmd.SetGlobalMatrix(ShaderPropertyId.ProjectionMatrix, gpuProj);
            cmd.SetGlobalMatrix(ShaderPropertyId.ViewAndProjectionMatrix, vp);
        }
        
        private static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // Override to top-down orthographic view for data texture rendering
            SetAllViewProjMatrices(cmd, data.ViewMatrix, data.ProjectionMatrix);

            // --- Height Map ---
            cmd.SetRenderTarget(data.HeightTexture, data.HeightDepthTexture);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.DrawRendererList(data.HeightMapRendererList);

            // --- Mask ---
            cmd.SetRenderTarget(data.MaskTexture);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawRendererList(data.MaskRendererList);

            // --- Color ---
            cmd.SetRenderTarget(data.ColorTexture);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawRendererList(data.ColorRendererList);

            // --- Slope ---
            cmd.SetRenderTarget(data.SlopeTexture);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.DrawRendererList(data.SlopeRendererList);

            // Restore camera render target and matrices
            cmd.SetRenderTarget(data.CameraColorTarget, data.CameraDepthTarget);
            SetAllViewProjMatrices(cmd, data.OriginalViewMatrix, data.OriginalProjectionMatrix);

            // --- Compute grass positions ---
            var kernelIndex = data.EnableLodLevels ? 0 : 1;
                
            var cs = data.ComputeShader;
            cs.SetMatrix(ShaderPropertyId.VpMatrix, data.CameraVpMatrix);
            cs.SetFloat(ShaderPropertyId.FullDensityDistance, data.FullDensityDistance);
            cs.SetVector(ShaderPropertyId.BoundsMin, data.CameraBounds.min);
            cs.SetVector(ShaderPropertyId.BoundsMax, data.CameraBounds.max);
            cs.SetVector(ShaderPropertyId.CameraPosition, data.CameraPosition);
            cs.SetVector(ShaderPropertyId.CenterPos, data.CenterPos);
            cs.SetFloat(ShaderPropertyId.DrawDistance, data.DrawDistance);
            cs.SetFloat(ShaderPropertyId.TextureUpdateThreshold, data.TextureUpdateThreshold);
            cs.SetTexture(kernelIndex, ShaderPropertyId.GrassHeightMapRT, data.HeightRTHandle);
            cs.SetTexture(kernelIndex, ShaderPropertyId.GrassMaskMapRT, data.MaskRTHandle);
                
            for (var i = 0; i < data.PositionBuffers.Count; i++)
            {
                var settings = InfiniteGrassUtility.Settings[i];
                var posBuffer = data.PositionBuffers[i];
                var spacing = settings.spacing * data.QualityScale;
                var gridSize = new Vector2Int(Mathf.CeilToInt(data.CameraBounds.size.x / spacing), Mathf.CeilToInt(data.CameraBounds.size.z / spacing));
                var gridStartIndex = new Vector2Int(Mathf.FloorToInt(data.CameraBounds.min.x / spacing), Mathf.FloorToInt(data.CameraBounds.min.z / spacing));
                    
                cmd.SetComputeFloatParam(cs, ShaderPropertyId.Spacing, spacing);
                cmd.SetComputeIntParam(cs, ShaderPropertyId.Offset, settings.offset);
                cmd.SetComputeVectorParam(cs, ShaderPropertyId.GridStartIndex, (Vector2)gridStartIndex);
                cmd.SetComputeVectorParam(cs, ShaderPropertyId.GridSize, (Vector2)gridSize);
                cmd.SetComputeVectorParam(cs, ShaderPropertyId.RemapGreenChannel, settings.greenChannel.ToVector4());
                cmd.SetComputeVectorParam(cs, ShaderPropertyId.RemapBlueChannel, settings.blueChannel.ToVector4());
                cmd.SetComputeBufferParam(cs, kernelIndex, ShaderPropertyId.GrassPositions, posBuffer);
                posBuffer.SetCounterValue(0);
                    
                cmd.DispatchCompute(cs, kernelIndex, Mathf.CeilToInt((float)gridSize.x / 8), Mathf.CeilToInt((float)gridSize.y / 8), 1);
            }
        }

        public bool IsSupported()
        {
            return _heightMapMat != null && _computeShader != null && _computeShader.IsSupported(0) && _computeShader.IsSupported(1);
        }

        private sealed class PassData
        {
            public TextureHandle HeightTexture;
            public TextureHandle HeightDepthTexture;
            public TextureHandle MaskTexture;
            public TextureHandle ColorTexture;
            public TextureHandle SlopeTexture;

            public RTHandle HeightRTHandle;
            public RTHandle MaskRTHandle;
                
            public TextureHandle CameraColorTarget;
            public TextureHandle CameraDepthTarget;

            public RendererListHandle HeightMapRendererList;
            public RendererListHandle MaskRendererList;
            public RendererListHandle ColorRendererList;
            public RendererListHandle SlopeRendererList;
                
            public ComputeShader ComputeShader;

            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ProjectionMatrix;
            public Matrix4x4 OriginalViewMatrix;
            public Matrix4x4 OriginalProjectionMatrix;
            
            public Vector2 CenterPos;
            public Bounds CameraBounds;
            public float FullDensityDistance;
            public float DrawDistance;
            public float TextureUpdateThreshold;
            public Matrix4x4 CameraVpMatrix;
            public Vector3 CameraPosition;
            public float QualityScale;
            public bool EnableLodLevels;

            public List<GraphicsBuffer> PositionBuffers;
        }

        private static class ShaderPropertyId
        {
            public static readonly int ViewMatrix = Shader.PropertyToID("unity_MatrixV");
            public static readonly int InverseViewMatrix = Shader.PropertyToID("unity_MatrixInvV");
            public static readonly int ProjectionMatrix = Shader.PropertyToID("unity_MatrixP");
            public static readonly int ViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixVP");
            
            // Per buffer properties
            public static readonly int Spacing = Shader.PropertyToID("_Spacing");
            public static readonly int Offset = Shader.PropertyToID("_Offset");
            public static readonly int GridStartIndex = Shader.PropertyToID("_GridStartIndex");
            public static readonly int GridSize = Shader.PropertyToID("_GridSize");
            public static readonly int RemapGreenChannel = Shader.PropertyToID("_RemapGreenChannel");
            public static readonly int RemapBlueChannel = Shader.PropertyToID("_RemapBlueChannel");
            public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
            
            // Global properties
            public static readonly int GrassCenterPos = Shader.PropertyToID("_GrassCenterPos");
            public static readonly int GrassDrawDistance = Shader.PropertyToID("_GrassDrawDistance");
            public static readonly int GrassTextureUpdateThreshold = Shader.PropertyToID("_GrassTextureUpdateThreshold");
            
            public static readonly int DrawDistance = Shader.PropertyToID("_DrawDistance");
            public static readonly int TextureUpdateThreshold = Shader.PropertyToID("_TextureUpdateThreshold");
            public static readonly int FullDensityDistance = Shader.PropertyToID("_FullDensityDistance");
            public static readonly int CameraPosition = Shader.PropertyToID("_CameraPosition");
            public static readonly int CenterPos = Shader.PropertyToID("_CenterPos");
            public static readonly int BoundsYMinMax = Shader.PropertyToID("_BoundsYMinMax");
            public static readonly int BoundsMin = Shader.PropertyToID("_BoundsMin");
            public static readonly int BoundsMax = Shader.PropertyToID("_BoundsMax");
            public static readonly int VpMatrix = Shader.PropertyToID("_VPMatrix");
            public static readonly int GrassHeightMapRT = Shader.PropertyToID("_GrassHeightMapRT");
            public static readonly int GrassMaskMapRT = Shader.PropertyToID("_GrassMaskMapRT");
        }
    }
}