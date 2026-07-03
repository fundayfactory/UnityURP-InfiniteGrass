using System.Collections.Generic;
using UnityEngine;

namespace InfiniteGrass
{
    public static class InfiniteGrassUtility
    {
        internal static readonly List<EntityId> EntityIds = new();
        internal static readonly List<GraphicsBuffer> Buffers = new();
        internal static readonly List<GraphicsBuffer> ArgsBuffers = new();
        internal static readonly List<InfiniteGrassSettings> Settings = new();
        internal static readonly List<Mesh> Meshes = new();
        internal static readonly List<Material> Materials = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitialize()
        {
            ReleaseAll();
        }
        
        public static Bounds CalculateCameraBounds(Camera camera, float drawDistance)
        {
            var nTopLeft = camera.ViewportToWorldPoint(new Vector3(0f, 1f, camera.nearClipPlane));
            var nTopRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, camera.nearClipPlane));
            var nBottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, camera.nearClipPlane));
            var nBottomRight = camera.ViewportToWorldPoint(new Vector3(1f, 0f, camera.nearClipPlane));

            var fTopLeft = camera.ViewportToWorldPoint(new Vector3(0f, 1f, drawDistance));
            var fTopRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, drawDistance));
            var fBottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, drawDistance));
            var fBottomRight = camera.ViewportToWorldPoint(new Vector3(1f, 0f, drawDistance));
            
            var startX = Max(fTopLeft.x, fTopRight.x, nTopLeft.x, nTopRight.x, fBottomLeft.x, fBottomRight.x, nBottomLeft.x, nBottomRight.x);
            var endX = Min(fTopLeft.x, fTopRight.x, nTopLeft.x, nTopRight.x, fBottomLeft.x, fBottomRight.x, nBottomLeft.x, nBottomRight.x);

            var startY = Max(fTopLeft.y, fTopRight.y, nTopLeft.y, nTopRight.y, fBottomLeft.y, fBottomRight.y, nBottomLeft.y, nBottomRight.y);
            var endY = Min(fTopLeft.y, fTopRight.y, nTopLeft.y, nTopRight.y, fBottomLeft.y, fBottomRight.y, nBottomLeft.y, nBottomRight.y);

            var startZ = Max(fTopLeft.z, fTopRight.z, nTopLeft.z, nTopRight.z, fBottomLeft.z, fBottomRight.z, nBottomLeft.z, nBottomRight.z);
            var endZ = Min(fTopLeft.z, fTopRight.z, nTopLeft.z, nTopRight.z, fBottomLeft.z, fBottomRight.z, nBottomLeft.z, nBottomRight.z);

            var center = new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2);
            var size = new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ));

            var bounds = new Bounds(center, size);
            bounds.Expand(1);
            return bounds;
        }

        private static float Max(float f0, float f1, float f2, float f3, float f4, float f5, float f6, float f7)
        {
            var value = f0;

            if (f1 > value)
                value = f1;
            if (f2 > value)
                value = f2;
            if (f3 > value)
                value = f3;
            if (f4 > value)
                value = f4;
            if (f5 > value)
                value = f5;
            if (f6 > value)
                value = f6;
            if (f7 > value)
                value = f7;

            return value;
        }

        private static float Min(float f0, float f1, float f2, float f3, float f4, float f5, float f6, float f7)
        {
            var value = f0;

            if (f1 < value)
                value = f1;
            if (f2 < value)
                value = f2;
            if (f3 < value)
                value = f3;
            if (f4 < value)
                value = f4;
            if (f5 < value)
                value = f5;
            if (f6 < value)
                value = f6;
            if (f7 < value)
                value = f7;

            return value;
        }

        public static void Release(EntityId entityId)
        {
            var index = EntityIds.IndexOf(entityId);

            if (index == -1)
                return;
            
            Buffers[index]?.Release();
            ArgsBuffers[index]?.Release();

            EntityIds.RemoveAt(index);
            Buffers.RemoveAt(index);
            ArgsBuffers.RemoveAt(index);
            Settings.RemoveAt(index);
            Meshes.RemoveAt(index);
            Materials.RemoveAt(index);
        }

        public static void ReleaseAll()
        {
            for (var i = 0; i < EntityIds.Count; i++)
            {
                Buffers[i]?.Release();
                ArgsBuffers[i]?.Release();
            }
            
            EntityIds.Clear();
            Buffers.Clear();
            ArgsBuffers.Clear();
            Settings.Clear();
            Meshes.Clear();
            Materials.Clear();
        }

        public static void Reserve(EntityId entityId, GraphicsBuffer buffer, GraphicsBuffer args, InfiniteGrassSettings settings, Mesh mesh, Material material)
        {
            EntityIds.Add(entityId);
            Buffers.Add(buffer);
            ArgsBuffers.Add(args);
            Settings.Add(settings);
            Meshes.Add(mesh);
            Materials.Add(material);
        }

        public static void GetSettings(List<InfiniteGrassSettings> list)
        {
            list.Clear();
            list.AddRange(Settings);
        }
    }
}
