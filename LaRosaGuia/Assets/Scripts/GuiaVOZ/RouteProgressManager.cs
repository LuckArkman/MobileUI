using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LuckArkman.XR.AI;

namespace LuckArkman.XR.Main
{
    /// <summary>
    /// Gerencia duas responsabilidades complementares:
    ///
    ///  1) PROGRESSO DO ROTEIRO — Rastreia em qual CheckPoint (0–5) o utilizador
    ///     se encontra, persiste o progresso com PlayerPrefs, e avança
    ///     automaticamente quando chamado pelo script de apresentação.
    ///
    ///  2) EVASÃO DE OBSTÁCULOS — Monitora os dados MiDaS em tempo real e,
    ///     quando detecta um obstáculo no caminho, emite uma sequência de
    ///     comandos de voz para guiar o utilizador em volta do obstáculo,
    ///     aguarda a limpeza do caminho e retoma o roteiro.
    /// </summary>
    public class RouteProgressManager : MonoBehaviour
    {
        // ====================================================================
        // SECÇÃO 1 — REFERÊNCIAS
        // ====================================================================

        [Header("Módulos")]
        [Tooltip("Referência ao script Guia para emitir comandos de voz.")]
        public Guia guia;

        // ====================================================================
        // SECÇÃO 2 — CONFIGURAÇÃO DO ROTEIRO
        // ====================================================================

        [Header("Roteiro — CheckPoints")]
        [Tooltip("Total de checkpoints do roteiro (normalmente 6: CP0 a CP5).")]
        public int totalCheckPoints = 6;

        [Tooltip("Chave usada pelo PlayerPrefs para persistir o progresso.")]
        public string saveKey = "GuiaRouteProgress";

        [Tooltip("Se verdadeiro, apaga o progresso guardado ao iniciar (útil em testes).")]
        public bool resetarProgressoAoIniciar = false;

        [Tooltip("Avançar automaticamente para o próximo CP após reproduzir o audio do CP actual.")]
        public bool autoAvancarCheckPoint = false;

        [Tooltip("Segundos de espera após o áudio do CP antes de avançar automaticamente.")]
        public float tempoEntreCheckPoints = 3.0f;

        // ====================================================================
        // SECÇÃO 3 — CONFIGURAÇÃO DE EVASÃO DE OBSTÁCULOS
        // ====================================================================

        [Header("Evasão de Obstáculos")]
        [Tooltip(
            "Score MiDaS mínimo (0–10) para o sistema considerar que há um obstáculo\n" +
            "bloqueando a rota. 6.0 = obstáculo próximo e perigoso."
        )]
        [Range(3f, 9f)] public float limiarObstaculo = 6.0f;

        [Tooltip(
            "Número de frames consecutivos com dangerScore > limiarObstaculo\n" +
            "antes de acionar a sequência de evasão. Evita falsos positivos."
        )]
        [Range(1, 20)] public int framesConsecutivosParaEvasao = 5;

        [Tooltip("Segundos máximos aguardando o caminho libertar antes de re-tentar evasão.")]
        public float timeoutEvasao = 8.0f;

        [Tooltip(
            "Quando verdadeiro, o sistema tenta a evasão mesmo se o Guia estiver\n" +
            "no modo de apresentação (modoApresentacao)."
        )]
        public bool evasaoAtivaNaModoApresentacao = true;

        [Tooltip("Segundos antes de repetir 'Siga em frente' quando o caminho está livre.")]
        public float intervaloFeedbackProativo = 15.0f;

        // ====================================================================
        // SECÇÃO 4 — ESTADO INTERNO
        // ====================================================================

        // ── Progresso ────────────────────────────────────────────────────────
        private int _checkPointAtual = 0;
        private bool _roteiroConcluido = false;

        // ── Evasão ───────────────────────────────────────────────────────────
        private int  _framesComObstaculo   = 0;
        private bool _evasaoEmCurso        = false;
        private Coroutine _corotinaEvasao  = null;
        private float _tempoUltimaInstrucaoProativa = 0f;

