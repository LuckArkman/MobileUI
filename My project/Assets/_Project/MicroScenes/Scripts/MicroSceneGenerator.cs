using UnityEngine;
using ARProject.Objects;

namespace ARProject.MicroScenes
{
    /// <summary>
    /// Generates content for a MicroScene at runtime.
    /// </summary>
    public class MicroSceneGenerator : MonoBehaviour
    {
        [SerializeField] private GameObject floorPrefab;
        [SerializeField] private GameObject[] obstaclePrefabs;

        public void GenerateContent(Transform root)
        {
            // Create simplified 100x100 (scaled down for AR usually 1m x 1m)
            // Assuming MicroScene scales internal coordinates.

            if (floorPrefab != null)
            {
                Instantiate(floorPrefab, root);
            }

            for (int i = 0; i < 5; i++)
            {
                if (obstaclePrefabs != null && obstaclePrefabs.Length > 0)
                {
                    GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
                    Vector3 randomPos = new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
                    GameObject obj = Instantiate(prefab, root);
                    obj.transform.localPosition = randomPos;

                    // Attach ARSpatialObject if missing
                    if (obj.GetComponent<ARSpatialObject>() == null)
                    {
                        obj.AddComponent<ARSpatialObject>();
                    }

                    // Attach ProximityChecker
                    if (obj.GetComponent<ProximityChecker>() == null)
                    {
                        obj.AddComponent<ProximityChecker>();
                    }
                }
            }
        }
    }
}
