using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InfiniteGrass
{
    public sealed class InfiniteGrassData : IDisposable
    {
        public readonly List<GraphicsBuffer> PositionBuffers;
        public readonly float QualityScale;
        
        private readonly int _textureSize;
        
        public RTHandle HeightRT;
        public RTHandle HeightDepthRT;
        public RTHandle MaskRT;
        public RTHandle ColorRT;
        public RTHandle SlopeRT;
        
        private bool _disposed;

        public InfiniteGrassData(int textureSize, float qualityScale)
        {
            PositionBuffers = new List<GraphicsBuffer>();
            QualityScale = qualityScale;
            
            _textureSize = textureSize;
        }

        public void Update()
        {
            if (PositionBuffers.Count != InfiniteGrassUtility.ArgsBuffers.Count)
            {
                for (var i = 0; i < PositionBuffers.Count; i++)
                {
                    PositionBuffers[i]?.Release();
                }

                PositionBuffers.Clear();

                for (var i = 0; i < InfiniteGrassUtility.ArgsBuffers.Count; i++)
                {
                    PositionBuffers.Add(new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append, 1000 * InfiniteGrassUtility.Settings[i].maxBufferCount, sizeof(float) * 3));    
                }
            }
            else
            {
                for (var i = 0; i < PositionBuffers.Count; i++)
                {
                    var maxCount = 1000 * InfiniteGrassUtility.Settings[i].maxBufferCount;
                        
                    if (PositionBuffers[i].count == maxCount)
                        continue;
                        
                    PositionBuffers[i]?.Release();
                    PositionBuffers[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append, maxCount, sizeof(float) * 3);    
                }
            }
        }
                
        public void EnsureRTHandles()
        {
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref HeightRT, new RenderTextureDescriptor(_textureSize, _textureSize, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear, name: "_GrassHeight");
            RenderingUtils.ReAllocateIfNeeded(ref HeightDepthRT, new RenderTextureDescriptor(_textureSize, _textureSize, RenderTextureFormat.RHalf, 16), FilterMode.Bilinear, name: "_GrassHeightDepth");
 
            var halfTextureSize = _textureSize / 2;
            RenderingUtils.ReAllocateIfNeeded(ref MaskRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.RHalf, 0), FilterMode.Bilinear, name: "_GrassMask");
            RenderingUtils.ReAllocateIfNeeded(ref ColorRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear, name: "_GrassColor");
            RenderingUtils.ReAllocateIfNeeded(ref SlopeRT, new RenderTextureDescriptor(halfTextureSize, halfTextureSize, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear, name: "_GrassSlope");
#pragma warning restore 0618
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            
            foreach (var buffer in PositionBuffers)
            {
                buffer?.Dispose();
            }
            
            PositionBuffers.Clear();
            
            HeightRT?.Release();
            HeightDepthRT?.Release();
            MaskRT?.Release();
            ColorRT?.Release();
            SlopeRT?.Release();
        }
    }
}