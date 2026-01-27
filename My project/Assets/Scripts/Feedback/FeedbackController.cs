using Spatial;
using UnityEngine;

namespace Feedback
{
    using UnityEngine;

    public class FeedbackController : MonoBehaviour
    {
        public float warning = 2f;
        public float critical = 1f;

        void Update()
        {
            float d = Vector3.Distance(
                UserSpatialTracker.Instance.Position,
                transform.position
            );

            if (d < critical)
                Handheld.Vibrate();
        }
    }

}