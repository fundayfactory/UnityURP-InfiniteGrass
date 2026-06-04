using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace InfiniteGrass
{
    [Serializable]
    public sealed class InfiniteGrassDataRendererFeature : ScriptableRendererFeature
    {
        [Header("Pass"), SerializeField]
        private RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPrePasses;
        
        [Header("Compute"), SerializeField] 
        private ComputeShader computeShader;
    
        [Header("Height"), SerializeField] 
        private LayerMask heightMapLayer;
        [SerializeField] 
        private Material heightMapMat;

        [Header("Draw"), SerializeField, Min(1f), Tooltip("Draw distance")]
        private float drawDistance = 300;
        [SerializeField, Min(1f), Tooltip("After this distance, we start removing some blades of grass in sake of performance (Requires: Enable Lod levels to be true)")]
        private float fullDensityDistance = 50;
        
        [Header("Update Distance"), SerializeField, Tooltip("The distance that the camera should move before we update the \"Data Textures\"")]
        private float textureUpdateThreshold = 10f;

        [Header("Quality"), Tooltip("Scales spacing, so High = 1.0, Medium = 2.0, Low = 4.0 and Very Low = 8.0")]
        public Quality quality = Quality.High;
        [Tooltip("Quality level sets texture sizes used internally")]
        public Precision precision = Precision.High;
        [Tooltip("True, if it should draw less grass the further away from the camera, thereby increasing performance")]
        public bool enableLodLevels = true;
        
        public enum Precision { Low, Medium, High }
        public enum Quality { VeryLow, Low, Medium, High }
        
        private Pass _pass;

        public override void Create()
        {
            var textureSize = GetTextureSize();
            var qualityScale = GetQualityScale();
            
            _pass = new Pass(heightMapLayer, heightMapMat, computeShader, fullDensityDistance, drawDistance, textureUpdateThreshold, enableLodLevels, textureSize, qualityScale)
            {
                renderPassEvent = passEvent
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) 
                _pass.Dispose();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass.IsSupported() && (!TryGetVolumeComponent(out var component) || !component.enabled.overrideState || component.enabled.value))
                renderer.EnqueuePass(_pass);
        }

        private int GetTextureSize()
        {
            if (precision == Precision.Low)
                return 512;
            if (precision == Precision.Medium)
                return 1024;

            return 2048;
        }

        private float GetQualityScale()
        {
            if (quality == Quality.VeryLow)
                return 8f;
            if (quality == Quality.Low)
                return 4f;
            if (quality == Quality.Medium)
                return 2f;
                
            return 1f;
        }

        private static bool TryGetVolumeComponent(out InfiniteGrassVolumeComponent component)
        {
            component = null;
            var stack = VolumeManager.instance?.stack;
            
            if (stack == null)
                return false;
            
            component = stack.GetComponent<InfiniteGrassVolumeComponent>();
            return component != null;
        }

        private sealed class Pass : ScriptableRenderPass
        {
            private static readonly List<ShaderTagId> MaskShaderTag = new() { new ShaderTagId("GrassMask") };
            private static readonly List<ShaderTagId> ColorShaderTag = new() { new ShaderTagId("GrassColor") };
            private static readonly List<ShaderTagId> SlopeShaderTag = new() { new ShaderTagId("GrassSlope") };

            private readonly LayerMask _heightMapLayer;
            private readonly Material _heightMapMat;
            private readonly ComputeShader _computeShader;
            private readonly float _fullDensityDistance;
            private readonly float _drawDistance;
            private readonly float _textureUpdateThreshold;
            private readonly bool _enableLodLevels;
            private readonly int _textureSize;
            private readonly float _qualityScale;
            private readonly List<ShaderTagId> _shaderTagsList = new();
            private readonly List<ComputeBuffer> _positions = new();

            private RTHandle _heightRT;
            private RTHandle _heightDepthRT;
            private RTHandle _maskRT;
            private RTHandle _colorRT;
            private RTHandle _slopeRT;
            private MaterialPropertyBlock _propertyBlock;
            
            public Pass(LayerMask heightMapLayer, Material heightMapMat, ComputeShader computeShader, float fullDensityDistance, float drawDistance, float textureUpdateThreshold, bool enableLodLevels, int textureSize, float qualityScale)
            {
                _heightMapLayer = heightMapLayer;
                _computeShader = computeShader;
                _fullDensityDistance = fullDensityDistance;
                _drawDistance = drawDistance;
                _textureUpdateThreshold = textureUpdateThreshold;
                _enableLodLevels = enableLodLevels;
                _textureSize = textureSize;
                _qualityScale = qualityScale;
                _heightMapMat = heightMapMat;

                _shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
                _shaderTagsList.Add(new ShaderTagId("UniversalForward"));
                _shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));
                
                _propertyBlock = new MaterialPropertyBlock();
            }
        
            public void Dispose()
            {
                _heightRT?.Release();
                _heightDepthRT?.Release();
                _maskRT?.Release();
                _colorRT?.Release();
                _slopeRT?.Release();

                for (var i = 0; i < _positions.Count; i++)
                {
                    _positions[i]?.Release();
                }
                
                _positions.Clear();
            }
        
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
            {
                var cameraData = frameContext.Get<UniversalCameraData>();
                var mainCamera = Camera.main;

                if (mainCamera == null)
                    mainCamera = cameraData.camera;
            
                if (InfiniteGrassUtility.ArgsBuffers.Count == 0 || mainCamera == null)
                    return;
            
                EnsureRTHandles();
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

                if (_positions.Count != InfiniteGrassUtility.Buffers.Count)
                {
                    for (var i = 0; i < _positions.Count; i++)
                    {
                        _positions[i]?.Release();
                    }

                    _positions.Clear();

                    for (var i = 0; i < InfiniteGrassUtility.Buffers.Count; i++)
                    {
                        _positions.Add(new ComputeBuffer(1000 * InfiniteGrassUtility.Settings[i].maxBufferCount, sizeof(float) * 3, ComputeBufferType.Append));    
                    }
                }
                else
                {
                    for (var i = 0; i < _positions.Count; i++)
                    {
                        var maxCount = 1000 * InfiniteGrassUtility.Settings[i].maxBufferCount;
                        
                        if (_positions[i].count == maxCount)
                            continue;
                        
                        _positions[i]?.Release();
                        _positions[i] = new ComputeBuffer(maxCount, sizeof(float) * 3, ComputeBufferType.Append);    
                    }
                }

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
                passData.HeightTexture = renderGraph.ImportTexture(_heightRT);
                passData.HeightDepthTexture = renderGraph.ImportTexture(_heightDepthRT);
                passData.MaskTexture = renderGraph.ImportTexture(_maskRT);
                passData.ColorTexture = renderGraph.ImportTexture(_colorRT);
                passData.SlopeTexture = renderGraph.ImportTexture(_slopeRT);

                builder.UseTexture(passData.HeightTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.HeightDepthTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.MaskTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SlopeTexture, AccessFlags.ReadWrite);
                
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

                passData.HeightRTHandle = _heightRT;
                passData.MaskRTHandle = _maskRT;
                    
                passData.ComputeShader = _computeShader;
                passData.ViewMatrix = viewMatrix;
                passData.ProjectionMatrix = projectionMatrix;
                passData.OriginalViewMatrix = cameraData.GetViewMatrix();
                passData.OriginalProjectionMatrix = cameraData.GetProjectionMatrix();
                passData.PropertyBlock = _propertyBlock;
                passData.CenterPos = centerPos;
                passData.CameraBounds = cameraBounds;
                passData.FullDensityDistance = _fullDensityDistance;
                passData.DrawDistance = _drawDistance;
                passData.TextureUpdateThreshold = _textureUpdateThreshold;
                passData.CameraVpMatrix = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                passData.CameraPosition = cameraPos;
                passData.PositionBuffers = _positions;
                passData.QualityScale = _qualityScale;
                passData.EnableLodLevels = _enableLodLevels;

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        
            private void EnsureRTHandles()
            {
                #pragma warning disable 0618
                RenderingUtils.ReAllocateIfNeeded(ref _heightRT, new RenderTextureDescriptor(_textureSize, _textureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Bilinear, name: "_GrassHeight");
                RenderingUtils.ReAllocateIfNeeded(ref _heightDepthRT, new RenderTextureDescriptor(_textureSize, _textureSize, RenderTextureFormat.RFloat, 32), FilterMode.Bilinear, name: "_GrassHeightDepth");

                var halfTextureSize = _textureSize / 2;
                RenderingUtils.ReAllocateIfNeeded(ref _maskRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.RFloat, 0), FilterMode.Bilinear, name: "_GrassMask");
                RenderingUtils.ReAllocateIfNeeded(ref _colorRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Bilinear, name: "_GrassColor");
                RenderingUtils.ReAllocateIfNeeded(ref _slopeRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Bilinear, name: "_GrassSlope");
                #pragma warning restore 0618
            }

            private static void SetAllViewProjMatrices(CommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj)
            {
                // SetViewProjectionMatrices sets the built-in cbuffer matrices
                // (unity_MatrixV, unity_MatrixP, unity_MatrixVP etc.)
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
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                cmd.DrawRendererList(data.MaskRendererList);

                // --- Color ---
                cmd.SetRenderTarget(data.ColorTexture);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                cmd.DrawRendererList(data.ColorRendererList);

                // --- Slope ---
                cmd.SetRenderTarget(data.SlopeTexture);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                cmd.DrawRendererList(data.SlopeRendererList);

                cmd.SetGlobalTexture(ShaderPropertyId.GrassColorRT, data.ColorTexture);
                cmd.SetGlobalTexture(ShaderPropertyId.GrassSlopeRT, data.SlopeTexture);

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
                    
                    cmd.SetComputeVectorParam(cs, ShaderPropertyId.GridStartIndex, (Vector2)gridStartIndex);
                    cmd.SetComputeVectorParam(cs, ShaderPropertyId.GridSize, (Vector2)gridSize);
                    cmd.SetComputeFloatParam(cs, ShaderPropertyId.Spacing, spacing);
                    cmd.SetComputeVectorParam(cs, ShaderPropertyId.RemapGreenChannel, settings.greenChannel.ToVector4());
                    cmd.SetComputeVectorParam(cs, ShaderPropertyId.RemapBlueChannel, settings.blueChannel.ToVector4());
                    cmd.SetComputeIntParam(cs, ShaderPropertyId.Offset, settings.offset);
                    cmd.SetComputeBufferParam(cs, kernelIndex, ShaderPropertyId.GrassPositions, posBuffer);
                    posBuffer.SetCounterValue(0);
                    
                    cmd.DispatchCompute(cs, kernelIndex, Mathf.CeilToInt((float)gridSize.x / 8), Mathf.CeilToInt((float)gridSize.y / 8), 1);
                }
                
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
                    var settings = InfiniteGrassUtility.Settings[i];
                    var posBuffer = data.PositionBuffers[i];
                    cmd.SetGlobalBuffer(ShaderPropertyId.GrassPositions, posBuffer);
                    cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.ArgsBuffers[i], 4);
                    
                    if (settings.previewVisibleGrassCount)
                        cmd.CopyCounterValue(posBuffer, InfiniteGrassUtility.Buffers[i], 0);
                    
                    cmd.DrawMeshInstancedIndirect(InfiniteGrassUtility.Meshes[i], 0, InfiniteGrassUtility.Materials[i], -1, InfiniteGrassUtility.ArgsBuffers[i], 0, mpb);
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

                public MaterialPropertyBlock PropertyBlock;
                public Vector2 CenterPos;
                public Bounds CameraBounds;
                public float FullDensityDistance;
                public float DrawDistance;
                public float TextureUpdateThreshold;
                public Matrix4x4 CameraVpMatrix;
                public Vector3 CameraPosition;
                public float QualityScale;
                public bool EnableLodLevels;

                public List<ComputeBuffer> PositionBuffers;
            }

            private static class ShaderPropertyId
            {
                public static readonly int ViewMatrix = Shader.PropertyToID("unity_MatrixV");
                public static readonly int InverseViewMatrix = Shader.PropertyToID("unity_MatrixInvV");
                public static readonly int ProjectionMatrix = Shader.PropertyToID("unity_MatrixP");
                public static readonly int ViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixVP");
            
                public static readonly int BoundsYMinMax = Shader.PropertyToID("_BoundsYMinMax");
                public static readonly int GrassPositions = Shader.PropertyToID("_GrassPositions");
                public static readonly int GrassMaskMapRT = Shader.PropertyToID("_GrassMaskMapRT");
                public static readonly int GrassHeightMapRT = Shader.PropertyToID("_GrassHeightMapRT");
                public static readonly int GridSize = Shader.PropertyToID("_GridSize");
                public static readonly int GridStartIndex = Shader.PropertyToID("_GridStartIndex");
                public static readonly int Spacing = Shader.PropertyToID("_Spacing");
                public static readonly int Offset = Shader.PropertyToID("_Offset");
                public static readonly int RemapGreenChannel = Shader.PropertyToID("_RemapGreenChannel");
                public static readonly int RemapBlueChannel = Shader.PropertyToID("_RemapBlueChannel");
                public static readonly int TextureUpdateThreshold = Shader.PropertyToID("_TextureUpdateThreshold");
                public static readonly int DrawDistance = Shader.PropertyToID("_DrawDistance");
                public static readonly int CenterPos = Shader.PropertyToID("_CenterPos");
                public static readonly int CameraPosition = Shader.PropertyToID("_CameraPosition");
                public static readonly int BoundsMax = Shader.PropertyToID("_BoundsMax");
                public static readonly int BoundsMin = Shader.PropertyToID("_BoundsMin");
                public static readonly int FullDensityDistance = Shader.PropertyToID("_FullDensityDistance");
                public static readonly int VpMatrix = Shader.PropertyToID("_VPMatrix");
                public static readonly int GrassSlopeRT = Shader.PropertyToID("_GrassSlopeRT");
                public static readonly int GrassColorRT = Shader.PropertyToID("_GrassColorRT");
            
                public static readonly int GrassCenterPos = Shader.PropertyToID("_GrassCenterPos");
                public static readonly int GrassDrawDistance = Shader.PropertyToID("_GrassDrawDistance");
                public static readonly int GrassTextureUpdateThreshold = Shader.PropertyToID("_GrassTextureUpdateThreshold");
                
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
}