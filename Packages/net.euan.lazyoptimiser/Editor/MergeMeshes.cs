using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace LazyOptimiser
{
    public class MergeMeshes : IVRCSDKPreprocessAvatarCallback
    {
        public struct SkinnedMeshAnimationReference
        {
            public EditorCurveBinding curve;
            public AnimationReferences animationReference;
        }

        public int callbackOrder => -90;

        [MenuItem("Tools/Lazy Optimiser/Print Mergeable Meshes")]
        public static void PrintMergeableMeshes()
        {
            ProcessAvatar(Selection.activeGameObject);
        }

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (Util.ShouldOptimise)
            {
                ProcessAvatar(avatarGameObject, true);
            }
            return true;
        }

        public static void ProcessAvatar(GameObject avatarGameObject, bool doDestroy = false)
        {
            VRCAvatarDescriptor descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();

            List<AnimationReferences> animationRefs = Util.GetAllAnimations(descriptor);

            Dictionary<SkinnedMeshRenderer, HashSet<string>> linkedMeshes = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();
            Dictionary<SkinnedMeshRenderer, List<SkinnedMeshAnimationReference>> linkedAnimations = new Dictionary<SkinnedMeshRenderer, List<SkinnedMeshAnimationReference>>();

            // Euan: Add comparison between skinned mesh renderer options, also materials?

            // Euan: Future thing, support merging non skinned meshes, account for them being attached to bones and such

            foreach (SkinnedMeshRenderer skinnedMesh in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                linkedMeshes.Add(skinnedMesh, new HashSet<string> { GetUniqueKey(skinnedMesh) });
                linkedAnimations.Add(skinnedMesh, new List<SkinnedMeshAnimationReference>());
            }

            GroupMeshes(animationRefs, linkedMeshes, linkedAnimations);

            foreach (var group in linkedMeshes.GroupBy(kvp => kvp.Value, HashSet<string>.CreateSetComparer()).Select(group => group.Select(kvp2 => kvp2.Key)))
            {
                List<SkinnedMeshRenderer> skinnedMeshes = group.ToList();
                if (skinnedMeshes.Count > 1)
                {
                    if (doDestroy)
                    {
                        Debug.Log($"Grouping meshes: {string.Join(", ", group.Select(smr => smr.name))}");
                        AdjustAnimations(descriptor, skinnedMeshes[0], linkedAnimations.Where(kvp => skinnedMeshes.Contains(kvp.Key)).ToDictionary(a => a.Key, b => b.Value));

                        if (skinnedMeshes.Contains(descriptor.VisemeSkinnedMesh))
                        {
                            descriptor.VisemeSkinnedMesh = skinnedMeshes[0];
                        }

                        if (skinnedMeshes.Contains(descriptor.customEyeLookSettings.eyelidsSkinnedMesh))
                        {
                            descriptor.customEyeLookSettings.eyelidsSkinnedMesh = skinnedMeshes[0];
                        }

                        var skinnedMeshDatas = skinnedMeshes.Select(sm => sm.ToSkinnedMeshData()).ToList();

                        MeshUtil.MergeSkinnedMeshes(skinnedMeshDatas);

                        skinnedMeshDatas[0].Apply(skinnedMeshes[0]);

                        Debug.Log($"Processing: {skinnedMeshes[0].gameObject.name}");
                        

                        for (int i = 1; i < skinnedMeshes.Count; i++)
                        {
                            SkinnedMeshRenderer skinnedMesh = skinnedMeshes[i];
                            // Euan: Could we be smarter here?
                            if (skinnedMesh.gameObject.GetComponents<Component>().Length == 2)
                                Object.DestroyImmediate(skinnedMesh.gameObject);
                            else
                                Object.DestroyImmediate(skinnedMesh);
                        }
                    }
                    else
                    {
                        Debug.LogError($"Could group meshes: {string.Join(", ", group.Select(smr => smr.name))}");
                    }
                }
            }

            if (doDestroy)
            {
                AssetDatabase.SaveAssets();
            }
        }

        // Euan: Right now this is generating a long string to act as a unique key which can then be grouped by,
        // a different solution to deeply compare how something is animated directly would be more reliable
        private static string GetUniqueKey(SkinnedMeshRenderer skinnedMesh, AnimationReferences animationReference, EditorCurveBinding editorCurve, AnimationCurve animationCurve)
        {
            string uniqueKey = "A";
            uniqueKey += $"/{(skinnedMesh.rootBone ? skinnedMesh.rootBone.GetInstanceID().ToString() : "null")}";
            uniqueKey += $"/{animationReference.GetHashCode()}/{editorCurve.propertyName}/{animationCurve.length}";

            foreach (var key in animationCurve.keys)
            {
                uniqueKey += $"/{key.inTangent}/{key.inWeight}/{key.outTangent}/{key.outWeight}/{key.time}/{key.value}/{key.weightedMode}";
            }

            return uniqueKey;
        }

        private static string GetUniqueKey(SkinnedMeshRenderer skinnedMesh)
        {
            string uniqueKey = "S";
            uniqueKey += $"/{(skinnedMesh.rootBone ? skinnedMesh.rootBone.GetInstanceID().ToString() : "null")}";
            uniqueKey += $"/{skinnedMesh.enabled}";
            uniqueKey += $"/{skinnedMesh.gameObject.activeSelf}";
            uniqueKey += $"/{skinnedMesh.shadowCastingMode}";
            uniqueKey += $"/{skinnedMesh.receiveShadows}";
            uniqueKey += $"/{skinnedMesh.updateWhenOffscreen}";
            uniqueKey += $"/{skinnedMesh.lightProbeUsage}";
            uniqueKey += $"/{skinnedMesh.reflectionProbeUsage}";
            uniqueKey += $"/{skinnedMesh.quality}";
            uniqueKey += $"/{(skinnedMesh.probeAnchor ? skinnedMesh.probeAnchor.GetInstanceID().ToString() : "null")}";
            uniqueKey += $"/{skinnedMesh.skinnedMotionVectors}";
            uniqueKey += $"/{skinnedMesh.allowOcclusionWhenDynamic}";
            uniqueKey += $"/{skinnedMesh.sharedMesh.blendShapeCount != 0}";
            return uniqueKey;
        }

        private static void GroupMeshes(List<AnimationReferences> animationRefs,
                                        Dictionary<SkinnedMeshRenderer, HashSet<string>> linkedMeshes,
                                        Dictionary<SkinnedMeshRenderer, List<SkinnedMeshAnimationReference>> linkedAnimations)
        {
            foreach (AnimationReferences animationReference in animationRefs)
            {
                for (int i = 0; i < animationReference.curveBindings.Length; i++)
                {
                    EditorCurveBinding edCurve = animationReference.curveBindings[i];
                    Object refObj = animationReference.referencedObjects[i];
                    AnimationCurve aniCurve = AnimationUtility.GetEditorCurve(animationReference.clip, edCurve);

                    if (refObj is SkinnedMeshRenderer refSkinnedMesh)
                    {
                        if (edCurve.propertyName.StartsWith("material.") || edCurve.propertyName.StartsWith("m_Materials."))
                        {
                            linkedAnimations[refSkinnedMesh].Add(new SkinnedMeshAnimationReference { curve = edCurve, animationReference = animationReference });
                            // Debug.LogError($"{animationReference.clip.name} - {curve.propertyName} - {curve.path}", refObj);
                        }
                        else
                        {
                            linkedMeshes[refSkinnedMesh].Add(GetUniqueKey(refSkinnedMesh, animationReference, edCurve, aniCurve));
                        }
                    }
                    else if (refObj is GameObject refGameObject)
                    {
                        SkinnedMeshRenderer skinnedMesh = refGameObject.GetComponent<SkinnedMeshRenderer>();

                        if (skinnedMesh != null)
                        {
                            linkedMeshes[skinnedMesh].Add(GetUniqueKey(skinnedMesh, animationReference, edCurve, aniCurve));
                        }
                    }
                }
            }
        }

        private static void AdjustAnimations(VRCAvatarDescriptor descriptor,
                                             SkinnedMeshRenderer targetSkinnedMesh,
                                             Dictionary<SkinnedMeshRenderer, List<SkinnedMeshAnimationReference>> linkedAnimations)
        {
            Dictionary<AnimationClip, AnimationClip> clipRef = new Dictionary<AnimationClip, AnimationClip>();

            int materialsSoFar = 0;

            foreach (var kvp in linkedAnimations)
            {
                if (kvp.Key != targetSkinnedMesh)
                {
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        SkinnedMeshAnimationReference skinnedAniRef = kvp.Value[i];

                        if (clipRef.ContainsKey(skinnedAniRef.animationReference.clip))
                        {
                            skinnedAniRef.animationReference.clip = clipRef[skinnedAniRef.animationReference.clip];
                        }
                        else
                        {
                            AnimationClip copy = Util.CloneAsset(skinnedAniRef.animationReference.clip, null, true);
                            clipRef.Add(skinnedAniRef.animationReference.clip, copy);
                            skinnedAniRef.animationReference.clip = copy;
                        }

                        AnimationClip clip = skinnedAniRef.animationReference.clip;
                        EditorCurveBinding curveBinding = skinnedAniRef.curve;

                        string path = AnimationUtility.CalculateTransformPath(targetSkinnedMesh.transform, skinnedAniRef.animationReference.root.transform);

                        if (skinnedAniRef.curve.propertyName.StartsWith("m_Materials."))
                        {
                            ObjectReferenceKeyframe[] newCurve = AnimationUtility.GetObjectReferenceCurve(clip, curveBinding);
                            // AnimationUtility.SetObjectReferenceCurve(clip, curveBinding, null); // Euan: it doesn't seem to like me removing the old curve

                            int startIndex = curveBinding.propertyName.IndexOf('[')+1;
                            int endIndex = curveBinding.propertyName.IndexOf(']');
                            int matIndex = int.Parse(curveBinding.propertyName.Substring(startIndex, endIndex - startIndex));

                            Debug.Log($"Adjusted material swap on {clip.name} from {matIndex} to {materialsSoFar + matIndex}");

                            EditorCurveBinding newCurveBinding = EditorCurveBinding.PPtrCurve(path, curveBinding.type, $"m_Materials.Array.data[{materialsSoFar + matIndex}]");

                            AnimationUtility.SetObjectReferenceCurve(clip, newCurveBinding, newCurve);
                        }
                        else
                        {
                            AnimationCurve newCurve = AnimationUtility.GetEditorCurve(clip, curveBinding);
                            // AnimationUtility.SetEditorCurve(clip, curveBinding, null); // Euan: it doesn't seem to like me removing the old curve

                            EditorCurveBinding newCurveBinding = curveBinding.isDiscreteCurve ?
                                EditorCurveBinding.DiscreteCurve(path, curveBinding.type, curveBinding.propertyName) :
                                EditorCurveBinding.FloatCurve(path, curveBinding.type, curveBinding.propertyName);
                            AnimationUtility.SetEditorCurve(clip, newCurveBinding, newCurve);
                        }
                    }
                }

                materialsSoFar += kvp.Key.sharedMaterials.Length;
            }

            // Adjust blendshape mesh, eye blendshapes + mesh if needed

            foreach (AnimationClip clip in clipRef.Values)
            {
                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();

            Util.ReplaceAnimations(descriptor, clipRef);
        }
    }
}