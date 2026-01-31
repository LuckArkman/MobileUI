using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using ARProject.Core;
using ARProject.Spatial;
using ARProject.MicroScenes;
using ARProject.Heatmap;
using ARProject.Feedback;
using System.IO;
using ARProject.Objects;

namespace ARProject.Editor
{
    public class ProjectSetupTools
    {
        [MenuItem("AR Project/1. Create Main AR Scene")]
        public static void CreateMainScene()
        {
            // 1. Create new Scene
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = "Assets/_Project/Scenes/MainARScanner.unity";

            Directory.CreateDirectory("Assets/_Project/Scenes");

            // 2. Setup AR Session
            GameObject arSessionGO = new GameObject("AR Session");
            arSessionGO.AddComponent<ARSession>();
            arSessionGO.AddComponent<ARInputManager>();

            // 3. Setup XR Origin (AR Rig)
            GameObject xrOriginGO = new GameObject("XR Origin (AR Rig)");
            XROrigin xrOrigin = xrOriginGO.AddComponent<XROrigin>();

            // Camera Offset & Main Camera
            GameObject camOffsetGO = new GameObject("Camera Offset");
            camOffsetGO.transform.SetParent(xrOriginGO.transform, false);
            xrOrigin.CameraFloorOffsetObject = camOffsetGO;

            GameObject mainCamGO = new GameObject("Main Camera");
            mainCamGO.transform.SetParent(camOffsetGO.transform, false);
            mainCamGO.tag = "MainCamera";
            Camera cam = mainCamGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            xrOrigin.Camera = cam;

            // AR Components on Camera
            mainCamGO.AddComponent<ARCameraManager>();
            mainCamGO.AddComponent<ARCameraBackground>();
            mainCamGO.AddComponent<AROcclusionManager>(); // For Depth/Occlusion

            // AR Components on XR Origin
            xrOriginGO.AddComponent<ARPlaneManager>();
            xrOriginGO.AddComponent<ARRaycastManager>();
            xrOriginGO.AddComponent<ARAnchorManager>();

            // Custom Spatial Components
            mainCamGO.AddComponent<UserSpatialTracker>(); // Tracks user head
            xrOriginGO.AddComponent<DepthService>();
            xrOriginGO.AddComponent<RaycastService>();
            xrOriginGO.AddComponent<LightEstimationManager>();

            // 4. App Systems (Managers)
            GameObject systemsGO = new GameObject("App Systems");

            // Core
            systemsGO.AddComponent<ARBootstrap>();
            systemsGO.AddComponent<ARSessionController>();
            systemsGO.AddComponent<PerformanceManager>();

            // Gameplay Managers
            MicroSceneManager microManager = systemsGO.AddComponent<MicroSceneManager>();
            HeatmapController heatManager = systemsGO.AddComponent<HeatmapController>();
            systemsGO.AddComponent<FeedbackManager>();

            // 5. Connect References (if possible via code)
            // Example: Assign RaycastService to MicroSceneManager if fields were public/serialized
            // Note: Since we use FindFirstObjectByType in Awake, strict inspector wiring isn't required for keeping it simple.

            EditorSceneManager.SaveScene(newScene, scenePath);
            Debug.Log($"[AR Project] Scene created at {scenePath}");
        }

        [MenuItem("AR Project/2. Create MicroScene Prefab")]
        public static void CreateMicroScenePrefab()
        {
            string prefabPath = "Assets/_Project/MicroScenes/Prefabs";
            Directory.CreateDirectory(prefabPath);

            GameObject root = new GameObject("MicroScene_Default");

            // Components
            root.AddComponent<ARAnchor>(); // Required for MicroSceneAnchor
            root.AddComponent<MicroSceneAnchor>();
            root.AddComponent<MicroScene>();
            root.AddComponent<SpatialBounds>();
            root.AddComponent<MicroSceneGenerator>();

            // Visual Placeholder (Ground)
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "GroundVisual";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(0.1f, 1f, 0.1f); // 1m x 1m
            // Create a transparent material or wireframe if desired, using default for now.

            // Warning: Do not use AssetDatabase in runtime builds, fine for Editor scripts.
            string fillPath = Path.Combine(prefabPath, "MicroScene_Default.prefab");
            PrefabUtility.SaveAsPrefabAsset(root, fillPath);

            Object.DestroyImmediate(root);
            Debug.Log($"[AR Project] Prefab created at {fillPath}");
        }

        [MenuItem("AR Project/3. Create Heatmap Material")]
        public static void CreateHeatmapMaterial()
        {
            string matPath = "Assets/_Project/Heatmap/Materials";
            Directory.CreateDirectory(matPath);

            Shader shader = Shader.Find("ARProject/Heatmap");
            if (shader == null)
            {
                Debug.LogError("Shader 'ARProject/Heatmap' not found. Ensure Heatmap.shader is created.");
                return;
            }

            Material mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, Path.Combine(matPath, "Heatmap_Mat.mat"));
            AssetDatabase.SaveAssets();
            Debug.Log("[AR Project] Heatmap Material created.");
        }
    }
}
