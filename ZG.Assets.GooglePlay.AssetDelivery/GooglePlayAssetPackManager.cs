#if ENABLE_PAD && (UNITY_ANDROID || UNITY_EDITOR)
#define USE_PAD
#endif

using System;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
#if USE_PAD
using Google.Play.Common;
using Google.Play.AssetDelivery;
#endif
using UnityEngine;

namespace ZG
{
#if USE_PAD
    public class GooglePlayAssetPackEnumerator : IAssetPackEnumerator
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

        public GooglePlayAssetPackEnumerator(AssetLocation location, string targetPath)
        {
            Task = Task.Run(() =>
            {
                using (var reader = File.OpenRead(location.Path))
                using (var writer = File.OpenWrite(targetPath))
                {
                    ulong size = location.Size, offset = 0, step;
                    byte[] bytes = null;

                    reader.Position = (long)location.Offset;

                    do
                    {
                        progress = (float)(offset * 1.0 / size);

                        step = Math.Min(MAX_BYTES_TO_COPY_PER_TIME, size - offset);
                        if (bytes == null)
                            bytes = new byte[step];

                        reader.Read(bytes, 0, (int)step);

                        writer.Write(bytes, 0, (int)step);

                        offset += step;
                    } while (offset < size);
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

    public class GooglePlayAssetPackLocator : IAssetPackLocator
    {
        public readonly PlayAssetPackRequest Request;

        public GooglePlayAssetPackLocator(PlayAssetPackRequest request)
        {
            Request = request;
        }

        public bool Update(ref string filePath, ref ulong fileOffset)
        {
            var location = Request.GetAssetLocation(filePath);
            if (location == null)
            {
                Debug.LogError($"Get Asset Location Failed: {Request.Status}, {filePath}");

                return false;
            }

            filePath = location.Path;
            fileOffset = location.Offset;

            return true;
        }

        public IAssetPackEnumerator Copy(string targetPath, string filePath, ulong fileOffset)
        {
            var location = Request.GetAssetLocation(filePath);
            if (location == null)
            {
                Debug.LogError($"Get Asset Location Failed: {Request.Status}, {filePath}");

                return null;
            }

            return new GooglePlayAssetPackEnumerator(location, targetPath);
        }
    }

    public class GooglePlayAssetPackHeader : IAssetPackHeader
    {
        public const string NAME_PREFIX = "GooglePlay@";

        public readonly string Name;
        public readonly PlayAsyncOperation<long, AssetDeliveryErrorCode> Operation;

        public bool isDone
        {
            get
            {
                var error = Operation.Error;
                if (error != AssetDeliveryErrorCode.NoError)
                    Debug.LogError(error);
                else if (Operation.IsDone)
                    return true;

                return false;
            }
        }

        public ulong fileSize
        {
            get
            {
                return (ulong)Operation.GetResult();
            }
        }

        public string filePath => null;

        public string name => GetName(Name);

        public static string GetName(string name) => NAME_PREFIX + name;

        public GooglePlayAssetPackHeader(string name)
        {
            Name = name;
            Operation = PlayAssetDelivery.GetDownloadSize(name);
        }
    }

    public class GooglePlayAssetPack : IAssetPack
    {
        public static PlayAsyncOperation<ConfirmationDialogResult, AssetDeliveryErrorCode> __userConfirmationOperation = null;

        public readonly bool IsOverridePath;
        public readonly string Path;
        public readonly string Name;
        public readonly PlayAssetPackRequest Request;

        private GooglePlayAssetPackHeader __header;

        public bool isDone
        {
            get
            {
                var error = Request.Error;
                if (error != AssetDeliveryErrorCode.NoError)
                    Debug.LogError(error);
                else
                {
                    if (Request.IsDone)
                        return true;

                    if (Request.Status == AssetDeliveryStatus.WaitingForWifi)
                    {
                        if (__userConfirmationOperation == null)
                            __userConfirmationOperation = PlayAssetDelivery.ShowCellularDataConfirmation();

                        if (__userConfirmationOperation.IsDone)
                        {
                            switch (__userConfirmationOperation.GetResult())
                            {
                                case ConfirmationDialogResult.Accepted:
                                    // User accepted the confirmation dialog - download will start
                                    // automatically (no action needed).
                                    __userConfirmationOperation = null;
                                    break;
                                case ConfirmationDialogResult.Denied:
                                    // User canceled or declined the dialog. Await Wi-Fi connection, or
                                    // re-prompt the user.
                                    Application.Quit();

                                    break;
                                default:
                                    // userConfirmationOperation finished with an error. Something went
                                    // wrong when displaying the prompt to the user, and they weren't
                                    // able to interact with the dialog. In this case, we recommend
                                    // developers wait for Wi-Fi before attempting to download again.
                                    // You can get more info by calling GetError() on the operation.
                                    break;
                            }
                        }
                    }
                    else
                        Debug.LogError(Request.Status);
                }

                return false;
            }
        }

        public float downloadProgress => Request.DownloadProgress;

        public IAssetPackHeader header
        {
            get
            {
                if(__header == null)
                    __header = new GooglePlayAssetPackHeader(Name);

                return __header;
            }
        }

        public bool Contains(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf(GooglePlayAssetPackHeader.NAME_PREFIX) == 0;
        }

        public bool GetFileInfo(
            string name,
            out ulong fileOffset,
            out string filePath)
        {
            string path;
            if(IsOverridePath)
            {
                path = System.IO.Path.GetFileName(name);
                if (!string.IsNullOrEmpty(Path))
                    path = System.IO.Path.Combine(Path, path);
            }
            else
                path = name;

            var location = Request.GetAssetLocation(path);
            if (location == null)
            {
                Debug.LogError($"Get Asset Location Failed: {Request.Status}, {path}");

                fileOffset = 0;
                filePath = null;

                return false;
            }

            fileOffset = location.Offset;
            filePath = path;// location.Path;

            return true;
        }

        public GooglePlayAssetPack(bool isOverridePath, string path, string name)
        {
            IsOverridePath = isOverridePath;

            Path = path;

            Name = name;

            Request = PlayAssetDelivery.RetrieveAssetPackAsync(name);

            AssetUtility.Register(GooglePlayAssetPackHeader.GetName(name), new GooglePlayAssetPackLocator(Request));
        }
    }
#endif

    public class GooglePlayAssetPackManager : UnityEngine.MonoBehaviour
    {
        [Serializable]
        public struct Pack
        {
            public string name;

            public bool isOverridePath;

            public string packPath;

            public string[] filePaths;
        }

#if USE_PAD
        private class Factory : IAssetPackFactory
        {
            public readonly bool IsOverridePath;
            public readonly string PackPath;
            public readonly string PackName;

            private GooglePlayAssetPack __pack;

            public Factory(bool isOverridePath, string packPath, string packName)
            {
                IsOverridePath = isOverridePath;
                PackPath = packPath;
                PackName = packName;
            }

            public IAssetPack Retrieve()
            {
                if (__pack == null)
                    __pack = new GooglePlayAssetPack(IsOverridePath, PackPath, PackName);

                return __pack;
            }
        }
#endif

        public Pack[] packs;

        void Awake()
        {
#if USE_PAD && UNITY_ANDROID && !UNITY_EDITOR
            if(packs != null)
            {
                Factory factory;
                foreach (var pack in packs)
                {
                    factory = new Factory(pack.isOverridePath, pack.packPath, pack.name);
                    foreach(var filePath in pack.filePaths)
                        AssetUtility.Register(filePath, factory);
                }
            }
#endif
        }
    }
}