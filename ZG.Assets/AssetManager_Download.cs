#if !UNITY_WEBGL
#define ASSET_MANAGER_USE_TASK
#endif

using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace ZG
{
    public interface IAssetPackHeader
    {
        bool isDone { get; }

        ulong fileSize { get; }

        string filePath { get; }

        string name { get; }
    }

    public interface IAssetPack
    {
        bool isDone { get; }
        
        float downloadProgress { get; }

        IAssetPackHeader header { get; }

        bool Contains(string packName);

        bool GetFileInfo(string name, out ulong fileOffset, out string filePath);
    }

    public interface IAssetPackFactory
    {
        IAssetPack Retrieve();
    }

    public interface IAssetPackEnumerator : IEnumerator
    {
        bool isSuccessful { get; }

        float progress { get; }
    }

    public interface IAssetPackLocator
    {
        bool Update(ref string filePath, ref ulong fileOffset);

        IAssetPackEnumerator Copy(string targetPath, string filePath, ulong fileOffset);
    }

    public struct AssetPackLocator : IAssetPackLocator
    {
        public bool Update(ref string filePath, ref ulong fileOffset)
        {
            filePath = Path.Combine(Application.streamingAssetsPath, filePath);

            return true;
        }

        public IAssetPackEnumerator Copy(string targetPath, string filePath, ulong fileOffset)
        {
            UnityEngine.Assertions.Assert.AreEqual(0UL, fileOffset);

            return new AssetPackEnumerator(targetPath, filePath);
        }
    }

    public struct AssetPackEnumerator : IAssetPackEnumerator
    {
        private UnityWebRequest __www;

        public bool isSuccessful
        {
            get
            {
                if (__www == null)
                    return true;

                if(__www.isDone)
                {
                    var error = __www.error;
                    if (string.IsNullOrEmpty(error))
                    {
                        __www.Dispose();

                        __www = null;

                        return true;
                    }

                    Debug.LogError(error);
                }

                return false;
            }
        }

        public float progress
        {
            get
            {
                return __www == null ? 1.0f : __www.downloadProgress;
            }
        }

        public AssetPackEnumerator(string targetPath, string filePath)
        {
            __www = new UnityWebRequest(filePath, UnityWebRequest.kHttpVerbGET, new DownloadHandlerFile(targetPath), null);
            __www.SendWebRequest();
        }

        public bool MoveNext() => !isSuccessful;

        void IEnumerator.Reset() => throw new NotSupportedException();

        object IEnumerator.Current => null;
    }

    public struct AssetPath
    {
        public string url;
        public string folder;
        public IAssetPack assetPack;

        public AssetPath(string url, string folder, IAssetPack assetPack)
        {
            this.url = url;
            this.folder = folder;

            this.assetPack = assetPack;
        }
    }

    public static class AssetUtility
    {
        private static Dictionary<string, IAssetPackFactory> __packFactories;
        private static Dictionary<string, IAssetPackLocator> __packLocators;

        public static string GetPath(string path)
        {
            path = path.Replace('\\', '/');

            return path.ToLower();
        }

        public static void Register(string filePath, IAssetPackFactory packFactory)
        {
            if (__packFactories == null)
                __packFactories = new Dictionary<string, IAssetPackFactory>();

            __packFactories.Add(GetPath(filePath), packFactory);
        }

        public static void Register(string packName, IAssetPackLocator packLocator)
        {
            if (__packLocators == null)
                __packLocators = new Dictionary<string, IAssetPackLocator>();

            __packLocators[packName] = packLocator;
        }

        public static IAssetPack RetrievePack(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) &&__packFactories != null && __packFactories.TryGetValue(GetPath(filePath), out var packFactory))
                return packFactory.Retrieve();

            return null;
        }

        public static bool UpdatePack(string packName, ref string filePath, ref ulong fileOffset)
        {
            if (__packLocators != null && __packLocators.TryGetValue(packName, out var packLocator))
                return packLocator.Update(ref filePath, ref fileOffset);

            if(!string.IsNullOrEmpty(packName))
                Debug.LogError($"Update Pack {packName} : {filePath} Failed");

            return false;
        }

        public static IAssetPackEnumerator CopyPack(string packName, string targetPath, string filePath, ulong fileOffset)
        {
            if (__packLocators != null && __packLocators.TryGetValue(packName, out var packLocator))
                return packLocator.Copy(targetPath, filePath, fileOffset);

            if (!string.IsNullOrEmpty(packName))
                Debug.LogError($"Copy Pack {packName} : {filePath} Failed");

            return null;
        }

        static AssetUtility()
        {
            AssetPackLocator assetPackLocator;
            Register(AssetManager.DefaultAssetPackHeader.NAME, assetPackLocator);
        }
    }

    public partial class AssetManager
    {
        public struct DefaultAssetPackHeader : IAssetPackHeader
        {
            public const string NAME = "StreamingAssets";

            public bool isDone => true;

            public ulong fileSize => int.MaxValue;

            public string filePath { get; }

            public string name => NAME;

            public DefaultAssetPackHeader(string filePath)
            {
                this.filePath = filePath;
            }
        }

        public struct DefaultAssetPack : IAssetPack
        {
            public readonly string Path;
            public readonly string Directory;

            public bool isDone => true;

            public float downloadProgress => 1.0f;

            public bool Contains(string packName)
            {
                return packName == DefaultAssetPackHeader.NAME;
            }

            public IAssetPackHeader header => new DefaultAssetPackHeader(string.IsNullOrEmpty(Path) ? null : Path + FILE_SUFFIX_ASSET_PACKAGE);

            public DefaultAssetPack(string path, string directory)
            {
                Path = path;
                Directory = directory;
            }

            public bool GetFileInfo(
                string name, 
                out ulong fileOffset, 
                out string filePath)
            {
                filePath = string.IsNullOrEmpty(Directory) ? name : 
                    System.IO.Path.Combine(Directory, System.IO.Path.GetFileName(name));
                fileOffset = 0;

                return true;
            }
        }

        public delegate void DownloadHandler(
            string name,
            float progress,
            uint bytesDownload,
            ulong totalBytesDownload,
            ulong totalBytes,
            int index,
            int count);

        public class DownloadFileHandler : DownloadHandlerScript, IDisposable
        {
            private ulong __streamOffset;
            private ulong __startBytes;
            private AssetPack __pack;
            private IReadOnlyList<KeyValuePair<string, Asset>> __assets;
            private Writer __writer;
            private Stream __stream;
            private MD5 __md5;

            public bool isDownloading
            {
                get;

                private set;
            }

            public string assetName
            {
                get;

                private set;
            }

            public float assetProgress
            {
                get;

                private set;
            }

            public int assetCount
            {
                get;

                private set;
            }

            public uint bytesDownloaded
            {
                get;

                private set;
            }

            public ulong fileBytesDownloaded
            {
                get;

                private set;
            }

            public DownloadFileHandler(byte[] preallocatedBuffer) : base(preallocatedBuffer)
            {
                isDownloading = true;
            }

            public new void Dispose()
            {
                if (__stream != null)
                {
                    __stream.Dispose();

                    __stream = null;
                }

                if (__md5 != null)
                {
                    __md5.Dispose();

                    __md5 = null;
                }

                base.Dispose();
            }

            public void Init(
                ulong startBytes,
                in AssetPack pack, 
                IReadOnlyList<KeyValuePair<string, Asset>> assets,
                Writer writer)
            {
                __streamOffset = 0;

                __startBytes = startBytes;

                __pack = pack;
                __assets = assets;

                __writer = writer;

                bytesDownloaded = 0;
                fileBytesDownloaded = 0;

                assetCount = 0;

                if (__stream != null)
                {
                    var stream = __stream;

                    __stream = null;

                    isDownloading = __ReceiveData(((MemoryStream)stream).GetBuffer(), (int)stream.Position);

                    stream.Dispose();
                }
            }

            public void Clear()
            {
                isDownloading = true;

                __assets = null;

                if (__stream != null)
                {
                    __stream.Dispose();

                    __stream = null;
                }

                if (__md5 != null)
                {
                    __md5.Dispose();

                    __md5 = null;
                }
            }

            public bool ReceiveData(int dataLength)
            {
                return ReceiveData(null, dataLength);
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (isDownloading)
                {
                    if (__assets == null)
                    {
                        if (__stream == null)
                            __stream = new MemoryStream();

                        __stream.Write(data, 0, dataLength);

                        return true;
                    }

                    isDownloading = __ReceiveData(data, dataLength);
                }

                return isDownloading;
            }

            private bool __ReceiveData(byte[] data, int dataLength)
            {
                __streamOffset += (ulong)dataLength;

                if (__startBytes < __streamOffset)
                {
                    int length = (int)Math.Min((ulong)dataLength, __streamOffset - __startBytes), offset;
                    if (length < dataLength)
                    {
                        offset = dataLength - length;

                        dataLength = length;
                    }
                    else
                        offset = 0;

                    try
                    {
                        KeyValuePair<string, Asset> pair;
                        AssetData assetData;
                        string assetName, assetPath;
                        int size, numAssets = __assets.Count;

                        do
                        {
                            pair = __assets[assetCount];

                            assetName = pair.Key;

                            assetData = pair.Value.data;

                            if (assetData.info.size <= bytesDownloaded)
                                bytesDownloaded = 0;

                            assetPath = __writer.AssetManager.__GetAssetPath(assetName, assetData.info.fileName);

                            if (bytesDownloaded == 0)
                            {
                                if (__stream != null)
                                {
                                    __stream.Dispose();

                                    __stream = null;
                                }

                                if (!__pack.isVail)
                                {
                                    CreateDirectory(assetPath);

                                    __stream = File.Create(assetPath);
                                }

                                if (__md5 != null)
                                    __md5.Dispose();

                                if(data != null)
                                    __md5 = MD5.Create();
                            }

                            size = (int)(assetData.info.size - bytesDownloaded);
                            length = Math.Min(dataLength, size);

                            if(__stream != null)
                                __stream.Write(data, offset, length);

                            bytesDownloaded += (uint)length;

                            if (length < size)
                            {
                                if(__md5 != null)
                                    __md5.TransformBlock(data, offset, length, data, 0);

                                dataLength = 0;

                                assetProgress = bytesDownloaded * 1.0f / assetData.info.size;
                            }
                            else
                            {
                                if (__stream != null)
                                {
                                    __stream.Dispose();
                                    __stream = null;
                                }

                                if (__md5 != null)
                                {
                                    __md5.TransformFinalBlock(data, offset, length);

                                    var md5Hash = __md5.Hash;

                                    __md5.Dispose();
                                    __md5 = null;

                                    if (assetData.info.md5 == null)
                                        assetData.info.md5 = md5Hash;
                                    else if (!MemoryEquals(md5Hash, assetData.info.md5))
                                    {
                                        Debug.LogError($"{assetPath} MD5 Has Been Vailed Fail.");

                                        return false;
                                    }
                                }

                                if (__pack.isVail)
                                {
                                    assetData.pack = __pack;
                                    assetData.pack.fileOffset += fileBytesDownloaded;
                                }

                                if(!__writer.Write(assetName, assetData))
                                {
                                    Debug.LogError($"{assetPath} Has Been Writed Fail.");

                                    return false;
                                }

                                dataLength -= length;

                                offset += length;

                                fileBytesDownloaded += bytesDownloaded;

                                bytesDownloaded = 0;

                                assetProgress = 0.0f;

                                if (++assetCount >= numAssets)
                                    return false;
                            }
                        } while (dataLength > 0);

                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e.InnerException ?? e);

                        return false;
                    }
                }

                return true;
            }
        }

        public const string FILE_SUFFIX_ASSET_CONFIG = ".json";
        public const string FILE_SUFFIX_ASSET_PACKAGE = ".pack";

        public static string RemoveHashFromAssetName(string name) => name.Remove(name.Length - 33);

        public static AssetList LoadAssetList(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    return JsonUtility.FromJson<AssetList>(text);
                }
                catch (Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }

            return null;
        }

        public static Dictionary<string, AssetInfo> LoadAssetInfos(byte[] bytes, out uint version)
        {
            if (bytes == null)
            {
                version = 0;

                return null;
            }

            var assetInfos = new Dictionary<string, AssetInfo>();
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(bytes)))
                {
                    version = reader.ReadUInt32();

                    string assetBundleName = reader.ReadString();
                    AssetInfo assetInfo;
                    while (!string.IsNullOrEmpty(assetBundleName))
                    {
                        /*assetInfo.version = reader.ReadUInt32();
                        assetInfo.size = reader.ReadUInt32();

                        assetInfo.md5 = version > 0 ? reader.ReadBytes(16) : null;*/

                        assetInfo = AssetInfo.Read(reader, version);

                        if (assetInfos == null)
                            assetInfos = new Dictionary<string, AssetInfo>();

                        assetInfos.Add(assetBundleName, assetInfo);

                        assetBundleName = reader.ReadString();
                    }

                    if (version > 2)
                    {
                        using (var md5 = new MD5CryptoServiceProvider())
                        {
                            var md5Hash = md5.ComputeHash(bytes, 0, (int)reader.BaseStream.Position);
                            if (!MemoryEquals(md5Hash, reader.ReadBytes(md5Hash.Length)))
                                return null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e.InnerException ?? e);

                version = 0;

                return null;
            }

            return assetInfos;
        }

        public static void SaveAssetInfos(string path, Dictionary<string, AssetInfo> results)
        {
            if (results != null)
            {
                //using (var writer = new BinaryWriter(File.Open(filename, FileMode.OpenOrCreate, FileAccess.Write)))

                using (var writer = new BinaryWriter(new MemoryStream()))
                {
                    writer.Write(VERSION);

                    AssetInfo assetInfo;
                    foreach (var pair in results)
                    {
                        writer.Write(pair.Key);

                        assetInfo = pair.Value;
                        assetInfo.Write(writer);
                        /*writer.Write(assetInfo.version);
                        writer.Write(assetInfo.size);
                        writer.Write(assetInfo.md5);*/
                    }

                    writer.Write(string.Empty);

                    var stream = (MemoryStream)writer.BaseStream;
                    using (var md5 = new MD5CryptoServiceProvider())
                    {
                        long position = stream.Position;
                        stream.Position = 0L;
                        var md5Hash = md5.ComputeHash(stream);
                        stream.Position = position;
                        writer.Write(md5Hash, 0, md5Hash.Length);
                    }

                    File.WriteAllBytes(path, stream.ToArray());
                }
            }
        }

        public static void WriteAssetInfo(bool isAppendHashToAssetBundleName, string assetPath, ref uint assetVersion)
        {
            string assetDirectory = Path.GetDirectoryName(assetPath),
                assetName = Path.GetFileName(assetPath),
                assetInfosPath = Path.Combine(assetDirectory, Path.GetFileName(assetDirectory)) + FILE_SUFFIX_ASSET_INFOS;
            var assetInfos = File.Exists(assetInfosPath) ? LoadAssetInfos(File.ReadAllBytes(assetInfosPath), out _) : new Dictionary<string, AssetInfo>(1);

            foreach (var value in assetInfos.Values)
                assetVersion = Math.Max(assetVersion, value.version);

            ++assetVersion;

            AssetInfo assetInfo;
            using (var md5 = new MD5CryptoServiceProvider())
            {
                assetInfo.version = 0;

                try
                {
                    assetInfo.size = (uint)new FileInfo(assetPath).Length;
                }
                catch (Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);

                    return;
                }

                assetInfo.version = assetVersion;

                if (isAppendHashToAssetBundleName)
                {
                    assetInfo.fileName = assetName;

                    assetName = RemoveHashFromAssetName(assetName);
                }
                else
                    assetInfo.fileName = string.Empty;

                assetInfo.md5 = md5.ComputeHash(File.ReadAllBytes(assetPath));

                assetInfos[assetName] = assetInfo;
            }

            SaveAssetInfos(assetInfosPath, assetInfos);
        }

        public static void UpdateAfterBuild(bool isAppendHashToAssetBundleName, AssetBundleManifest source, AssetBundleManifest destination, string path, ref uint assetVersion)
        {
            string[] assetBundleNames = destination == null ? null : destination.GetAllAssetBundles();
            if (assetBundleNames == null)
                return;

            string filename = Path.Combine(path, Path.GetFileName(path) + FILE_SUFFIX_ASSET_INFOS);
            var assetInfos = File.Exists(filename) ? LoadAssetInfos(File.ReadAllBytes(filename), out _) : null;
            if (assetInfos != null)
            {
                foreach (var value in assetInfos.Values)
                    assetVersion = Math.Max(assetVersion, value.version);
            }

            ++assetVersion;

            var result = new Dictionary<string, AssetInfo>();

            string assetName;
            AssetInfo assetInfo;
            using (var md5 = new MD5CryptoServiceProvider())
            {
                assetInfo.version = 0;
                foreach (string assetBundleName in assetBundleNames)
                {
                    assetName = isAppendHashToAssetBundleName ? RemoveHashFromAssetName(assetBundleName) : assetBundleName;
                    
                    if (assetInfos != null && 
                        assetInfos.TryGetValue(assetName, out assetInfo))
                    {
                        assetInfos.Remove(assetName);

                        if (source != null && 
                            source.GetAssetBundleHash(string.IsNullOrEmpty(assetInfo.fileName) ? assetName : assetInfo.fileName) == destination.GetAssetBundleHash(assetBundleName))
                        {
                            assetInfo.fileName = isAppendHashToAssetBundleName ? assetBundleName : string.Empty;
                            
                            result.Add(assetBundleName, assetInfo);

                            continue;
                        }
                    }

                    try
                    {
                        assetInfo.size = (uint)new FileInfo(Path.Combine(path, assetBundleName)).Length;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e.InnerException ?? e);

                        continue;
                    }

                    assetInfo.version = assetVersion;

                    assetInfo.fileName = isAppendHashToAssetBundleName ? assetBundleName : string.Empty;

                    assetInfo.md5 = md5.ComputeHash(File.ReadAllBytes(Path.Combine(path, assetBundleName)));

                    result.Add(assetName, assetInfo);
                }
            }

            SaveAssetInfos(filename, result);
            
            if (assetInfos != null)
            {
                foreach (var pair in assetInfos)
                {
                    filename = Path.Combine(path, pair.Key);
                    if (File.Exists(filename))
                        File.Delete(filename);
                }
            }
        }

        public static void Package(string path
#if UNITY_EDITOR
        , bool isShowProgressBar
#endif
        )
        {
            string filename = Path.Combine(path, Path.GetFileName(path)), assetInfosFileName = filename + FILE_SUFFIX_ASSET_INFOS;
            var assetInfos = File.Exists(assetInfosFileName) ? LoadAssetInfos(File.ReadAllBytes(assetInfosFileName), out _) : null;
            if (assetInfos == null)
                return;

            using (var writer = new BinaryWriter(File.Open(filename + FILE_SUFFIX_ASSET_PACKAGE, FileMode.OpenOrCreate, FileAccess.Write)))
            {
#if UNITY_EDITOR
                int index = 0, count = assetInfos.Count;
#endif
                foreach (var assetName in assetInfos.Keys)
                {
#if UNITY_EDITOR
                    if (isShowProgressBar && UnityEditor.EditorUtility.DisplayCancelableProgressBar("Package", assetName, index++ * 1.0f / count))
                        break;
#endif
                    writer.Write(File.ReadAllBytes(Path.Combine(path, assetName)));
                }

#if UNITY_EDITOR
                if (isShowProgressBar)
                    UnityEditor.EditorUtility.ClearProgressBar();
#endif
            }
        }

        public IEnumerator GetOrDownload(
            Func<ulong, IEnumerator> confirm,
            DownloadHandler handler,
            params AssetPath[] paths)
        {
            bool isForce = confirm != null;
            string folder;
            Dictionary<string, Asset> packAssets;
            Dictionary<string, (IAssetPack, string, Dictionary<string, Asset>)> results = null;
            foreach (var path in paths)
            {
                packAssets = new Dictionary<string, Asset>();
                yield return __Load(packAssets, path.url, path.folder, isForce | path.assetPack != null);

                if (packAssets.Count < 1)
                    continue;

                folder = path.folder;
                if (!string.IsNullOrEmpty(folder))
                    folder = folder.Replace('\\', '/');

                if (results == null)
                    results = new Dictionary<string, (IAssetPack, string, Dictionary<string, Asset>)>();

                results.Add(folder, (path.assetPack, path.url, packAssets));
            }

            if (results == null)
                yield break;

            bool isNeedToSave = false;
            uint version;
            string assetName;
            (IAssetPack, string, Dictionary<string, Asset>) resultValue;
            Dictionary<string, uint> versions = null;
            List<string> assetNamesToDelete = null;
            if (__assets != null)
            {
                uint oldVersion;
                foreach (var oldAsset in __assets)
                {
                    assetName = oldAsset.Key;
                    folder = GetFolderName(assetName);

                    if(results.TryGetValue(folder, out resultValue))
                    {
                        if (versions == null)
                            versions = new Dictionary<string, uint>();

                        version = oldAsset.Value.data.info.version;
                        if (versions.TryGetValue(folder, out oldVersion))
                        {
                            if (version > oldVersion)
                                versions[folder] = version;
                        }
                        else
                            versions[folder] = version;

                        packAssets = resultValue.Item3;
                        if (!packAssets.ContainsKey(assetName))
                        {
                            if (assetNamesToDelete == null)
                                assetNamesToDelete = new List<string>();

                            assetNamesToDelete.Add(assetName);

#if DEBUG
                            Debug.Log($"Delete Asset: {assetName}");
#endif

                            isNeedToSave = string.IsNullOrEmpty(folder);
                        }
                    }
                }
            }

            int numAssets = 0, assetCount, assetIndex, i;
            ulong minSize = ulong.MaxValue, size = 0, startBytes;
            IAssetPack pack;
            IAssetPackHeader packHeader;
            AssetData destination;
            Asset source;
            KeyValuePair<string, Asset> asset;
            var assets = new List<KeyValuePair<string, Asset>>();
            Dictionary<string, ulong> folderAssetStartBytes = null;
            foreach (var result in results)
            {
                folder = result.Key;
                pack = result.Value.Item1;
                packAssets = result.Value.Item3;

                packHeader = null;

                assets.Clear();
                assets.AddRange(packAssets);

                startBytes = 0;
                assetIndex = 0;

                assetCount = assets.Count;
                for (i = 0; i < assetCount; ++i)
                {
                    asset = assets[i];
                    assetName = asset.Key;
                    destination = asset.Value.data;
                    if (__assets == null || 
                        (__assets.TryGetValue(assetName, out source) ? 
                        (source.data.isReadOnly && (pack == null ? confirm == null : !pack.Contains(source.data.pack.name)) ? Math.Max(source.data.info.version, 1) - 1 : source.data.info.version) : //Ϊ���÷����İ����滻
                        (versions != null && versions.TryGetValue(folder, out version) ? Math.Max(version, 1) - 1 : 0)) < 
                        destination.info.version)
                    {
#if DEBUG
                        Debug.Log($"Update Asset {assetName} To Version {destination.info.version}(URL: {result.Value.Item2})");
#endif

                        ++numAssets;

                        minSize = Math.Min(minSize, destination.info.size);

                        if(pack == null)
                            size += destination.info.size;
                        else if (packHeader == null)
                            packHeader = pack.header;
                    }
                    else
                    {
                        if (assetIndex == i)
                        {
                            startBytes += destination.info.size;

                            ++assetIndex;
                        }
                        else
                            assetIndex = -1;

                        packAssets.Remove(assetName);
                    }
                }

                if (assetIndex >= 0)
                {
                    if (folderAssetStartBytes == null)
                        folderAssetStartBytes = new Dictionary<string, ulong>();

                    folderAssetStartBytes.Add(result.Key ?? string.Empty, startBytes);
                }

                if (packHeader != null)
                {
                    while (!packHeader.isDone)
                        yield return null;

                    size += packHeader.fileSize;
                }
            }

            if (numAssets < 1)
                yield break;

            if (confirm != null)
                yield return confirm(size);

            int index;
            if (assetNamesToDelete != null)
            {
                int numAssetNamesToDelete = assetNamesToDelete.Count;
                index = 0;
                assetName = null;
                
#if ASSET_MANAGER_USE_TASK
                using (var task = Task.Run(() =>
                     {
#endif
                         do
                         {
                             try
                             {
                                 assetName = assetNamesToDelete[index];

                                 __Delete(assetName);
                                 
#if !ASSET_MANAGER_USE_TASK
                                 if(handler != null)
                                     handler(
                                         assetName,
                                         0.0f,
                                         0,
                                         0,
                                         size,
                                         index,
                                         numAssetNamesToDelete);
#endif
                             }
                             catch (Exception e)
                             {
                                 Debug.LogException(e.InnerException ?? e);
                             }

                         } while (++index < numAssetNamesToDelete);
                         
#if ASSET_MANAGER_USE_TASK
                     }))
                {
                    do
                    {
                        yield return null;
                        try
                        {
                            if(handler != null)
                                handler(
                                    assetName,
                                    0.0f,
                                    0,
                                    0,
                                    size,
                                    index,
                                    numAssetNamesToDelete);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e.InnerException ?? e);
                        }
                    } while (!task.IsCompleted);
                }
#endif

                assetNamesToDelete = null;

                if (isNeedToSave)
                    SaveFolder();
            }

            bool isDownloading;
            int totalAssetIndex = 0, count, length;
            ulong totalBytesDownload = 0, downloadedBytes, fileOffset, packSize, packDownloadedBytes, oldPackDownloadedBytes;
            long responseCode;
            string fileName, url, fullURL, packName, filePath;
            
