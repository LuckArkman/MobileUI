using UnityEngine;
using ARProject.Spatial;

namespace ARProject.Objects
{
    /// <summary>
    /// Detects proximity to the user and triggers feedback.
    /// </summary>
    public class ProximityChecker : MonoBehaviour
    {
        [SerializeField] private float warningDistance = 2.0f;
        [SerializeField] private float criticalDistance = 0.5f;

        private UserSpatialTracker userTracker;
        private ARSpatialObject spatialObj;

        [SerializeField] private Feedback.FeedbackManager feedbackManager;

        private void Start()
        {
            userTracker = FindFirstObjectByType<UserSpatialTracker>();
            spatialObj = GetComponent<ARSpatialObject>();
            if (feedbackManager == null) feedbackManager = FindFirstObjectByType<Feedback.FeedbackManager>();
        }

        private void Update()
        {
            if (userTracker == null) return;

            float distance = Vector3.Distance(transform.position, userTracker.CurrentPosition);

            if (distance < criticalDistance)
            {
                Debug.Log($"CRITICAL PROXIMITY: {name}");
                if (feedbackManager)
                {
                    feedbackManager.ShowAlert("CRITICAL DISTANCE!", Color.red);
                    feedbackManager.TriggerHaptics();
                }
            }
            else if (distance < warningDistance)
            {
                Debug.Log($"Warning Proximity: {name}");
                if (feedbackManager)
                {
                    feedbackManager.ShowAlert("Approaching Object...", Color.yellow);
                }
            }
        }
    }
}
