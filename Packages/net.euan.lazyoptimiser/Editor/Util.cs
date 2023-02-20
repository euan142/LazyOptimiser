using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace LazyOptimiser
{
    public class AnimationReferences
    {
        public GameObject root;
        public AnimationClip clip;
        public EditorCurveBinding[] curveBindings;
        public Object[] referencedObjects;
    }

    public static class Util
    {
        private static bool? _shouldOptimise = null;
        public static bool ShouldOptimise {
            private set { _shouldOptimise = value; PlayerPrefs.SetInt("lazyoptimiser.shouldOptimise", value ? 1 : 0); }
            get => _shouldOptimise ?? (ShouldOptimise = PlayerPrefs.GetInt("lazyoptimiser.shouldOptimise", 1) == 1);
        }
        

        [MenuItem("Tools/Lazy Optimiser/Toggle Optimiser", false, 1)]
        public static void ToggleOptimiser()
        {
            ShouldOptimise = !ShouldOptimise;
            EditorUtility.DisplayDialog("Lazy Optimiser", $"{(ShouldOptimise ? "Enabled" : "Disabled")} automatic optimisation", "OK");
        }

        public static List<AnimationReferences> GetAllAnimations(VRCAvatarDescriptor avatarDescriptor)
        {
            List<AnimationReferences> animationClips = new List<AnimationReferences>();
            foreach (var animator in avatarDescriptor.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null)
                    animationClips.AddRange(animator.runtimeAnimatorController.animationClips.Select(clip => new AnimationReferences
                    {
                        root = animator.gameObject,
                        clip = clip,
                        curveBindings = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray()
                    }));
            }

            List<VRCAvatarDescriptor.CustomAnimLayer> customAnimLayers = avatarDescriptor.baseAnimationLayers.ToList();
            customAnimLayers.AddRange(avatarDescriptor.specialAnimationLayers);

            animationClips.AddRange(customAnimLayers.Where(cal => cal.animatorController != null).SelectMany(cal => cal.animatorController.animationClips).Select(clip => new AnimationReferences
            {
                root = avatarDescriptor.gameObject,
                clip = clip,
                curveBindings = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).ToArray()
            }));


            foreach (AnimationReferences clip in animationClips)
            {
                clip.referencedObjects = clip.curveBindings.Select(cb => AnimationUtility.GetAnimatedObject(clip.root, cb)).ToArray();
            }

            return animationClips;
        }

        public static void ReplaceAnimations(VRCAvatarDescriptor avatarDescriptor, Dictionary<AnimationClip, AnimationClip> animationMap)
        {
            foreach (var animator in avatarDescriptor.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController is AnimatorController animatorController)
                    animator.runtimeAnimatorController = ReplaceAnimationsInController(animatorController);
            }

            for (int i = 0; i < avatarDescriptor.baseAnimationLayers.Length; i++)
            {
                VRCAvatarDescriptor.CustomAnimLayer animationLayer = avatarDescriptor.baseAnimationLayers[i];
                if (animationLayer.animatorController is AnimatorController animatorController)
                    animationLayer.animatorController = ReplaceAnimationsInController(animatorController);
                avatarDescriptor.baseAnimationLayers[i] = animationLayer;
            }

            for (int i = 0; i < avatarDescriptor.specialAnimationLayers.Length; i++)
            {
                VRCAvatarDescriptor.CustomAnimLayer animationLayer = avatarDescriptor.specialAnimationLayers[i];
                if (animationLayer.animatorController is AnimatorController animatorController)
                    animationLayer.animatorController = ReplaceAnimationsInController(animatorController);
                avatarDescriptor.specialAnimationLayers[i] = animationLayer;
            }

            AnimatorController ReplaceAnimationsInController(AnimatorController originalController)
            {
                AnimatorController controller = CloneAsset(originalController, null, true);
                var layers = controller.layers;

                foreach (var layer in layers)
                {
                    ReplaceAnimationsInStateMachine(layer.stateMachine);
                }

                controller.layers = layers;

                EditorUtility.SetDirty(controller);
                return controller;
            }

            AnimatorStateMachine ReplaceAnimationsInStateMachine(AnimatorStateMachine stateMachine)
            {
                ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
                for (int i = 0; i < childStateMachines.Length; i++)
                {
                    ChildAnimatorStateMachine childStateMachine = childStateMachines[i];
                    childStateMachine.stateMachine = ReplaceAnimationsInStateMachine(childStateMachine.stateMachine);
                }
                stateMachine.stateMachines = childStateMachines;

                ChildAnimatorState[] states = stateMachine.states;
                foreach (ChildAnimatorState state in states)
                {
                    if (state.state.motion is AnimationClip origAnimation && animationMap.ContainsKey(origAnimation))
                    {
                        state.state.motion = animationMap[origAnimation];
                    }
                }
                stateMachine.states = states;

                return stateMachine;
            }
        }

        const string BaseFolderPath = "Assets/LazyOptimiser";

        public static T CloneAsset<T>(T assetToCopy, string suffix = null, bool randomisedName = false) where T : Object
        {
            string newPath = $"{BaseFolderPath}/Generated";

            if (AssetDatabase.IsValidFolder(newPath) == false)
                Directory.CreateDirectory(newPath);

            string newAssetName = randomisedName ? VRC.Tools.GetRandomHex(6) : assetToCopy.name;

            if (assetToCopy is GameObject gameObjectToCopy)
            {
                newPath += $"/{newAssetName}{(suffix != null ? $"_{suffix}" : "")}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(gameObjectToCopy, newPath) as T;
            }
            // Euan: Basically a deep copy, we may want to do this for everything
            else if (assetToCopy is AnimatorController)
            {
                newPath += $"/{newAssetName}{(suffix != null ? $"_{suffix}" : "")}.asset";
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(assetToCopy), newPath);
                return AssetDatabase.LoadAssetAtPath<T>(newPath);
            }
            else
            {
                newPath += $"/{newAssetName}{(suffix != null ? $"_{suffix}" : "")}.asset";
                T copy = Object.Instantiate(assetToCopy);
                AssetDatabase.CreateAsset(copy, newPath);

                return copy;
            }
        }

        public static void ClearGeneratedAssets()
        {
            FileUtil.DeleteFileOrDirectory($"{BaseFolderPath}/Generated");
        }
    }
}