        // ── Dados MiDaS mais recentes (atualizados a cada frame pelo Orchestrator) ──
        private MidasResult _ultimoMidas;

        // ====================================================================
        // SECÇÃO 5 — PROPRIEDADES PÚBLICAS (leitura)
        // ====================================================================

        /// <summary>Índice do CheckPoint actual (0 = início, 5 = destino).</summary>
        public int CheckPointAtual    => _checkPointAtual;

        /// <summary>True se o utilizador chegou ao CheckPoint final (CP5).</summary>
        public bool RoteiroConcluido  => _roteiroConcluido;

        /// <summary>True enquanto uma manobra de evasão de obstáculo está a decorrer.</summary>
        public bool EvasaoEmCurso     => _evasaoEmCurso;

        // ====================================================================
        // SECÇÃO 6 — CICLO DE VIDA UNITY
        // ====================================================================

        private void Awake()
        {
            if (guia == null)
                guia = FindFirstObjectByType<Guia>();

            CarregarProgresso();
        }

        private void Start()
        {
            Debug.Log($"[RouteProgress] CheckPoint actual carregado: CP{_checkPointAtual} / {totalCheckPoints - 1}");
        }

        // ====================================================================
        // SECÇÃO 7 — API PÚBLICA: PROGRESSO DO ROTEIRO
        // ====================================================================

        /// <summary>
        /// Toca o áudio do CheckPoint actual e opcionalmente avança para o próximo.
        /// Deve ser chamado externamente (ex: botão, trigger de proximidade, ou
        /// MainSystemOrchestrator após condição cumprida).
        /// </summary>
        public void TocarCheckPointAtual()
        {
            if (_roteiroConcluido)
            {
                Debug.Log("[RouteProgress] Roteiro já concluído — CP5 já foi reproduzido.");
                return;
            }

            Debug.Log($"[RouteProgress] ▶ Reproduzindo CP{_checkPointAtual}");
            guia.TocarCheckPoint(_checkPointAtual);

            if (autoAvancarCheckPoint)
                StartCoroutine(AguardarEAvancar());
        }

        /// <summary>
        /// Avança manualmente para o próximo CheckPoint e reproduz o seu áudio.
        /// Persiste o novo índice em PlayerPrefs.
        /// </summary>
        public void AvancarParaProximoCheckPoint()
        {
            if (_roteiroConcluido)
            {
                Debug.LogWarning("[RouteProgress] Roteiro já concluído — não há próximo CP.");
                return;
            }

            _checkPointAtual++;
            SalvarProgresso();

            if (_checkPointAtual >= totalCheckPoints)
            {
                _checkPointAtual = totalCheckPoints - 1; // garante que não ultrapassa CP5
                _roteiroConcluido = true;
                Debug.Log("[RouteProgress] ✅ Roteiro concluído — CP5 atingido!");
            }

            Debug.Log($"[RouteProgress] ▶ Avançou para CP{_checkPointAtual}");
            guia.TocarCheckPoint(_checkPointAtual);
        }

        /// <summary>
        /// Volta ao CheckPoint anterior (ex: utilizador perdeu-se).
        /// </summary>
        public void VoltarCheckPoint()
        {
            if (_checkPointAtual <= 0)
            {
                Debug.Log("[RouteProgress] Já está no CP0 — não pode voltar.");
                return;
            }

            _checkPointAtual--;
            _roteiroConcluido = false;
            SalvarProgresso();
            Debug.Log($"[RouteProgress] ◀ Voltou para CP{_checkPointAtual}");
            guia.TocarCheckPoint(_checkPointAtual);
        }

        /// <summary>
        /// Reinicia o roteiro do zero (CP0) e apaga o progresso persistido.
        /// </summary>
        public void ResetarRoteiro()
        {
            _checkPointAtual = 0;
            _roteiroConcluido = false;
            SalvarProgresso();
            Debug.Log("[RouteProgress] 🔄 Roteiro resetado para CP0.");
            guia.TocarCheckPoint(0);
        }

