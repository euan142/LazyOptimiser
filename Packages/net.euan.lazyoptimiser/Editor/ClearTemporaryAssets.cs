using VRC.SDKBase.Editor.BuildPipeline;

namespace LazyOptimiser
{
    public class ClearTemporaryAssets : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;


        public void OnPostprocessAvatar()
        {
            Util.ClearGeneratedAssets();
        }
    }
}