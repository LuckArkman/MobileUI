using UnityEngine;

namespace ARProject.Objects
{
    /// <summary>
    /// Base class for any object that sits within the AR MicroScene.
    /// Handles interaction and spatial queries.
    /// </summary>
    public class ARSpatialObject : MonoBehaviour
    {
        [SerializeField] private string objectName;
        [SerializeField] private Collider objectCollider;

        public Collider Collider => objectCollider != null ? objectCollider : GetComponent<Collider>();

        public virtual void OnInteract()
        {
            Debug.Log($"Interact with {objectName}");
        }
    }
}
