using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

namespace ZG
{
    public class AssetEditor : EditorWindow
    {
        //public const string ROOT_GUID_KEY = "AvatarRootGUID";
        //public const string ROOT_PATH_KEY = "AvatarRootPath";
        //public const string BUILD_ASSET_BUNDLE_KEY = "ZGBuildAssetBundle";
        
        public const string BUILD_ASSET_BUNDLE_OPTIONS = "ZGBuildAssetBundleOptions";
        public const string BUILD_TARGET = "ZGBuildTarget";
        public const string ASSET_CONFIG_PATH = "ZGAssetConfigPath";

        public const string PATH = "ZGBuildAssetPath";

        public const string VERSION = "ZGBuildAssetVersion";

        //private bool __isBuildAssetBundle;
        private BuildAssetBundleOptions __buildAssetBundleOptions;
        private BuildOptions __buildOptions;
        private BuildTarget __buildTarget;

        public static bool isAppendHashToName =>
            (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) ==
            BuildAssetBundleOptions.AppendHashToAssetBundleName;

        public static BuildAssetBundleOptions buildAssetBundleOptions => (BuildAssetBundleOptions)EditorPrefs.GetInt(BUILD_ASSET_BUNDLE_OPTIONS);

        public static uint version
        {
            get
            {
                return (uint)EditorPrefs.GetInt(VERSION);
            }
            
            set
            {
                EditorPrefs.SetInt(VERSION, (int)value);
            }
        }
        
        public static AssetConfig assetConfig
        {
            get
            {
                return AssetDatabase.LoadAssetAtPath<AssetConfig>(EditorPrefs.GetString(ASSET_CONFIG_PATH));
            }

            set
            {
                EditorPrefs.SetString(ASSET_CONFIG_PATH, AssetDatabase.GetAssetPath(value));
            }
        }

        //private Transform __boneRoot;

        [MenuItem("Window/ZG/Asset Editor")]
        public static void GetWindow()
        {
            GetWindow<AssetEditor>();
        }

        [MenuItem("Assets/ZG/Assets/Remove Unused Asset Bundle Names")]
        public static void RemoveUnusedAssetBundleNames()
        {
            AssetDatabase.RemoveUnusedAssetBundleNames();
        }

        [MenuItem("Assets/ZG/Assets/Build Scene")]
        public static void BuildScene()
        {
            string[] guids = Selection.assetGUIDs;
            int numGUIDs = guids == null ? 0 : guids.Length;
            if (numGUIDs < 1)
                return;

            string path = EditorUtility.SaveFilePanel("Build Scene", EditorPrefs.GetString(PATH), numGUIDs == 1 ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guids[0])) : "Scenes", "scene");
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            string[] paths = new string[numGUIDs];
            for (int i = 0; i < numGUIDs; ++i)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
            var report = BuildPipeline.BuildPlayer(paths, path, buildTarget, BuildOptions.BuildAdditionalStreamedScenes);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                return;

            string directoryName = Path.GetDirectoryName(path);
            AssetManager assetManager = new AssetManager(Path.Combine(directoryName, Path.GetFileName(directoryName)));

