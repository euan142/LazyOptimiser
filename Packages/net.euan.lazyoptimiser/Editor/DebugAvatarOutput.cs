using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class DebugAvatarOutput : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameobject)
        {
            if (Util.ShouldOptimise && ClearTemporaryAssets.ShouldCleanup == false)
            {
                Util.CloneAsset(avatarGameobject);
            }
            return true;
        }
    }
}