using UnityEditor;
using UnityEngine;

namespace LazyOptimiser
{
    public class LazyOptimiserSettings : EditorWindow
    {
        [MenuItem("Tools/Lazy Optimiser/Settings")]
        public static void ShowWindow()
        {
            var window = GetWindow<LazyOptimiserSettings>("Lazy Optimiser Settings");
            window.minSize = new Vector2(300, 100);
            window.maxSize = new Vector2(300, 100);
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Settings", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 10, 10)});
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // a checkbox for enabling and disabling optimising
            RenderToggle("Optimise", Util.ShouldOptimise, value => Util.ShouldOptimise = value);

            // a checkbox for enabling and disabling asset cleanup
            RenderToggle("Cleanup", ClearTemporaryAssets.ShouldCleanup, value => ClearTemporaryAssets.ShouldCleanup = value);
        }

        private void RenderToggle(string label, bool value, System.Action<bool> setter)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            setter(EditorGUILayout.Toggle(label, value));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    
    }
}