            uint version = AssetEditor.version;
            assetManager.Update(isAppendHashToName ? NewHash()/*Hash128.Parse(report.summary.guid.ToString())*/ : default, Path.GetFileName(path), ref version);
            AssetEditor.version = version;

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Assets/ZG/Assets/Build Scene Uncompressed Asset Bundle")]
        public static void BuildSceneUncompressedAssetBundle()
        {
            string[] guids = Selection.assetGUIDs;
            int numGUIDs = guids == null ? 0 : guids.Length;
            if (numGUIDs < 1)
                return;

            string path = EditorUtility.SaveFilePanel("Build Scene", EditorPrefs.GetString(PATH), numGUIDs == 1 ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guids[0])) : "Scenes", "scene");
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            string[] paths = new string[numGUIDs];
            for (int i = 0; i < numGUIDs; ++i)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
            var report = BuildPipeline.BuildPlayer(paths, path, buildTarget, BuildOptions.BuildAdditionalStreamedScenes | BuildOptions.UncompressedAssetBundle);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                return;

            string directoryName = Path.GetDirectoryName(path);
            AssetManager assetManager = new AssetManager(Path.Combine(directoryName, Path.GetFileName(directoryName)));
            
            uint version = AssetEditor.version;
            assetManager.Update(isAppendHashToName ? NewHash()/*Hash128.Parse(report.summary.guid.ToString())*/ : default, Path.GetFileName(path), ref version);
            AssetEditor.version = version;

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Assets/ZG/Assets/Build Scene By Batch")]
        public static void BuildSceneByBatch()
        {
            string[] guids = Selection.assetGUIDs;
            int numGUIDs = guids == null ? 0 : guids.Length;
            if (numGUIDs < 1)
                return;

            string path = EditorUtility.OpenFolderPanel("Build Scene", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));

            AssetManager assetManager = new AssetManager(Path.Combine(path, Path.GetFileName(path)));

            bool isAppendHashToName = AssetEditor.isAppendHashToName;
            uint version = AssetEditor.version, maxVersion = version, minVersion;
            UnityEditor.Build.Reporting.BuildReport report;
            string assetPath;
            string[] paths = null;
            for (int i = 0; i < numGUIDs; ++i)
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (EditorUtility.DisplayCancelableProgressBar("Build Scene By Batch", assetPath, i * 1.0f / numGUIDs))
                    break;

                if (paths == null)
                    paths = new string[1];

                paths[0] = assetPath;

                assetPath = Path.GetFileNameWithoutExtension(assetPath) + ".scene";
                report = BuildPipeline.BuildPlayer(paths, Path.Combine(path, assetPath), buildTarget, BuildOptions.BuildAdditionalStreamedScenes);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    EditorUtility.ClearProgressBar();

                    return;
                }

                minVersion = version;

                assetManager.Update(isAppendHashToName ? NewHash()/*Hash128.Parse(report.summary.guid.ToString())*/ : default, assetPath, ref minVersion);

                maxVersion = Math.Max(maxVersion, minVersion);
            }

            AssetEditor.version = maxVersion;

            EditorUtility.ClearProgressBar();

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Assets/ZG/Assets/Build Selection")]
        public static void BuildSelection()
        {
            string[] guids = Selection.assetGUIDs;
            int numGUIDs = guids == null ? 0 : guids.Length;
            if (numGUIDs < 1)
                return;

            string path = EditorUtility.SaveFilePanel("Build Selection", EditorPrefs.GetString(PATH), numGUIDs == 1 ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guids[0])) : "Selection", string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            string fileName = Path.GetFileName(path);

            AssetBundleBuild assetBundleBuild = new AssetBundleBuild();
            assetBundleBuild.assetBundleName = fileName;
            assetBundleBuild.assetNames = new string[numGUIDs];
            for (int i = 0; i < numGUIDs; ++i)
                assetBundleBuild.assetNames[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            List<AssetBundleBuild> assetBundleBuilds = new List<AssetBundleBuild>();
            assetBundleBuilds.Add(assetBundleBuild);

            string folder = Path.GetDirectoryName(path);
            AssetBundle sourceAssetBundle = AssetBundle.LoadFromFile(Path.Combine(folder, Path.GetFileName(folder)));
            AssetBundleManifest source = sourceAssetBundle == null ? null : sourceAssetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");
            string[] assetBundleNames = source == null ? null : source.GetAllAssetBundles();

            if (assetBundleNames != null)
            {
                fileName = fileName.ToLower();
                
                bool isAppendHashToName = AssetEditor.isAppendHashToName;
                string assetName, assetPath;
                AssetBundle assetBundle;
                foreach (string assetBundleName in assetBundleNames)
                {
                    assetPath = Path.Combine(folder, assetBundleName);
                    assetName = isAppendHashToName
                        ? AssetManager.RemoveHashFromAssetName(assetBundleName)
                        : assetBundleName;
                    if (assetName == fileName)
                    {
                        File.Delete(assetPath);

                        continue;
                    }

                    assetBundle = AssetBundle.LoadFromFile(assetPath);
                    assetBundleBuild.assetNames = assetBundle == null ? null : assetBundle.GetAllAssetNames();

                    if (assetBundle != null)
                        assetBundle.Unload(true);

                    File.Delete(assetPath);

                    if (assetBundleBuild.assetNames == null || assetBundleBuild.assetNames.Length < 1)
                        continue;

                    assetBundleBuild.assetBundleName = assetName;

                    assetBundleBuilds.Add(assetBundleBuild);
                }
            }

            BuildAssetBundleOptions buildAssetBundleOptions = AssetEditor.buildAssetBundleOptions;
            BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
            AssetBundleManifest destination = BuildPipeline.BuildAssetBundles(Path.GetDirectoryName(path), assetBundleBuilds.ToArray(), buildAssetBundleOptions, buildTarget);

            uint version = AssetEditor.version;
            AssetManager.UpdateAfterBuild(
                (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                source, 
                destination, 
                folder, 
                ref version);
            AssetEditor.version = version;

            if (sourceAssetBundle != null)
                sourceAssetBundle.Unload(true);

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Assets/ZG/Assets/Build Selection By Batch")]
        public static void BuildSelectionByBatch()
        {
            string[] guids = Selection.assetGUIDs;
            int numGUIDs = guids == null ? 0 : guids.Length;
            if (numGUIDs < 1)
                return;

            string path = EditorUtility.OpenFolderPanel("Build Selection", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            string assetPath;
            AssetBundleBuild assetBundleBuild = new AssetBundleBuild();
            List<AssetBundleBuild> assetBundleBuilds = null;
            for (int i = 0; i < numGUIDs; ++i)
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                assetBundleBuild.assetBundleName = Path.GetFileNameWithoutExtension(assetPath);
                assetBundleBuild.assetNames = new string[1];
                assetBundleBuild.assetNames[0] = assetPath;

                if (assetBundleBuilds == null)
                    assetBundleBuilds = new List<AssetBundleBuild>();

                assetBundleBuilds.Add(assetBundleBuild);
            }

            AssetBundle sourceAssetBundle = AssetBundle.LoadFromFile(Path.Combine(path, Path.GetFileName(path)));
            AssetBundleManifest source = sourceAssetBundle == null ? null : sourceAssetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");
            string[] assetBundleNames = source == null ? null : source.GetAllAssetBundles();

            if (assetBundleNames != null)
            {
                bool isAppendHashToName = AssetEditor.isAppendHashToName;
                string assetName;
                AssetBundle assetBundle;
                foreach (string assetBundleName in assetBundleNames)
                {
                    assetPath = Path.Combine(path, assetBundleName);

                    assetName = isAppendHashToName
                        ? AssetManager.RemoveHashFromAssetName(assetBundleName)
                        : assetBundleName;
                    if (assetBundleBuilds != null && assetBundleBuilds.FindIndex(x => x.assetBundleName.ToLower() == assetName) != -1)
                    {
                        File.Delete(assetPath);

                        continue;
                    }

                    assetBundle = AssetBundle.LoadFromFile(assetPath);
                    assetBundleBuild.assetNames = assetBundle == null ? null : assetBundle.GetAllAssetNames();

                    if (assetBundle != null)
                        assetBundle.Unload(true);

                    File.Delete(assetPath);

                    if (assetBundleBuild.assetNames == null || assetBundleBuild.assetNames.Length < 1)
                        continue;

                    assetBundleBuild.assetBundleName = assetName;

                    if (assetBundleBuilds == null)
                        assetBundleBuilds = new List<AssetBundleBuild>();

                    assetBundleBuilds.Add(assetBundleBuild);
                }
            }

            if (assetBundleBuilds == null)
                return;

            BuildAssetBundleOptions buildAssetBundleOptions = AssetEditor.buildAssetBundleOptions;
            BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
            AssetBundleManifest destination = BuildPipeline.BuildAssetBundles(path, assetBundleBuilds.ToArray(), buildAssetBundleOptions, buildTarget);
            
            uint version = AssetEditor.version;
            AssetManager.UpdateAfterBuild(
                (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                source, 
                destination, 
                path, 
                ref version);
            AssetEditor.version = version;

            if (sourceAssetBundle != null)
                sourceAssetBundle.Unload(true);

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Assets/ZG/Assets/Build All By Folder")]
        public static void BuildAllByFolder()
        {
            string assetFolder = EditorUtility.OpenFolderPanel("Build Path", EditorPrefs.GetString(PATH), string.Empty);
            if (!string.IsNullOrEmpty(assetFolder))
            {
                EditorPrefs.SetString(PATH, assetFolder);

                BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
                int numFolderLength, numAssets, index;
                string folder, extension, assetPath, subPath, assetBundleName;
                UnityEngine.Object asset;
                UnityEngine.Object[] assets;
                List<string> assetNames;
                Dictionary<string, List<string>> assetNameMap = null;
                var config = AssetEditor.assetConfig;
                var assetGUIDs = Selection.assetGUIDs;
                foreach (var assetGUID in assetGUIDs)
                {
                    folder = AssetDatabase.GUIDToAssetPath(assetGUID);
                    if (!string.IsNullOrEmpty(Path.GetExtension(folder)))
                        continue;

                    numFolderLength = folder.Length;
                    assets = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.DeepAssets);

                    numAssets = assets.Length;
                    for (int i = 0; i < numAssets; ++i)
                    {
                        asset = assets[i];

                        assetPath = AssetDatabase.GetAssetPath(assets[i]);
                        if (config != null && config.IsMask(buildTarget, AssetDatabase.LoadMainAssetAtPath(assetPath)))
                            continue;

                        extension = Path.GetExtension(assetPath);
                        if (string.IsNullOrEmpty(extension))
                            continue;

                        subPath = assetPath.Remove(0, numFolderLength + 1);
                        index = subPath.IndexOf('/');
                        if (index == -1)
                            continue;

                        if (subPath.IndexOf('/', index + 1) != -1)
                            continue;

                        assetBundleName = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(assetPath));

                        if (assetNameMap == null)
                            assetNameMap = new Dictionary<string, List<string>>();

                        if (assetNameMap.TryGetValue(assetBundleName, out assetNames))
                        {
                            if (extension == "unity")
                            {

                            }
                        }
                        else
                        {
                            assetNames = new List<string>();

                            assetNameMap[assetBundleName] = assetNames;
                        }

                        assetNames.Add(assetPath);
                    }
                }

                var assetBundleNames = AssetDatabase.GetAllAssetBundleNames();

                foreach (var assetBundleNameToBuild in assetBundleNames)
                {
                    if (assetNameMap == null)
                        assetNameMap = new Dictionary<string, List<string>>();

                    if (!assetNameMap.TryGetValue(assetBundleNameToBuild, out assetNames))
                    {
                        assetNames = new List<string>();

                        assetNameMap[assetBundleNameToBuild] = assetNames;
                    }

                    assetNames.AddRange(AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleNameToBuild));
                }

                if (assetNameMap == null)
                    return;

                AssetBundleBuild assetBundleBuild = new AssetBundleBuild();
                List<AssetBundleBuild> assetBundleBuilds = null;

                foreach (var pair in assetNameMap)
                {
                    assetBundleName = pair.Key;
                    assetNames = pair.Value;

                    assetBundleBuild.assetBundleName = assetBundleName;
                    assetBundleBuild.assetNames = assetNames.ToArray();

                    if (assetBundleBuilds == null)
                        assetBundleBuilds = new List<AssetBundleBuild>();

                    assetBundleBuilds.Add(assetBundleBuild);
                }

                var assetBundle = AssetBundle.LoadFromFile(Path.Combine(assetFolder, Path.GetFileName(assetFolder)));
                var source = assetBundle == null ? null : assetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");

                //AssetDatabase.SaveAssets();
                //AssetDatabase.Refresh();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                var buildAssetBundleOptions = AssetEditor.buildAssetBundleOptions;
                var destination = BuildPipeline.BuildAssetBundles(assetFolder, assetBundleBuilds.ToArray(), buildAssetBundleOptions, buildTarget);

                uint version = AssetEditor.version;
                AssetManager.UpdateAfterBuild(
                    (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                    source, 
                    destination, 
                    assetFolder, 
                    ref version);
                AssetEditor.version = version;

                if (assetBundle != null)
                    assetBundle.Unload(true);

                EditorUtility.RevealInFinder(assetFolder);
            }
        }

        [MenuItem("Assets/ZG/Assets/Build Folder To Bundle")]
        public static void BuildFolderToBundle()
        {
            string path = EditorUtility.SaveFilePanel("Save Bundle", EditorPrefs.GetString(PATH), string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(path))
            {
                EditorPrefs.SetString(PATH, path);

                //string dataPath = Application.dataPath;
                //dataPath = dataPath.Remove(dataPath.Length - 6, 6);

                BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
                string assetPath, targetPath;
                TextAsset  textAsset;
                UnityEngine.Object[] assets = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.DeepAssets);
                List<string> assetNames = new List<string>();
                foreach (var asset in assets)
                {
                    assetPath = AssetDatabase.GetAssetPath(asset);
                    if(AssetDatabase.IsValidFolder(assetPath))
                        continue;

                    if (asset is DefaultAsset)
                    {
                        targetPath = assetPath + ".bytes";
                        AssetDatabase.MoveAsset(assetPath, targetPath);
                        
                        assetPath = targetPath;
                    }

                    assetNames.Add(assetPath);
                }
                
                AssetDatabase.SaveAssets();

                AssetBundleBuild assetBundleBuild = new AssetBundleBuild();

                assetBundleBuild.assetBundleName = Path.GetFileName(path);
                assetBundleBuild.assetNames = assetNames.ToArray();

                var folder = Path.GetDirectoryName(path);
                var assetBundle = AssetBundle.LoadFromFile(Path.Combine(folder, Path.GetFileName(folder)));
                var source = assetBundle == null ? null : assetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");

                //AssetDatabase.SaveAssets();
                //AssetDatabase.Refresh();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                var buildAssetBundleOptions = AssetEditor.buildAssetBundleOptions;
                var destination = BuildPipeline.BuildAssetBundles(
                    folder, 
                    new AssetBundleBuild[] { assetBundleBuild }, 
                    buildAssetBundleOptions, 
                    buildTarget);

                uint version = AssetEditor.version;
                AssetManager.UpdateAfterBuild(
                    (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                    source, 
                    destination, 
                    folder, 
                    ref version);
                AssetEditor.version = version;

                if (assetBundle != null)
                    assetBundle.Unload(true);

                EditorUtility.RevealInFinder(folder);
            }
        }

        [MenuItem("Assets/ZG/Assets/Build All")]
        public static void BuildAll()
        {
            string assetFolder = EditorUtility.OpenFolderPanel("Build Path", EditorPrefs.GetString(PATH), string.Empty);
            if (!string.IsNullOrEmpty(assetFolder))
            {
                AssetBundle assetBundle = AssetBundle.LoadFromFile(Path.Combine(assetFolder, Path.GetFileName(assetFolder)));
                AssetBundleManifest source = assetBundle == null ? null : assetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");

                //AssetDatabase.SaveAssets();
                //AssetDatabase.Refresh();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                BuildAssetBundleOptions buildAssetBundleOptions = AssetEditor.buildAssetBundleOptions;
                BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
                AssetBundleManifest destination = BuildPipeline.BuildAssetBundles(assetFolder, buildAssetBundleOptions, buildTarget);

                uint version = AssetEditor.version;
                AssetManager.UpdateAfterBuild(
                    (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                    source, 
                    destination, 
                    assetFolder, 
                    ref version);
                AssetEditor.version = version;

                if (assetBundle != null)
                    assetBundle.Unload(true);

                EditorUtility.RevealInFinder(assetFolder);
            }

            /*AssetBundleBuild assetBundleBuild;
            List<AssetBundleBuild> assetBundleBuilds = null;

            string[] assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
            if (assetBundleNames != null)
            {
                foreach (string assetBundleName in assetBundleNames)
                {
                    assetBundleBuild.assetBundleName = assetBundleName;
                    assetBundleBuild.assetBundleVariant = null;
                    assetBundleBuild.assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                    assetBundleBuild.addressableNames = assetBundleBuild.assetNames;

                    if (assetBundleBuilds == null)
                        assetBundleBuilds = new List<AssetBundleBuild>();

                    assetBundleBuilds.Add(assetBundleBuild);
                }
            }
                
            if (assetBundleBuilds != null)
            {
                string assetFolder = EditorUtility.OpenFolderPanel("Build Path", EditorPrefs.GetString(PATH), string.Empty);
                if (!string.IsNullOrEmpty(assetFolder))
                {
                    BuildAssetBundleOptions buildAssetBundleOptions = (BuildAssetBundleOptions)EditorPrefs.GetInt(BUILD_ASSET_BUNDLE_OPTIONS);
                    BuildTarget buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), EditorPrefs.GetString(BUILD_TARGET));
                    BuildPipeline.BuildAssetBundles(assetFolder, assetBundleBuilds.ToArray(), buildAssetBundleOptions, buildTarget);

                    EditorUtility.RevealInFinder(assetFolder);
                }
            }*/
        }

        [MenuItem("Assets/ZG/Assets/Build Asset Infos")]
        public static void BuildAssetInfos()
        {
            string path = EditorUtility.OpenFolderPanel("Build Asset Infos", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            AssetBundle assetBundle = AssetBundle.LoadFromFile(Path.Combine(path, Path.GetFileName(path)));
            if (assetBundle != null)
            {
                uint version = AssetEditor.version;
                AssetManager.UpdateAfterBuild(
                    (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                    null, 
                    assetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest"), 
                    path, 
                    ref version);
                AssetEditor.version = version;

                assetBundle.Unload(true);
            }
        }

        [MenuItem("Assets/ZG/Assets/Write Asset Info")]
        public static void WriteAssetInfo()
        {
            string path = EditorUtility.OpenFilePanel("Write Asset Info", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            uint version = AssetEditor.version;
            AssetManager.WriteAssetInfo(
                (buildAssetBundleOptions & BuildAssetBundleOptions.AppendHashToAssetBundleName) == BuildAssetBundleOptions.AppendHashToAssetBundleName, 
                path, 
                ref version);
            AssetEditor.version = version;
        }

        /*[MenuItem("Assets/ZG/Assets/Recompress Asset(LZMA)")]
        public static void RecompressAsset()
        {
            string path = EditorUtility.OpenFilePanel("Write Asset Info", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            AssetBundle.RecompressAssetBundleAsync(path, path, BuildCompression.LZMA);

            uint version = AssetEditor.version;
            AssetManager.WriteAssetInfo(path, ref version);
            AssetEditor.version = version;
        }*/

        [MenuItem("Assets/ZG/Assets/Package")]
        public static void Package()
        {
            string path = EditorUtility.OpenFolderPanel("Package", EditorPrefs.GetString(PATH), string.Empty);
            if (string.IsNullOrEmpty(path))
                return;

            EditorPrefs.SetString(PATH, path);

            AssetManager.Package(path, true);
        }

        public static Hash128 NewHash()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return new Hash128(BitConverter.ToUInt64(bytes, 0), BitConverter.ToUInt64(bytes, 8));
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var assetConfig = EditorGUILayout.ObjectField("Config", AssetEditor.assetConfig, typeof(AssetConfig), false) as AssetConfig;
            if (EditorGUI.EndChangeCheck())
                AssetEditor.assetConfig = assetConfig;

            EditorGUI.BeginChangeCheck();
            __buildAssetBundleOptions = (BuildAssetBundleOptions)EditorGUILayout.EnumFlagsField("Build Asset Bundle Options", __buildAssetBundleOptions);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(BUILD_ASSET_BUNDLE_OPTIONS, (int)__buildAssetBundleOptions);

            EditorGUI.BeginChangeCheck();
            __buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", __buildTarget);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(BUILD_TARGET, Enum.GetName(typeof(BuildTarget), __buildTarget));

            EditorGUI.BeginChangeCheck();
            int version = EditorGUILayout.IntField("Version", (int)AssetEditor.version);
            if (EditorGUI.EndChangeCheck())
                AssetEditor.version = (uint)version;
        }

        void OnEnable()
        {
            //Transform transform = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(ROOT_GUID_KEY)), typeof(Transform)) as Transform;
            //__boneRoot = transform == null ? null : transform.Find(EditorPrefs.GetString(ROOT_PATH_KEY));

            __buildAssetBundleOptions = (BuildAssetBundleOptions)EditorPrefs.GetInt(BUILD_ASSET_BUNDLE_OPTIONS);
            string buildTarget = EditorPrefs.GetString(BUILD_TARGET);
            if (!string.IsNullOrEmpty(buildTarget))
                __buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTarget);
        }
    }
}