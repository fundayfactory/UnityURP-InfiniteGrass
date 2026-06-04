using System;
using UnityEngine;

namespace InfiniteGrass
{
    [Serializable]
    public sealed class InfiniteGrassSettings
    {
        [Header("Density"), Min(0.1f), Tooltip("Spacing between blades")]
        public float spacing = 0.1f;

        [Header("Offset")]
        public sbyte offset;
        
        [Header("Max Buffer Count (Thousands)"), Min(1), Tooltip("The number we gonna use to initialise the positions buffer.\nDon't make it too high cause that gonna impact performance, usually 2000 - 3000 should be enough unless you are using a crazy spacing.\nAlso don't make it too low cause it's gonna negatively impact the performance.")]
        public int maxBufferCount = 2000;
        
        [Header("Channels")]
        public RemapChannel greenChannel = new(1f, 1f, 1f, 1f);
        public RemapChannel blueChannel = new(0f, 0f, 1f, 1f);
        
        [Header("Debug"), Tooltip("Enabling this will make the performance drop a lot")]
        public bool previewVisibleGrassCount;

        [Serializable]
        public struct RemapChannel
        {
            [Range(0f, 1f)]
            public float minValue;
            [Range(0f, 1f)]
            public float maxValue;
            
            [Range(0f, 1f)]
            public float remapMinValue;
            [Range(0f, 1f)]
            public float remapMaxValue;

            public RemapChannel(float minValue, float maxValue, float remapMinValue, float remapMaxValue)
            {
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.remapMinValue = remapMinValue;
                this.remapMaxValue = remapMaxValue;
            }

            public Vector4 ToVector4()
            {
                var v = new Vector4(minValue, maxValue, remapMinValue, remapMaxValue);

                if (maxValue < minValue)
                {
                    v.x = maxValue;
                    v.y = minValue;
                }
                
                return v;
            }
        }
    }
}