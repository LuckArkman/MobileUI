using UnityEngine;

namespace ARProject.Objects
{
    /// <summary>
    /// Defines the physical boundaries of an object or microscene.
    /// Used for anti-overlap calculations.
    /// </summary>
    public class SpatialBounds : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new Vector3(1, 1, 1);
        [SerializeField] private Vector3 centerOffset = Vector3.zero;

        public Bounds GetWorldBounds()
        {
            return new Bounds(transform.position + centerOffset, size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position + centerOffset, size);
        }
    }
}
