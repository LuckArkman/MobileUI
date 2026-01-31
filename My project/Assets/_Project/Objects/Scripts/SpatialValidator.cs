using UnityEngine;
using System.Collections.Generic;

namespace ARProject.Objects
{
    /// <summary>
    /// Validates spatial positioning to prevent overlaps and ensure safe placement.
    /// </summary>
    public class SpatialValidator : MonoBehaviour
    {
        public bool IsPositionValid(Vector3 position, Vector3 size, LayerMask collisionLayer)
        {
            // Simple box overlap check
            return !Physics.CheckBox(position, size / 2, Quaternion.identity, collisionLayer);
        }
    }
}
