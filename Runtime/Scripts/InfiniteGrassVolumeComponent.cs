using System;
using UnityEngine.Rendering;

namespace InfiniteGrass
{
    [Serializable]
    [VolumeComponentMenu("Infinite Grass")]
    public sealed class InfiniteGrassVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enabled = new(true, BoolParameter.DisplayType.Checkbox);

        public bool IsActive()
        {
            return !enabled.overrideState || enabled.value;
        }
    }
}