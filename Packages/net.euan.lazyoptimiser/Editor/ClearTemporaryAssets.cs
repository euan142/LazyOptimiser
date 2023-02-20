using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class ClearTemporaryAssets : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;

        public static bool ShouldCleanup { private set; get; } = true;

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