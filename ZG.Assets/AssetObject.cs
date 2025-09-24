using System.Collections;
using UnityEngine;

namespace ZG
{
    public class AssetObject : MonoBehaviour
    {
        internal AssetBundleLoader<GameObject> _loader;

        private void OnDestroy()
        {
            //UnityEngine.Debug.LogError($"Dispose {name}");
            _loader.Dispose();
        }
    }

    public abstract class AssetObjectBase : MonoBehaviour
    {
        public enum Space
        {
            Local, 
            World
        }
        
        private AssetBundleLoader<GameObject> __loader;

        private GameObject __target;

        public event System.Action<GameObject> onLoadComplete;
        
        public float progress => __loader.progress;

        public abstract Space space { get; }

        public abstract float time { get; }

        public abstract string fileName { get; }

        public abstract string assetName { get; }
        
        public abstract AssetManager assetManager { get; }
        
        public GameObject target
        {
            get
            {
                if (__target == null && this != null)
                {
                    var gameObject = __loader.value;
                    if (gameObject != null)
                    {
                        var transform = this.transform;
                        gameObject = space == Space.World ? Instantiate(gameObject, transform.position, transform.rotation) : Instantiate(gameObject, transform);

                        var target = gameObject.AddComponent<AssetObject>();
                        target.name = assetName;
                        target._loader = __loader;

                        __target = gameObject;
                    }

                    __loader = default;
                }

                return __target;
            }
        }

        protected void OnEnable()
        {
#if UNITY_EDITOR
            var assetPaths = UnityEditor.AssetDatabase.GetAssetPathsFromAssetBundle(fileName);

            string assetName = this.assetName;
            foreach (var assetPath in assetPaths)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(assetPath) == assetName)
                {
                    __target = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    if (__target != null)
                    {
                        var transform = this.transform;
                        __target = space == Space.World ? Instantiate(
                                __target, 
                                transform.position, 
                                transform.rotation) : 
                            Instantiate(__target, transform);

                        if(onLoadComplete != null)
                            onLoadComplete(__target);
                        
                        return;
                    }
                }
            }
#endif
            
            __loader = new AssetBundleLoader<GameObject>(fileName, assetName, assetManager);

            StartCoroutine(__Load());
        }

        protected void OnDisable()
        {
            float time = this.time;
            if (time > Mathf.Epsilon)
            {
                var target = this.target;
                if (target != null)
                {
                    Destroy(target, time);

                    __target = null;

                    __loader = default;
                }
            }
            else if(__target != null)
            {
                Destroy(__target);

                __target = null;

                __loader = default;
            }

            __loader.Dispose();
        }

        private IEnumerator __Load()
        {
            yield return __loader;

            var gameObject = target;
            if (gameObject == null)
            {
                Debug.LogError($"Asset Object {assetName} Load Fail.", this);

                yield break;
            }

            if(onLoadComplete != null)
                onLoadComplete(gameObject);
        }
    }
}