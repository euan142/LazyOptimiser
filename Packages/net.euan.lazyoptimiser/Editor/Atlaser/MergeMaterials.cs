using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace LazyOptimiser.Atlaser
{
    // Kiba : Copied from MergeMeshes
    public class MergeMaterials
    {
        public struct MaterialAnimationReference
        {
            public EditorCurveBinding curve;
            public AnimationReferences animationReference;
        }

        public static void Merge(VRCAvatarDescriptor descriptor, bool doDestroy = false)
        {
            List<AnimationReferences> animationRefs = Util.GetAllAnimations(descriptor);

            Dictionary<Material, HashSet<string>> linkedMaterials = new Dictionary<Material, HashSet<string>>();
            Dictionary<Material, List<MergeMaterials.MaterialAnimationReference>> linkedAnimations = new Dictionary<Material, List<MergeMaterials.MaterialAnimationReference>>();

            
            
            foreach (Material[] mats in descriptor.GetComponentsInChildren<Renderer>(true).ToList().Select(x => x.sharedMaterials))
            {
                foreach (var mat in mats)
                {
                    if(linkedMaterials.ContainsKey(mat)) continue;
                    
                    linkedMaterials.Add(mat, new HashSet<string> { GetUniqueKey(mat) });
                    linkedAnimations.Add(mat, new List<MaterialAnimationReference>());
                }
            }
            
            foreach (var group in GetGroupedMaterials(linkedMaterials))
            {
                List<Material> skinnedMeshes = group.ToList();
                if (skinnedMeshes.Count > 1)
                {
                    if (doDestroy)
                    {
                        Debug.Log($"Grouping materials: {string.Join(", ", group.Select(smr => smr.name))}");
                    }
                    else
                    {
                        Debug.LogError($"Could group materials: {string.Join(", ", group.Select(smr => smr.name))}");
                    }
                }
            }

            if (doDestroy)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static IEnumerable<IEnumerable<Material>> GetGroupedMaterials(Dictionary<Material, HashSet<string>> Materials) =>
            Materials.GroupBy(kvp => kvp.Value, HashSet<string>.CreateSetComparer())
                .Select(group => group.Select(kvp2 => kvp2.Key));
        
        private static string GetUniqueKey(Material material) => new Key<Material>(new[] {
            "shader.InstanceID", "color"
        }).GetUniqueKey(material, 'M');
    }
}