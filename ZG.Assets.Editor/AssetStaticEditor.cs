using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace ZG
{
    public class AssetStaticEditor : EditorWindow
    {
        [MenuItem("Window/ZG/Asset Static Editor")]
        public static void ShowWindow()
        {
            GetWindow<AssetStaticEditor>();
        }

        private bool __isDirty;
        private HashSet<Object> __modelImporters = new HashSet<Object>();
        
        private ReorderableList __staticModelImporters;
        private ReorderableList __dynamicModelImporters;

        void OnGUI()
        {
            if (GUILayout.Button("Generate Model Importer List"))
            {
                var staticModelImporters = new List<ModelImporter>();
                var dynamicModelImporters = new List<ModelImporter>();

                MeshFilter[] meshFilters;
                string[] assetPaths;
                var assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
                string assetBundleName;
                GameObject gameObject;
                Mesh mesh;
                int numAssetBundleNames = assetBundleNames.Length;
                for(int i = 0; i < numAssetBundleNames; ++i)
                {
                    assetBundleName = assetBundleNames[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Generate Model Importer List..", assetBundleName,
                            (float)i / numAssetBundleNames))
                        break;
                    
                    assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                    foreach (var assetPath in assetPaths)
                    {
                        gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (gameObject == null)
                            continue;

                        meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(true);
                        foreach (var meshFilter in meshFilters)
                        {
                            mesh = meshFilter.sharedMesh;
                            if (AssetImporter.GetAtPath(
                                    AssetDatabase.GetAssetPath(mesh)) is ModelImporter modelImporter)
                            {
                                if(meshFilter.gameObject.isStatic)
                                    staticModelImporters.Add(modelImporter);
                                else
                                    dynamicModelImporters.Add(modelImporter);
                            }
                        }
                    }
                }
                
                EditorUtility.ClearProgressBar();
                
                __staticModelImporters = new ReorderableList(
                    staticModelImporters,  
                    typeof(ModelImporter), 
                    true, 
                    true, 
                    false, 
                    false);

                __staticModelImporters.multiSelect = true;
                
                __staticModelImporters.headerHeight = EditorGUIUtility.singleLineHeight;
                __staticModelImporters.elementHeight = EditorGUIUtility.singleLineHeight;
                __staticModelImporters.drawHeaderCallback += rect => EditorGUI.LabelField(rect, "Static Model Importers");
                
                __staticModelImporters.drawElementCallback += (
                    Rect rect,
                    int index,
                    bool isActive,
                    bool isFocused) =>
                {
                    var modelImporter = __staticModelImporters.list[index] as ModelImporter;
                    EditorGUI.LabelField(rect, modelImporter.assetPath);

                    if (isActive)
                        __isDirty = __modelImporters.Add(modelImporter) | __isDirty;
                    else
                        __isDirty = __modelImporters.Remove(modelImporter) | __isDirty;
                };
                
                __dynamicModelImporters = new ReorderableList(
                    dynamicModelImporters,  
                    typeof(ModelImporter), 
                    true, 
                    true, 
                    false, 
                    false);
                
                __dynamicModelImporters.multiSelect = true;

                __dynamicModelImporters.headerHeight = EditorGUIUtility.singleLineHeight;
                __dynamicModelImporters.elementHeight = EditorGUIUtility.singleLineHeight;
                __dynamicModelImporters.drawHeaderCallback += rect => EditorGUI.LabelField(rect, "Dynamic Model Importers");

                __dynamicModelImporters.drawElementCallback += (
                    Rect rect,
                    int index,
                    bool isActive,
                    bool isFocused) =>
                {
                    var modelImporter = __dynamicModelImporters.list[index] as ModelImporter;
                    EditorGUI.LabelField(rect, modelImporter.assetPath);

                    if (isActive)
                        __isDirty = __modelImporters.Add(modelImporter) | __isDirty;
                    else
                        __isDirty = __modelImporters.Remove(modelImporter) | __isDirty;
                };
            }
            
            __staticModelImporters?.DoLayoutList();
            __dynamicModelImporters?.DoLayoutList();

            if (__isDirty)
            {
                var objects = new Object[__modelImporters.Count];
                __modelImporters.CopyTo(objects, 0);
                Selection.objects = objects;
            }
        }
    }
}