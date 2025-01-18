#if ENABLE_PAD
#define USE_PAD
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

#if USE_PAD
using Google.Android.AppBundle.Editor;
using Google.Android.AppBundle.Editor.AssetPacks;
using Google.Android.AppBundle.Editor.Internal;


namespace ZG
{
    public class GooglePlayAssetPackEditor : EditorWindow
    {
        private AssetPackConfig __assetPackConfig;
        private ReorderableList __reorderableList;

        [MenuItem("Window/ZG/Google Play Asset Pack Editor")]
        public static void GetWindow()
        {
            GetWindow<GooglePlayAssetPackEditor>();
        }

        private void OnGUI()
        {
            bool isNeedToSave = false, isLoaded = GUILayout.Button("Load");
            if(isLoaded)
            {
                __assetPackConfig = AssetPackConfigSerializer.LoadConfig(EditorUtility.OpenFilePanel(
                    "Load Asset Pack Config", 
                    string.Empty, 
                    string.Empty));
                if (__assetPackConfig != null)
                {
                    __reorderableList = null;

                    isNeedToSave = true;
                }
            }

            if(__assetPackConfig == null)
                __assetPackConfig = AssetPackConfigSerializer.LoadConfig();

            if (__assetPackConfig == null)
                __assetPackConfig = new AssetPackConfig();

            if (__reorderableList == null)
            {
                var assetPacks = __assetPackConfig.AssetPacks;
                __reorderableList = new ReorderableList(
                    assetPacks == null ? new List<KeyValuePair<string, AssetPack>>() : new List<KeyValuePair<string, AssetPack>>(assetPacks), 
                    typeof(KeyValuePair<string, AssetPack>));
            }

            EditorGUI.BeginChangeCheck();

            __assetPackConfig.SplitBaseModuleAssets = EditorGUILayout.Toggle("Split Base Module Assets", __assetPackConfig.SplitBaseModuleAssets);

            isNeedToSave |= EditorGUI.EndChangeCheck();

            //__list.elementHeight = EditorGUIUtility.singleLineHeight * 3.0f;

            __reorderableList.drawElementCallback = (rect, index, active, focused) =>
            {
                var pair = (KeyValuePair<string, AssetPack>)__reorderableList.list[index];

                rect.width /= 3.0f;

                EditorGUI.BeginChangeCheck();

                string name = GUI.TextField(rect, pair.Key);

                rect.x += rect.width;

                var assetPack = pair.Value;

                if (assetPack == null)
                    assetPack = new AssetPack();

                assetPack.DeliveryMode = (AssetPackDeliveryMode)EditorGUI.EnumPopup(rect, assetPack.DeliveryMode);

                bool isDirty = EditorGUI.EndChangeCheck();

                rect.x += rect.width;

                string assetPackDirectoryPath = assetPack.AssetPackDirectoryPath;
                if (GUI.Button(rect, assetPackDirectoryPath))
                {
                    string path = EditorUtility.OpenFolderPanel("Google Play Asset Pack", assetPackDirectoryPath, string.Empty);
                    if (!string.IsNullOrEmpty(path))
                    {
                        assetPack.AssetPackDirectoryPath = path;

                        isDirty = true;
                    }
                }

                if (isDirty)
                {
                    __reorderableList.list[index] = new KeyValuePair<string, AssetPack>(name, assetPack);

                    __reorderableList.onChangedCallback(__reorderableList);
                }
            };

            __reorderableList.onChangedCallback = (list) =>
            {
                __assetPackConfig.AssetPacks.Clear();

                foreach (var pair in (IEnumerable<KeyValuePair<string, AssetPack>>)list.list)
                {
                    try
                    {
                        __assetPackConfig.AddAssetsFolder(pair.Key, pair.Value.AssetPackDirectoryPath, pair.Value.DeliveryMode);

                        isNeedToSave = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e.InnerException ?? e);
                    }
                }
            };

            __reorderableList.DoLayoutList();

            if(isNeedToSave)
                AssetPackConfigSerializer.SaveConfig(__assetPackConfig);

            if(GUILayout.Button("Save"))
            {
                AssetPackConfigSerializer.SaveConfig(
                    __assetPackConfig, 
                    EditorUtility.SaveFilePanel("Save Asset Pack Config", string.Empty, "AssetPackConfig", string.Empty));
            }

            if (GUILayout.Button("Build"))
            {
                AppBundlePublisher.Build();
            }
        }
    }
}
#endif