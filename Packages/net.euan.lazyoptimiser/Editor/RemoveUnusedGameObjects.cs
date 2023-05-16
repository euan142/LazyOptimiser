using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class RemoveUnusedGameObjects : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -100;

        [MenuItem("Tools/Lazy Optimiser/Print Unused GameObjects")]
        public static void PrintUnusedGameObjects()
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
            HashSet<Object> allReferencedObjects = new HashSet<Object>();

            foreach (AnimationReferences aniRef in animationRefs)
            {
                for (int i = 0; i < aniRef.curveBindings.Length; i++)
                {
                    EditorCurveBinding thing = aniRef.curveBindings[i];
                    if (thing.propertyName == "m_IsActive" || thing.propertyName == "m_enabled")
                    {
                        allReferencedObjects.Add(aniRef.referencedObjects[i]);
                        Transform t = null;
                        if (aniRef.referencedObjects[i] is GameObject refgo)
                        {
                            t = refgo.transform;
                        }
                        else if (aniRef.referencedObjects[i] is MonoBehaviour refMono)
                        {
                            t = refMono.transform;
                        } 
                        else continue; //Failed to find the transform
                        
                        // Euan: Traverse each level manually, stop where the child isn't active
                        foreach (var go in t.GetComponentsInChildren<Transform>(true))
                        {
                            if (go.gameObject.activeSelf)
                                allReferencedObjects.Add(go);
                        }
                    }
                }
            }

            allReferencedObjects.Add(descriptor.customEyeLookSettings.leftEye);
            allReferencedObjects.Add(descriptor.customEyeLookSettings.rightEye);
            allReferencedObjects.Add(descriptor.customEyeLookSettings.lowerLeftEyelid);
            allReferencedObjects.Add(descriptor.customEyeLookSettings.lowerRightEyelid);
            allReferencedObjects.Add(descriptor.customEyeLookSettings.upperLeftEyelid);
            allReferencedObjects.Add(descriptor.customEyeLookSettings.upperRightEyelid);

            allReferencedObjects.Add(descriptor.lipSyncJawBone);
            allReferencedObjects.Add(descriptor.VisemeSkinnedMesh);

            HashSet<GameObject> gameObjectsToRemove = new HashSet<GameObject>();

            Animator animator = avatarGameObject.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                foreach (var b in (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone)
                        continue;

                    Transform boneTransform = animator.GetBoneTransform(b);
                    if (boneTransform != null)
                        allReferencedObjects.Add(boneTransform);
                }
            }

            foreach (SkinnedMeshRenderer skinnedMesh in avatarGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform t = skinnedMesh.transform;
                if (ShouldntRemoveTransform(allReferencedObjects, t))
                    UsedGameobjectsInSkinnedMeshRenderer(skinnedMesh, allReferencedObjects);
            }

            foreach (IConstraint constraint in avatarGameObject.GetComponentsInChildren<IConstraint>(true))
            {
                Transform t = ((Behaviour)constraint).transform;
                if (ShouldntRemoveTransform(allReferencedObjects, t))
                {
                    allReferencedObjects.Add(t);
                    UsedGameobjectsInConstraint(constraint, allReferencedObjects);
                }
            }

            foreach (VRCPhysBone physBone in avatarGameObject.GetComponentsInChildren<VRCPhysBone>(true))
            {
                Transform t = physBone.transform;
                if (ShouldntRemoveTransform(allReferencedObjects, t))
                {
                    allReferencedObjects.Add(t);
                    UsedGameobjectsInPhysBone(physBone, allReferencedObjects);
                }
            }

            foreach (ContactBase contact in avatarGameObject.GetComponentsInChildren<ContactBase>(true))
            {
                Transform t = contact.transform;
                if (ShouldntRemoveTransform(allReferencedObjects, t))
                {
                    allReferencedObjects.Add(t);
                    allReferencedObjects.Add(contact.rootTransform);
                }
            }

            foreach (var obj in allReferencedObjects.ToArray())
            {
                Transform objectTransform;

                if (obj == null)
                    continue;

                if (obj is Component objComp)
                    objectTransform = objComp.transform;
                else if (obj is GameObject objGo)
                    objectTransform = objGo.transform;
                else
                    continue;

                // Euan: Sometimes things not under the gameobject hierarchy get referenced, we want to ignore those
                if (objectTransform.IsChildOf(avatarGameObject.transform) == false)
                    continue;

                allReferencedObjects.Add(objectTransform.gameObject);

                while (objectTransform != avatarGameObject.transform)
                {
                    objectTransform = objectTransform.parent;
                    allReferencedObjects.Add(objectTransform.gameObject);
                }
            }

            foreach (Transform t in avatarGameObject.GetComponentsInChildren<Transform>(true))
            {
                if (ShouldntRemoveTransform(allReferencedObjects, t))
                    continue;

                gameObjectsToRemove.Add(t.gameObject);
            }

            foreach (GameObject gameObject in gameObjectsToRemove)
            {
                if (gameObject == null)
                    continue;

                if (doDestroy)
                {
                    Debug.Log($"Stripping unused gameobject: {gameObject.name}");
                    Object.DestroyImmediate(gameObject);
                }
                else
                {
                    Debug.LogError($"Would strip unused gameobject: {gameObject.name}", gameObject);
                }
            }
        }

        private static bool ShouldntRemoveTransform(HashSet<Object> allowedObjects, Transform target)
        {
            return allowedObjects.Contains(target)
                || allowedObjects.Contains(target.gameObject)
                || target.gameObject.activeInHierarchy && target.GetComponents<Component>().Length != 1;
        }

        private static void UsedGameobjectsInSkinnedMeshRenderer(SkinnedMeshRenderer skinnedMesh, HashSet<Object> usedGameObjects)
        {
            var boneWeights = skinnedMesh.sharedMesh.GetAllBoneWeights();

            HashSet<int> usedBones = new HashSet<int>();

            foreach (var bw in boneWeights)
            {
                usedBones.Add(bw.boneIndex);
            }

            for (int i = 0; i < skinnedMesh.bones.Length; i++)
            {
                Transform t = skinnedMesh.bones[i];
                if (usedBones.Contains(i))
                {
                    usedGameObjects.Add(t.gameObject);

                    while (t != skinnedMesh.rootBone && t != null)
                    {
                        t = t.parent;
                        usedGameObjects.Add(t?.gameObject);
                    }
                }
            }
        }

        private static void UsedGameobjectsInConstraint(IConstraint constraint, HashSet<Object> usedGameObjects)
        {
            List<ConstraintSource> sources = new List<ConstraintSource>();
            constraint.GetSources(sources);
            foreach (var source in sources)
            {
                usedGameObjects.Add(source.sourceTransform?.gameObject);
            }
        }

        private static void UsedGameobjectsInPhysBone(VRCPhysBone physBone, HashSet<Object> usedGameObjects)
        {
            Transform rootTransform = physBone.rootTransform;

            if (rootTransform == null)
                rootTransform = physBone.transform;

            List<Transform> usedTransforms = rootTransform.GetComponentsInChildren<Transform>().ToList();

            foreach (var excludedGameobject in physBone.ignoreTransforms)
            {
                usedTransforms = usedTransforms.Except(excludedGameobject.GetComponentsInChildren<Transform>()).ToList();
            }

            usedTransforms.AddRange(physBone.colliders.Where(c => c != null).Select(c => c.transform));

            foreach (Transform t in usedTransforms)
            {
                usedGameObjects.Add(t);
            }
        }
    }
}
