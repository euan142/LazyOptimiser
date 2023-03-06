using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class RemoveMarkedBlendshapes : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -96;

        [MenuItem("Tools/Lazy Optimiser/Print Marked Blendshapes")]
        public static void PrintUnusedBlendshapes()
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

            Dictionary<SkinnedMeshRenderer, HashSet<string>> usedBlendshapes = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();

            foreach (SkinnedMeshRenderer skinnedMesh in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                HashSet<string> blendshapes = new HashSet<string>();

                for (var i = 0; i < skinnedMesh.sharedMesh.blendShapeCount; i++)
                {
                    string blendshapeName = skinnedMesh.sharedMesh.GetBlendShapeName(i);

                    if (blendshapeName.StartsWith("remove_") && skinnedMesh.GetBlendShapeWeight(i) != 0)
                    {
                        blendshapes.Add(blendshapeName);
                    }
                }
                
                usedBlendshapes.Add(skinnedMesh, blendshapes);
            }

            // We want to avoid removing any blendshapes that are animated, even if it is one of the worse ways to hide mesh.
            // There is also the edge case not caught here of someone using the blendshapes being removed for visemes or eye movement however
            // that is not something that should be done really as such I'm not going add support unless someone provides a valid use case
            List<AnimationReferences> animationRefs = Util.GetAllAnimations(descriptor);
            foreach (AnimationReferences animationReference in animationRefs)
            {
                foreach (EditorCurveBinding curve in animationReference.curveBindings)
                {
                    if (curve.type == typeof(SkinnedMeshRenderer) && curve.propertyName.StartsWith("blendShape."))
                    {
                        string blendshapeName = curve.propertyName.Remove(0, "blendShape.".Length);
                        SkinnedMeshRenderer skinnedMesh = (SkinnedMeshRenderer)AnimationUtility.GetAnimatedObject(avatarGameObject, curve);
                        if (skinnedMesh == null) continue; //Handle possible nullref

                        if (usedBlendshapes[skinnedMesh].Contains(blendshapeName))
                        {
                            usedBlendshapes[skinnedMesh].Remove(blendshapeName);
                        }
                    }
                }
            }

            foreach (var kvp in usedBlendshapes)
            {
                if (doDestroy)
                {
                    StripBlendshapeVertices(descriptor, kvp.Key, kvp.Value);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    if (kvp.Value.Count != 0)
                    {
                        Debug.LogError($"{kvp.Key.name} has {kvp.Value.Count} blendshapes that will strip vertices: {string.Join(", ", kvp.Value)}", kvp.Key);
                    }
                }
            }
        }

        private static void StripBlendshapeVertices(VRCAvatarDescriptor descriptor, SkinnedMeshRenderer skinnedMeshRenderer, HashSet<string> markedBlendshapes)
        {
            if (markedBlendshapes.Count == 0)
            {
                return;
            }

            string[] eyeBlendshapes = null;

            if (skinnedMeshRenderer == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
            {
                eyeBlendshapes = descriptor.customEyeLookSettings.eyelidsBlendshapes.Select(n => n >= 0 && n < skinnedMeshRenderer.sharedMesh.blendShapeCount ? skinnedMeshRenderer.sharedMesh.GetBlendShapeName(n) : "").ToArray();
            }

            var skinnedMeshData = skinnedMeshRenderer.ToSkinnedMeshData();

            bool[] verticesToRemove = Enumerable.Repeat(false, skinnedMeshData.vertices.Length).ToArray();

            foreach (var blendshape in skinnedMeshData.blendshapes.ToArray())
            {
                if (markedBlendshapes.Contains(blendshape.name) == false)
                {
                    continue;
                }

                var lastFrame = blendshape.frames[blendshape.frames.Count - 1];

                for (int i = 0; i < skinnedMeshData.vertices.Length; i++)
                {
                    verticesToRemove[i] |= lastFrame.deltaVertices[i] != Vector3.zero;
                }

                skinnedMeshData.blendshapes.Remove(blendshape);
            }

            skinnedMeshData.RemoveVertices(verticesToRemove);

            skinnedMeshData.Apply(skinnedMeshRenderer);

            if (skinnedMeshRenderer == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
            {
                descriptor.customEyeLookSettings.eyelidsBlendshapes = eyeBlendshapes.Select(skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex).ToArray();
            }
        }
    }
}
