using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZG
{
    public partial class AssetManager : IEnumerable<KeyValuePair<string, AssetManager.Asset>>
    {
        public enum AssetType
        {
            //只读且无法解压
            Uncompressed,
            //已运行时解压
            UncompressedRuntime,
            //只读，运行时完全解压
            Compressed,
            //只读，运行时以LZ4解压
            LZ4,
            //只读，运行时拷贝不解压
            Stream
        }

        [Serializable]
        public class AssetList
        {
            [Serializable]
            public struct Asset
            {
                public string name;
                public AssetType type;
            }

            public List<Asset> assets;
        }

        public struct AssetPack
        {
            public static readonly AssetPack Default = default;

            public string name;

            public string filePath;

            public ulong fileOffset;

            public bool isVail => !string.IsNullOrEmpty(filePath);

            //public bool canRecompress => true;// string.IsNullOrEmpty(filePath) || fileOffset == 0;

            public AssetPack(string name, string filePath, ulong fileOffset)
            {
                this.name = name;
                this.filePath = filePath;
                this.fileOffset = fileOffset;
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(fileOffset);
                writer.Write(string.IsNullOrEmpty(filePath) ? string.Empty : filePath);
                writer.Write(string.IsNullOrEmpty(name) ? string.Empty : name);
            }

            public static AssetPack Read(BinaryReader reader, uint version)
            {
                AssetPack pack;
                if (version > 3)
                {
                    pack.fileOffset = reader.ReadUInt64();
                    pack.filePath = reader.ReadString();

                    if (version > 6)
                        pack.name = reader.ReadString();
                    else
                        pack.name = string.IsNullOrEmpty(pack.filePath) ? null : DefaultAssetPackHeader.NAME;
                }
                else
                {
                    pack.fileOffset = 0;
                    pack.filePath = null;
                    pack.name = null;
                }

                return pack;
            }

        }

        public struct AssetInfo
        {
            public uint version;
            public uint size;
            public string fileName;
            public byte[] md5;

            public static AssetInfo Read(BinaryReader reader, uint version)
            {
                AssetInfo assetInfo;
                assetInfo.version = reader.ReadUInt32();
                assetInfo.size = reader.ReadUInt32();

                if (version < 1)
                {
                    assetInfo.fileName = null;
                    assetInfo.md5 = null;
                }
                else if (version < 9)
                {
                    assetInfo.fileName = null;
                    assetInfo.md5 = reader.ReadBytes(16);
                }
                else
                {
                    assetInfo.fileName = reader.ReadString();
                    assetInfo.md5 = reader.ReadBytes(16);
                }
                
                return assetInfo;
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(version);
                writer.Write(size);

                writer.Write(fileName ?? string.Empty);
                UnityEngine.Assertions.Assert.AreEqual(16, md5.Length);

                writer.Write(md5);
            }
        }

        public struct AssetData
        {
            public AssetType type;

            public AssetInfo info;

            /*public ulong fileOffset;

            public string filePath;*/
            public AssetPack pack;

            public string[] dependencies;

            public bool isReadOnly => type != AssetType.UncompressedRuntime && pack.isVail;

            public static AssetData Read(BinaryReader reader, uint version)
            {
                AssetData data;
                if (version > 5)
                    data.type = (AssetType)reader.ReadByte();
                else
                    data.type = AssetType.Uncompressed;

                data.info = AssetInfo.Read(reader, version);
                data.pack = AssetPack.Read(reader, version);

                int numDependencies = reader.ReadInt32();
                data.dependencies = numDependencies > 0 ? new string[numDependencies] : null;
                for (int i = 0; i < numDependencies; ++i)
                    data.dependencies[i] = reader.ReadString();

                return data;
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write((byte)type);

                info.Write(writer);

                pack.Write(writer);

                int numDependencies = dependencies == null ? 0 : dependencies.Length;
                writer.Write(numDependencies);
                for (int i = 0; i < numDependencies; ++i)
                    writer.Write(dependencies[i]);
            }
        }

        public struct Asset
        {
            public long offset;

            public AssetData data;
        }

        /*public class AssetDownloadHandler : DownloadHandlerScript, IDisposable
        {
            private ulong __streamOffset;
            private ulong __streamLength;
            private ulong __startBytes;
            private ulong __overrideOffset;
            private string __overridePath;
            private MemoryStream __stream;
            private AssetManager __manager;
            private IReadOnlyList<KeyValuePair<string, Asset>> __assets;

            private ReaderWriterLockSlim __lock;

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

            public AssetDownloadHandler(
                ulong maxSize,
                AssetManager manager) : base(new byte[maxSize])
            {
                isDownloading = true;

                __manager = manager;

                __lock = new ReaderWriterLockSlim();
            }

            public new void Dispose()
            {
                if (__stream != null)
                    __stream.Dispose();

                base.Dispose();
            }

            public void Clear()
            {
                isDownloading = true;

                __lock.EnterWriteLock();

                __streamOffset = 0;
                __streamLength = 0;

                if (__stream != null)
                    __stream.Position = 0L;

                __lock.ExitWriteLock();
            }

            public void Init(
                ulong startBytes,
                ulong overrideOffset,
                string overridePath,
                IReadOnlyList<KeyValuePair<string, Asset>> assets)
            {
                __startBytes = startBytes;

                __overrideOffset = overrideOffset;
                __overridePath = overridePath;

                bytesDownloaded = 0;
                fileBytesDownloaded = 0;

                assetCount = 0;
                __assets = assets;
            }

            public bool ThreadUpdate(bool isDone, int count)
            {
                if (isDownloading)
                    isDownloading = __Update(isDone, count);

                return isDownloading;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (isDownloading)
                {
                    ulong streamLength = __streamLength + (ulong)dataLength;
                    if (string.IsNullOrEmpty(__overridePath))
                    {
                        __lock.EnterUpgradeableReadLock();

                        ulong streamOffset = this.fileBytesDownloaded + __startBytes;
                        if (streamOffset < streamLength)
                        {
                            bool isWrite = false;
                            try
                            {
                                if (__stream == null)
                                {
                                    isWrite = true;

                                    __lock.EnterWriteLock();

                                    __stream = new MemoryStream();
                                }

                                if (streamOffset > __streamOffset)
                                {
                                    if (streamOffset < __streamLength)
                                    {
                                        int count = (int)(__streamLength - streamOffset);
                                        var buffer = new byte[count];
                                        __stream.Seek(-count, SeekOrigin.Current);
                                        __stream.Read(buffer, 0, count);

                                        if (!isWrite)
                                        {
                                            isWrite = true;

                                            __lock.EnterWriteLock();
                                        }

                                        __stream.Position = 0;
                                        __stream.Write(buffer, 0, count);

                                        __stream.Write(data, 0, dataLength);
                                    }
                                    else
                                    {
                                        if (!isWrite)
                                        {
                                            isWrite = true;

                                            __lock.EnterWriteLock();
                                        }

                                        __stream.Position = 0;
                                        int offset = (int)(streamOffset - __streamLength);
                                        __stream.Write(data, offset, dataLength - offset);
                                    }

                                    __streamOffset = streamOffset;
                                }
                                else
                                {
                                    if (!isWrite)
                                    {
                                        isWrite = true;

                                        __lock.EnterWriteLock();
                                    }

                                    __stream.Write(data, 0, dataLength);
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogError(e.InnerException ?? e);

                                streamLength = __streamLength;

                                isDownloading = false;
                            }
                            finally
                            {
                                if (isWrite)
                                    __lock.ExitWriteLock();
                            }
                        }
                        else
                        {
                            __streamOffset += (ulong)(dataLength + __stream.Position);

                            __stream.Position = 0L;
                        }

                        __lock.ExitUpgradeableReadLock();
                    }

                    __streamLength = streamLength;

                    //Debug.LogError($"ReceiveData {dataLength}");
                }

                return isDownloading;
            }

            //TODO: Save Mem
            public bool __Update(bool isDone, int count)
            {
                KeyValuePair<string, Asset> pair;
                //string assetName;
                AssetData data;
                int offset;
                uint assetBytesDownloaded;//, bytesDownloaded;
                ulong fileBytesDownloaded;
                byte[] buffer;
                for (int i = 0; i < count; ++i)
                {
                    fileBytesDownloaded = this.fileBytesDownloaded + __startBytes;
                    if (__streamLength <= fileBytesDownloaded)
                        return !isDone;

                    assetBytesDownloaded = (uint)(__streamLength - fileBytesDownloaded);

                    //Debug.LogError($"__Update {assetBytesDownloaded}");

                    pair = __assets[assetCount];
                    data = pair.Value.data;

                    assetName = pair.Key;

                    bytesDownloaded = Math.Min(assetBytesDownloaded, data.info.size);

                    assetProgress = (float)(bytesDownloaded * 1.0 / data.info.size);

                    if (assetBytesDownloaded < data.info.size)
                        return !isDone;

                    if (string.IsNullOrEmpty(__overridePath))
                    {
                        __lock.EnterReadLock();

                        offset = (int)(fileBytesDownloaded - __streamOffset);
                        buffer = __stream.GetBuffer();
                        using (var md5 = new MD5CryptoServiceProvider())
                        {
                            var md5Hash = md5.ComputeHash(buffer, offset, (int)data.info.size);
                            if (data.info.md5 == null)
                                data.info.md5 = md5Hash;
                            else if (!MemoryEquals(md5Hash, data.info.md5))
                            {
                                __lock.ExitReadLock();

                                Debug.LogError($"{assetName} MD5 Fail.Offset : {__streamOffset}");

                                return false;
                            }
                        }

                        try
                        {
                            __manager.Create(assetName, data, buffer, offset, data.info.size);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);

                            return false;
                        }
                        finally
                        {
                            __lock.ExitReadLock();
                        }
                    }
                    else
                    {
                        data.fileOffset = __overrideOffset + fileBytesDownloaded;
                        data.filePath = __overridePath;

                        try
                        {
                            __manager.__Create(assetName, data);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);

                            return false;
                        }
                    }

                    this.fileBytesDownloaded += data.info.size;
                    //assetBytesDownloaded = 0;

                    //++__totalAssetIndex;

                    if (++assetCount >= __assets.Count)
                        return false;
                }

                return true;
            }
        }*/

        public const uint VERSION = 9;
        public const string FILE_SUFFIX_ASSET_INFOS = ".info";

        private string __path;
        private Dictionary<string, Asset> __assets;

        public static string GetPlatformPath(string path)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    return "file:///" + path;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return "file://" + path;
            }

            return path;
        }

        public static bool MemoryEquals(byte[] source, byte[] destination)
        {
            int length = source.Length;
            if (length != destination.Length)
                return false;

            for (int i = 0; i < length; ++i)
            {
                if (source[i] != destination[i])
                    return false;
            }

            return true;
        }

        /*public static AssetManager Create(string path, IEnumerable<KeyValuePair<string, Asset>> assets)
        {
            AssetManager assetManager = new AssetManager();
            assetManager.__path = path;

            if (assets != null)
            {
                assetManager.__assets = new Dictionary<string, Asset>();
                foreach (KeyValuePair<string, Asset> asset in assets)
                    assetManager.__assets.Add(asset.Key, asset.Value);
            }

            assetManager.Save();

            return assetManager;
        }*/

        public uint version
        {
            get;

            private set;
        }

        public int assetCount
        {
            get
            {
                return __assets == null ? 0 : __assets.Count;
            }
        }

        public string path
        {
            get
            {
                return __path;
            }
        }

        public static string GetFilePath(string assetName, string fileName)
        {
            string path;
            if (string.IsNullOrEmpty(fileName))
                path = assetName;
            else
            {
                string folder = Path.GetDirectoryName(assetName);
                path = string.IsNullOrEmpty(folder) ? fileName : Path.Combine(folder, fileName);
            }
            
            return path;
        }
        
        public AssetManager(string path, IAssetBundleFactory factory = null) : this(factory)
        {
            __path = path;

            LoadFrom(null);
        }

        public bool Contains(string folder)
        {
            if (folder == null)
                return false;

            Dictionary<string, Asset>.KeyCollection keys = __assets == null ? null : __assets.Keys;
            if (keys == null)
                return false;

            folder = folder.Replace('\\', '/') + '/';
            foreach (string key in keys)
            {
                if (key != null && key.Contains(folder))
                    return true;
            }

            return false;
        }

        public int CountOf(string folder)
        {
            var keys = __assets == null ? null : __assets.Keys;
            if (keys == null)
                return 0;

            if (!string.IsNullOrEmpty(folder))
                folder = folder.Replace('\\', '/');

            int result = 0;
            string name;
            foreach (string key in keys)
            {
                if (key == null)
                    continue;

                name = Path.GetDirectoryName(key);
                if (string.IsNullOrEmpty(folder) ? string.IsNullOrEmpty(name) : name.Replace('\\', '/') == folder)
                    ++result;
            }

            return result;
        }

        public bool Get(string name, out Asset asset)
        {
            if (__assets == null)
            {
                asset = default(Asset);

                return false;
            }

            return __assets.TryGetValue(name, out asset);
        }

        public bool GetAssetPath(string name, out Asset asset, out ulong fileOffset, out string filePath)
        {
            if (__assets == null || !__assets.TryGetValue(name, out asset))
            {
                asset = default;
                fileOffset = 0U;
                filePath = null;

                return false;
            }

            if (asset.data.isReadOnly)
            {
                fileOffset = asset.data.pack.fileOffset;
                filePath = asset.data.pack.filePath;

                AssetUtility.UpdatePack(asset.data.pack.name, ref filePath, ref fileOffset);
            }
            else
            {
                fileOffset = 0;
                filePath = __GetAssetPath(name, asset.data.info.fileName);
            }

            return true;
        }

        public IEnumerator<KeyValuePair<string, Asset>> GetEnumerator()
        {
            if (__assets == null)
                __assets = new Dictionary<string, Asset>();

            return __assets.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private string __GetManagerPath(string folder)
        {
            return string.IsNullOrEmpty(folder) ? __path : Path.Combine(Path.GetDirectoryName(__path), folder, Path.GetFileName(folder));
        }

        private string __GetAssetPath(string assetName, string fileName)
        {
            return Path.Combine(Path.GetDirectoryName(__path), GetFilePath(assetName, fileName));
        }
    }
}