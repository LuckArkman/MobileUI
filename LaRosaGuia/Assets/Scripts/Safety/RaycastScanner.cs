using UnityEngine;
using LuckArkman.XR.AI;
using LuckArkman.XR.Main;


namespace LuckArkman.XR.Safety
{
    /// <summary>
    /// "Bengala Virtual" do App La Rosa.
    ///
    /// Converte o Depth Map gerado pelo MiDaS / Depth Anything V2 em distâncias
    /// métricas reais, disparando raios matemáticos virtuais a partir do centro da
    /// imagem para medir a distância de colisão nas zonas Esquerda, Centro e Direita.
    ///
    /// Alimenta o DecisionEngine com distâncias em metros e passos, substituindo os
    /// dangerScores abstratos (0–10) por valores físicos calibráveis.
    ///
    /// Wiring no Orchestrator:
    ///   1. Chamar Scan(ultimoMidasData) a cada turno MiDaS.
    ///   2. Passar ObterMidasCalibrado() para decisionMatrix.AvaliarCenario().
    ///   3. Usar FrontDistanceSteps como passosCalculados.
    /// </summary>
    public class RaycastScanner : MonoBehaviour
    {
        // =====================================================================
        // CONSTANTES
        // =====================================================================

        /// <summary>Distância média de um passo humano em metros.</summary>
        private const float METROS_POR_PASSO = 0.75f;

        // =====================================================================
        // CALIBRAÇÃO DE PROFUNDIDADE
        // =====================================================================

        [Header("Calibração de Profundidade")]
        [Tooltip(
            "Distância estimada (metros) quando dangerScore = 0.\n" +
            "Representa o caminho completamente livre."
        )]
        public float distanciaMaxima = 7.0f;

        [Tooltip(
            "Distância estimada (metros) quando dangerScore = 10.\n" +
            "Representa um obstáculo imediato / iminente colisão."
        )]
        public float distanciaMinima = 0.3f;

        [Tooltip(
            "Constante de curvatura da função exponencial score → metros.\n" +
            "Valores menores tornam a curva mais agressiva (distância cai rápido).\n" +
            "Valores maiores tornam a curva mais suave (distância cai devagar).\n" +
            "Recomendado: 0.35 para espaços internos, 0.55 para espaços externos."
        )]
        [Range(0.1f, 1.0f)]
        public float curvaturaConversao = 0.35f;

        // =====================================================================
        // LIMIARES DE ALERTA
        // =====================================================================

        [Header("Limiares de Alerta")]
        [Tooltip("Distância (m) abaixo da qual o scanner emite PERIGO IMEDIATO → força Parar.")]
        public float limiarPerigoImediato = 0.8f;

        [Tooltip("Distância (m) abaixo da qual o scanner emite ZONA DE ATENÇÃO → ativa Frente1/2.")]
        public float limiarAtencao = 2.5f;

        // =====================================================================
        // SAÍDAS EM TEMPO REAL (visíveis no Inspector para calibração rápida)
        // =====================================================================

        [Header("Leitura em Tempo Real (somente leitura)")]
        [SerializeField] private float _frontDistanceMeters;
        [SerializeField] private float _leftDistanceMeters;
        [SerializeField] private float _rightDistanceMeters;
        [SerializeField] private int   _frontDistanceSteps;
        [SerializeField] private bool  _velocidadeAlerta;

        // =====================================================================
        // PROPRIEDADES PÚBLICAS
        // =====================================================================

        /// <summary>Distância estimada do obstáculo frontal em metros.</summary>
        public float FrontDistanceMeters => _frontDistanceMeters;

        /// <summary>Distância estimada do obstáculo à esquerda em metros.</summary>
        public float LeftDistanceMeters  => _leftDistanceMeters;

        /// <summary>Distância estimada do obstáculo à direita em metros.</summary>
        public float RightDistanceMeters => _rightDistanceMeters;

