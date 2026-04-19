using UnityEngine;
using System.Collections.Generic;
using System.Text;

public class MobileDebugPanel : MonoBehaviour
{
    private StringBuilder logBuilder = new StringBuilder();
    private Vector2 scrollPosition;
    public bool mostrarPainel = true;

    // Referências do seu sistema (Arraste no Inspector)
    public LuckArkman.XR.Main.Guia guia;

    // Variáveis de Redimensionamento
    private float alturaPainel;
    private bool isDragging = false;
    private float alturaBarraDrag = 50f; // Tamanho ideal para o dedo tocar

    void Start()
    {
        // O painel começa ocupando 25% da parte inferior da tela
        alturaPainel = Screen.height * 0.25f;
    }

    void OnEnable() => Application.logMessageReceived += HandleLog;
    void OnDisable() => Application.logMessageReceived -= HandleLog;

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        string cor = type == LogType.Error ? "red" : (type == LogType.Warning ? "yellow" : "white");
        logBuilder.AppendLine($"<color={cor}>[{System.DateTime.Now:HH:mm:ss}] {logString}</color>");
        
        // Mantém o console leve na memória
        if (logBuilder.Length > 5000) logBuilder.Remove(0, 1000); 
    }

    void Update()
    {
        // Toque triplo na tela para esconder/mostrar o painel por completo
        if (Input.touchCount == 3 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            mostrarPainel = !mostrarPainel;
        }
    }

    void OnGUI()
    {
        if (!mostrarPainel) return;

        // O Y do painel é o fundo da tela menos a altura atual dele
        Rect areaPainel = new Rect(0, Screen.height - alturaPainel, Screen.width, alturaPainel);
        
        // A área de "pegada" para puxar fica bem no topo do painel
        Rect areaDrag = new Rect(0, Screen.height - alturaPainel, Screen.width, alturaBarraDrag);

        // ==========================================
        // LÓGICA DE DRAG (Puxar a janela)
        // ==========================================
        Event e = Event.current;

        // Se o dedo tocar dentro da barra de drag
        if (e.type == EventType.MouseDown && areaDrag.Contains(e.mousePosition))
        {
            isDragging = true;
        }
        else if (e.type == EventType.MouseUp)
        {
            isDragging = false;
        }

        // Se estiver arrastando o dedo
        if (isDragging && e.type == EventType.MouseDrag)
        {
            // Subtrai o movimento (para cima no eixo Y é negativo, então subtrair aumenta a altura)
            alturaPainel -= e.delta.y;
            
            // Trava a altura entre 15% e 85% da tela para não sumir nem engolir a tela inteira
            alturaPainel = Mathf.Clamp(alturaPainel, Screen.height * 0.15f, Screen.height * 0.85f);
        }

        // ==========================================
        // DESENHO DA INTERFACE
        // ==========================================
        
        // Fundo principal do painel
        GUI.Box(areaPainel, ""); 
        
        // Barra superior de puxar (Drag Handle)
        GUI.Box(areaDrag, "================ PUXE AQUI PARA REDIMENSIONAR ================");

        // Cria uma área interna isolada abaixo da barra de drag para o conteúdo não vazar
        GUILayout.BeginArea(new Rect(10, Screen.height - alturaPainel + alturaBarraDrag, Screen.width - 20, alturaPainel - alturaBarraDrag - 10));
        
        // 1. WATCHER DE ESTADO (O X-9 do bug)
        string statusTTS = guia != null ? $"TTS Ocupado: {guia.EstaTocandoAudioDeSistema}" : "Guia: NULL";
        GUILayout.Label($"<b><size=14>STATUS DO SISTEMA:</size></b> {statusTTS}");

        // 2. CONSOLE DE LOGS
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        // Rolagem automática para o final se a barra estiver muito perto da base
        GUILayout.Label(logBuilder.ToString());
        GUILayout.EndScrollView();

        // 3. BOTÃO DE LIMPEZA
        if (GUILayout.Button("Limpar Console", GUILayout.Height(50)))
        {
            logBuilder.Clear();
        }

        GUILayout.EndArea();
    }
}