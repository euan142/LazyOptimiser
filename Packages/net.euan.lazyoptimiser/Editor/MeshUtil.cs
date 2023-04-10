using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LazyOptimiser
{
    public static class MeshUtil
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

        public class SkinnedMeshData
        {
            public Vector3[] vertices;
            public List<Vector2>[] uvChannels;
            public Vector3[] normals;
            public Vector4[] tangents;
            public List<Color> colors;
            public Dictionary<Material, int[]> subMeshes;
            public List<IIDBasedBoneWeight> weights;
            internal Dictionary<Transform, Matrix4x4> bindPoses;
            public List<BlendShapeData> blendshapes;
            public Bounds bounds;

            public Matrix4x4 localToWorldMatrix;
            public Matrix4x4 worldToLocalMatrix;

            public void Apply(SkinnedMeshRenderer skinnedMeshRenderer, bool newMesh = true)
            {
                Mesh mesh = newMesh ? Util.CloneAsset(new Mesh(), null, true) : skinnedMeshRenderer.sharedMesh;
                skinnedMeshRenderer.sharedMesh = mesh;

                mesh.indexFormat = vertices.Length > ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;

                mesh.vertices = vertices;

                for (int uvChannel = 0; uvChannel < 8; uvChannel++)
                {
                    if (uvChannels[uvChannel].Exists(uv => uv != DEFAULT_UV))
                    {
                        mesh.SetUVs(uvChannel, uvChannels[uvChannel]);
                    }
                }

                mesh.normals = normals;
                mesh.tangents = tangents;
                mesh.colors = colors.ToArray();

                var materials = skinnedMeshRenderer.materials = subMeshes.Keys.ToArray();

                mesh.subMeshCount = subMeshes.Count;
                for (int i = 0; i < subMeshes.Count; i++)
                {
                    mesh.SetTriangles(subMeshes[materials[i]], i, false);
                }

                HashSet<Transform> bones = new HashSet<Transform>();
                bones.UnionWith(weights.Select(w => w.bone0));
                bones.UnionWith(weights.Select(w => w.bone1));
                bones.UnionWith(weights.Select(w => w.bone2));
                bones.UnionWith(weights.Select(w => w.bone3));

                skinnedMeshRenderer.bones = bones.ToArray();

                mesh.boneWeights = weights.Select(IIDBased => new BoneWeight
                {
                    boneIndex0 = Array.IndexOf(skinnedMeshRenderer.bones, IIDBased.bone0),
                    boneIndex1 = Array.IndexOf(skinnedMeshRenderer.bones, IIDBased.bone1),
                    boneIndex2 = Array.IndexOf(skinnedMeshRenderer.bones, IIDBased.bone2),
                    boneIndex3 = Array.IndexOf(skinnedMeshRenderer.bones, IIDBased.bone3),
                    weight0 = IIDBased.weight0,
                    weight1 = IIDBased.weight1,
                    weight2 = IIDBased.weight2,
                    weight3 = IIDBased.weight3
                }).ToArray();

                mesh.bindposes = bones.Select(bone => bone != null ? bindPoses[bone] : Matrix4x4.zero).ToArray();

                for (int i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(i, 0);
                }

                foreach (var blendShape in blendshapes)
                {
                    foreach (var frame in blendShape.frames)
                    {
                        mesh.AddBlendShapeFrame(blendShape.name, frame.weight, frame.deltaVertices, frame.deltaNormals, frame.deltaTangents);
                    }

                    skinnedMeshRenderer.SetBlendShapeWeight(mesh.GetBlendShapeIndex(blendShape.name), blendShape.weight);
                }

                skinnedMeshRenderer.localBounds = bounds;

                mesh.Optimize();


                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(skinnedMeshRenderer);
            }

            public void RemoveVertices(bool[] verticesToRemove)
            {
                Dictionary<int, int> changeMap = new Dictionary<int, int>();
                int newIndex = -1;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (verticesToRemove[i])
                    {
                        changeMap[i] = -1;
                    }
                    else
                    {
                        newIndex++;
                        changeMap[i] = newIndex;
                    }
                }

                vertices = vertices.Where((_, i) => verticesToRemove[i] == false).ToArray();
                normals = normals.Where((_, i) => verticesToRemove[i] == false).ToArray();
                tangents = tangents.Where((_, i) => verticesToRemove[i] == false).ToArray();

                colors = colors.Where((_, i) => verticesToRemove[i] == false).ToList();
                weights = weights.Where((_, i) => verticesToRemove[i] == false).ToList();

                for (int i = 0; i < uvChannels.Length; i++)
                {
                    uvChannels[i] = uvChannels[i].Where((_, j) => verticesToRemove[j] == false).ToList();
                }

                foreach (var subMesh in subMeshes.ToArray())
                {
                    List<int> newTris = new List<int>();
                    for (int i = 0; i < subMesh.Value.Length; i += 3)
                    {
                        int t1 = changeMap[subMesh.Value[i]];
                        int t2 = changeMap[subMesh.Value[i+1]];
                        int t3 = changeMap[subMesh.Value[i+2]];
                        if (t1 != -1 && t2 != -1 && t3 != -1)
                        {
                            newTris.Add(t1);
                            newTris.Add(t2);
                            newTris.Add(t3);
                        }
                    }
                    subMeshes[subMesh.Key] = newTris.ToArray();
                }

                foreach (var blendshape in blendshapes)
                {
                    foreach (var frame in blendshape.frames)
                    {
                        frame.deltaVertices = frame.deltaVertices.Where((_, i) => verticesToRemove[i] == false).ToArray();
                        frame.deltaNormals = frame.deltaNormals.Where((_, i) => verticesToRemove[i] == false).ToArray();
                        frame.deltaTangents = frame.deltaTangents.Where((_, i) => verticesToRemove[i] == false).ToArray();
                    }
                }
            }
        }

        public class BlendShapeData
        {
            public string name;
            public List<BlendShapeFrame> frames;
            internal float weight;
        }

        public class BlendShapeFrame
        {
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
            public float weight;

            public BlendShapeFrame()
            {
            }

            public BlendShapeFrame(int vertexCount, float weight)
            {
                deltaVertices = new Vector3[vertexCount];
                deltaNormals = new Vector3[vertexCount];
                deltaTangents = new Vector3[vertexCount];
                this.weight = weight;
            }
        }

        private static readonly Vector2 DEFAULT_UV = Vector2.zero;
        private static readonly Color DEFAULT_COLOR = new Color(1, 1, 1, 0);

        public static void MergeSkinnedMeshes(List<SkinnedMeshData> skinnedMeshDatas)
        {
            if (skinnedMeshDatas.Count <= 1) return; //Nothing to combine

            SkinnedMeshData baseSkinnedMeshData = skinnedMeshDatas[0];

            var others = skinnedMeshDatas.Skip(1).ToList();

            foreach (var other in others)
            {
                Matrix4x4 diffMatrix = baseSkinnedMeshData.worldToLocalMatrix * other.localToWorldMatrix;
                Vector3 ConvertVertex(Vector3 v) => diffMatrix.MultiplyPoint(v);

                int vertexOffset = baseSkinnedMeshData.vertices.Length;
                baseSkinnedMeshData.vertices = baseSkinnedMeshData.vertices.Concat(other.vertices.Select(v => ConvertVertex(v))).ToArray();

                for (int i = 0; i < other.uvChannels.Length; i++)
                {
                    baseSkinnedMeshData.uvChannels[i].AddRange(other.uvChannels[i]);
                }

                baseSkinnedMeshData.normals = baseSkinnedMeshData.normals.Concat(other.normals).ToArray();
                baseSkinnedMeshData.tangents = baseSkinnedMeshData.tangents.Concat(other.tangents).ToArray();

                baseSkinnedMeshData.colors.AddRange(other.colors);

                foreach (var subMesh in other.subMeshes)
                {
                    var newTris = subMesh.Value.Select(t => t + vertexOffset);

                    if (baseSkinnedMeshData.subMeshes.ContainsKey(subMesh.Key))
                        baseSkinnedMeshData.subMeshes[subMesh.Key] = baseSkinnedMeshData.subMeshes[subMesh.Key].Concat(newTris).ToArray();
                    else
                        baseSkinnedMeshData.subMeshes.Add(subMesh.Key, newTris.ToArray());
                }

                baseSkinnedMeshData.weights.AddRange(other.weights);

                foreach (var kvp in other.bindPoses)
                {
                    if (!baseSkinnedMeshData.bindPoses.ContainsKey(kvp.Key))
                    {
                        baseSkinnedMeshData.bindPoses[kvp.Key] = kvp.Value * diffMatrix.inverse;
                    }
                }

                foreach (var blendshape in other.blendshapes)
                {
                    var existingBlendshape = baseSkinnedMeshData.blendshapes.Find(b => b.name == blendshape.name);
                    if (existingBlendshape == null)
                    {
                        List<BlendShapeFrame> frameData = new List<BlendShapeFrame>();
                        foreach (var frame in blendshape.frames)
                        {
                            frameData.Add(new BlendShapeFrame()
                            {
                                deltaVertices = Enumerable.Repeat(Vector3.zero, vertexOffset).Concat(frame.deltaVertices).ToArray(),
                                deltaNormals = Enumerable.Repeat(Vector3.zero, vertexOffset).Concat(frame.deltaNormals).ToArray(),
                                deltaTangents = Enumerable.Repeat(Vector3.zero, vertexOffset).Concat(frame.deltaTangents).ToArray(),
                                weight = frame.weight,
                            });
                        }
                        baseSkinnedMeshData.blendshapes.Add(new BlendShapeData
                        {
                            name = blendshape.name,
                            frames = frameData,
                            weight = blendshape.weight,
                        });
                    }
                    else
                    {
                        int maxFrames = Math.Max(existingBlendshape.frames.Count, blendshape.frames.Count);
                        for (int i = 0; i < maxFrames; i++)
                        {
                            BlendShapeFrame eframe;
                            if (existingBlendshape.frames.Count > i)
                            {
                                eframe = existingBlendshape.frames[i];
                            }
                            else
                            {
                                eframe = existingBlendshape.frames[existingBlendshape.frames.Count-1];
                                eframe = new BlendShapeFrame()
                                {
                                    deltaVertices = eframe.deltaVertices,
                                    deltaNormals = eframe.deltaNormals,
                                    deltaTangents = eframe.deltaTangents,
                                    weight = eframe.weight,
                                };
                                existingBlendshape.frames.Add(eframe);
                            }

                            BlendShapeFrame frame = blendshape.frames.Count > i ? blendshape.frames[i] : blendshape.frames[blendshape.frames.Count-1];
                            eframe.deltaVertices = eframe.deltaVertices.Concat(frame.deltaVertices).ToArray();
                            eframe.deltaNormals = eframe.deltaNormals.Concat(frame.deltaNormals).ToArray();
                            eframe.deltaTangents = eframe.deltaTangents.Concat(frame.deltaTangents).ToArray();
                            eframe.weight = Math.Max(eframe.weight, frame.weight);
                        }
                    }
                }

                baseSkinnedMeshData.bounds.Encapsulate(other.bounds);
            }
        }

        public static SkinnedMeshData ToSkinnedMeshData(this SkinnedMeshRenderer skinnedMeshRenderer)
        {
            Mesh mesh = skinnedMeshRenderer.sharedMesh;
            int missing;
            int vertexCount = mesh.vertexCount;

            SkinnedMeshData skinnedMeshData = new SkinnedMeshData();

            skinnedMeshData.vertices = mesh.vertices;

            // Get all uvchannels (there is 8 of them, though most meshes and shaders use only 4 first
            skinnedMeshData.uvChannels = new List<Vector2>[8];
            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
            {
                var UVs = new List<Vector2>();
                mesh.GetUVs(uvChannel, UVs);
                skinnedMeshData.uvChannels[uvChannel] = UVs;
            }

            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
                if ((missing = vertexCount - skinnedMeshData.uvChannels[uvChannel].Count) > 0)
                    skinnedMeshData.uvChannels[uvChannel].AddRange(Enumerable.Repeat(DEFAULT_UV, missing));


            // Recalculate the normals if they arent present
            if (mesh.normals.Length < vertexCount)
                mesh.RecalculateNormals();
            skinnedMeshData.normals = mesh.normals.ToArray();

            // Recalculate the tangents if they arent present
            if (mesh.tangents.Length < vertexCount)
                mesh.RecalculateTangents();
            skinnedMeshData.tangents = mesh.tangents.ToArray();

            //If colors dont exist theyll be padded alongside uvs later
            skinnedMeshData.colors = mesh.colors.ToList();

            if ((missing = vertexCount - skinnedMeshData.colors.Count) > 0)
                skinnedMeshData.colors.AddRange(Enumerable.Repeat(DEFAULT_COLOR, missing));

            //Get all the triangles per material, this wont need to be padded
            skinnedMeshData.subMeshes = new Dictionary<Material, int[]>();
            for (int materialSlot = 0; materialSlot < mesh.subMeshCount; materialSlot++)
            {
                //Add to the facesPerMaterial
                if (skinnedMeshData.subMeshes.ContainsKey(skinnedMeshRenderer.sharedMaterials[materialSlot]))
                    skinnedMeshData.subMeshes[skinnedMeshRenderer.sharedMaterials[materialSlot]] = skinnedMeshData.subMeshes[skinnedMeshRenderer.sharedMaterials[materialSlot]].Concat(mesh.GetTriangles(materialSlot, true)).ToArray();
                else
                    skinnedMeshData.subMeshes.Add(skinnedMeshRenderer.sharedMaterials[materialSlot], mesh.GetTriangles(materialSlot, true));
            }

            //Vertex weights normally reference the bone index in the skinned mesh renderer,
            //however this index likely wont be the same in the combined mesh renderer,
            //therefore we store the instanceID of the transform instead to use it as a lookup instead.
            skinnedMeshData.weights = mesh.boneWeights.Select(indexedBoneWeight => new IIDBasedBoneWeight
            {
                bone0 = skinnedMeshRenderer.bones[indexedBoneWeight.boneIndex0],
                bone1 = skinnedMeshRenderer.bones[indexedBoneWeight.boneIndex1],
                bone2 = skinnedMeshRenderer.bones[indexedBoneWeight.boneIndex2],
                bone3 = skinnedMeshRenderer.bones[indexedBoneWeight.boneIndex3],
                weight0 = indexedBoneWeight.weight0,
                weight1 = indexedBoneWeight.weight1,
                weight2 = indexedBoneWeight.weight2,
                weight3 = indexedBoneWeight.weight3,
            }).ToList();

            if ((missing = vertexCount - skinnedMeshData.weights.Count) > 0)
            {
                var rootBone = skinnedMeshRenderer.rootBone;
                var defaultWeight = new IIDBasedBoneWeight()
                {
                    bone0 = rootBone,
                    bone1 = rootBone,
                    bone2 = rootBone,
                    bone3 = rootBone,
                    weight0 = 1
                };
                skinnedMeshData.weights.AddRange(Enumerable.Repeat(defaultWeight, missing));
            }

            skinnedMeshData.bindPoses = new Dictionary<Transform, Matrix4x4>();

            for (int i = 0; i < skinnedMeshRenderer.bones.Length; i++)
            {
                if (skinnedMeshRenderer.bones[i] == null)
                    continue;

                skinnedMeshData.bindPoses[skinnedMeshRenderer.bones[i]] = mesh.bindposes[i];
            }

            skinnedMeshData.blendshapes = new List<BlendShapeData>();
            for (int blendShapeIndex = 0; blendShapeIndex < mesh.blendShapeCount; blendShapeIndex++)
            {
                List<BlendShapeFrame> frameData = new List<BlendShapeFrame>();
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(blendShapeIndex); frame++)
                {
                    BlendShapeFrame currentFrame = new BlendShapeFrame(vertexCount, mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame));
                    mesh.GetBlendShapeFrameVertices(blendShapeIndex, frame, currentFrame.deltaVertices, currentFrame.deltaNormals, currentFrame.deltaTangents);
                    frameData.Add(currentFrame);
                }
                skinnedMeshData.blendshapes.Add(new BlendShapeData
                {
                    name = mesh.GetBlendShapeName(blendShapeIndex),
                    frames = frameData,
                    weight = skinnedMeshRenderer.GetBlendShapeWeight(blendShapeIndex),
                });
            }

            skinnedMeshData.bounds = skinnedMeshRenderer.localBounds;

            skinnedMeshData.localToWorldMatrix = skinnedMeshRenderer.transform.localToWorldMatrix;
            skinnedMeshData.worldToLocalMatrix = skinnedMeshRenderer.transform.worldToLocalMatrix;

            return skinnedMeshData;
        }
    }
}