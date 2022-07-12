using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

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

        private static void StripBlendshapes(VRCAvatarDescriptor descriptor, SkinnedMeshRenderer skinnedMesh, HashSet<string> usedBlendshapes, HashSet<string> animatedBlendshapes)
        {
            Mesh oldMesh = skinnedMesh.sharedMesh;
            Mesh newMesh = Util.CloneAsset(oldMesh, null, true);

            Dictionary<int, float> weightsToTransfer = new Dictionary<int, float>();

            newMesh.ClearBlendShapes();

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();

            newMesh.GetVertices(vertices);
            newMesh.GetNormals(normals);
            newMesh.GetTangents(tangents);

            foreach (string blendshape in usedBlendshapes)
            {
                int index = oldMesh.GetBlendShapeIndex(blendshape);

                if (index == -1)
                    continue;

                int frameCount = oldMesh.GetBlendShapeFrameCount(index);

                if (animatedBlendshapes.Contains(blendshape))
                {
                    for (int frameIndex = 0;frameIndex < frameCount;frameIndex++)
                    {
                        var deltaVertices = new Vector3[oldMesh.vertexCount];
                        var deltaNormals = new Vector3[oldMesh.vertexCount];
                        var deltaTangents = new Vector3[oldMesh.vertexCount];
                        oldMesh.GetBlendShapeFrameVertices(index, frameIndex, deltaVertices, deltaNormals, deltaTangents);
                        newMesh.AddBlendShapeFrame(blendshape, oldMesh.GetBlendShapeFrameWeight(index, frameIndex), deltaVertices, deltaNormals, deltaTangents);
                    }

                    float oldWeight = skinnedMesh.GetBlendShapeWeight(index);
                    if (oldWeight != 0)
                    {
                        weightsToTransfer.Add(newMesh.GetBlendShapeIndex(blendshape), oldWeight);
                    }
                }
                else
                {
                    var deltaVertices = new Vector3[oldMesh.vertexCount];
                    var deltaNormals = new Vector3[oldMesh.vertexCount];
                    var deltaTangents = new Vector3[oldMesh.vertexCount];
                    // Euan: Instead of `* targetWeight`, would it be more appropriate to use the frame count?
                    oldMesh.GetBlendShapeFrameVertices(index, frameCount-1, deltaVertices, deltaNormals, deltaTangents);
                    float targetWeight = skinnedMesh.GetBlendShapeWeight(index) / 100;

                    for (int i = 0; i < vertices.Count; i++)
                    {
                        vertices[i] += deltaVertices[i] * targetWeight;
                    }

                    for (int i = 0; i < normals.Count; i++)
                    {
                        normals[i] += deltaNormals[i] * targetWeight;
                    }

                    for (int i = 0; i < tangents.Count; i++)
                    {
                        tangents[i] += (Vector4)(deltaTangents[i] * targetWeight);
                    }
                }
            }

            newMesh.SetVertices(vertices);
            newMesh.SetNormals(normals);
            newMesh.SetTangents(tangents);

            skinnedMesh.sharedMesh = newMesh;

            for (int i = 0;i < newMesh.blendShapeCount;i++)
            {
                skinnedMesh.SetBlendShapeWeight(i, 0);
            }

            foreach (var kvp in weightsToTransfer)
            {
                skinnedMesh.SetBlendShapeWeight(kvp.Key, kvp.Value);
            }

            if (skinnedMesh == descriptor.customEyeLookSettings.eyelidsSkinnedMesh)
            {
                for (int i = 0; i < descriptor.customEyeLookSettings.eyelidsBlendshapes.Length; i++)
                {
                    int oldIndex = descriptor.customEyeLookSettings.eyelidsBlendshapes[i];
                    if (oldIndex >= 0 && oldIndex < oldMesh.blendShapeCount)
                    {
                        descriptor.customEyeLookSettings.eyelidsBlendshapes[i] = newMesh.GetBlendShapeIndex(oldMesh.GetBlendShapeName(oldIndex));
                    }
                }
            }


            EditorUtility.SetDirty(newMesh);
        }
    }
}
