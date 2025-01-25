using UnityEngine;

namespace ZG
{
    public class AssetPackManager : MonoBehaviour
    {
        [System.Serializable]
        public struct Pack
        {
            public bool hasHeader;
            
            public string path;

            public string directory;

            public Pack(bool hasHeader, string path, string directory)
            {
                this.hasHeader = hasHeader;
                this.path = path;
                this.directory = directory;
            }

            public IAssetPack Retrieve() => new AssetManager.DefaultAssetPack(
                hasHeader ? path : null, 
                string.IsNullOrEmpty(directory) ?System.IO.Path.GetDirectoryName(path) : directory);
        }

        public struct Factory : IAssetPackFactory
        {
            public readonly Pack Pack;

            public Factory(Pack pack)
            {
                Pack = pack;
            }

            public IAssetPack Retrieve() => Pack.Retrieve();
        }

        public Pack[] packs;

        void Awake()
        {
            if (packs != null)
            {
                foreach (var pack in packs)
                    AssetUtility.Register(pack.path, new Factory(pack));
            }
        }
    }
}