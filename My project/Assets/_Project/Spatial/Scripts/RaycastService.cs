using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARProject.Spatial
{
    /// <summary>
    /// Centralizes Raycasting logic for interacting with Planes and Feature Points.
    /// </summary>
    public class RaycastService : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;

        private void Awake()
        {
            if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
        }

        public bool RaycastFromScreen(Vector2 screenMsg, out Pose resultPose, TrackableType trackableTypes = TrackableType.PlaneWithinPolygon)
        {
            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenMsg, hits, trackableTypes))
            {
                resultPose = hits[0].pose;
                return true;
            }

            resultPose = Pose.identity;
            return false;
        }
    }
}
