using UnityEditor;
using UnityEngine;

namespace InfiniteGrass.Editor
{
    [CustomEditor(typeof(InfiniteGrassRenderer))]
    [CanEditMultipleObjects]
    public class EditorInfiniteGrassRenderer : UnityEditor.Editor
    {
        private SerializedProperty _spMaterial;
        private UnityEditor.Editor _materialEditor;

        private void OnEnable()
        {
            _spMaterial = serializedObject.FindProperty("material");
            CreateMaterialEditor(_spMaterial.objectReferenceValue as Material);
        }

        private void OnDisable()
        {
            DestroyMaterialEditor();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            if (_spMaterial.hasMultipleDifferentValues)
                DestroyMaterialEditor();
            else if (_materialEditor == null && _spMaterial.objectReferenceValue != null)
                CreateMaterialEditor(_spMaterial.objectReferenceValue as Material);
            else if (_materialEditor != null && _materialEditor.target != _spMaterial.objectReferenceValue)
                CreateMaterialEditor(_spMaterial.objectReferenceValue as Material);

            if (_materialEditor != null && _spMaterial.objectReferenceValue != null)
            {
                _materialEditor.DrawHeader();

                using (new EditorGUI.DisabledGroupScope(false))
                {
                    _materialEditor.OnInspectorGUI();
                }
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        private void CreateMaterialEditor(Material material)
        {
            if (_materialEditor != null)
            {
                DestroyImmediate(_materialEditor);
                _materialEditor = null;
            }

            if (material != null)
                _materialEditor = CreateEditor(_spMaterial.objectReferenceValue, typeof(MaterialEditor));
        }

        private void DestroyMaterialEditor()
        {
            if (_materialEditor == null)
                return;
            
            DestroyImmediate(_materialEditor);
            _materialEditor = null;
        }
    }
}