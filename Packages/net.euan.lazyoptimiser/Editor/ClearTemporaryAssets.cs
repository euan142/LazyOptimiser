using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class ClearTemporaryAssets : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;

        private static bool? _shouldCleanup = null;
        public static bool ShouldCleanup {
            private set { _shouldCleanup = value; PlayerPrefs.SetInt("lazyoptimiser.shouldCleanup", value ? 1 : 0); }
            get => _shouldCleanup ?? (ShouldCleanup = PlayerPrefs.GetInt("lazyoptimiser.shouldCleanup", 1) == 1);
        }

        [MenuItem("Tools/Lazy Optimiser/Toggle Asset Cleanup", false, 2)]
        public static void ToggleCleanup()
        {
            ShouldCleanup = !ShouldCleanup;
            EditorUtility.DisplayDialog("Lazy Optimiser", $"{(ShouldCleanup ? "Enabled" : "Disabled")} cleanup of auto-generated assets", "OK");
        }

        public void OnPostprocessAvatar()
        {
            if (Util.ShouldOptimise && ShouldCleanup)
            {
                Util.ClearGeneratedAssets();
            }
        }
    }
}