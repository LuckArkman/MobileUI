using UnityEngine;

namespace MicroScenes
{
    public class MicroScene : MonoBehaviour
    {
        public const float Size = 100f;

        public void Initialize()
        {
            transform.localScale = Vector3.one;
        }
    }

}