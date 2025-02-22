using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LazyOptimiser
{
    // Kiba : Move from MergeMeshes.cs to Key.cs because I want to use it in other classes as well.
    public class Key<T> where T : Object
    {
        public string[] properties = { };
        
        public Key(string[] properties = null)
        {
            this.properties = properties;
        }

        public void Add(string property) => this.properties = this.properties.Append(property).ToArray();

        public void Add(string[] properties) => this.properties = this.properties.Concat(properties).ToArray();
        
        public void Clear() => this.properties = new string[] { };

        // Euan: Right now this is generating a long string to act as a unique key which can then be grouped by,
        // a different solution to deeply compare how something is animated directly would be more reliable
        public string GetUniqueKey(T referenceObject, AnimationReferences animationReference, EditorCurveBinding editorCurve, AnimationCurve animationCurve, char prefix = 'A')
        {
            string uniqueKey = GetUniqueKey(referenceObject, prefix);
            
            uniqueKey += $"/{animationReference.GetHashCode()}/{editorCurve.propertyName}/{animationCurve.length}";

            foreach (var key in animationCurve.keys)
            {
                uniqueKey += $"/{key.inTangent}/{key.inWeight}/{key.outTangent}/{key.outWeight}/{key.time}/{key.value}/{key.weightedMode}";
            }

            return uniqueKey;
        }

        // Convert.ToString(null) => return string.Empty
        // (null).ToString() => Occur <NullException>
        public string GetUniqueKey(T referenceObject, char prefix = 'S')
        {
            string uniqueKey = prefix.ToString();
            
            foreach (var property in properties)
            {
                uniqueKey += '/';
                
                object obj = referenceObject;
                foreach (var valueDepth in property.Split('.'))
                {
                    switch (valueDepth)
                    {
                        case "InstanceID" :
                            if(obj != null) obj = (obj as Object).GetInstanceID();
                            else obj = "null";
                            break;
                        case "activeSelf" :
                            obj = (obj as GameObject).activeSelf;
                            break;
                        
                        default :
                            obj = typeof(T).GetProperty(valueDepth).GetValue(obj);
                            break;
                    }
                }
                uniqueKey += Convert.ToString(obj);
            }
            
            return uniqueKey;
        }
    }
}