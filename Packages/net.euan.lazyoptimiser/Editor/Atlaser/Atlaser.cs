using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser.Atlaser
{
    public class Atlaser : IVRCSDKPreprocessAvatarCallback
    {
        
        public int callbackOrder => -90;
        
        
        
        [MenuItem("Tools/Lazy Optimiser/Print Mergeable Materials")]
        public static void PrintMergeableMeshes()
        {
            ProcessAvatar(Selection.activeGameObject);
        }
        
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            ProcessAvatar(Selection.activeGameObject, true);
            return true;
        }

        public static void ProcessAvatar(GameObject avatarGameObject, bool doDestroy = false)
        {
            VRCAvatarDescriptor descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();

            MergeMaterials.Merge(descriptor);
        }
    }
}