        /// <summary>
        /// Obstáculo frontal convertido em passos (1 passo ≈ 0.75 m).
        /// Faixa: 1 (muito próximo) a 4 (caminho livre).
        /// Mapeia diretamente para Frente1 / Frente2 / Frente3 / Frente4.
        /// </summary>
        public int FrontDistanceSteps => _frontDistanceSteps;

        /// <summary>
        /// True quando há um objeto se aproximando rapidamente (absoluteVelocityAlert = true).
        /// Nesse caso, o sistema ignora a distância calculada e força PARAR imediato.
        /// </summary>
        public bool VelocidadeAlerta => _velocidadeAlerta;

        /// <summary>
        /// True se o obstáculo frontal estiver dentro do limiar de perigo imediato.
        /// O Orchestrator deve forçar Parar quando este flag estiver ativo.
        /// </summary>
        public bool IsImmediateDanger => _frontDistanceMeters <= limiarPerigoImediato || _velocidadeAlerta;

        /// <summary>
        /// True se qualquer zona (frente, esq ou dir) estiver dentro do limiar de atenção.
        /// </summary>
        public bool IsAttentionZone =>
            _frontDistanceMeters <= limiarAtencao ||
            _leftDistanceMeters  <= limiarAtencao ||
            _rightDistanceMeters <= limiarAtencao;

        // =====================================================================
        // API PÚBLICA
        // =====================================================================

        /// <summary>
        /// Processa os dados do MiDaS / Depth Anything e atualiza as distâncias em metros.
        /// Deve ser chamado pelo MainSystemOrchestrator a cada turno MiDaS (dentro do escalonador de 14 frames).
        /// </summary>
        public void Scan(MidasResult midas)
        {
            _velocidadeAlerta    = midas.absoluteVelocityAlert;
            _frontDistanceMeters = ScoreParaMetros(midas.dangerScore);
            _leftDistanceMeters  = ScoreParaMetros(midas.leftZoneDanger);
            _rightDistanceMeters = ScoreParaMetros(midas.rightZoneDanger);

            // Objeto em movimento rápido → reduz distância percebida para forçar Parar imediato
            if (_velocidadeAlerta)
            {
                _frontDistanceMeters = Mathf.Min(_frontDistanceMeters, limiarPerigoImediato * 0.7f);
                Debug.LogWarning("[RaycastScanner] ⚡ Alerta de velocidade! Objeto em aproximação rápida — distância forçada para " +
                                 $"{_frontDistanceMeters:F2} m.");
            }

            _frontDistanceSteps = MetrosParaPassos(_frontDistanceMeters);

            Debug.Log($"[RaycastScanner] Frente={_frontDistanceMeters:F2}m ({_frontDistanceSteps}p) | " +
                      $"Esq={_leftDistanceMeters:F2}m | Dir={_rightDistanceMeters:F2}m | " +
                      $"Velocidade⚡={_velocidadeAlerta}");
        }

        /// <summary>
        /// Converte o resultado do scanner para a instrução de Frente equivalente.
        /// Retorna Frente1 (muito próximo) a Frente4 (caminho livre).
        /// Chamado pelo Orchestrator para popular o comando principal quando sem obstáculo evasivo.
        /// </summary>
        public Guia.EstadoInstrucao ObterNivelFrente()
        {
            if (IsImmediateDanger)            return Guia.EstadoInstrucao.Parar;
            if (_frontDistanceSteps <= 1)     return Guia.EstadoInstrucao.Frente1;
            if (_frontDistanceSteps == 2)     return Guia.EstadoInstrucao.Frente2;
            if (_frontDistanceSteps == 3)     return Guia.EstadoInstrucao.Frente3;
            return Guia.EstadoInstrucao.Frente4;
        }

