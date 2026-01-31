using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARProject.Core
{
    /// <summary>
    /// Handles advanced AR light estimation to make scene lighting match real world.
    /// </summary>
    public class LightEstimationManager : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Light sceneDirectionalLight;

        private void Awake()
        {
            if (cameraManager == null) cameraManager = FindFirstObjectByType<ARCameraManager>();
        }

        private void OnEnable()
        {
            if (cameraManager != null)
                cameraManager.frameReceived += OnFrameReceived;
        }

        private void OnDisable()
        {
            if (cameraManager != null)
                cameraManager.frameReceived -= OnFrameReceived;
        }

        private void OnFrameReceived(ARCameraFrameEventArgs args)
        {
            if (sceneDirectionalLight == null) return;

            ARLightEstimationData lightData = args.lightEstimation;

            if (lightData.averageBrightness.HasValue)
            {
                sceneDirectionalLight.intensity = lightData.averageBrightness.Value;
            }

            if (lightData.averageColorTemperature.HasValue)
            {
                sceneDirectionalLight.colorTemperature = lightData.averageColorTemperature.Value;
            }

            if (lightData.mainLightColor.HasValue)
            {
                sceneDirectionalLight.color = lightData.mainLightColor.Value;
            }

            if (lightData.mainLightDirection.HasValue)
            {
                sceneDirectionalLight.transform.rotation = Quaternion.LookRotation(lightData.mainLightDirection.Value);
            }
        }
    }
}
