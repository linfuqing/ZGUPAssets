using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

namespace ZG
{
    [CreateAssetMenu(menuName = "ZG/Asset Config", fileName = "AssetConfig")]
    public class AssetConfig : ScriptableObject
    {
        [Serializable]
        public struct Mask
        {
            public BuildTarget buildTarget;
            public UnityEngine.Object[] targets; 
        }

        public Mask[] masks;

        public bool IsMask(BuildTarget buildTarget, UnityEngine.Object target)
        {
            foreach(var mask in masks)
            {
                if(mask.buildTarget == buildTarget)
                {
                    if (Array.IndexOf(mask.targets, target) != -1)
                        return true;
                }
            }

            return false;
        }
    }
}
#endif