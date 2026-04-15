using UnityEngine;
using UnityEngine.UIElements;

namespace LuckArkman.XR.UI
{
    /// <summary>
    /// Overlay de diagnóstico para visualização de performance em tempo real (Sprint 5).
    /// </summary>
    public class DiagnosticOverlay : MonoBehaviour
    {
        private UIDocument uiDocument;
        private Label fpsLabel;
        private Label memoryLabel;
        
        private float deltaTime = 0.0f;

        private void OnEnable()
        {
            uiDocument = GetComponent<UIDocument>();
            var root = uiDocument.rootVisualElement;
            
            // Tenta encontrar campos de diagnóstico na UI
            fpsLabel = root.Q<Label>("DiagnosticFPS");
            memoryLabel = root.Q<Label>("DiagnosticMem");
        }

        private void Update()
        {
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            
            if (fpsLabel != null)
            {
                float fps = 1.0f / deltaTime;
                fpsLabel.text = $"FPS: {Mathf.Ceil(fps)}";
                fpsLabel.style.color = fps < 30 ? new StyleColor(Color.red) : new StyleColor(Color.green);
            }

            if (memoryLabel != null && Time.frameCount % 30 == 0)
            {
                long mem = System.GC.GetTotalMemory(false) / (1024 * 1024);
                memoryLabel.text = $"MEM: {mem} MB";
            }
        }
    }
}
