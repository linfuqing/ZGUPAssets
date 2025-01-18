using UnityEngine;

namespace ZG
{
    public class AssetPackManager : MonoBehaviour
    {
        [System.Serializable]
        public struct Pack
        {
            public bool hasPathOfHeader;

            public string path;

            public IAssetPack Retrieve() => new AssetManager.DefaultAssetPack(hasPathOfHeader, path);
        }

        private struct Factory : IAssetPackFactory
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