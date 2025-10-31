using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ZG
{
    public interface IAssetBundleAsyncRequest : IDisposable
    {
        bool isDone { get; }
        
        float progress { get; }
        
        AssetBundle assetBundle { get; }
    }

    public interface IAssetBundleFactory
    {
        IAssetBundleAsyncRequest LoadFromFileAsync(string path, ulong offset);
        
        AssetBundle LoadFromFile(string path, ulong offset);
    }

    public class AssetBundleAsyncRequest : IAssetBundleAsyncRequest
    {
        private AssetBundleCreateRequest __instance;

        public bool isDone => __instance.isDone;

        public float progress => __instance.progress;

        public AssetBundle assetBundle => __instance.assetBundle;

        public AssetBundleAsyncRequest(AssetBundleCreateRequest instance)
        {
            __instance = instance;
        }

        public void Dispose()
        {
        }
    }

    public class AssetBundleFactory : IAssetBundleFactory
    {
        public IAssetBundleAsyncRequest LoadFromFileAsync(string path, ulong offset)
        {
            var instance = AssetBundle.LoadFromFileAsync(path, 0, offset);

            return new AssetBundleAsyncRequest(instance);
        }

        public AssetBundle LoadFromFile(string path, ulong offset)
        {
            return AssetBundle.LoadFromFile(path, 0, offset);
        }
    }
    
    public class AssetBundleLoader : CustomYieldInstruction
    {
        private bool __isRecursive = false;
        private ulong __offset;
        private string __path;
        private AssetBundle __assetBundle = null;
        private IAssetBundleAsyncRequest __createRequest = null;
        private IAssetBundleFactory __factory;
        private AssetBundleLoader[] __dependencies;

        public Dictionary<(string, Type), AssetBundleRequest> assetBundleRequests;
        public Dictionary<(string, Type), UnityEngine.Object[]> assets;

        public override bool keepWaiting
        {
            get
            {
                if (__isRecursive)
                {
                    Debug.LogError($"AssetBundle {__path} Is Recursive Dependency!");

                    return false;
                }

                if (__dependencies != null)
                {
                    bool result = false;

                    __isRecursive = true;
                    foreach (var dependency in __dependencies)
                    {
                        if (dependency.keepWaiting)
                        {
                            result = true;

                            break;
                        }
                    }
                    __isRecursive = false;

                    if (result)
                        return true;
                }

                if (refCount > 0)
                {
                    if (isDone)
                    {
                        UnityEngine.Assertions.Assert.IsNull(__createRequest);

                        return false;
                    }

                    if (__createRequest == null)
                        __createRequest = __factory.LoadFromFileAsync(__path, __offset);

                    return !__createRequest.isDone;
                }
                
                if (isDone)
                {
                    UnityEngine.Assertions.Assert.IsNull(__createRequest);

                    __assetBundle.Unload(true);
                    UnityEngine.Object.Destroy(__assetBundle);

                    __assetBundle = null;
                }
                else if (__createRequest != null)
                {
                    UnityEngine.Assertions.Assert.IsNull(__assetBundle);

                    //if (__createRequest.isDone)
                    {
                        var assetBundle = __createRequest.assetBundle;
                        assetBundle.Unload(true);
                        UnityEngine.Object.Destroy(assetBundle);
                        __createRequest = null;
                    }
                    /*else
                        return true;*/
                }

                return false;
            }
        }

        public bool isDone => ((object)__assetBundle) != null;

        public int refCount
        {
            get;

            private set;
        }

        public float progress
        {
            get
            {
                float progressOffset = 0.0f, progressScale = 1.0f;
                if (__isRecursive)
                    Debug.LogError($"AssetBundle {__path} Is Recursive Dependency!");
                else
                {
                    int numDependencies = __dependencies == null ? 0 : __dependencies.Length;
                    if (numDependencies > 0)
                    {
                        progressScale = 1.0f / (numDependencies + 1.0f);

                        __isRecursive = true;
                        foreach (var dependency in __dependencies)
                            progressOffset += dependency.progress;
                        __isRecursive = false;
                    }
                }
                
                if (__createRequest != null)
                {
                    UnityEngine.Assertions.Assert.IsNull(__assetBundle);

                    return (__createRequest.progress + progressOffset) * progressScale;
                }

                if (isDone)
                    return 1.0f;

                return 0.0f;
            }
        }

        public AssetBundle assetBundle => __GetOrLoadSync();

        public AssetBundleLoader(ulong offset, [NotNull]string path, [NotNull]IAssetBundleFactory factory, AssetBundleLoader[] dependencies)
        {
            __offset = offset;
            __path = path;
            __factory = factory;
            __dependencies = dependencies;
        }

        public int Retain()
        {
            if (__isRecursive)
                return refCount;

            if (__dependencies != null)
            {
                __isRecursive = true;
                foreach (var dependency in __dependencies)
                    dependency.Retain();
                __isRecursive = false;
            }

            return ++refCount;
        }

        public int Release()
        {
            if (__isRecursive)
                return refCount;

            UnityEngine.Assertions.Assert.IsTrue(refCount > 0);
            if (--refCount == 0)
            {
                if (assets != null)
                {
                    /*foreach (var asset in assets.Values)
                        UnityEngine.Object.DestroyImmediate(asset);*/

                    assets.Clear();
                }

                if (assetBundleRequests != null)
                {
                    /*foreach (var assetBundleRequest in assetBundleRequests.Values)
                    {
                        if(assetBundleRequest.isDone)
                            UnityEngine.Object.DestroyImmediate(assetBundleRequest.asset, true);
                    }*/

                    assetBundleRequests.Clear();
                }

                if (isDone)
                {
                    UnityEngine.Assertions.Assert.IsNull(__createRequest);

                    __assetBundle.Unload(true);
                    UnityEngine.Object.Destroy(__assetBundle);

                    __assetBundle = null;
                }
                else if (__createRequest != null)
                {
                    UnityEngine.Assertions.Assert.IsNull(__assetBundle);

                    //if (__createRequest.isDone)
                    {
                        var assetBundle = __createRequest.assetBundle;
                        if (assetBundle != null)
                        {
                            assetBundle.Unload(true);
                            UnityEngine.Object.Destroy(assetBundle);
                        }

                        __createRequest = null;
                    }
                }
            }

            if (__dependencies != null)
            {
                __isRecursive = true;
                foreach (var dependency in __dependencies)
                    dependency.Release();
                __isRecursive = false;
            }

            return refCount;
        }

        public override string ToString()
        {
            return $"ABL({__path} : {__offset})";
        }

        private AssetBundle __GetOrLoadSync()
        {
            if (__isRecursive)
            {
                Debug.LogError($"{this} Is Recursive Dependency!");

                return null;
            }

            if (__dependencies != null)
            {
                __isRecursive = true;
                foreach (var dependency in __dependencies)
                    dependency.__GetOrLoadSync();
                __isRecursive = false;
            }

            if (__createRequest != null)
            {
                UnityEngine.Assertions.Assert.IsFalse(isDone);

                __assetBundle = __createRequest.assetBundle;

                __createRequest = null;
            }

            if (!isDone)
                __assetBundle = __factory.LoadFromFile(__path, __offset);

            return __assetBundle;
        }
    }

    public readonly struct AssetBundleLoader<T> : IEnumerator where T : UnityEngine.Object
    {
        public readonly bool IsManaged;

        public readonly string AssetName;

        public readonly AssetBundleLoader Loader;
        
        public bool isVail => Loader != null;
        
        public bool isDone => !__GetOrLoad(false, out _, out _);

        public float progress
        {
            get
            {
                __GetOrLoad(false, out float result, out T[] value);

                return result;
            }
        }

        public T value
        {
            get
            {
                var values = this.values;
                return values == null || values.Length < 1 ? null : values[0];
            }
        }

        public T[] values
        {
            get
            {
                __GetOrLoad(true, out _, out T[] value);

                return value;
            }
        }

        public AssetBundleLoader(string bundleName, string assetName, AssetManager manager)
        {
            IsManaged = false;

            AssetName = assetName;

            Loader = manager.GetOrCreateAssetBundleLoader(bundleName);

            if (Loader != null)
                Loader.Retain();
        }

        public AssetBundleLoader(string assetName, AssetBundleLoader loader)
        {
            IsManaged = true;

            AssetName = assetName;

            Loader = loader;
        }

        public bool Unload()
        {
            if (IsManaged || Loader == null)
                return false;

            if (Loader.Release() == 0)
                return true;

            UnityEngine.Object[] assets = null;
            if (Loader.assetBundleRequests != null && Loader.assetBundleRequests.TryGetValue((AssetName, typeof(T)), out var assetBundleRequest))
            {
                assets = assetBundleRequest.allAssets;

                Loader.assetBundleRequests.Remove((AssetName, typeof(T)));
            }
            else if (Loader.assets != null && Loader.assets.TryGetValue((AssetName, typeof(T)), out assets))
            {
                //UnityEngine.Object.DestroyImmediate(asset);

                Loader.assets.Remove((AssetName, typeof(T)));
            }

            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is GameObject)
                        continue;
                    
                    Resources.UnloadAsset(asset);
                }
            }
            /*var gameObject = asset as GameObject;
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);*/

            return false;
        }

        public void Dispose()
        {
            if (IsManaged || Loader == null)
                return;

            Loader.Release();
        }

        public bool MoveNext() => __GetOrLoad(false, out _, out _);

        private bool __GetOrLoad(bool isSync, out float progress, out T[] value)
        {
            if (Loader == null)
            {
                value = null;

                progress = 1.0f;

                return false;
            }

            if (Loader.assets != null && Loader.assets.TryGetValue((AssetName, typeof(T)), out var target))
            {
                value = (T[])target;

                progress = 1.0f;

                return false;
            }

            if (Loader.assetBundleRequests != null && Loader.assetBundleRequests.TryGetValue((AssetName, typeof(T)), out var request))
            {
                if (request != null && (request.isDone || isSync))
                {
                    Loader.assetBundleRequests.Remove((AssetName, typeof(T)));

                    T[] assets =  Array.ConvertAll(request.allAssets, x => (T)x);

                    if (Loader.assets == null)
                        Loader.assets = new Dictionary<(string, Type), UnityEngine.Object[]>();

                    Loader.assets.Add((AssetName, typeof(T)), assets);

                    value = assets;

                    progress = 1.0f;

                    return false;
                }
                
                progress = 0.9f + request.progress * 0.1f;
            }
            else if (isSync)
            {
                var assetBundle = Loader.assetBundle;
                value = assetBundle == null ? null : assetBundle.LoadAssetWithSubAssets<T>(AssetName);
                if (value != null)
                {
                    if (Loader.assets == null)
                        Loader.assets = new Dictionary<(string, Type), UnityEngine.Object[]>();

                    Loader.assets.Add((AssetName, typeof(T)), value);
                }

                progress = 1.0f;

                return false;
            }
            else if (!Loader.keepWaiting)
            {
                if (Loader.refCount < 1)
                {
                    value = null;

                    progress = 1.0f;

                    return false;
                }

                var assetBundle = Loader.assetBundle;

                request = assetBundle == null ? null : assetBundle.LoadAssetWithSubAssetsAsync<T>(AssetName);
                if (request == null)
                {
                    Debug.LogError($"Asset {AssetName} loaded fail.");

                    value = null;

                    progress = 1.0f;

                    return false;
                }

                if (Loader.assetBundleRequests == null)
                    Loader.assetBundleRequests = new Dictionary<(string, Type), AssetBundleRequest>();

                Loader.assetBundleRequests.Add((AssetName, typeof(T)), request);

                progress = 0.9f;
            }
            else
                progress = 0.9f * Loader.progress;

            value = null;

            return true;
        }

        void IEnumerator.Reset()
        {
            throw new NotSupportedException();
        }

        object IEnumerator.Current => null;
    }

    public class AssetBundlePool : IDisposable
    {
        public readonly AssetManager Manager;

        public HashSet<string> __bundleNames;

        public AssetBundlePool(AssetManager manager)
        {
            Manager = manager;
        }

        public void Clear()
        {
            if (__bundleNames == null)
                return;

            foreach (var bundleName in __bundleNames)
                Manager.UnloadAssetBundle(bundleName);

            __bundleNames.Clear();
        }

        public void Dispose()
        {
            if (__bundleNames == null)
                return;

            foreach(var bundleName in __bundleNames)
                Manager.UnloadAssetBundle(bundleName);

            __bundleNames = null;
        }

        public AssetBundleLoader<T> Load<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
            var loader = Manager.GetOrCreateAssetBundleLoader(bundleName);

            if (__bundleNames == null)
                __bundleNames = new HashSet<string>();

            if (__bundleNames.Add(bundleName))
                loader.Retain();

            return new AssetBundleLoader<T>(assetName, loader);
        }

        public T LoadSync<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
            return Load<T>(bundleName, assetName).value;
        }
    }

    public partial class AssetManager
    {
        private IAssetBundleFactory __factory;
        private Dictionary<string, AssetBundleLoader> __assetBundleLoaders;

        private AssetManager(IAssetBundleFactory factory)
        {
            __factory = factory;
        }

        public AssetBundleLoader GetOrCreateAssetBundleLoader(string name)
        {
            if (__assetBundleLoaders != null && __assetBundleLoaders.TryGetValue(name, out var assetBundleLoader))
                return assetBundleLoader;

            if (GetAssetPath(name, out var asset, out ulong fileOffset, out string filePath))
            {
                if (__factory == null)
                    __factory = new AssetBundleFactory();
                
                int numDependencies = asset.data.dependencies == null ? 0 : asset.data.dependencies.Length;
                AssetBundleLoader[] dependencies = numDependencies > 0 ? new AssetBundleLoader[numDependencies] : null;

                assetBundleLoader = new AssetBundleLoader(
                    fileOffset,
                    filePath,
                    __factory,
                    dependencies);

                if (__assetBundleLoaders == null)
                    __assetBundleLoaders = new Dictionary<string, AssetBundleLoader>();

                __assetBundleLoaders[name] = assetBundleLoader;

                for (int i = 0; i < numDependencies; ++i)
                    dependencies[i] = GetOrCreateAssetBundleLoader(asset.data.dependencies[i]);

            }
            else
            {
                if (!string.IsNullOrEmpty(name))
                    Debug.LogError($"Load Asset Bundle {name} Fail.");

                assetBundleLoader = null;
            }

            return assetBundleLoader;
        }

        public bool Unload<T>(string fileName, string assetName) where T : UnityEngine.Object
        {
            if (__assetBundleLoaders != null && __assetBundleLoaders.TryGetValue(fileName, out var assetBundleLoader))
            {
                if (assetBundleLoader.Release() == 0)
                    return true;

                UnityEngine.Object[] assets = null;
                if (assetBundleLoader.assetBundleRequests != null && assetBundleLoader.assetBundleRequests.TryGetValue((assetName, typeof(T)), out var assetBundleRequest))
                {
                    assets = assetBundleRequest.allAssets;

                    assetBundleLoader.assetBundleRequests.Remove((assetName, typeof(T)));
                }
                else if (assetBundleLoader.assets != null && assetBundleLoader.assets.TryGetValue((assetName, typeof(T)), out assets))
                {
                    //UnityEngine.Object.DestroyImmediate(asset);

                    assetBundleLoader.assets.Remove((assetName, typeof(T)));
                }

                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        if(asset is GameObject)
                            continue;
                        
                        Resources.UnloadAsset(asset);
                    }
                }

                /*var gameObject = asset as GameObject;
                if (gameObject != null)
                    UnityEngine.Object.DestroyImmediate(gameObject);*/
            }

            return false;
        }

        public bool UnloadAssetBundle(string name)
        {
            if (__assetBundleLoaders != null && __assetBundleLoaders.TryGetValue(name, out var assetBundleLoader))
                return assetBundleLoader.Release() == 0;

            return false;
        }

        public AssetBundle LoadAssetBundle(string name)
        {
            var assetBundleLoader = GetOrCreateAssetBundleLoader(name);
            if (assetBundleLoader == null)
            {
                /*if(!string.IsNullOrEmpty(name))
                    Debug.LogError($"Load Asset Bundle {name} Fail.");*/

                return null;
            }

            assetBundleLoader.Retain();
            return assetBundleLoader.assetBundle;
        }

        public T[] LoadAssets<T>(string fileName, string assetName) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return null;

            var assetBundleLoader = GetOrCreateAssetBundleLoader(fileName);
            if (assetBundleLoader == null)
                return null;

            assetBundleLoader.Retain();

            if (assetBundleLoader.assets != null && assetBundleLoader.assets.TryGetValue((assetName, typeof(T)), out var target))
                return (T[])target;

            T[] assets = null;
            if (assetBundleLoader.assetBundleRequests != null && assetBundleLoader.assetBundleRequests.TryGetValue((assetName, typeof(T)), out var assetBundleRequest))
            {
                if (assetBundleRequest.isDone)
                    assets = Array.ConvertAll(assetBundleRequest.allAssets, x => (T)x);

                assetBundleLoader.assetBundleRequests.Remove((assetName, typeof(T)));
            }

            if (assets == null)
            {
                var assetBundle = assetBundleLoader.assetBundle;
                assets = assetBundle == null ? null : assetBundle.LoadAssetWithSubAssets<T>(assetName);
            }

            if (assets != null)
            {
                if (assetBundleLoader.assets == null)
                    assetBundleLoader.assets = new Dictionary<(string, Type), UnityEngine.Object[]>();

                assetBundleLoader.assets.Add((assetName, typeof(T)), assets);
            }

            return assets;
        }

        public T Load<T>(string fileName, string assetName) where T : UnityEngine.Object
        {
            var assets = LoadAssets<T>(fileName, assetName);

            return assets == null || assets.Length < 1 ? null : assets[0];
        }

        public IEnumerator LoadAssetBundleAsync(string name, Action<float> onProgress, Action<AssetBundle> onComplete)
        {
            var assetBundleLoader = GetOrCreateAssetBundleLoader(name);
            if (assetBundleLoader == null)
            {
                /*if (!string.IsNullOrEmpty(name))
                    Debug.LogError($"AssetBundle {name} Load Fail.");*/

                if (onComplete != null)
                    onComplete(null);

                yield break;
            }

            assetBundleLoader.Retain();
            if (onProgress == null)
                yield return assetBundleLoader;
            else
            {
                while (assetBundleLoader.keepWaiting)
                {
                    onProgress(assetBundleLoader.progress);

                    yield return null;

                    if (assetBundleLoader.refCount < 1)
                        yield break;
                }
            }

            if (assetBundleLoader.refCount < 1)
                yield break;

            if (onComplete != null)
                onComplete(assetBundleLoader.assetBundle);
        }

        public IEnumerator Load<T>(
            string fileName,
            string assetName,
            Action<float> onProgress,
            Action<AssetBundle, T[]> onComplete) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetName))
                yield break;
            
            var assetBundleLoader = GetOrCreateAssetBundleLoader(fileName);
            if (assetBundleLoader == null)
            {
                if (onComplete != null)
                    onComplete(null, null);

                yield break;
            }

            assetBundleLoader.Retain();

            AssetBundle assetBundle;
            if (assetBundleLoader.assets != null && assetBundleLoader.assets.TryGetValue((assetName, typeof(T)), out var target))
            {
                if (onComplete != null)
                {
                    assetBundle = assetBundleLoader.assetBundle;
                    /*if(assetBundle == null)
                        assetBundle = assetBundleLoader.assetBundle;*/

                    onComplete(assetBundle, (T[])target);
                }

                yield break;
            }

            if (onProgress == null)
                yield return assetBundleLoader;
            else
            {
                while (assetBundleLoader.keepWaiting)
                {
                    onProgress(assetBundleLoader.progress);

                    yield return null;

                    if (assetBundleLoader.refCount < 1)
                        yield break;
                }
            }

            if (assetBundleLoader.refCount < 1)
                yield break;
            
            assetBundle = assetBundleLoader.assetBundle;

            AssetBundleRequest assetBundleRequest;
            if (assetBundleLoader.assetBundleRequests != null && assetBundleLoader.assetBundleRequests.TryGetValue((assetName, typeof(T)), out assetBundleRequest))
            {
                /*if (!assetBundleRequest.isDone)
                    yield return assetBundleRequest;*/
                while (!assetBundleRequest.isDone)
                    yield return null;
            }
            else
            {
                if (assetBundleLoader.assets != null && assetBundleLoader.assets.TryGetValue((assetName, typeof(T)), out target))
                {
                    if (onComplete != null)
                    {
                        if (assetBundle == null)
                            assetBundle = assetBundleLoader.assetBundle;

                        onComplete(assetBundle, (T[])target);
                    }

                    yield break;
                }

                assetBundleRequest = assetBundle == null ? null : assetBundle.LoadAssetWithSubAssetsAsync<T>(assetName);
                if (assetBundleRequest == null)
                    Debug.LogError($"Asset {assetName} from {fileName} loaded fail.");
                else
                {
                    if (assetBundleLoader.assetBundleRequests == null)
                        assetBundleLoader.assetBundleRequests = new Dictionary<(string, Type), AssetBundleRequest>();

                    assetBundleLoader.assetBundleRequests.Add((assetName, typeof(T)), assetBundleRequest);

                    yield return assetBundleRequest;
                }
            }

            T[] assets = null;
            if (assetBundleLoader.assetBundleRequests != null)
            {
                if (assetBundleLoader.assetBundleRequests.TryGetValue((assetName, typeof(T)), out assetBundleRequest))
                {
                    assetBundleLoader.assetBundleRequests.Remove((assetName, typeof(T)));

                    assets = Array.ConvertAll(assetBundleRequest.allAssets, x => (T)x);
                }
                else if (assetBundleLoader.assets != null && assetBundleLoader.assets.TryGetValue((assetName, typeof(T)), out target))
                {
                    if (onComplete != null)
                    {
                        if (assetBundle == null)
                            assetBundle = assetBundleLoader.assetBundle;

                        onComplete(assetBundle, (T[])target);
                    }

                    yield break;
                }
                else
                    Debug.LogError($"Asset {assetName} from {fileName} has been destroied.");
            }

            if (assets != null)
            {
                if (assetBundleLoader.assets == null)
                    assetBundleLoader.assets = new Dictionary<(string, Type), UnityEngine.Object[]>();

                assetBundleLoader.assets.Add((assetName, typeof(T)), assets);
            }

            if (onComplete != null)
                onComplete(assetBundle, assets);
        }

        public IEnumerator Recompress(DownloadHandler handler)
        {
            if (__assets == null)
                yield break;

            /*BuildCompression method;
            switch (type)
            {
                case AssetType.Uncompressed:
                    method = BuildCompression.UncompressedRuntime;
                    break;
                case AssetType.LZ4:
                    method = BuildCompression.LZ4Runtime;
                    break;
                default:
                    yield break;
            }*/

            ulong size = 0UL, totalBytesDownload = 0UL;
            AssetData data;
            List<string> assetNames = null;
            foreach (var pair in __assets)
            {
                data = pair.Value.data;

                size += data.info.size;

                if (data.type == AssetType.Uncompressed)
                    continue;

                if (data.type == AssetType.UncompressedRuntime/* || !data.pack.canRecompress*/)
                {
                    totalBytesDownload += data.info.size;

                    continue;
                }

                if (assetNames == null)
                    assetNames = new List<string>();

                assetNames.Add(pair.Key);
            }

            if (assetNames == null)
                yield break;

            bool result;
            int numAssets = assetNames.Count;
            uint downloadedBytes;
            ulong fileOffset;
            float progress;
            string assetName, inputPath, outputPath;
            Asset asset;
            AssetBundleRecompressOperation assetBundleRecompressOperation;
            IAssetPackEnumerator packEnumerator;
            for (int i = 0; i < numAssets; ++i)
            {
                assetName = assetNames[i];
                asset = __assets[assetName];

                outputPath = __GetAssetPath(assetName, asset.data.info.fileName);

                if (asset.data.pack.isVail)
                {
                    inputPath = asset.data.pack.filePath;
                    fileOffset = asset.data.pack.fileOffset;
                    if (asset.data.pack.fileOffset > 0 || asset.data.type == AssetType.Stream)
                    {
                        packEnumerator = AssetUtility.CopyPack(asset.data.pack.name, outputPath, inputPath, fileOffset);
                        if (packEnumerator == null)
                            continue;

                        yield return packEnumerator;

                        if (!packEnumerator.isSuccessful)
                            continue;

                        inputPath = outputPath;
                    }
                    else if (!AssetUtility.UpdatePack(asset.data.pack.name, ref inputPath, ref fileOffset))
                        continue;
                }
                else
                    inputPath = outputPath;

                if (asset.data.type == AssetType.Stream)
                    result = true;
                else
                {
                    CreateDirectory(outputPath);

                    assetBundleRecompressOperation = AssetBundle.RecompressAssetBundleAsync(
                        inputPath,
                        outputPath,
                        asset.data.type == AssetType.LZ4 ? BuildCompression.LZ4Runtime : BuildCompression.UncompressedRuntime/*,
                    0,
                    UnityEngine.ThreadPriority.High*/);

                    if (handler == null)
                        yield return assetBundleRecompressOperation;
                    else
                    {
                        while (!assetBundleRecompressOperation.isDone)
                        {
                            yield return null;

                            try
                            {
                                progress = assetBundleRecompressOperation.progress;
                                downloadedBytes = (uint)Mathf.RoundToInt(progress * asset.data.info.size);
                                handler(
                                    assetName,
                                    progress,
                                    downloadedBytes,
                                    totalBytesDownload + downloadedBytes,
                                    size,
                                    i,
                                    numAssets);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError(e.InnerException ?? e);
                            }
                        }

                        totalBytesDownload += asset.data.info.size;
                    }

                    result = assetBundleRecompressOperation.success;

                    if(!result)
                        Debug.LogError(assetBundleRecompressOperation.humanReadableResult);
                }

                if (result)
                {
                    try
                    {
                        using (var streamWrapper = new StreamWrapper(File.Open(__GetManagerPath(Path.GetDirectoryName(assetName)), FileMode.Open, FileAccess.ReadWrite)))
                        {
                            streamWrapper.Write(asset.offset, (byte)AssetType.UncompressedRuntime);
                        }

                        asset.data.type = AssetType.UncompressedRuntime;
                        __assets[assetName] = asset;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e.InnerException ?? e);
                    }
                    finally
                    {
                        //GC.Collect();
                    }
                }
            }
        }
    }
}