        // ====================================================================
        // SECÇÃO 8 — API PÚBLICA: EVASÃO DE OBSTÁCULOS
        // ====================================================================

        /// <summary>
        /// Deve ser chamado pelo MainSystemOrchestrator a cada frame com os
        /// dados mais recentes do MiDaS. O RouteProgressManager decide
        /// internamente se e quando iniciar uma sequência de evasão.
        /// </summary>
        public void AtualizarDadosMidas(MidasResult midas)
        {
            _ultimoMidas = midas;
            VerificarObstaculo(midas);
        }

        /// <summary>
        /// Força o início imediato de uma sequência de evasão com os dados
        /// MiDaS mais recentes. Útil quando o Orchestrator já decidiu evadir
        /// e quer delegar a sequência ao RouteProgressManager.
        /// </summary>
        public void IniciarEvasaoImediata(MidasResult midas)
        {
            if (_evasaoEmCurso) return;

            _ultimoMidas = midas;
            _corotinaEvasao = StartCoroutine(SequenciaEvasao(midas));
        }

        // ====================================================================
        // SECÇÃO 9 — LÓGICA INTERNA: PROGRESSO
        // ====================================================================

        private IEnumerator AguardarEAvancar()
        {
            yield return new WaitForSeconds(tempoEntreCheckPoints);

            // Aguarda o áudio do CP terminar antes de avançar
            yield return new WaitUntil(() =>
                guia == null ||
                (!guia.EstaTocandoAudioDeSistema &&
                 (guia.spatialAudio == null || !guia.spatialAudio.EstaReproduziindo))
            );

            AvancarParaProximoCheckPoint();
        }

        private void CarregarProgresso()
        {
            if (resetarProgressoAoIniciar)
            {
                PlayerPrefs.DeleteKey(saveKey);
                _checkPointAtual = 0;
                _roteiroConcluido = false;
                return;
            }

            _checkPointAtual = PlayerPrefs.GetInt(saveKey, 0);
            _checkPointAtual = Mathf.Clamp(_checkPointAtual, 0, totalCheckPoints - 1);
            _roteiroConcluido = (_checkPointAtual >= totalCheckPoints - 1 &&
                                  PlayerPrefs.GetInt(saveKey + "_done", 0) == 1);

            Debug.Log($"[RouteProgress] Progresso carregado: CP{_checkPointAtual}" +
                      (_roteiroConcluido ? " (CONCLUÍDO)" : ""));
        }

