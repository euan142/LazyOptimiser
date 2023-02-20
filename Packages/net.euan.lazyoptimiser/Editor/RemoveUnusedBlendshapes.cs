using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using static LazyOptimiser.MeshUtil;

namespace LazyOptimiser
{
    public class RemoveUnusedBlendshapes : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -95;

        [MenuItem("Tools/Lazy Optimiser/Print Unused Blendshapes")]
        public static void PrintUnusedBlendshapes()
        {
            ProcessAvatar(Selection.activeGameObject);
        }

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            ProcessAvatar(avatarGameObject, true);
            return true;
        }

        public static void ProcessAvatar(GameObject avatarGameObject, bool doDestroy = false)
        {
            VRCAvatarDescriptor descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();

            List<AnimationReferences> animationRefs = Util.GetAllAnimations(descriptor);

            Dictionary<SkinnedMeshRenderer, HashSet<string>> usedBlendshapes = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();
            Dictionary<SkinnedMeshRenderer, HashSet<string>> animatedBlendshapes = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();

            foreach (SkinnedMeshRenderer skinnedMesh in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                HashSet<string> blendshapes = new HashSet<string>();
                HashSet<string> aniBlendshapes = new HashSet<string>();

                if (skinnedMesh == descriptor.VisemeSkinnedMesh)
                {
                    foreach (var blendshape in descriptor.VisemeBlendShapes)
                    {
                        blendshapes.Add(blendshape);
                        aniBlendshapes.Add(blendshape);
                    }
                }

                if (skinnedMesh == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
                {
                    foreach (var i in descriptor.customEyeLookSettings.eyelidsBlendshapes)
                    {
                        if (i >= 0 && i < skinnedMesh.sharedMesh.blendShapeCount)
                        {
                            string blendshapeName = skinnedMesh.sharedMesh.GetBlendShapeName(i);
                            blendshapes.Add(blendshapeName);
                            aniBlendshapes.Add(blendshapeName);
                        }
                    }
                }

                for (var i = 0; i < skinnedMesh.sharedMesh.blendShapeCount; i++)
                {
                    if (skinnedMesh.GetBlendShapeWeight(i) != 0)
                    {
                        blendshapes.Add(skinnedMesh.sharedMesh.GetBlendShapeName(i));
                    }
                }
                
                usedBlendshapes.Add(skinnedMesh, blendshapes);
                animatedBlendshapes.Add(skinnedMesh, aniBlendshapes);
            }

            foreach (AnimationReferences animationReference in animationRefs)
            {
                foreach (EditorCurveBinding curve in animationReference.curveBindings)
                {
                    if (curve.type == typeof(SkinnedMeshRenderer) && curve.propertyName.StartsWith("blendShape."))
                    {
                        string blendshapeName = curve.propertyName.Remove(0, "blendShape.".Length);
                        SkinnedMeshRenderer skinnedMesh = (SkinnedMeshRenderer)AnimationUtility.GetAnimatedObject(avatarGameObject, curve);
                        if (skinnedMesh == null) continue; //Handle possible nullref

                        usedBlendshapes[skinnedMesh].Add(blendshapeName);
                        animatedBlendshapes[skinnedMesh].Add(blendshapeName);
                    }
                }
            }

            foreach (var kvp in usedBlendshapes)
            {
                if (doDestroy)
                {
                    StripBlendshapes(descriptor, kvp.Key, kvp.Value, animatedBlendshapes[kvp.Key]);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    List<string> unusedBlendshapes = new List<string>();

                    for (var i = 0; i < kvp.Key.sharedMesh.blendShapeCount; i++)
                    {
                        string blendshapeName = kvp.Key.sharedMesh.GetBlendShapeName(i);

                        if (kvp.Value.Contains(blendshapeName) == false)
                        {
                            unusedBlendshapes.Add(blendshapeName);
                        }
                    }

                    if (unusedBlendshapes.Count != 0)
                    {
                        Debug.LogError($"{kvp.Key.name} has {unusedBlendshapes.Count} unused blendshapes: {string.Join(", ", unusedBlendshapes)}", kvp.Key);
                    }
                }
            }
        }

        private static void StripBlendshapes(VRCAvatarDescriptor descriptor, SkinnedMeshRenderer skinnedMeshRenderer, HashSet<string> usedBlendshapes, HashSet<string> animatedBlendshapes)
        {
            string[] eyeBlendshapes = null;

            if (skinnedMeshRenderer == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
            {
                eyeBlendshapes = descriptor.customEyeLookSettings.eyelidsBlendshapes.Select(n => n >= 0 && n < skinnedMeshRenderer.sharedMesh.blendShapeCount ? skinnedMeshRenderer.sharedMesh.GetBlendShapeName(n) : "").ToArray();
            }

            var skinnedMeshData = skinnedMeshRenderer.ToSkinnedMeshData();

            foreach (var blendshape in skinnedMeshData.blendshapes.ToArray())
            {
                if (usedBlendshapes.Contains(blendshape.name) == false)
                {
                    skinnedMeshData.blendshapes.Remove(blendshape);
                    continue;
                }

                if (animatedBlendshapes.Contains(blendshape.name))
                {
                    continue;
                }

                // Euan: Instead of blendshape weight, would it be more appropriate to use the frame count?
                var lastFrame = blendshape.frames[blendshape.frames.Count - 1];
                float weightMulti = blendshape.weight / 100;

                for (int i = 0; i < skinnedMeshData.vertices.Length; i++)
                {
                    skinnedMeshData.vertices[i] += lastFrame.deltaVertices[i] * weightMulti;
                }

                for (int i = 0; i < skinnedMeshData.normals.Length; i++)
                {
                    skinnedMeshData.normals[i] += lastFrame.deltaNormals[i] * weightMulti;
                }

                for (int i = 0; i < skinnedMeshData.tangents.Length; i++)
                {
                    skinnedMeshData.tangents[i] += (Vector4)(lastFrame.deltaTangents[i] * weightMulti);
                }

                skinnedMeshData.blendshapes.Remove(blendshape);
            }

            skinnedMeshData.Apply(skinnedMeshRenderer);

            if (skinnedMeshRenderer == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
            {
                descriptor.customEyeLookSettings.eyelidsBlendshapes = eyeBlendshapes.Select(skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex).ToArray();
            }
        }
    }
}
