using System;
using System.Collections.Generic;
using UnityEngine;
using LuckArkman.XR.Navigation;

// AR Foundation é opcional. O código compila sem o pacote.
// Quando AR Foundation estiver instalado, o Unity define automaticamente
// UNITY_AR_FOUNDATION_PRESENT via seu package.json.
// Adicione manualmente em Player Settings → Scripting Define Symbols se necessário.
#if UNITY_AR_FOUNDATION_PRESENT
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace LuckArkman.XR.AR
{
    /// <summary>
    /// Sistema de Marcação de Checkpoints em Realidade Aumentada.
    ///
    /// Implementa o fluxo dos botões da especificação App La Rosa:
    ///   [Estabelecer Checkpoints] → ativa o modo de marcação
    ///   [Marcar Checkpoint]       → ancora um waypoint no chão real (ARPlane)
    ///   [Iniciar]                 → congela a rota e entrega ao RouteProgressTracker
    ///
    /// COMPILAÇÃO:
    ///   • Sem AR Foundation → compila normalmente, funciona em modo Editor/Fallback.
    ///   • Com AR Foundation → ativo via #if UNITY_AR_FOUNDATION_PRESENT.
    ///     Instalação: Package Manager → AR Foundation (com.unity.xr.arfoundation).
    /// </summary>
    public class ARCheckpointPlacer : MonoBehaviour
    {
        // =====================================================================
        // SEÇÃO 1 — REFERÊNCIAS AR (condicionais)
        // =====================================================================

#if UNITY_AR_FOUNDATION_PRESENT
        [Header("AR Foundation — Componentes (requer pacote instalado)")]
        [Tooltip("ARRaycastManager da cena AR. Hit test contra planos detecados.")]
        public ARRaycastManager arRaycastManager;

        [Tooltip("ARPlaneManager da cena AR. Detecta superfícies (chão, mesas, etc).")]
        public ARPlaneManager arPlaneManager;

        [Tooltip("ARAnchorManager da cena AR. Ancora os checkpoints no mundo físico.")]
        public ARAnchorManager arAnchorManager;
#endif

        // =====================================================================
        // SEÇÃO 2 — CONFIGURAÇÃO DE ROTA
        // =====================================================================

        [Header("Sistema de Navegação")]
        [Tooltip("RouteProgressTracker que receberá os waypoints marcados.")]
        public RouteProgressTracker routeTracker;

        [Header("Marcadores Visuais")]
        [Tooltip("Prefab do preview flutuante. Se nulo, cria esfera primitiva.")]
        public GameObject checkpointMarkerPrefab;

        [Tooltip("Prefab do checkpoint confirmado. Se nulo, cria esfera colorida.")]
        public GameObject checkpointConfirmedPrefab;

        [Tooltip("Raio de tolerância de chegada ao checkpoint (metros).")]
        [Range(0.3f, 3.0f)]
        public float checkpointRadius = 1.2f;

        // =====================================================================
        // SEÇÃO 3 — CORES DE FEEDBACK
        // =====================================================================

        [Header("Feedback Visual")]
        public Color corSurfaceDetected = new Color(0.2f, 1f, 0.4f, 0.85f);
        public Color corSearching       = new Color(1f, 0.6f, 0.1f, 0.6f);

        // =====================================================================
        // SEÇÃO 4 — ESTADO INTERNO
        // =====================================================================

        private bool _modoMarcacaoAtivo = false;
        private bool _rotaConcluida     = false;
        private bool _temHitValido      = false;

        // Âncoras e marcadores (ARAnchor quando AR Foundation ativo, GameObject simples caso contrário)
        private readonly List<GameObject> _anchorObjects    = new List<GameObject>();
        private readonly List<GameObject> _confirmedMarkers = new List<GameObject>();

        private GameObject _previewObject;

#if UNITY_AR_FOUNDATION_PRESENT
        private Pose _hitPoseAtual;
        private readonly List<ARRaycastHit> _arHits = new List<ARRaycastHit>();
#endif

        // =====================================================================
        // SEÇÃO 5 — PROPRIEDADES PÚBLICAS
        // =====================================================================

        public int  TotalCheckpoints     => _anchorObjects.Count;
        public bool ModoMarcacaoAtivo    => _modoMarcacaoAtivo;
        public bool TemSuperficieDetectada => _temHitValido;

        // =====================================================================
        // SEÇÃO 6 — EVENTOS
        // =====================================================================

        public event Action<int>            OnCheckpointMarcado;
        public event Action<List<Transform>> OnRotaEstabelecida;

        // =====================================================================
        // SEÇÃO 7 — CICLO DE VIDA
        // =====================================================================

        private void Awake()
        {
            _previewObject = checkpointMarkerPrefab != null
                ? Instantiate(checkpointMarkerPrefab)
                : CriarMarcadorPrimitivoPreview();

            _previewObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_previewObject != null)
                Destroy(_previewObject);
        }

        private void Update()
        {
            if (!_modoMarcacaoAtivo) return;
            AtualizarPreview();
        }

        // =====================================================================
        // SEÇÃO 8 — API PÚBLICA
        // =====================================================================

        /// <summary>[Botão: Estabelecer Checkpoints] Ativa o modo de marcação AR.</summary>
        public void AtivarModoMarcacao()
        {
            if (_rotaConcluida)
            {
                Debug.LogWarning("[ARCheckpointPlacer] Rota concluída. Reinicie para remarcar.");
                return;
            }

            _modoMarcacaoAtivo = true;

#if UNITY_AR_FOUNDATION_PRESENT
            if (arPlaneManager != null) arPlaneManager.enabled = true;
#endif
            _previewObject?.SetActive(true);
            Debug.Log("[ARCheckpointPlacer] 📍 Modo de marcação ativado. Aponte para o chão e toque [Marcar Checkpoint].");
        }

        /// <summary>[Botão: Marcar Checkpoint] Confirma o checkpoint na posição atual.</summary>
        public void MarcarCheckpointAtual()
        {
            if (!_modoMarcacaoAtivo)
            {
                Debug.LogWarning("[ARCheckpointPlacer] Modo de marcação não ativo.");
                return;
            }

#if UNITY_AR_FOUNDATION_PRESENT
            if (!_temHitValido)
            {
                Debug.LogWarning("[ARCheckpointPlacer] ⚠️ Nenhuma superfície AR detectada. Aponte para o chão.");
                return;
            }

            Vector3 posicao = _hitPoseAtual.position;
            Quaternion rotacao = _hitPoseAtual.rotation;
#else
            // Fallback: usa a posição à frente do usuário (Editor/teste sem AR)
            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector3 posicao = camPos + (Camera.main != null ? Camera.main.transform.forward : Vector3.forward) * 1.5f;
            posicao.y = 0f;
            Quaternion rotacao = Quaternion.identity;
#endif
            var anchorGO = new GameObject($"Checkpoint_{_anchorObjects.Count + 1}");
            anchorGO.transform.SetPositionAndRotation(posicao, rotacao);
            _anchorObjects.Add(anchorGO);

            var marcador = CriarMarcadorConfirmado(posicao, _anchorObjects.Count);
            _confirmedMarkers.Add(marcador);

            int indice = _anchorObjects.Count;
            Debug.Log($"[ARCheckpointPlacer] ✅ Checkpoint {indice} marcado em {posicao}.");
            OnCheckpointMarcado?.Invoke(indice);
        }

        /// <summary>[Botão: Iniciar] Finaliza a marcação e injeta waypoints no RouteProgressTracker.</summary>
        public void FinalizarEIniciarNavegacao()
        {
            if (_anchorObjects.Count < 2)
            {
                Debug.LogWarning($"[ARCheckpointPlacer] ⚠️ Mínimo de 2 checkpoints. Atual: {_anchorObjects.Count}.");
                return;
            }

            _modoMarcacaoAtivo = false;
            _rotaConcluida     = true;
            _previewObject?.SetActive(false);

#if UNITY_AR_FOUNDATION_PRESENT
            if (arPlaneManager != null)
            {
                foreach (var plane in arPlaneManager.trackables)
                    plane.gameObject.SetActive(false);
                arPlaneManager.enabled = false;
            }
#endif
            var waypointTransforms = new List<Transform>();
            foreach (var go in _anchorObjects)
                if (go != null) waypointTransforms.Add(go.transform);

            if (routeTracker != null)
            {
                routeTracker.routeWaypoints.Clear();
                routeTracker.routeWaypoints.AddRange(waypointTransforms);
                routeTracker.radiusTolerance = checkpointRadius;
                Debug.Log($"[ARCheckpointPlacer] 🏁 Rota estabelecida: {waypointTransforms.Count} checkpoints.");
            }

            OnRotaEstabelecida?.Invoke(waypointTransforms);
        }

        /// <summary>Remove o último checkpoint marcado (desfazer).</summary>
        public void DesfazerUltimoCheckpoint()
        {
            if (_anchorObjects.Count == 0) return;

            var ultimo = _anchorObjects[_anchorObjects.Count - 1];
            _anchorObjects.RemoveAt(_anchorObjects.Count - 1);
            if (ultimo != null) Destroy(ultimo);

            if (_confirmedMarkers.Count > 0)
            {
                var m = _confirmedMarkers[_confirmedMarkers.Count - 1];
                _confirmedMarkers.RemoveAt(_confirmedMarkers.Count - 1);
                if (m != null) Destroy(m);
            }

            Debug.Log($"[ARCheckpointPlacer] ↩️ Checkpoint removido. Restam: {_anchorObjects.Count}.");
        }

        /// <summary>Reseta toda a rota.</summary>
        public void ResetarRota()
        {
            foreach (var a in _anchorObjects) if (a != null) Destroy(a);
            _anchorObjects.Clear();

            foreach (var m in _confirmedMarkers) if (m != null) Destroy(m);
            _confirmedMarkers.Clear();

            _rotaConcluida = _modoMarcacaoAtivo = false;
            _previewObject?.SetActive(false);
            if (routeTracker != null) routeTracker.routeWaypoints.Clear();
            Debug.Log("[ARCheckpointPlacer] 🔄 Rota resetada.");
        }

        // =====================================================================
        // SEÇÃO 9 — LÓGICA INTERNA
        // =====================================================================

        private void AtualizarPreview()
        {
#if UNITY_AR_FOUNDATION_PRESENT
            if (arRaycastManager == null || Camera.main == null) return;

            Vector2 centroTela = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _arHits.Clear();
            bool acertou = arRaycastManager.Raycast(centroTela, _arHits, TrackableType.PlaneWithinPolygon);

            if (acertou && _arHits.Count > 0)
            {
                _hitPoseAtual = _arHits[0].pose;
                _temHitValido = true;
                _previewObject.transform.SetPositionAndRotation(
                    _hitPoseAtual.position + Vector3.up * 0.05f,
                    _hitPoseAtual.rotation);
                _previewObject.SetActive(true);
                AtualizarCorPreview(corSurfaceDetected);
            }
            else
            {
                _temHitValido = false;
                AtualizarCorPreview(corSearching);
            }
#else
            // Sem AR Foundation: preview sempre na frente do usuário
            if (Camera.main != null)
            {
                Vector3 pos = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
                pos.y = 0f;
                _previewObject.transform.position = pos;
                _temHitValido = true;
                AtualizarCorPreview(corSurfaceDetected);
            }
#endif
        }

        private GameObject CriarMarcadorPrimitivoPreview()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AR_CheckpointPreview";
            go.transform.localScale = Vector3.one * 0.15f;
            if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = corSurfaceDetected;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        private GameObject CriarMarcadorConfirmado(Vector3 posicao, int indice)
        {
            if (checkpointConfirmedPrefab != null)
                return Instantiate(checkpointConfirmedPrefab, posicao + Vector3.up * 0.15f, Quaternion.identity);
            return CriarMarcadorPrimitivoConfirmado(posicao, indice);
        }

        private GameObject CriarMarcadorPrimitivoConfirmado(Vector3 posicao, int indice)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"AR_Checkpoint_{indice}";
            go.transform.position = posicao + Vector3.up * 0.15f;
            go.transform.localScale = Vector3.one * 0.20f;
            if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            Color[] cores = { Color.green, Color.cyan, Color.yellow, Color.magenta, new Color(1f, 0.5f, 0f) };
            mat.color = cores[(indice - 1) % cores.Length];
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        private void AtualizarCorPreview(Color cor)
        {
            if (_previewObject == null) return;
            var rend = _previewObject.GetComponent<Renderer>();
            if (rend != null) rend.material.color = cor;
        }

        // =====================================================================
        // SEÇÃO 10 — DIAGNÓSTICO EDITOR
        // =====================================================================

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
                richText  = true
            };

            bool arAtivo = false;
#if UNITY_AR_FOUNDATION_PRESENT
            arAtivo = true;
#endif
            string corModo   = _modoMarcacaoAtivo ? "lime" : "gray";
            string corHit    = _temHitValido ? "lime" : "orange";
            string modoLabel = _modoMarcacaoAtivo ? "✏️ MARCANDO" : (_rotaConcluida ? "✅ ROTA PRONTA" : "⏸ AGUARDANDO");
            string arLabel   = arAtivo ? "AR Foundation ✅" : "Modo Fallback (sem AR Foundation)";

            string texto =
                $"<color={corModo}>📍 [ARCheckpointPlacer] {modoLabel} [{arLabel}]</color>\n" +
                $"Checkpoints: {_anchorObjects.Count} | " +
                $"<color={corHit}>Superfície: {(_temHitValido ? "✅ detectada" : "⏳ aguardando")}</color>\n" +
                $"[Marcar Checkpoint] para adicionar. [Iniciar] para navegar.";

            GUI.Box(new Rect(10, 355, 480, 70), texto, style);
        }
#endif
    }
}