        private void SalvarProgresso()
        {
            PlayerPrefs.SetInt(saveKey, _checkPointAtual);
            PlayerPrefs.SetInt(saveKey + "_done", _roteiroConcluido ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[RouteProgress] 💾 Progresso guardado: CP{_checkPointAtual}");
        }

        // ====================================================================
        // SECÇÃO 10 — LÓGICA INTERNA: EVASÃO DE OBSTÁCULOS
        // ====================================================================

        private void VerificarObstaculo(MidasResult midas)
        {
            if (_evasaoEmCurso) return;

            bool caminhoFrontalBloqueado = midas.dangerScore > limiarObstaculo;

            if (caminhoFrontalBloqueado)
            {
                _framesComObstaculo++;

                if (_framesComObstaculo >= framesConsecutivosParaEvasao)
                {
                    _framesComObstaculo = 0;
                    Debug.Log($"[RouteProgress] ⚠️ Obstáculo detectado por {framesConsecutivosParaEvasao} frames " +
                              $"(dangerScore={midas.dangerScore:F1}). Iniciando evasão.");
                    _corotinaEvasao = StartCoroutine(SequenciaEvasao(midas));
                }
            }
            else
            {
                // Decrementa gradualmente para evitar reset instantâneo
                if (_framesComObstaculo > 0) _framesComObstaculo--;

                // Feedback proativo (Siga em frente / Caminho livre)
                if (Time.time - _tempoUltimaInstrucaoProativa > intervaloFeedbackProativo)
                {
                    _tempoUltimaInstrucaoProativa = Time.time;
                    Guia.EstadoInstrucao frenteSegura =
                        midas.dangerScore < 2f ? Guia.EstadoInstrucao.Frente4 :
                        midas.dangerScore < 4f ? Guia.EstadoInstrucao.Frente3 :
                                                 Guia.EstadoInstrucao.Frente2;

                    Debug.Log($"[RouteProgress] 🟢 Feedback Proativo: {frenteSegura} (Score: {midas.dangerScore:F1})");
                    guia.ExecutarComando(frenteSegura);
                }
            }
        }

        /// <summary>
        /// Sequência completa de evasão:
        ///  1. Anuncia o obstáculo (Stop ou comando direcional)
        ///  2. Emite comandos de evasão baseados nas zonas livres
        ///  3. Aguarda o caminho libertar (com timeout)
        ///  4. Confirma via áudio que o caminho está livre
        /// </summary>
        private IEnumerator SequenciaEvasao(MidasResult midasInicial)
        {
            _evasaoEmCurso = true;

            // ── Passo 1: Parar e anunciar obstáculo ─────────────────────────
            Debug.Log("[RouteProgress] 🚧 Evasão — Passo 1: Parar");
            guia.ExecutarComando(Guia.EstadoInstrucao.Parar);

            // Aguarda o áudio de parar terminar (máx 3s)
            float t = Time.time;
            yield return new WaitUntil(() =>
                Time.time - t > 3f ||
                (guia.spatialAudio != null && !guia.spatialAudio.EstaReproduziindo)
            );

            // ── Passo 2: Determinar direcção de evasão ───────────────────────
            Guia.EstadoInstrucao comandoEvasao = DeterminarDirecaoEvasao(midasInicial);
            Debug.Log($"[RouteProgress] 🚧 Evasão — Passo 2: {comandoEvasao} " +
                      $"(Esq={midasInicial.leftZoneDanger:F1} Dir={midasInicial.rightZoneDanger:F1})");

            guia.ExecutarComando(comandoEvasao);

            // Aguarda o áudio do comando de evasão
            float t2 = Time.time;
            yield return new WaitUntil(() =>
                Time.time - t2 > 5f ||
                (guia.spatialAudio != null && !guia.spatialAudio.EstaReproduziindo)
            );

            // ── Passo 3: Aguardar o caminho libertar ─────────────────────────
            Debug.Log("[RouteProgress] 🚧 Evasão — Passo 3: Aguardando caminho livre...");
            float inicioEspera = Time.time;
            bool caminhoLivre = false;

            while (Time.time - inicioEspera < timeoutEvasao)
            {
                yield return new WaitForSeconds(0.5f);

                if (_ultimoMidas.dangerScore < limiarObstaculo - 1.5f)
                {
                    caminhoLivre = true;
                    break;
                }

                // Re-emite comando de evasão se o obstáculo persistir
                // (útil quando o utilizador não se moveu o suficiente)
                if (Time.time - inicioEspera > 3f && (guia.spatialAudio == null || !guia.spatialAudio.EstaReproduziindo))
                {
                    Debug.Log("[RouteProgress] 🚧 Evasão — Re-emitindo comando de evasão...");
                    guia.ExecutarComando(comandoEvasao);
                    yield return new WaitForSeconds(2.0f);
                }
            }

            // ── Passo 4: Confirmar resultado ─────────────────────────────────
            if (caminhoLivre)
            {
                Debug.Log("[RouteProgress] ✅ Evasão concluída — caminho livre!");
                // Volta ao frente com o nível de cautela adequado
                Guia.EstadoInstrucao frenteSegura =
                    _ultimoMidas.dangerScore < 2f ? Guia.EstadoInstrucao.Frente4 :
                    _ultimoMidas.dangerScore < 4f ? Guia.EstadoInstrucao.Frente3 :
                                                    Guia.EstadoInstrucao.Frente2;

                guia.ExecutarComando(frenteSegura);
            }
            else
            {
                // Timeout — obstáculo não foi contornado
                Debug.LogWarning($"[RouteProgress] ⏱️ Timeout de evasão ({timeoutEvasao}s) — obstáculo ainda presente. Parando.");
                guia.ExecutarComando(Guia.EstadoInstrucao.Parar);
            }

            _evasaoEmCurso = false;
            _corotinaEvasao = null;
        }

        /// <summary>
        /// Escolhe o melhor lado para desviar com base nas zonas laterais do MiDaS.
        /// Considera evasão dupla quando ambos os lados estão relativamente livres
        /// mas o frente está muito bloqueado (dangerScore > 8).
        /// </summary>
        private Guia.EstadoInstrucao DeterminarDirecaoEvasao(MidasResult midas)
        {
            bool esqLivre = midas.leftZoneDanger  < limiarObstaculo - 0.5f;
            bool dirLivre = midas.rightZoneDanger < limiarObstaculo - 0.5f;
            bool obstaculoExtremo = midas.dangerScore > 8.0f;

            // Evasão dupla — obstáculo extremo com um lado completamente livre
            if (obstaculoExtremo)
            {
                if (esqLivre && !dirLivre) return Guia.EstadoInstrucao.DesviarDuploEsquerda;
                if (dirLivre && !esqLivre) return Guia.EstadoInstrucao.DesviarDuploDireita;
            }

            // Evasão simples — escolhe o lado mais livre
            if (esqLivre && dirLivre)
            {
                // Ambos livres: escolhe o mais livre
                return midas.leftZoneDanger <= midas.rightZoneDanger
                    ? Guia.EstadoInstrucao.DesviarEsquerda
                    : Guia.EstadoInstrucao.DesviarDireita;
            }

            if (esqLivre)  return Guia.EstadoInstrucao.DesviarEsquerda;
            if (dirLivre)  return Guia.EstadoInstrucao.DesviarDireita;

            // Ambos os lados bloqueados — gira para o menos bloqueado
            return midas.leftZoneDanger <= midas.rightZoneDanger
                ? Guia.EstadoInstrucao.GirarEsquerda
                : Guia.EstadoInstrucao.GirarDireita;
        }

        // ====================================================================
        // SECÇÃO 11 — CICLO DE VIDA: CLEANUP
        // ====================================================================

        private void OnDisable()
        {
            if (_corotinaEvasao != null)
            {
                StopCoroutine(_corotinaEvasao);
                _corotinaEvasao = null;
            }
            _evasaoEmCurso = false;
        }

        private void OnDestroy()
        {
            OnDisable();
        }

#if UNITY_EDITOR
        // ====================================================================
        // SECÇÃO 12 — GIZMOS EDITOR (Debug visual no Scene View)
        // ====================================================================

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize   = 14,
                alignment  = TextAnchor.MiddleLeft,
                richText   = true
            };

            string cor      = _roteiroConcluido ? "lime" : (_evasaoEmCurso ? "orange" : "cyan");
            string estado   = _roteiroConcluido ? "CONCLUÍDO" : (_evasaoEmCurso ? "⚠ EVASÃO" : "EM CURSO");
            string obst     = $"<color=yellow>Obstáculo: {_ultimoMidas.dangerScore:F1}/10 " +
                              $"(Esq={_ultimoMidas.leftZoneDanger:F1} Dir={_ultimoMidas.rightZoneDanger:F1})</color>";

            string texto =
                $"<color={cor}>[RouteProgress] CP{_checkPointAtual}/{totalCheckPoints - 1} — {estado}</color>\n" +
                obst +
                $"\nFrames obstáculo: {_framesComObstaculo}/{framesConsecutivosParaEvasao}";

            GUI.Box(new Rect(10, 10, 480, 80), texto, style);
        }
#endif
    }
}