        /// <summary>
        /// Retorna um MidasResult com os scores recalibrados a partir das distâncias métricas.
        /// Permite injeção transparente no DecisionEngine sem alterar a assinatura de AvaliarCenario().
        ///
        /// Fluxo: MiDaS (raw) → Scan() → ObterMidasCalibrado() → Decision.AvaliarCenario()
        /// </summary>
        public MidasResult ObterMidasCalibrado(MidasResult original)
        {
            return new MidasResult
            {
                dangerScore           = MetrosParaScore(_frontDistanceMeters),
                leftZoneDanger        = MetrosParaScore(_leftDistanceMeters),
                rightZoneDanger       = MetrosParaScore(_rightDistanceMeters),
                absoluteVelocityAlert = original.absoluteVelocityAlert,
            };
        }

        // =====================================================================
        // MATEMÁTICA DE CONVERSÃO (PRIVADA)
        // =====================================================================

        /// <summary>
        /// Converte dangerScore (0–10) para metros via função exponencial inversa.
        ///
        /// Curva:  f(score) = distanMin + (distanMax - distanMin) × exp(−score/10 / curvatura)
        ///   score = 0  → distanciaMaxima  (caminho livre)
        ///   score = 10 → distanciaMinima  (obstáculo imediato)
        ///
        /// A função é contínua, monótona e calibrável via curvaturaConversao.
        /// </summary>
        private float ScoreParaMetros(float score)
        {
            float t     = Mathf.Clamp01(score / 10f);
            float fator = Mathf.Exp(-t / curvaturaConversao);
            return Mathf.Lerp(distanciaMinima, distanciaMaxima, fator);
        }

        /// <summary>
        /// Inverso de ScoreParaMetros: metros → dangerScore (para compatibilidade com Decision.cs).
        ///
        /// Derivação da equação inversa:
        ///   metros = min + (max - min) × exp(-t/c)
        ///   fator  = (metros - min) / (max - min)
        ///   t      = -c × ln(fator)
        ///   score  = t × 10
        /// </summary>
        private float MetrosParaScore(float metros)
        {
            float range = distanciaMaxima - distanciaMinima;
            if (range <= 0f) return 0f;

            float fator = Mathf.Clamp01((metros - distanciaMinima) / range);
            if (fator <= 0.001f) return 10f;  // evita ln(0)
            if (fator >= 0.999f) return 0f;   // evita ln negativo

            float t = -curvaturaConversao * Mathf.Log(fator);
            return Mathf.Clamp(t * 10f, 0f, 10f);
        }

        /// <summary>
        /// Converte metros em número de passos (1 passo ≈ 0.75 m).
        /// Retorna no mínimo 1, no máximo 4 para mapear em Frente1–4.
        /// </summary>
        private int MetrosParaPassos(float metros)
        {
            int passos = Mathf.FloorToInt(metros / METROS_POR_PASSO);
            return Mathf.Clamp(passos, 1, 4);
        }

        // =====================================================================
        // DIAGNÓSTICO NO EDITOR (SOMENTE EM PLAY MODE)
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

            string corFrente =
                IsImmediateDanger                              ? "red"    :
                _frontDistanceMeters <= limiarAtencao          ? "orange" : "lime";

            string corEsq =
                _leftDistanceMeters  <= limiarPerigoImediato   ? "red"    :
                _leftDistanceMeters  <= limiarAtencao          ? "orange" : "lime";

            string corDir =
                _rightDistanceMeters <= limiarPerigoImediato   ? "red"    :
                _rightDistanceMeters <= limiarAtencao          ? "orange" : "lime";

            string alertaVel = _velocidadeAlerta ? " <color=red>⚡ VELOCIDADE</color>" : "";

            string texto =
                $"<color=white>🔭 [RaycastScanner]{alertaVel}</color>\n" +
                $"<color={corFrente}>Frente : {_frontDistanceMeters:F2} m  →  {_frontDistanceSteps} passo(s)  →  {ObterNivelFrente()}</color>\n" +
                $"<color={corEsq}>Esq    : {_leftDistanceMeters:F2} m</color>   " +
                $"<color={corDir}>Dir    : {_rightDistanceMeters:F2} m</color>";

            GUI.Box(new Rect(10, 100, 430, 85), texto, style);
        }
#endif
    }
}
