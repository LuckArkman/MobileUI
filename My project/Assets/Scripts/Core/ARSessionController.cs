using UnityEngine;

namespace Core
{
    using UnityEngine.XR.ARFoundation;

    public class ARSessionController : MonoBehaviour
    {
        public ARSession session;

        void Start()
        {
            if (session != null)
                session.enabled = true;
        }
    }

}