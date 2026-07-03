using UnityEngine;

namespace InfiniteGrass
{
    [ExecuteAlways]
    public sealed class InfiniteGrassRenderer : MonoBehaviour
    {
        [Header("Material")]
        public Material material;
        
        [Header("Mesh"), SerializeField]
        private Mesh mesh;
        [SerializeField, Min(1)]
        private int grassMeshSubdivision = 5;
        
        [Header("Settings")]
        public InfiniteGrassSettings settings;
        
        private Mesh _generatedMesh;
        private int _oldSubdivision = -1;

        private uint[] _argsArr;

        private void OnDestroy()
        {
            InfiniteGrassUtility.Release(GetEntityId());
        }

        private void OnDisable()
        {
            InfiniteGrassUtility.Release(GetEntityId());
        }

        private void LateUpdate()
        {
            InfiniteGrassUtility.Release(GetEntityId());

            if (material == null) 
                return;

            var cachedMesh = GetOrCreateMesh();
            
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            
            _argsArr ??= new uint[5];
            _argsArr[0] = cachedMesh.GetIndexCount(0);
            _argsArr[1] = (uint)(settings.maxBufferCount * 1000);
            _argsArr[2] = cachedMesh.GetIndexStart(0);
            _argsArr[3] = cachedMesh.GetBaseVertex(0);
            _argsArr[4] = 0;
            
            args.SetData(_argsArr);
            
            InfiniteGrassUtility.Reserve(GetEntityId(), buffer, args, settings, cachedMesh, material);
        }
        
        private Mesh GetOrCreateMesh()
        {
            if (mesh != null)
                return mesh;
            if (_generatedMesh != null && _oldSubdivision == grassMeshSubdivision)
                return _generatedMesh;
        
            _generatedMesh = new Mesh();

            var vertices = new Vector3[3 + 4 * grassMeshSubdivision];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[(1 + 2 * grassMeshSubdivision) * 3];

            for (var i = 0; i < grassMeshSubdivision; i++)
            {
                var y1 = (float)i / (grassMeshSubdivision + 1);
                var y2 = (float)(i + 1) / (grassMeshSubdivision + 1);

                var bottomLeft = new Vector3(-0.25f, y1);
                var bottomRight = new Vector3(0.25f, y1);
                var topLeft = new Vector3(-0.25f, y2);
                var topRight = new Vector3(0.25f, y2);

                var bottomLeftIndex = i * 4;
                var bottomRightIndex = i * 4 + 1;
                var topLeftIndex = i * 4 + 2;
                var topRightIndex = i * 4 + 3;

                vertices[bottomLeftIndex] = bottomLeft;
                vertices[bottomRightIndex] = bottomRight;
                vertices[topLeftIndex] = topLeft;
                vertices[topRightIndex] = topRight;
                
                triangles[i * 6] = bottomLeftIndex;
                triangles[i * 6 + 1] = topRightIndex;
                triangles[i * 6 + 2] = bottomRightIndex;

                triangles[i * 6 + 3] = bottomLeftIndex;
                triangles[i * 6 + 4] = topLeftIndex;
                triangles[i * 6 + 5] = topRightIndex;
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = new Vector3(0f, 0f, 1f);
            }
            
            vertices[grassMeshSubdivision * 4] = new Vector3(-0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));
            vertices[grassMeshSubdivision * 4 + 1] = new Vector3(0, 1);
            vertices[grassMeshSubdivision * 4 + 2] = new Vector3(0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));

            triangles[grassMeshSubdivision * 6] = grassMeshSubdivision * 4;
            triangles[grassMeshSubdivision * 6 + 1] = grassMeshSubdivision * 4 + 1;
            triangles[grassMeshSubdivision * 6 + 2] = grassMeshSubdivision * 4 + 2;

            _generatedMesh.SetVertices(vertices);
            _generatedMesh.SetNormals(normals);
            _generatedMesh.SetTriangles(triangles, 0);

            _oldSubdivision = grassMeshSubdivision;
            return _generatedMesh;
        }
    }
}