#if ASSET_MANAGER_USE_TASK
            Exception exception;
#endif
            
            (ulong, string) fileOffsetAndPath;
            Writer writer;
            byte[] md5hash, preallocatedBuffer = null;
            Dictionary<string, (ulong, string)> fileOffsetsAndPaths = null;
            using (var md5 = new MD5CryptoServiceProvider())
            {
                foreach (var result in results)
                {
                    resultValue = result.Value;
                    packAssets = resultValue.Item3;
                    if (packAssets.Count < 1)
                        continue;

                    folder = result.Key;

                    isNeedToSave = GetVersion(folder) != VERSION;
                    if (__assets != null)
                    {
                        foreach (var key in packAssets.Keys)
                            isNeedToSave = __assets.Remove(key) || isNeedToSave;
                    }

                    using (writer = new Writer(folder, this))
                    {
                        if(isNeedToSave)
                            writer.Save();

                        fullURL = resultValue.Item2;

                        url = fullURL;
                        fileName = Path.GetFileName(url);
                        url = url.Remove(url.LastIndexOf(fileName));

                        assets.Clear();
                        assets.AddRange(packAssets);

                        assetCount = assets.Count;
                        //assetIndex = 0;

                        pack = resultValue.Item1;
                        packHeader = pack == null ? null : pack.header;
                        if (packHeader == null)
                        {
                            packSize = 0;

                            packName = folder;
                        }
                        else
                        {
                            while (!packHeader.isDone)
                            {
                                yield return null;
                            }

                            packSize = packHeader.fileSize;

                            packName = packHeader.name;
                        }

                        if (folderAssetStartBytes != null && folderAssetStartBytes.TryGetValue(folder, out startBytes))
                        {
                            if (pack == null)
                            {
                                fullURL += FILE_SUFFIX_ASSET_PACKAGE;

                                if (preallocatedBuffer == null)
                                    preallocatedBuffer = new byte[minSize];

                                using (var downloadFileHandler = new DownloadFileHandler(preallocatedBuffer))
                                using (var www = new UnityWebRequest(fullURL, UnityWebRequest.kHttpVerbGET,
                                           downloadFileHandler, null))
                                {
                                    //downloadFileHandler.Clear();

                                    www.SetRequestHeader("Range", $"bytes={startBytes}-");

                                    www.SendWebRequest();

                                    do
                                    {
                                        yield return null;

                                        responseCode = www.responseCode;
                                    } while (responseCode == -1);

                                    downloadFileHandler.Init(
                                        responseCode == 206 ? 0 : startBytes,
                                        //responseCode == 206 ? startBytes : 0,
                                        AssetPack
                                            .Default, //string.IsNullOrEmpty(filePath) ? null : filePath + FILE_SUFFIX_ASSET_PACKAGE,
                                        assets,
                                        writer);

                                    if (handler == null)
                                        yield return www;
                                    else
                                    {
                                        while (!www.isDone)
                                        {
                                            yield return null;

                                            downloadedBytes = downloadFileHandler.bytesDownloaded;

                                            handler(
                                                downloadFileHandler.assetName,
                                                downloadFileHandler.assetProgress,
                                                (uint)downloadedBytes,
                                                totalBytesDownload + downloadFileHandler.fileBytesDownloaded +
                                                downloadedBytes,
                                                size,
                                                totalAssetIndex + downloadFileHandler.assetCount,
                                                numAssets);
                                        }
                                    }

                                    assetIndex = downloadFileHandler.assetCount;
                                    totalAssetIndex += assetIndex;
                                    totalBytesDownload += downloadFileHandler.fileBytesDownloaded;
                                }
                            }
                            else
                            {
                                filePath = packHeader?.filePath;

                                if (string.IsNullOrEmpty(filePath))
                                {
                                    assetIndex = 0;//assets.assetCount;
                                    /*totalAssetIndex += assetIndex;
                                    totalBytesDownload += downloadFileHandler.fileBytesDownloaded;*/
                                }
                                else
                                {
                                    if (preallocatedBuffer == null)
                                        preallocatedBuffer = new byte[minSize];

                                    using (var downloadFileHandler = new DownloadFileHandler(preallocatedBuffer))
                                    {
                                        downloadFileHandler.Init(
                                            0,
                                            new AssetPack(packName, filePath, startBytes),
                                            assets,
                                            writer);

                                        oldPackDownloadedBytes = 0;

                                        isDownloading = true;
                                        do
                                        {
                                            yield return null;

                                            packDownloadedBytes =
                                                (ulong)Math.Round(packSize * (double)pack.downloadProgress);

#if ASSET_MANAGER_USE_TASK
                                            using (var task = Task.Run(() =>
                                                   {
#endif
                                                       isDownloading =
                                                           downloadFileHandler.ReceiveData(
                                                               (int)(packDownloadedBytes - oldPackDownloadedBytes));
#if ASSET_MANAGER_USE_TASK
                                                   }))
                                            {

                                                do
                                                {

                                                    yield return null;
#endif

                                                    if (handler != null)
                                                    {
                                                        downloadedBytes = downloadFileHandler.bytesDownloaded;

                                                        handler(
                                                            downloadFileHandler.assetName,
                                                            downloadFileHandler.assetProgress,
                                                            (uint)downloadedBytes,
                                                            totalBytesDownload +
                                                            downloadFileHandler.fileBytesDownloaded +
                                                            downloadedBytes,
                                                            size,
                                                            totalAssetIndex + downloadFileHandler.assetCount,
                                                            numAssets);
                                                    }
#if ASSET_MANAGER_USE_TASK
                                                    exception = task.Exception;
                                                    if (exception != null)
                                                    {
                                                        Debug.LogException(exception.InnerException ?? exception);

                                                        isDownloading = false;

                                                        break;
                                                    }
                                                } while (!task.IsCompleted);
                                            }
#endif

                                            oldPackDownloadedBytes = packDownloadedBytes;
                                        } while (isDownloading);
                                        
                                        assetIndex = downloadFileHandler.assetCount;
                                        totalAssetIndex += assetIndex;
                                        totalBytesDownload += downloadFileHandler.fileBytesDownloaded;
                                    }
                                }
                            }

                            //GarbageCollector.CollectIncremental();
                        }
                        else
                            assetIndex = 0;

                        if (!string.IsNullOrEmpty(folder))
                        {
                            length = url.Length;
                            count = folder.Length;
                            if (length > count)
                            {
                                index = length - count - 1;
                                if (url.Substring(index, count).Replace('\\', '/') == folder)
                                    url = url.Remove(index);
                            }
                        }

                        if (pack == null)
                        {
                            while (assetIndex < assetCount)
                            {
                                asset = assets[assetIndex++];
                                assetName = asset.Key;
                                destination = asset.Value.data;

                                fullURL = url + GetFilePath(assetName, destination.info.fileName);

                                filePath = __GetAssetPath(assetName, null);
                                CreateDirectory(filePath);

                                using (var downloadHandlerFile = new DownloadHandlerFile(filePath))
                                {
                                    while (true)
                                    {
                                        using (var www = new UnityWebRequest(fullURL, UnityWebRequest.kHttpVerbGET, downloadHandlerFile, null))
                                        {
                                            if (handler == null)
                                            {
                                                yield return www.SendWebRequest();

                                                downloadedBytes = www.downloadedBytes;
                                            }
                                            else
                                            {
                                                var asyncOperation = www.SendWebRequest();

                                                do
                                                {
                                                    yield return null;

                                                    downloadedBytes = www.downloadedBytes;
                                                    try
                                                    {
                                                        handler(
                                                            assetName,
                                                            www.downloadProgress,
                                                            (uint)downloadedBytes,
                                                            totalBytesDownload + downloadedBytes,
                                                            size,
                                                            totalAssetIndex,
                                                            numAssets);
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        Debug.LogError(e.InnerException ?? e);
                                                    }
                                                } while (!asyncOperation.isDone);
                                            }

                                            if (www.result != UnityWebRequest.Result.Success)
                                            {
                                                Debug.LogError($"{fullURL} : { www.error}");

                                                if (isForce)
                                                    continue;
                                                else
                                                    break;
                                            }
                                        }

                                        //data = www.downloadHandler?.data;
                                        if (!File.Exists(filePath))
                                            continue;

                                        md5hash = md5.ComputeHash(File.OpenRead(filePath));

                                        destination.info.fileName = string.Empty;
                                        
                                        if (destination.info.md5 == null)
                                            destination.info.md5 = md5hash;
                                        else if (!MemoryEquals(md5hash, destination.info.md5))
                                        {
                                            Debug.LogError($"{url}{assetName} MD5 Fail.");

                                            continue;
                                        }

                                        try
                                        {
                                            writer.Write(assetName, destination/*, data, 0, (uint)downloadedBytes*/);
                                        }
                                        catch (Exception e)
                                        {
                                            Debug.LogError(e.InnerException ?? e);

                                            continue;
                                        }
                                        /*finally
                                        {
                                            GarbageCollector.CollectIncremental();
                                        }*/

                                        break;
                                    }
                                }

                                totalBytesDownload += downloadedBytes;

                                ++totalAssetIndex;
                            }
                        }
                        else
                        {
                            while (!pack.isDone)
                            {
                                if (handler != null)
                                {
                                    try
                                    {
                                        packDownloadedBytes = (ulong)Math.Round(packSize * pack.downloadProgress);

                                        handler(
                                            packName,
                                            pack.downloadProgress,
                                            (uint)packDownloadedBytes,
                                            totalBytesDownload + packDownloadedBytes,
                                            size,
                                            totalAssetIndex,
                                            numAssets);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogError(e.InnerException ?? e);
                                    }
                                }

                                yield return null;
                            }

                            totalBytesDownload += packSize;

                            if (assetIndex < assetCount)
                            {
                                if (fileOffsetsAndPaths == null)
                                    fileOffsetsAndPaths = new Dictionary<string, (ulong, string)>();
                                else
                                    fileOffsetsAndPaths.Clear();

                                for (i = assetIndex; i < assetCount; ++i)
                                {
                                    asset = assets[i];

                                    assetName = asset.Key;
                                    if (!pack.GetFileInfo(GetFilePath(assetName, asset.Value.data.info.fileName),
                                            out fileOffset, out filePath))
                                        continue;

                                    fileOffsetsAndPaths.Add(assetName, (fileOffset, filePath));
                                }

                                //filePath = Path.GetDirectoryName(filePath);

                                do
                                {
                                    assetName = null;
                                    destination = default;

#if ASSET_MANAGER_USE_TASK
                                    using (var task = Task.Run(() =>
                                           {
#endif
                                               while (assetIndex < assetCount)
                                               {
                                                   asset = assets[assetIndex];

                                                   assetName = asset.Key;

#if !ASSET_MANAGER_USE_TASK
                                             try
                                             {
                                                 if (handler != null)
                                                     handler(
                                                         assetName,
                                                         1.0f, //pack.downloadProgress,
                                                         (uint)packSize,
                                                         totalBytesDownload,
                                                         size,
                                                         totalAssetIndex,
                                                         numAssets);
                                             }
                                             catch (Exception e)
                                             {
                                                 Debug.LogError(e.InnerException ?? e);
                                             }
#endif

                                                   if (!fileOffsetsAndPaths.TryGetValue(assetName,
                                                           out fileOffsetAndPath))
                                                   {
                                                       Debug.LogError($"Asset pack {assetName} can not been found!");

                                                       break;
                                                   }

                                                   fileOffset = fileOffsetAndPath.Item1;

                                                   destination = asset.Value.data;

                                                   destination.pack = new AssetPack(packName, fileOffsetAndPath.Item2,
                                                       fileOffset);

                                                   writer.Write(assetName, destination);

                                                   //totalBytesDownload += destination.info.size;

                                                   ++totalAssetIndex;

                                                   ++assetIndex;
                                               }
#if ASSET_MANAGER_USE_TASK
                                           }))
                                    {
                                        do
                                        {
                                            yield return null;

                                            try
                                            {
                                                if (handler != null)
                                                    handler(
                                                        assetName,
                                                        1.0f, //pack.downloadProgress,
                                                        (uint)packSize,
                                                        totalBytesDownload,
                                                        size,
                                                        totalAssetIndex,
                                                        numAssets);
                                            }
                                            catch (Exception e)
                                            {
                                                Debug.LogError(e.InnerException ?? e);
                                            }

                                            exception = task.Exception;
                                            if (exception != null)
                                            {
                                                Debug.LogException(exception.InnerException ?? exception);

                                                break;
                                            }
                                        } while (!task.IsCompleted);

                                        if (exception == null && !isForce)
                                            break;
                                    }
#endif
                                } while (assetIndex < assetCount);
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerator __Load(Dictionary<string, Asset> assets, string url, string folder, bool isForce)
        {
            if (assets == null)
                yield break;

            byte[] bytes = null;
            AssetBundle assetBundle = null;
            do
            {
                using (var www = UnityWebRequest.Get(url))
                {
                    /*if (timeout > 0.0f)
                    {
                        www.SendWebRequest();

                        float time = Time.time;
                        while (!www.isDone)
                        {
                            yield return null;
                            if ((Time.time - time) > timeout)
                            {
                                Debug.LogError("Timeout.");

                                yield break;
                            }
                        }
                    }
                    else*/
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        bytes = www.downloadHandler?.data;

                        using (var memoryStream = new MemoryStream(bytes))
                        {
                            if (__Load(memoryStream, folder, out _, ref assets))
                                yield break;
                        }

                        assetBundle = AssetBundle.LoadFromMemory(bytes);
                        if(assetBundle == null)
                        {
                            Debug.LogError(url);

                            if (isForce)
                                bytes = null;
                            else
                                yield break;
                        }
                    }
                    else
                    {
                        Debug.LogError(url + ':' + www.error);

                        if (!isForce)
                            yield break;
                    }
                }
            } while (bytes == null);

            var assetBundleManifest = assetBundle.LoadAsset<AssetBundleManifest>("assetBundleManifest");
            string[] assetBundleNames = assetBundleManifest == null ? null : assetBundleManifest.GetAllAssetBundles();
            if (assetBundleNames == null)
            {
                assetBundle.Unload(true);

                UnityEngine.Object.Destroy(assetBundle);

                yield break;
            }

            Dictionary<string, AssetInfo> assetInfos = null;
            do
            {
                using (var www = UnityWebRequest.Get(url + FILE_SUFFIX_ASSET_INFOS))
                {
                    /*if (timeout > 0.0f)
                    {
                        uwr.SendWebRequest();

                        float time = Time.time;
                        while (!uwr.isDone)
                        {
                            yield return null;
                            if ((Time.time - time) > timeout)
                            {
                                Debug.LogError("Timeout.");

                                yield break;
                            }
                        }
                    }
                    else*/
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                        assetInfos = LoadAssetInfos(www.downloadHandler?.data, out _);
                    else
                        Debug.LogError(url + ':' + www.error);
                }
            } while (assetInfos == null);

            Dictionary<string, AssetType> assetTypes = null;
            using (var www = UnityWebRequest.Get(url + FILE_SUFFIX_ASSET_CONFIG))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    var assetList = LoadAssetList(www.downloadHandler?.text);
                    if(assetList != null && assetList.assets != null)
                    {
                        foreach(var assetItem in assetList.assets)
                        {
                            if (assetTypes == null)
                                assetTypes = new Dictionary<string, AssetType>();

                            assetTypes.Add(assetItem.name, assetItem.type);
                        }
                    }
                }
            }

            string assetName, fileName;
            var assetNames = new Dictionary<string, string>();
            foreach (var assetInfo in assetInfos)
            {
                assetName = assetInfo.Key;
                fileName = assetInfo.Value.fileName;
                fileName = string.IsNullOrEmpty(fileName) ? assetName : fileName;
                assetNames[fileName] = assetName;
            }

            int i, numDependencies;
            
            Asset asset;
            asset.offset = -1L;
            asset.data.pack = AssetPack.Default;
            if (string.IsNullOrEmpty(folder))
            {
                foreach (string assetBundleName in assetBundleNames)
                {
                    if (!assetNames.TryGetValue(assetBundleName, out assetName) || !assetInfos.TryGetValue(assetName, out asset.data.info))
                    {
                        /*asset.info.version = 0;
                        asset.info.size = 0;
                        asset.info.md5 = new byte[16];*/

                        Debug.LogError($"Missing Asset Bundle {assetBundleName}");

                        continue;
                    }

                    if (assetTypes == null || !assetTypes.TryGetValue(assetName, out asset.data.type))
                        asset.data.type = AssetType.Uncompressed;
                    else
                        assetTypes.Remove(assetName);

                    //asset.data.pack = AssetPack.Default;
                    asset.data.dependencies = assetBundleManifest.GetDirectDependencies(assetBundleName);
                    numDependencies = asset.data.dependencies == null ? 0 : asset.data.dependencies.Length;
                    for(i = 0; i < numDependencies; ++i)
                    {
                        asset.data.dependencies[i] = assetNames.TryGetValue(asset.data.dependencies[i], out fileName)
                            ? fileName
                            : null;
                    }

                    assets.Add(assetName, asset);
                }
            }
            else
            {
                folder = FilterFolderName(folder) + '/';

                foreach (string assetBundleName in assetBundleNames)
                {
                    if (!assetNames.TryGetValue(assetBundleName, out assetName) || !assetInfos.TryGetValue(assetName, out asset.data.info))
                    {
                        Debug.LogError($"Missing Asset Bundle {assetBundleName}");

                        continue;
                    }

                    if (assetTypes == null || !assetTypes.TryGetValue(assetName, out asset.data.type))
                        asset.data.type = AssetType.Uncompressed;
                    else
                        assetTypes.Remove(assetName);

                    //asset.data.pack = AssetPack.Default;
                    asset.data.dependencies = assetBundleManifest.GetDirectDependencies(assetBundleName);
                    numDependencies = asset.data.dependencies == null ? 0 : asset.data.dependencies.Length;
                    for (i = 0; i < numDependencies; ++i)
                        asset.data.dependencies[i] = assetNames.TryGetValue(asset.data.dependencies[i], out fileName) ? folder + fileName : null;

                    assets.Add(folder + assetName, asset);
                }
            }

            if(assetTypes != null)
            {
                asset.data.dependencies = null;

                foreach (var pair in assetTypes)
                {
                    assetName = pair.Key;
                    if (!assetInfos.TryGetValue(assetName, out asset.data.info))
                        continue;

                    asset.data.type = pair.Value;

                    assets.Add(folder + assetName, asset);
                }
            }

            assetBundle.Unload(true);

            UnityEngine.Object.Destroy(assetBundle);
        }
    }
}