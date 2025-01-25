using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace ZG
{
    public enum AndroidAssetPackType
    {
        InstallTime, 
        FastFollow, 
        OnDemand
    }

    /*public struct AndroidAssetPackFileLocation
    {
        public ulong offset;

        public string path;

        public bool isVail => !string.IsNullOrEmpty(path) && File.Exists(path);

        public AndroidAssetPackFileLocation(string packName, string packFilePath)
        {
            offset = 0;
            path = AndroidAssetPacks.GetAssetPackPath(packName);
            if(!string.IsNullOrEmpty(path))
                path = Path.Combine(path, packFilePath);
        }
    }*/

    public class AndroidAssetPackEnumerator : IAssetPackEnumerator
    {
        public const ulong MAX_BYTES_TO_COPY_PER_TIME = 32 * 1024;
        public readonly Task Task;

        public bool isSuccessful
        {
            get
            {
                var exception = Task.Exception;
                if (exception != null)
                {
                    Debug.LogException(exception.InnerException ?? exception);

                    return false;
                }

                return true;
            }
        }

        public float progress
        {
            get;

            private set;
        }

        public AndroidAssetPackEnumerator(ulong locationSize, ulong offset, string path, string targetPath)
        {
            Task = Task.Run(() =>
            {
                using (var reader = File.OpenRead(path))
                using (var writer = File.OpenWrite(targetPath))
                {
                    ulong step;
                    byte[] bytes = null;

                    reader.Position = (long)offset;

                    do
                    {
                        progress = (float)(offset * 1.0 / locationSize);

                        step = Math.Min(MAX_BYTES_TO_COPY_PER_TIME, locationSize - offset);
                        if (bytes == null)
                            bytes = new byte[step];

                        reader.Read(bytes, 0, (int)step);

                        writer.Write(bytes, 0, (int)step);

                        offset += step;
                    } while (offset < locationSize);
                }
            });
        }

        public bool MoveNext()
        {
            return !Task.IsCompleted;
        }

        void IEnumerator.Reset()
        {

        }

        object IEnumerator.Current => null;
    }

    public class AndroidAssetPackHeader : IAssetPackHeader
    {
        public const string NAME_PREFIX = "Android@";

        public readonly string Name;
        public readonly GetAssetPackStateAsyncOperation Operation;

#if DEBUG
        private bool __isDone;
#endif

        public bool isDone
        {
            get
            {
#if DEBUG
                if (!__isDone)
                {
                    __isDone = true;

                    return false;
                }
#endif
                return Operation == null || Operation.isDone;
            }
        }

        public ulong fileSize
        {
            get
            {
                return Operation == null ? 0 : Operation.size;
            }
        }

        public string filePath
        {
            get;

            internal set;
        }

        public string name => GetName(Name);

        public static string GetName(string name) => NAME_PREFIX + name;

        public AndroidAssetPackHeader(string name, GetAssetPackStateAsyncOperation operation)
        {
            Name = name;
            Operation = operation;
        }
    }

    public class AndroidAssetPack : IAssetPack, IAssetPackLocator
    {
        public static RequestToUseMobileDataAsyncOperation __userConfirmationOperation = null;

        public readonly AndroidAssetPackType Type;
        public readonly bool IsOverridePath;
        public readonly string Path;
        public readonly string Name;

        private AndroidAssetPackHeader __header;

        private static DownloadAssetPackAsyncOperation __operation;
        
#if DEBUG
        private bool __isDone;
#endif
        
        public bool isDone
        {
            get
            {
#if DEBUG
                if (!__isDone)
                {
                    __isDone = true;

                    return false;
                }
#endif
                
                if (!__WaitingUserConfirmationOperation())
                    return false;

                if (Type != AndroidAssetPackType.InstallTime)
                    return status == AndroidAssetPackStatus.Completed;

                if (__operation == null)
                {
                    downloadProgress = 1.0f;

                    return true;
                }

                downloadProgress = __operation.progress;

                return __operation.isDone;
            }
        }

        public AndroidAssetPackStatus status
        {
            get;

            private set;
        }

        public ulong size
        {
            get;

            private set;
        }

        public float downloadProgress
        {
            get;

            private set;
        }

        public string path
        {
            get;

            private set;
        }

        public IAssetPackHeader header
        {
            get
            {
                if (__header == null)
                {
                    GetAssetPackStateAsyncOperation operation = null;
                    if (Type == AndroidAssetPackType.InstallTime)
                    {
                        Debug.Log("Begin GetCoreUnityAssetPackNames");
                        string[] coreUnityAssetPackNames = AndroidAssetPacks.GetCoreUnityAssetPackNames();
                        Debug.Log("End GetCoreUnityAssetPackNames");

                        if (coreUnityAssetPackNames != null && coreUnityAssetPackNames.Length > 0)
                        {
                            Debug.Log("Begin GetAssetPackStateAsync");
                            operation = AndroidAssetPacks.GetAssetPackStateAsync(coreUnityAssetPackNames);
                            Debug.Log("End GetAssetPackStateAsync");
                        }
                    }
                    else
                    {
                        Debug.Log($"Begin GetAssetPackStateAsync {Name}");
                        operation = AndroidAssetPacks.GetAssetPackStateAsync(new string[] { Name });
                        Debug.Log("End GetAssetPackStateAsync");
                    }

                    if (operation != null)
                    {
                        __header = new AndroidAssetPackHeader(Name, operation);

                        __header.filePath = path;
                    }
                }

                return __header;
            }
        }

        public static string GetLocationPath(bool isOverridePath, string path, string name)
        {
            string result;
            if (isOverridePath)
            {
                result = System.IO.Path.GetFileName(name);
                if (!string.IsNullOrEmpty(path))
                    result = System.IO.Path.Combine(path, result);
            }
            else
                result = name;

            return result;
        }

        public bool Contains(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf(AndroidAssetPackHeader.NAME_PREFIX) == 0;
        }

        public bool GetFileInfo(
            string name,
            out ulong fileOffset,
            out string filePath)
        {
            fileOffset = 0;
            
            string path = GetLocationPath(IsOverridePath, Path, name), temp = __TryGetFilepath(path);
            if (temp == null)
            {
                filePath = null;

                return false;
            }

            filePath = path;//location.path;

            return true;
        }

        public AndroidAssetPack(AndroidAssetPackType type, bool isOverridePath, string path, string name)
        {
            Type = type;

            IsOverridePath = isOverridePath;

            Path = path;

            Name = name;

            if (type == AndroidAssetPackType.InstallTime)
            {
                if (__operation == null)
                {
                    Debug.Log("Begin GetCoreUnityAssetPackNames");
                    string[] coreUnityAssetPackNames = AndroidAssetPacks.GetCoreUnityAssetPackNames();
                    Debug.Log("End GetCoreUnityAssetPackNames");

                    if (coreUnityAssetPackNames == null || coreUnityAssetPackNames.Length < 1)
                    {
                        downloadProgress = 1.0f;

                        status = AndroidAssetPackStatus.Completed;
                    }
                    else
                    {
                        Debug.Log("Begin DownloadAssetPackAsync");
                        __operation = AndroidAssetPacks.DownloadAssetPackAsync(coreUnityAssetPackNames);
                        Debug.Log("End DownloadAssetPackAsync");
                    }
                }

                AssetUtility.Register(AndroidAssetPackHeader.GetName(name), new AssetPackLocator());
            }
            else
            {
                this.path = AndroidAssetPacks.GetAssetPackPath(name);
#if UNITY_ANDROID && !UNITY_EDITOR
                if (string.IsNullOrEmpty(this.path))
                {
                    Debug.Log($"DownloadAssetPackAsync {name}");

                    AndroidAssetPacks.DownloadAssetPackAsync(new string[] { name }, __Callback);
                }
                else
#endif
                {
                    downloadProgress = 1.0f;

                    status = AndroidAssetPackStatus.Completed;
                }

                AssetUtility.Register(AndroidAssetPackHeader.GetName(name), this);
            }
        }

        public bool Update(ref string filePath, ref ulong fileOffset)
        {
            filePath = __TryGetFilepath(filePath);
            
            return filePath != null;
        }

        public IAssetPackEnumerator Copy(string targetPath, string filePath, ulong fileOffset)
        {
            filePath = __TryGetFilepath(filePath);
            if (filePath == null)
                return null;

            return new AndroidAssetPackEnumerator(
                size, 
                fileOffset, 
                filePath, 
                targetPath);
        }

        private string __TryGetFilepath(string filePath)
        {
            string path = string.IsNullOrEmpty(this.path) ? null : System.IO.Path.Combine(this.path, filePath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"Get Asset Location Failed: {status}, {Name} : {filePath}, {path}");

                return null;
            }
            
            return path;
        }

        private void __Callback(AndroidAssetPackInfo androidAssetPackInfo)
        {
            Debug.Log(
                $"DownloadingAssetPackAsync {androidAssetPackInfo.name} : {androidAssetPackInfo.status} : {androidAssetPackInfo.size} : {androidAssetPackInfo.transferProgress} : {androidAssetPackInfo.bytesDownloaded}");

            var error = androidAssetPackInfo.error;
            if (error != AndroidAssetPackError.NoError)
                Debug.LogError(error);
            else
            {
                switch (status = androidAssetPackInfo.status)
                {
                    case AndroidAssetPackStatus.Pending:
                    case AndroidAssetPackStatus.Downloading:
                    case AndroidAssetPackStatus.Transferring:

                        downloadProgress = androidAssetPackInfo.transferProgress * 0.1f +
                                           androidAssetPackInfo.bytesDownloaded * 0.9f / androidAssetPackInfo.size;
                        break;
                    case AndroidAssetPackStatus.Completed:
                        downloadProgress = 1.0f;

                        size = androidAssetPackInfo.size;
                        
                        path = AndroidAssetPacks.GetAssetPackPath(Name);

                        if (__header != null)
                            __header.filePath = path;

                        break;
                    case AndroidAssetPackStatus.WaitingForWifi:

                        if (__userConfirmationOperation == null)
                            __userConfirmationOperation = AndroidAssetPacks.RequestToUseMobileDataAsync();

                        __WaitingUserConfirmationOperation();
                        break;
                    default:
                        Debug.LogError(status);

                        Application.Quit();

                        break;
                }
            }

        }

        private bool __WaitingUserConfirmationOperation()
        {
            if (__userConfirmationOperation == null)
                return true;

            if (__userConfirmationOperation.isDone)
            {
                var result = __userConfirmationOperation.result;
                if (result == null)
                {
                    // userConfirmationOperation finished with an error. Something went
                    // wrong when displaying the prompt to the user, and they weren't
                    // able to interact with the dialog. In this case, we recommend
                    // developers wait for Wi-Fi before attempting to download again.
                    // You can get more info by calling GetError() on the operation.
                    Application.Quit();
                }
                else if (result.allowed)
                {
                    // User accepted the confirmation dialog - download will start
                    // automatically (no action needed).
                    __userConfirmationOperation = null;
                }
                else
                {
                    // User canceled or declined the dialog. Await Wi-Fi connection, or
                    // re-prompt the user.
                    Application.Quit();
                }

                return true;
            }

            return false;
        }
    }

    public class AndroidAssetPackManager : MonoBehaviour
    {
        [Serializable]
        public struct Pack
        {
            public string name;

            public AndroidAssetPackType type;

            public bool isOverridePath;

            public string packPath;

            public string[] filePaths;
        }

        private class Factory : IAssetPackFactory
        {
            public readonly AndroidAssetPackType Type;

            public readonly bool IsOverridePath;
            public readonly string PackPath;
            public readonly string PackName;

            private IAssetPack __pack;

            public Factory(AndroidAssetPackType type, bool isOverridePath, string packPath, string packName)
            {
                Type = type;
                IsOverridePath = isOverridePath;
                PackPath = packPath;
                PackName = packName;
            }

            public IAssetPack Retrieve()
            {
                if (__pack == null)
                    __pack = new AndroidAssetPack(Type, IsOverridePath, PackPath, PackName);

                return __pack;
            }
        }

        public Pack[] packs;

        void Awake()
        {
            if(packs != null)
            {
                IAssetPackFactory factory;
                foreach (var pack in packs)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    factory = new Factory(pack.type, pack.isOverridePath, pack.packPath, pack.name);
                    foreach(var filePath in pack.filePaths)
                        AssetUtility.Register(filePath, factory);
#else
                    foreach (var filePath in pack.filePaths)
                    {
                        factory = new AssetPackManager.Factory(new AssetPackManager.Pack(false, filePath, null));
                        AssetUtility.Register(filePath, factory);
                    }
#endif
                }
            }
        }
    }
}