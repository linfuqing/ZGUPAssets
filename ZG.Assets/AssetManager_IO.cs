using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace ZG
{
    public partial class AssetManager
    {
        public struct StreamWrapper : IDisposable
        {
            public readonly Stream Stream;

            public readonly long length
            {
                get
                {
                    return Math.Max(Stream.Length - 16L, sizeof(UInt32));
                }
            }

            public StreamWrapper(Stream stream)
            {
                Stream = stream;
            }

            public void Dispose()
            {
                if (Stream != null)
                    Stream.Dispose();
            }

            public void Write(long offset, byte value)
            {
                Stream.Position = offset;
                Stream.WriteByte(value);

                Update(length);
            }

            public void Update(long length)
            {
                using (var md5 = new MD5CryptoServiceProvider())
                {
                    Stream.SetLength(length);

                    //VERSION
                    Stream.Position = sizeof(UInt32);

                    //var bytes = new byte[length - sizeof(UInt32)];

                    //Stream.Read(bytes, 0, (int)length - sizeof(UInt32));

                    var md5Hash = md5.ComputeHash(Stream);

                    UnityEngine.Assertions.Assert.AreEqual(16, md5Hash.Length);

                    Stream.Position = length;
                    Stream.Write(md5Hash, 0, 16);
                }

                UnityEngine.Assertions.Assert.IsTrue(Verify());
            }

            public readonly bool Verify()
            {
                long length = this.length;
                Stream.Position = length;

                byte[] md5Hash = new byte[16];
                Stream.Read(md5Hash, 0, 16);

                Stream.Position = sizeof(UInt32);
                using (var md5 = new MD5CryptoServiceProvider())
                {
                    length -= sizeof(UInt32);

                    byte[] bytes = new byte[length];

                    Stream.Read(bytes, 0, (int)length);

                    if (!MemoryEquals(md5.ComputeHash(bytes), md5Hash))
                        return false;
                }

                return true;
            }
        }

        public struct Writer : IDisposable
        {
            public readonly string Folder;
            public readonly AssetManager AssetManager;
            public readonly StreamWrapper StreamWrapper;

            private BinaryReader __reader;
            private BinaryWriter __writer;

            public Writer(string folder, AssetManager assetManager)
            {
                Folder = FilterFolderName(folder);
                AssetManager = assetManager;

                string path = assetManager.__GetManagerPath(folder);

                CreateDirectory(path);

                StreamWrapper = new StreamWrapper(File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite));

                __reader = new BinaryReader(StreamWrapper.Stream);
                __writer = new BinaryWriter(StreamWrapper.Stream);
            }

            public void Dispose()
            {
                 StreamWrapper.Dispose();
            }

            public void Save()
            {
                StreamWrapper.Stream.Position = 0L;

                int count = AssetManager.CountOf(Folder);

                __writer.Write(VERSION);
                __writer.Write(count);
                if (count > 0)
                {
                    int index = 0;
                    var assetNames = new string[count];
                    foreach (var assetName in AssetManager.__assets.Keys)
                    {
                        if (GetFolderName(assetName) != Folder)
                            continue;

                        assetNames[index++] = assetName;
                    }

                    long offset;
                    Asset asset;
                    foreach (string assetName in assetNames)
                    {
                        __writer.Write(Path.GetFileName(assetName));

                        offset = StreamWrapper.Stream.Position;

                        asset = AssetManager.__assets[assetName];

                        if (asset.offset != offset)
                        {
                            asset.offset = offset;

                            AssetManager.__assets[assetName] = asset;
                        }

                        asset.data.Write(__writer);
                    }
                }

                long length = StreamWrapper.Stream.Position;
                StreamWrapper.Update(length);
            }

            public bool Write(string name, in AssetData data)
            {
                Asset asset;

                string folder = GetFolderName(name);
                if (folder != Folder)
                {
                    Debug.LogError($"The Folder Name {name} is vailed! (Need {Folder})");

                    return false;
                }

                if (AssetManager.__assets == null)
                    AssetManager.__assets = new Dictionary<string, Asset>();

                if (AssetManager.__assets.TryGetValue(name, out asset))
                {
                    //长度不一致，不能这么写入
                    /*if (asset.offset < 0L)
                        return false;

                    stream.Position = asset.offset;

                    data.Write(__writer);*/

                    asset.data = data;

                    AssetManager.__assets[name] = asset;

                    Save();
                }
                else
                {
                    StreamWrapper.Stream.Position = 0L;

                    __writer.Write((UInt32)VERSION);

                    Int32 count;
                    if (StreamWrapper.Stream.Length - StreamWrapper.Stream.Position < sizeof(Int32))
                    {
                        count = 0;

                        __writer.Write(count);
                    }
                    else
                        count = __reader.ReadInt32();

                    StreamWrapper.Stream.Position = Math.Max(StreamWrapper.Stream.Position, StreamWrapper.length);

                    //Debug.Log($"{__path} : {name} : {fileStream.Length} : {CountOf(folder)}");

                    __writer.Write(Path.GetFileName(name));

                    asset.offset = StreamWrapper.Stream.Position;

                    data.Write(__writer);

                    asset.data = data;

                    long length = StreamWrapper.Stream.Position;

                    StreamWrapper.Stream.Position = sizeof(UInt32);
                    __writer.Write(count + (Int32)1);

                    StreamWrapper.Update(length);

                    AssetManager.__assets[name] = asset;
                }

                return true;
            }
        }

        public static string FilterFolderName(string value)
        {
            return value.Replace('\\', '/');
        }

        public static string GetFolderName(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                folder = FilterFolderName(folder);

            return folder;
        }

        public static void CreateDirectory(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || Directory.Exists(folder))
                return;

            CreateDirectory(folder);

            Directory.CreateDirectory(folder);
        }

        public uint GetVersion(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return version;

            string path = __GetManagerPath(folder);

            if (!File.Exists(path))
                return 0;

            using (var reader = new BinaryReader(File.OpenRead(path)))
                return reader.ReadUInt32();
        }

        public void LoadFrom(string path)
        {
            string folder = Path.GetDirectoryName(path);
            path = string.IsNullOrEmpty(path) ? __path : Path.Combine(Path.GetDirectoryName(__path), path);
            if (File.Exists(path))
            {
                using (var fileStream = File.OpenRead(path))
                {
                    if(!__Load(fileStream, folder, out uint version, ref __assets))
                        Debug.LogError(path);

                    this.version = version;
                }
            }
        }

        public void SaveFolder()
        {
            using (var writer = new Writer(string.Empty, this))
            {
                writer.Save();
            }

            /*CreateDirectory(__path);

            using (var fileStream = File.Open(__path, FileMode.Create, FileAccess.Write))
            {
                if (fileStream == null)
                    return;

                using (var writer = new BinaryWriter(fileStream))
                {
                    writer.Write(VERSION);
                    writer.Write(CountOf(null));
                    if (__assets != null)
                    {
                        long offset;
                        Asset asset;
                        var assetNames = new List<string>(__assets.Keys);
                        foreach (string assetName in assetNames)
                        {
                            if (assetName == null || !string.IsNullOrEmpty(Path.GetDirectoryName(assetName)))
                                continue;

                            writer.Write(Path.GetFileName(assetName));

                            offset = fileStream.Position;

                            asset = __assets[assetName];

                            if (asset.offset != offset)
                            {
                                asset.offset = offset;

                                __assets[assetName] = asset;
                            }

                            __Save(writer, asset.data);
                        }
                    }
                }
            }*/
        }

        public void Verify(Action<string, int, int> handler)
        {
            if (__assets == null)
                return;

            using (var md5 = new MD5CryptoServiceProvider())
            {
                int index = 0, count = __assets.Count;
                Asset asset;
                string name, path;
                List<string> assetNames = null;
                foreach (var pair in __assets)
                {
                    name = pair.Key;

                    asset = pair.Value;
                    if (handler != null)
                        handler(name, index++, count);

                    path = Path.Combine(Path.GetDirectoryName(__path), name);
                    if (!File.Exists(path) || !MemoryEquals(md5.ComputeHash(File.OpenRead(path)), asset.data.info.md5))
                    {
                        if (assetNames == null)
                            assetNames = new List<string>();

                        assetNames.Add(name);
                    }
                }

                if (assetNames != null)
                {
                    foreach (string assetName in assetNames)
                        __assets.Remove(assetName);
                }
            }
        }

        public void Update(in Hash128 guid, string name, ref uint minVersion)
        {
            string directoryName = Path.GetDirectoryName(__path);
            if (version < 1)
            {
                version = VERSION;

                int assetCount = this.assetCount;
                if (assetCount > 0)
                {
                    var keys = new string[assetCount];
                    __assets.Keys.CopyTo(keys, 0);

                    using (var md5 = new MD5CryptoServiceProvider())
                    {
                        Asset asset;
                        foreach (var key in keys)
                        {
                            asset = __assets[key];
                            asset.data.info.md5 = md5.ComputeHash(File.ReadAllBytes(Path.Combine(directoryName, key)));
                            __assets[key] = asset;
                        }
                    }
                }
            }

            string path = Path.Combine(directoryName, name);
            if (File.Exists(path))
            {
                AssetData data;
                if (__assets != null && __assets.TryGetValue(name, out var asset))
                {
                    data = asset.data;

                    if (!string.IsNullOrEmpty(data.info.fileName) && data.info.fileName != name)
                    {
                        string filePath = Path.Combine(directoryName, data.info.fileName);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                    }
                }
                else
                {
                    data.info.version = 0;
                    data.type = AssetType.Uncompressed;
                    data.pack = AssetPack.Default;
                    data.dependencies = null;
                }

                data.info.version = Math.Max(data.info.version, minVersion);

                minVersion = ++data.info.version;

                data.info.size = (uint)new FileInfo(path).Length;

                using (var md5 = new MD5CryptoServiceProvider())
                    data.info.md5 = md5.ComputeHash(File.ReadAllBytes(path));

                data.info.fileName = guid.isValid ? $"{name}_{guid.ToString().Replace("-", string.Empty)}" : string.Empty;

                //__Create(name, data);
                using (var writer = new Writer(string.Empty, this))
                    writer.Write(name, data);

                if (!string.IsNullOrEmpty(data.info.fileName))
                {
                    string filePath = Path.Combine(directoryName, data.info.fileName);
                    if(File.Exists(filePath))
                        File.Delete(filePath);
                    
                    File.Move(path, filePath);
                }
            }
            else
            {
                __Delete(name);

                SaveFolder();
            }
        }

        public bool Write(string name, byte[] bytes)
        {
            if (__assets == null || !__assets.Remove(name, out var asset))
                return false;
            
            var folder = Path.GetDirectoryName(name);
            using (var writer = new Writer(folder, this))
                writer.Save();

            File.WriteAllBytes(__GetAssetPath(name, asset.data.info.fileName), bytes);
            
            asset.data.type = AssetType.UncompressedRuntime;
            asset.data.pack = AssetPack.Default;
            using (var writer = new Writer(folder, this))
                writer.Write(name, asset.data);

            return true;
        }

        public bool Write(string name, Stream stream, int bufferSize = 1024)
        {
            if (__assets == null || !__assets.Remove(name, out var asset))
                return false;

            bool isSaved = false;
            try
            {
                using (var fileStream = File.OpenWrite(__GetAssetPath(name, asset.data.info.fileName)))
                {
                    var folder = Path.GetDirectoryName(name);
                    using (var writer = new Writer(folder, this))
                        writer.Save();

                    isSaved = true;

                    int bytesToRead;
                    var buffer = new byte[bufferSize];
                    do
                    {
                        bytesToRead = stream.Read(buffer, 0, bufferSize);
                        fileStream.Write(buffer, 0, bufferSize);

                    } while (bytesToRead > 0);

                    //stream.CopyTo(fileStream);

                    asset.data.type = AssetType.UncompressedRuntime;
                    asset.data.pack = AssetPack.Default;
                    using (var writer = new Writer(folder, this))
                        writer.Write(name, asset.data);

                    return true;
                }
            }
            catch (Exception e)
            {
                if (!isSaved)
                    __assets[name] = asset;

                throw e;
            }
        }

        public IEnumerator Write(string name, IEnumerator enumerator)
        {
            if (__assets == null || !__assets.Remove(name, out var asset))
                yield break;

            bool isSaved = false;
            var folder = Path.GetDirectoryName(name);
            try
            {
                using (var writer = new Writer(folder, this))
                    writer.Save();

                isSaved = true;
            }
            catch (Exception e)
            {
                if (!isSaved)
                    __assets[name] = asset;

                throw e;
            }

            yield return enumerator;

            asset.data.type = AssetType.UncompressedRuntime;
            asset.data.pack = AssetPack.Default;
            using (var writer = new Writer(folder, this))
                writer.Write(name, asset.data);
        }

        private bool __Delete(string name)
        {
            if (__assets == null)
                return false;

            if (__assets.Remove(name, out var asset))
            {
                /*var assetManager = new AssetManager(__GetManagerPath(folder));

                string fileName = Path.GetFileName(name);
                if (assetManager.Delete(fileName))
                    return true;*/

                if (!asset.data.isReadOnly)
                    File.Delete(__GetAssetPath(name, asset.data.info.fileName));

                return true;
            }

            return false;
        }

        private static bool __Load(
            Stream stream,
            string folder,
            out uint version,
            ref Dictionary<string, Asset> assets)
        {
            if (assets == null)
                assets = new Dictionary<string, Asset>();

            try
            {
                var reader = new BinaryReader(stream);
                {
                    version = reader.ReadUInt32();
                    if (version > VERSION)
                        return false;

                    if (version > 7)
                    {
                        long position = stream.Position;

                        if (!new StreamWrapper(stream).Verify())
                        {
                            Debug.LogError("Verify Fail!");

                            return false;
                        }

                        stream.Position = position;
                    }

                    int numAssets = reader.ReadInt32();

                    if (!string.IsNullOrEmpty(folder))
                        folder = FilterFolderName(folder) + '/';

                    string name;
                    Asset asset;
                    for (int i = 0; i < numAssets; ++i)
                    {
                        name = reader.ReadString();

                        /*asset.info.version = reader.ReadUInt32();
                        asset.info.size = reader.ReadUInt32();
                        asset.info.md5 = reader.ReadBytes(16);*/

                        asset.offset = stream.Position;

                        asset.data = AssetData.Read(reader, version);

                        if (!string.IsNullOrEmpty(folder))
                            name = folder + name;

                        assets[name] = asset;
                    }
                }
            }
            catch (Exception e)
            {
                version = 0;

                Debug.LogException(e.InnerException ?? e);

                return false;
            }

            return true;
        }
    }
}