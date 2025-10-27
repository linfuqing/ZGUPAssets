using System;
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

    [Serializable]
    public class AssetObjectLoader : IDisposable
    {
        public enum Space
        {
            Local, 
            World
        }
        
        [SerializeField]
        internal Space _space;

        [SerializeField]
        internal string _fileName;

        [SerializeField]
        internal string _assetName;

        private MonoBehaviour __behaviour;

        private Transform __parent;

        private GameObject __target;
        
        private Coroutine __coroutine;

        private AssetBundleLoader<GameObject> __loader;

        public event Action<GameObject> onLoadComplete;

        public bool isLoading => __loader.isVail;

        public bool isDone => !isLoading && __GetOrInstantiate() != null;
        
        public float progress => __loader.progress;

        public GameObject target => __GetOrInstantiate();

        public AssetObjectLoader(
            Space space, 
            string fileName, 
            string assetName, 
            MonoBehaviour behaviour, 
            Transform parent)
        {
            _space = space;
            _fileName = fileName;
            _assetName = assetName;

            __behaviour = behaviour;

            __parent = parent;

            __target = null;

            __coroutine = null;

            __loader = default;

            onLoadComplete = null;
        }

        public void Init(MonoBehaviour behaviour, Transform parent = null)
        {
            __behaviour = behaviour;
            
            __parent = parent == null ? __parent ?? behaviour.transform : parent;
        }

        public void Load(AssetManager assetManager)
        {
            if (isLoading || isDone)
                return;
            
#if UNITY_EDITOR
            var assetPaths = UnityEditor.AssetDatabase.GetAssetPathsFromAssetBundle(_fileName);

            foreach (var assetPath in assetPaths)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(assetPath) == _assetName)
                {
                    __target = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    if (__target != null)
                    {
                        __target = _space == Space.World ? UnityEngine.Object.Instantiate(
                                __target, 
                                __parent.position, 
                                __parent.rotation) : 
                            UnityEngine.Object.Instantiate(__target, __parent);

                        if(onLoadComplete != null)
                            onLoadComplete(__target);
                        
                        return;
                    }
                }
            }
#endif
            
            __loader = new AssetBundleLoader<GameObject>(_fileName, _assetName, assetManager);

            __coroutine = __behaviour.StartCoroutine(__Load());
        }

        public void Dispose(float time, bool isForce = true)
        {
            if (time > Mathf.Epsilon && isForce)
            {
                var target = this.target;
                if (target != null)
                {
                    UnityEngine.Object.Destroy(target, time);

                    __target = null;
                }
            }
            else 
            {
                if(__target != null)
                {
                    UnityEngine.Object.Destroy(__target, time);

                    __target = null;
                }
                
                if (__coroutine != null)
                {
                    if(__behaviour != null && __behaviour.isActiveAndEnabled)
                        __behaviour.StopCoroutine(__coroutine);
                    
                    __coroutine = null;
                }
            }

            __loader.Dispose();

            __loader = default;
        }

        public void Dispose()
        {
            Dispose(0.0f);
        }

        private GameObject __GetOrInstantiate()
        {
            if (__loader.isVail)
            {
                var gameObject = __loader.value;
                if (gameObject == null)
                    Debug.LogError($"Asset Object {_assetName} Load Fail.", __behaviour);
                else if(__parent != null)
                {
                    UnityEngine.Assertions.Assert.IsNull(__target);
                    
                    gameObject = _space == Space.World ? 
                        UnityEngine.Object.Instantiate(gameObject, __parent.position, __parent.rotation) : 
                        UnityEngine.Object.Instantiate(gameObject, __parent);

                    var target = gameObject.AddComponent<AssetObject>();
                    target.name = _assetName;
                    target._loader = __loader;

                    __target = gameObject;
                }

                __loader = default;
            }

            if (__coroutine != null)
            {
                if(__behaviour != null && __behaviour.isActiveAndEnabled)
                    __behaviour.StopCoroutine(__coroutine);
                    
                __coroutine = null;
                    
                if(onLoadComplete != null)
                    onLoadComplete(__target);
            }

            return __target;
        }

        private IEnumerator __Load()
        {
            yield return __loader;

            __GetOrInstantiate();
        }
    }

    public abstract class AssetObjectBase : MonoBehaviour
    {
        private AssetObjectLoader __loader;
        
        private Action<GameObject> __onLoadComplete;

        public event Action<GameObject> onLoadComplete
        {
            add
            {
                if (__loader == null)
                    __onLoadComplete += value;
                else
                    __loader.onLoadComplete += value;
            }

            remove
            {
                if (__loader == null)
                    __onLoadComplete -= value;
                else
                    __loader.onLoadComplete -= value;
            }
        }
        
        public bool isDone => __loader != null && __loader.isDone;
        
        public float progress => __loader == null ? 0.0f : __loader.progress;

        public abstract AssetObjectLoader.Space space { get; }

        public abstract float time { get; }

        public abstract string fileName { get; }

        public abstract string assetName { get; }
        
        public abstract AssetManager assetManager { get; }
        
        public GameObject target => __loader == null ? null : __loader.target;

        protected void OnEnable()
        {
            if (__loader == null)
            {
                __loader = new AssetObjectLoader(space, fileName, assetName, this, transform);

                if (__onLoadComplete != null)
                {
                    __loader.onLoadComplete += __onLoadComplete;

                    __onLoadComplete = null;
                }
            }

            __loader.Load(assetManager);
        }

        protected void OnDisable()
        {
            __loader.Dispose(time);
        }
    }
}