using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    [Serializable]
    public sealed class InfiniteGrassDataRendererFeature : ScriptableRendererFeature
    {
        [Header("Pass"), SerializeField]
        private RenderPassEvent forwardPassEvent = RenderPassEvent.AfterRenderingOpaques;
        
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

        private InfiniteGrassData _infiniteGrassData;
        private InfiniteGrassDataPass _infiniteGrassDataPass;
        private InfiniteGrassDepthNormalPass _infiniteGrassDepthNormalPass;
        private InfiniteGrassGBufferPass _infiniteGrassGBufferPass;
        private InfiniteGrassRenderPass _infiniteGrassRenderPass;

        public override void Create()
        {
            _infiniteGrassData = new InfiniteGrassData(GetTextureSize(), GetQualityScale());
                        
            _infiniteGrassDataPass = new InfiniteGrassDataPass(
                _infiniteGrassData,
                heightMapLayer,
                heightMapMat,
                computeShader,
                fullDensityDistance,
                drawDistance,
                textureUpdateThreshold,
                enableLodLevels)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses
            };
            
            _infiniteGrassDepthNormalPass = new InfiniteGrassDepthNormalPass(_infiniteGrassData)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses + 1
            };
            
            _infiniteGrassGBufferPass = new InfiniteGrassGBufferPass(_infiniteGrassData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer
            };
            
            _infiniteGrassRenderPass = new InfiniteGrassRenderPass(_infiniteGrassData)
            {
                renderPassEvent = forwardPassEvent
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _infiniteGrassData?.Dispose();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (TryGetVolumeComponent(out var component) && component.enabled.overrideState && !component.enabled.value)
                return;
            
            _infiniteGrassData.Update();

            if (!_infiniteGrassDataPass.IsSupported())
                return;
            
            renderer.EnqueuePass(_infiniteGrassDataPass);
            renderer.EnqueuePass(_infiniteGrassDepthNormalPass);
            renderer.EnqueuePass(_infiniteGrassGBufferPass);
            renderer.EnqueuePass(_infiniteGrassRenderPass);
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
    }
}