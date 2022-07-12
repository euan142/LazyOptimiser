using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LazyOptimiser
{
    public class MeshUtil
    {
        public struct IIDBasedBoneWeight
        {
            public Transform bone0;
            public Transform bone1;
            public Transform bone2;
            public Transform bone3;
            public float weight0;
            public float weight1;
            public float weight2;
            public float weight3;
        }

        public struct SkinnedMeshData
        {
            public Vector3[] verticies;
            public List<Vector2>[] uvChannels;
            public Vector3[] normals;
            public Vector4[] tangents;
            public List<Color> colors;
            public Dictionary<Material, int[]> facesPerMaterial;
            public List<IIDBasedBoneWeight> weights;
            public List<BlendShapeData> blendShapes;
        }

        public struct BlendShapeData
        {
            public string name;
            public List<BlendShapeFrame> frames;
            public int frameCount => frames.Count;
        }

        public struct BlendShapeFrame
        {
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
            public int offset;
            public float weight;

            public BlendShapeFrame(int vertexCount, float weight, int offset)
            {
                deltaVertices = new Vector3[vertexCount];
                deltaNormals = new Vector3[vertexCount];
                deltaTangents = new Vector3[vertexCount];
                this.offset = offset;
                this.weight = weight;
            }
        }

        private static readonly Vector2 DEFAULT_UV = new Vector2(0, 0);
        private static readonly Color DEFAULT_COLOR = new Color(1, 1, 1, 0);

        public static void MergeSkinnedMeshes(List<SkinnedMeshRenderer> skinnedMeshRenderers, bool copyBlendshapes = true, bool force = false)
        {
            if (skinnedMeshRenderers.Count <= 1 && !force) return; //Nothing to combine

            Mesh mergedMesh = new Mesh() { name = string.Join("_", skinnedMeshRenderers.Select(smr => smr.name)) };
            var combinedSkinnedMeshRenderer = skinnedMeshRenderers[0];

            //Now we get all the bones
            var bones = skinnedMeshRenderers.SelectMany(smr => smr.bones).Distinct().Where(b => b != null).ToList();

            #region bone assignment and finding rootBone

            bool hasBones = false;
            Transform rootBone = combinedSkinnedMeshRenderer.transform;

            //Assign the bones and rootbone
            if (hasBones = bones.Count > 0)
            {
                //First we find the rootbone, and its index so that we can set it to be first in list, for tidyness
                rootBone = bones.First().root;
                var rootIndex = FindRootInList(rootBone, bones);
                rootBone = bones[rootIndex];
                bones.RemoveAt(rootIndex);
                bones.Insert(0, rootBone);
            }
            else
            {
                Debug.LogWarning("Combined mesh has no bones");
            }

            List<Vector3> originalBoneScales = new List<Vector3>();

            foreach (Transform bone in bones)
            {
                originalBoneScales.Add(bone.localScale);
                bone.localScale = Vector3.one;
            }

            #endregion

            Bounds newBounds = new Bounds();
            skinnedMeshRenderers.ForEach(smr => newBounds.Encapsulate(smr.localBounds));

            int totalVerticies = 0;
            var mergeData = skinnedMeshRenderers
                .Select(smr => ConstructMergeData(smr, ref totalVerticies, rootBone, copyBlendshapes)).ToList();

            //Assign the root bone and bones to the skinned mesh renderer
            combinedSkinnedMeshRenderer.bones = bones.ToArray();
            combinedSkinnedMeshRenderer.rootBone = rootBone;
            combinedSkinnedMeshRenderer.localBounds = newBounds;

            mergedMesh.vertices = mergeData.SelectMany(md => md.verticies).ToArray();
            mergedMesh.normals = mergeData.SelectMany(md => md.normals).ToArray();
            mergedMesh.tangents = mergeData.SelectMany(md => md.tangents).ToArray();

            //Only copy over colors if they are used
            var mergedColors = mergeData.SelectMany(md => md.colors).ToList();
            if (mergedColors.Exists(color => color != DEFAULT_COLOR))
                mergedMesh.colors = mergedColors.ToArray();

            //Uvs, only copy over the channels needed
            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
            {
                var UVs = mergeData.SelectMany(md => md.uvChannels[uvChannel]).ToList();
                if (UVs.Exists(uv => uv != DEFAULT_UV))
                {
                    mergedMesh.SetUVs(uvChannel, UVs);
                }
                else
                {
                    Debug.Log($"UV channel #{uvChannel + 1} is unused in merged mesh {mergedMesh.name}");
                }
            }

            #region faces and their materials (submeshes)

            //Assign the materials
            var materials = mergeData.SelectMany(md => md.facesPerMaterial.Keys.ToArray()).ToList();
            combinedSkinnedMeshRenderer.materials = materials.ToArray();

            //This merges all the submesh data and resolves the material slot in the combined skinned mesh renderer
            var subMeshData = mergeData.SelectMany(md => md.facesPerMaterial).GroupBy(key => key.Key)
                .ToDictionary(kvp => materials.IndexOf(kvp.Key), kvp => kvp.SelectMany(i => i.Value).ToArray());

            //assign the submesh data merged above
            mergedMesh.subMeshCount = materials.Count;
            foreach (var subMesh in subMeshData)
            {
                mergedMesh.SetTriangles(subMesh.Value, subMesh.Key);
            }

            #endregion
            #region weightpainting

            //Weight paiting and bindposes
            if (hasBones)
            {
                var vertexWeighting = mergeData.SelectMany(mD => mD.weights.Select(IIDBased => new BoneWeight
                {
                    boneIndex0 = bones.IndexOf(IIDBased.bone0),
                    boneIndex1 = bones.IndexOf(IIDBased.bone1),
                    boneIndex2 = bones.IndexOf(IIDBased.bone2),
                    boneIndex3 = bones.IndexOf(IIDBased.bone3),
                    weight0 = IIDBased.weight0,
                    weight1 = IIDBased.weight1,
                    weight2 = IIDBased.weight2,
                    weight3 = IIDBased.weight3
                })).ToArray();

                mergedMesh.boneWeights = vertexWeighting;

                var bindPoses = bones.Select(bone => bone.worldToLocalMatrix * combinedSkinnedMeshRenderer.localToWorldMatrix);
                mergedMesh.bindposes = bindPoses.ToArray();
            }

            #endregion
            #region blendshapes

            if (copyBlendshapes)
            {
                //merging blendshapes is a bit more complex, as several meshes can share the same blendshape name
                //But different information about said blendshape, therefore we must take some more special care.

                //Sort the mergeable data into a dictionary keyed by the resultinb blendshape name
                //And the values are an array of data to merge for that blendshape
                var mergeableBlendshapeData =
                    mergeData.SelectMany(md => md.blendShapes).GroupBy(blendShape => blendShape.name)
                        .ToDictionary(k => k.Key, v => v.ToArray());

                //Holds the merged blendshapes
                var mergedBlendshapes = new List<BlendShapeData>();

                #region merging of blendshapes

                foreach (var mergeJob in mergeableBlendshapeData)
                {
                    string blendshapeName = mergeJob.Key;
                    int frameCount = mergeJob.Value.Max(data => data.frameCount);
                    List<BlendShapeFrame> mergedFrames = new List<BlendShapeFrame>();

                    #region Merge the frame data

                    for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        var subFrames = mergeJob.Value.Select(blendShape => blendShape.frames[
                            //Not all meshes might have as many frames, so we get the last frame instead of going out ot bounds
                            Math.Min(frameIndex, blendShape.frameCount)]
                        ).ToArray();

                        //prepare the arrays
                        var mergedVertices = new Vector3[totalVerticies];
                        var mergedNormals = new Vector3[totalVerticies];
                        var mergedTangents = new Vector3[totalVerticies];

                        //Now we can apply the frames
                        foreach (var blendShapeFrame in subFrames)
                        {
                            Array.Copy(blendShapeFrame.deltaVertices, 0, mergedVertices, blendShapeFrame.offset, blendShapeFrame.deltaVertices.Length);
                            Array.Copy(blendShapeFrame.deltaNormals, 0, mergedNormals, blendShapeFrame.offset, blendShapeFrame.deltaNormals.Length);
                            Array.Copy(blendShapeFrame.deltaTangents, 0, mergedTangents, blendShapeFrame.offset, blendShapeFrame.deltaTangents.Length);
                        }

                        float mergedWeight = subFrames.Average(frame => frame.weight);
                        //Get the average frameWeight
                        mergedFrames.Add(new BlendShapeFrame
                        {
                            deltaVertices = mergedVertices,
                            deltaNormals = mergedNormals,
                            deltaTangents = mergedTangents,
                            weight = mergedWeight
                        });
                    }

                    #endregion

                    mergedBlendshapes.Add(new BlendShapeData
                    {
                        name = blendshapeName,
                        frames = mergedFrames
                    });
                }

                #endregion

                //Apply the merged blendshapes to the mesh
                foreach (var blendShapeData in mergedBlendshapes)
                {
                    for (int frame = 0; frame < blendShapeData.frameCount; frame++)
                    {
                        var frameData = blendShapeData.frames[frame];
                        mergedMesh.AddBlendShapeFrame(blendShapeData.name, frameData.weight, frameData.deltaVertices, frameData.deltaNormals, frameData.deltaTangents);
                    }
                }
            }

            #endregion

            // Re-apply bone scales
            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].localScale = originalBoneScales[i];
            }

            combinedSkinnedMeshRenderer.sharedMesh = Util.CloneAsset(mergedMesh, "merged", true);
            EditorUtility.SetDirty(mergedMesh);
            EditorUtility.SetDirty(combinedSkinnedMeshRenderer);

            for (int i = 1; i < skinnedMeshRenderers.Count; i++)
            {
                SkinnedMeshRenderer skinnedMesh = skinnedMeshRenderers[i];
                // Euan: Could we be smarter here?
                if (skinnedMesh.gameObject.GetComponents<Component>().Length == 2)
                    Object.DestroyImmediate(skinnedMesh.gameObject);
                else
                    Object.DestroyImmediate(skinnedMesh);
            }
        }

        public static int FindRootInList(Transform currentRoot, List<Transform> transforms)
        {
            int rootIndex;
            if ((rootIndex = transforms.IndexOf(currentRoot)) != -1) return rootIndex;

            for (int childIndex = 0; childIndex < currentRoot.childCount; childIndex++)
            {
                if ((rootIndex = FindRootInList(currentRoot.GetChild(childIndex), transforms)) != -1)
                    return rootIndex;
            }

            return -1; //Failed to find anything in this branch
        }

        private static SkinnedMeshData ConstructMergeData(SkinnedMeshRenderer smr, ref int vertexOffset,
            Transform defaultRootBone, bool blendshapeData)
        {
            var mesh = smr.sharedMesh;
            var vertexCount = mesh.vertexCount;
            var materialCount = smr.sharedMaterials.Length;

            #region Get data from mesh

            //Vertices array is always full size of vertexCount so we can just directly copy the array.
            var vertices = mesh.vertices;

            //Recalculate the normals and tangetns if they arent present
            if (mesh.normals.Length < vertexCount)
                mesh.RecalculateNormals();
            var vertexNormals = mesh.normals.ToArray();

            if (mesh.tangents.Length < vertexCount)
                mesh.RecalculateTangents();
            var vertexTangents = mesh.tangents.ToArray();

            //If colors dont exist theyll be padded alongside uvs later
            var vertexColors = mesh.colors.ToList();
            //Vertex weights normally reference the bone index in the skinned mesh renderer,
            //however this index likely wont be the same in the combined mesh renderer,
            //therefore we store the instanceID of the transform instead to use it as a lookup instead.
            var vertexWeights = mesh.boneWeights.Select(indexedBoneWeight => new IIDBasedBoneWeight
            {
                bone0 = smr.bones[indexedBoneWeight.boneIndex0],
                bone1 = smr.bones[indexedBoneWeight.boneIndex1] /*.GetInstanceID()*/,
                bone2 = smr.bones[indexedBoneWeight.boneIndex2] /*.GetInstanceID()*/,
                bone3 = smr.bones[indexedBoneWeight.boneIndex3] /*.GetInstanceID()*/,
                weight0 = indexedBoneWeight.weight0,
                weight1 = indexedBoneWeight.weight1,
                weight2 = indexedBoneWeight.weight2,
                weight3 = indexedBoneWeight.weight3,
            }).ToList();

            //Get all uvchannels (there is 8 of them, though most meshes and shaders use only 4 first
            var uvChannels = new List<Vector2>[8];
            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
            {
                var UVs = new List<Vector2>();
                mesh.GetUVs(uvChannel, UVs);
                uvChannels[uvChannel] = UVs;
            }

            //Get all the triangles per material, this wont need to be padded
            var facesPerMaterial = new Dictionary<Material, int[]>();
            for (int materialSlot = 0; materialSlot < materialCount; materialSlot++)
            {
                var trianglesInMaterial = mesh.GetTriangles(materialSlot, true);

                //Add the vertexOffset to them
                int triangleCount = trianglesInMaterial.Length;
                for (int i = 0; i < triangleCount; i++)
                    trianglesInMaterial[i] = trianglesInMaterial[i] + vertexOffset;

                //Add to the facesPerMaterial
                if (facesPerMaterial.ContainsKey(smr.sharedMaterials[materialSlot]))
                    facesPerMaterial[smr.sharedMaterials[materialSlot]] = facesPerMaterial[smr.sharedMaterials[materialSlot]].Concat(trianglesInMaterial).ToArray();
                else
                    facesPerMaterial.Add(smr.sharedMaterials[materialSlot], trianglesInMaterial);
            }

            #region blendshape

            List<BlendShapeData> blendShapes = new List<BlendShapeData>();

            if (blendshapeData)
            {
                for (int blendShapeIndex = 0; blendShapeIndex < mesh.blendShapeCount; blendShapeIndex++)
                {
                    List<BlendShapeFrame> frameData = new List<BlendShapeFrame>();
                    for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(blendShapeIndex); frame++)
                    {
                        BlendShapeFrame currentFrame = new BlendShapeFrame(vertexCount, mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame), vertexOffset);
                        mesh.GetBlendShapeFrameVertices(blendShapeIndex, frame, currentFrame.deltaVertices, currentFrame.deltaNormals, currentFrame.deltaTangents);
                        frameData.Add(currentFrame);
                    }
                    blendShapes.Add(new BlendShapeData
                    {
                        name = mesh.GetBlendShapeName(blendShapeIndex),
                        frames = frameData
                    });
                }
            }

            #endregion

            #endregion

            #region Pad the UV, color and weightpaint data if missing

            int missing = 0;
            if ((missing = vertexCount - vertexWeights.Count) > 0)
            {
                var rootBone = smr.rootBone ?? defaultRootBone;
                var defaultWeight = new IIDBasedBoneWeight()
                {
                    bone0 = rootBone,
                    bone1 = rootBone,
                    bone2 = rootBone,
                    bone3 = rootBone,
                    weight0 = 1
                };
                vertexWeights.AddRange(Enumerable.Repeat(defaultWeight, missing));
            }

            if ((missing = vertexCount - vertexColors.Count) > 0)
                vertexColors.AddRange(Enumerable.Repeat(DEFAULT_COLOR, missing));
            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
                if ((missing = vertexCount - uvChannels[uvChannel].Count) > 0)
                    uvChannels[uvChannel].AddRange(Enumerable.Repeat(DEFAULT_UV, missing));

            #endregion

            //Append to the vertex offset
            vertexOffset += vertexCount;

            return new SkinnedMeshData
            {
                verticies = vertices,
                uvChannels = uvChannels,
                normals = vertexNormals,
                tangents = vertexTangents,
                colors = vertexColors,
                facesPerMaterial = facesPerMaterial,
                weights = vertexWeights,
                blendShapes = blendShapes
            };
        }
    }
}