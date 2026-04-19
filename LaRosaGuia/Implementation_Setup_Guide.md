# 📖 Manual Exaustivo de Implementação: Sistema XR LuckArkman

Este documento fornece as diretrizes técnicas completas para a montagem, configuração e teste do sistema de Realidade Estendida no Unity 6. Siga rigorosamente estas instruções para garantir a integridade da comunicação entre os módulos de IA, AR e Streaming.

---

## 1. Arquitetura da Cena Unity (Hierarchy)

Abaixo estão os GameObjects obrigatórios e os componentes que devem ser anexados a cada um.

### A. [XRLock_Core]
Este objeto serve como o núcleo lógico do sistema.
- **Componentes:** 
  - `MainSystemOrchestrator.cs`: Orquestrador central.
  - `BatteryOptimizer.cs`: Gerencia o consumo energético em tempo real.
- **Configuração no Inspector:**
  - `Ai Process Interval`: `0.1s` (ajustável para performance).
  - `Discovery Manager`: Arraste o objeto `[Network_Manager]`.
  - `Signaling Client`: Arraste o objeto `[Network_Manager]`.
  - `Yolo AI`: Arraste o objeto `[AI_Module]`.
  - `Battery Optimizer`: Referencie o componente local.

### B. [Network_Manager]
Centraliza a conectividade WebRTC e Descoberta de Rede.
- **Componentes:**
  - `DiscoveryManager.cs`: Escuta sinais do headset VR via Wi-Fi.
  - `SignalingClient.cs`: Gerencia o handshake WebRTC.
- **Configuração no Inspector:**
  - `Server Port`: `8080`.
  - `Service Name`: `_luckarkman_xr._tcp`.

### C. [AI_Module]
Responsável pelo processamento de visão computacional na GPU.
- **Componentes:**
  - `YoloInferenceManager.cs`: Implementação do Inference Engine 2.x.
- **Configuração no Inspector:**
  - `Model Asset`: Arraste seu arquivo `.sentis`.
  - `Pre Process Shader`: Arraste o arquivo `YoloPreProcess.compute`.
  - `Target Width/Height`: `640` (padrão YOLOv11).

### D. [AR_World]
Gerencia a fusão sensorial e a sobreposição AR.
- **Componentes:**
  - `ArCoordinator.cs`: Interface com AR Foundation.
  - `ImuFusionProvider.cs`: Fusão de Giroscópio e Acelerômetro.
  - `HeatmapManager.cs`: Gerador visual de zonas de risco.
- **Configuração no Inspector:**
  - `Imu Provider`: Referencie o componente local.
  - `Ar Camera`: Arraste a `AR Camera` do `XROrigin`.

---

## 2. Detalhamento das Classes e Dependências

| Classe | Responsabilidade | Dependência Crítica |
| :--- | :--- | :--- |
| `MainSystemOrchestrator` | Alterna estados (Idle, Streaming, Warn). | Precisa de acesso a todos os Gerenciadores. |
| `YoloInferenceManager` | Executa a rede neural via GPU (Compute Shaders). | Depende do pacote `com.unity.ai.inference`. |
| `ImuFusionProvider` | Gera quaternions de pose estáveis. | Requer Input System ativado em **Both** ou **New**. |
| `SignalingClient` | Negocia a transmissão de vídeo de baixa latência. | Requer o pacote `com.unity.webrtc`. |

---

## 3. Guia de Configuração no Inspector (Passo a Passo)

### 3.1. Vinculação de Objetos (Drag & Drop)
1. Selecione o **[System_Core]**. 
2. No campo `Discovery Manager` do script `MainSystemOrchestrator`, arraste o objeto **[Network_Manager]** da hierarquia. O Unity detectará automaticamente o script anexado.
3. Repita o processo para `ArCoordinator`, `YoloAI`, `HudController` e os demais campos vazios. **Campos em vermelho (Missing) impedirão a compilação ou execução.**

### 3.2. Configuração do Modelo YOLO
- Clique no arquivo do seu modelo nas pastas do projeto. No Inspector, certifique-se de que ele está reconhecido como um `InferenceEngine Asset`.
- No `YoloInferenceManager`, valide se os nomes das camadas de entrada/saída coincidem com o seu modelo específico (ex: `images` e `output0`).

---

## 4. Validação e Teste (O que esperar?)

1. **Compilação:** Se você vir erros de namespace, clique em `Edit > Preferences > External Tools > Regenerate Project Files`.
2. **Logs (Console):**
   - `[Discovery] Listening on port 8080...` -> Rede ok.
   - `[YoloAI] Worker inicializado na GPU.` -> Inferência ok.
   - `[IMU] Sensores ativados.` -> AR ok.
3. **Indicadores Visuais:** No `DiagnosticOverlay`, a barra de memória não deve exceder 500MB em dispositivos mobile modernos.

---

## ⚠️ Alertas de Segurança e Performance

> [!WARNING]
> **Permissões Android:** O sistema falhará silenciosamente se as permissões de `CAMERA` e `INTERNET` não estiverem marcadas nas Configurações do Projeto (Player Settings).

> [!IMPORTANT]
> **Consumo de Bateria:** O `BatteryOptimizer` reduzirá automaticamente o FPS da IA se a temperatura do dispositivo exceder 45°C. Isso é esperado para evitar thermal throttling severo.
