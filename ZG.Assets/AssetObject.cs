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
        
        [System.Serializable]
        public class LoadedEvent : UnityEngine.Events.UnityEvent<GameObject> { }

        private AssetBundleLoader<GameObject> __loader;

        private GameObject __target;

        public LoadedEvent onLoaded;

        public abstract Space space { get; }

        public abstract float time { get; }

        public abstract string fileName { get; }

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
                        target.name = name;
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

            foreach (var assetPath in assetPaths)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(assetPath) == name)
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

                        return;
                    }
                }
            }
#endif
            
            __loader = new AssetBundleLoader<GameObject>(fileName, name, assetManager);

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

            //UnityEngine.Debug.LogError($"Dispose {fileName} : {name}");
            __loader.Dispose();

            /*var assetManager = this.assetManager;
            if(assetManager != null)
                assetManager.Unload<GameObject>(__fileName, __assetName);*/
        }

        private IEnumerator __Load()
        {
            yield return __loader;

            var gameObject = target;
            if (gameObject == null)
            {
                Debug.LogError($"Asset Object {name} Load Fail.", this);

                yield break;
            }

            onLoaded?.Invoke(gameObject);
        }
    }
}