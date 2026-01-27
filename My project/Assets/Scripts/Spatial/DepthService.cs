using UnityEngine;

namespace Spatial
{
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    public class DepthService : MonoBehaviour
    {
        public AROcclusionManager occlusionManager;

        public Texture GetDepthTexture()
        {
            return occlusionManager.environmentDepthTexture;
        }
    }

}