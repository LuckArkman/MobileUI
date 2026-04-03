# O GUIA DEFINITIVO DE MONTAGEM DA CENA (LUCKARKMAN XR)

Este manual é o seu **mapa completo e absoluto**. Ele consolida todos os scripts e componentes essenciais da arquitetura do aplicativo de mobilidade para deficientes visuais e garante que sua Cena (Scene) no Unity possua tudo amarrado corretamente.
Siga cada passo abaixo categoricamente. Se algum GameObject ainda não existir na sua aba `Hierarchy`, **crie-o como um "Empty GameObject"** e nomeie exatamente como está aqui para manter a organização.

---

## 1. O NÚCLEO DO SISTEMA 🧠
**GameObject Recomendado:** `[Core_Orquestrador]`
O Cérebro da aplicação. Onde tudo converge antes de ir para a interface ou ser enviado pelos alertas e atuações.

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`MainSystemOrchestrator`** | Controla toda a tomada de decisão em um Pipeline de 14 frames, unindo dados da Câmera, Rede YOLO, Rede MiDaS, enviando para o Decision e acionando o TTS. | Arraste os componentes `MjpegClient`, `YoloInferenceManager`, `MidasInferenceManager`, `DecisionMatrix`, `VoiceDirector` e `SmartphoneCameraSource` para ele. |
| **`BackgroundServiceManager`** | Garante que a Unity não hiberne ao fechar a tela. Mantém os módulos do óculos rodando por trás do bloqueio de tela via Foreground Android. | Não requer ligações. Funciona de forma autônoma. |
| **`BatteryOptimizer`** | Trava os quadros em 30 FPS e previne falhas de processador fritando o aplicativo. | Não requer ligações. |

---

## 2. A REDE NEURAL E VISÃO ESPACIAL 👁️
**GameObject Recomendado:** `[Sistema_Inteligencia_Artificial]`
A base computacional que consome placa de vídeo (Sentis) para entender o mundo.

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`YoloInferenceManager`** | Carrega o modelo ONNX da Yolo (v8/v7) ou MobileNet. Converte os pixels para caixas mágicas que dizem: "É um Carro", "É uma Pessoa". | Arraste o arquivo `.onnx` para o campo *Model Asset*. |
| **`MidasInferenceManager`** | Executa a Rede de Profundidade MiDaS que extrai a matriz tridimensional da cena e devolve a pontuação de relevo (Distance Map) de 0 a 10. | Arraste o arquivo de modelo de depth para o campo *Model Asset*. |

---

## 3. TOMADA DE DECISÃO E PREVENÇÃO DE PERIGO 🛡️
**GameObject Recomendado:** `[Sistema_Seguranca]`
O filtro humano. Traduz números em comandos práticos ("Gire a Esquerda").

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`Decision`** | Recebe a Matriz de YOLO e MiDaS e gera as ações humanas matemáticas cruzadas. Evita "falsos positivos" com a fila de histórico. | `HeatmapManager` deve ser arrastado caso deseje renderizar as zonas vermelhas na HUD. |
| **`RiskCalculator`** | Computador lógico puro para mensurar pesos dinâmicos do cenário (Ex: Caminhão = Risco 10, Cadeira = Risco 2). | Autônomo. Pode ser invocado pelo orquestrador. |
| **`HeatmapManager`** | Pega as coordenadas X e Y e tamanho bruto dos objetos para gerar esferas na tela de "Aviso de Calor" para quem enxerga subitamente. | Requer um Componente de UI e Imagem/Shader na aba Inspector se for usado em Overlay. |

---

## 4. O SISTEMA DE VOZ E NAVEGAÇÃO 🗣️
**GameObject Recomendado:** `[Motor_Rotas_E_Voz]`
Aqui mora a alma falante do aplicativo, o guia ativo e dinâmico.

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`SmallPrinceTTS`** | Comunica-se com o motor Text-To-Speech nativo do Android moldando o tom (Pitch 1.3) para soar acolhedor, rápido e didático. | Ajustar *Pitch* (1.3) e *Rate* (0.9) na janela do Unity. |
| **`VoiceDirectorService`** | Gestor de Fila Prioritária (Priority Queue). Interrompe "Siga reto" caso entre um grito superior de "Carro a frente, Pare!". | Arrastar o componente **`SmallPrinceTTS`** para funcionar. |
| **`GoogleMapsNavigator`** | Pega o texto e dispara a rota profunda diretamente para o aplicativo do Google Maps do Android de forma invisível/sobreposta. | Arrastar o `VoiceDirector` para avisos sistêmicos de início de rota. |

---

## 5. REDES E COMUNICAÇÃO DE DADOS (IOT ESP32) 📡
**GameObject Recomendado:** `[Networking_Bridge]`
Conectividade direta com o Óculos Físico e infraestrutura de pontes móveis.

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`WifiDiscoveryManager`** | A cada 1 segundo emite um pacote de Broadcast UDP na rede procurando a resposta mágica "Estou Ligado!" do Óculos ESP32. | Caso use simulação, ative *Usar Ip Fixo* e insira algo como 192.168.x.x. |
| **`HotspotFallbackManager`** | Monitora os Gritos de IP; se demorar 30s sem ouvir nada, chuta o balde e liga o modo "Ancoragem de Internet" (Hotspot) nativa do celular. | Arraste o `WifiDiscoveryManager` e o `VoiceDirector` para a aba. |
| **`MjpegTextureClient`** | O pipeline violento que recebe pacotes brutos `Array Bytes` da câmera do ESP32 e converte num `Texture2D` compatível nativo da Unity. | Nenhuma ação restrita além da invocação central. |
| **`LatencyMonitor`** | Faz as medições Round Trip Time (RTT). Mede se o Wifi está gargalando os envios. | Nenhuma ação requerida. |

---

## 6. A INTERFACE DO USUÁRIO AVANÇADA (UI TOOLKIT) 📺
**GameObject Recomendado:** `[Main_HUD_Canvas]` (Este deve possuir obrigatoriamente um componente nativo da Unity chamado `UIDocument`)

| Script / Componente | Função Fundamental | Onde Ligar no Inspector |
| :--- | :--- | :--- |
| **`HudController`** | Varre a interface XML atrás dos botões desenhados no painel e diz o que cada um faz na engenharia real do backend. | Requer preenchimento crítico no Inspector de 4 módulos (Arraste e solte): `WifiDiscoveryManager`, `YoloInferenceManager`, `SmartphoneCameraSource`, `GoogleMapsNavigator` (Adicionado na Sprint 7). |
| **`SmartphoneCameraSource`** | (Feature 4). Liga a Câmera nativa traseira do celular e sobrecrente para funcionar como fallback do Oculus. Fornece `Texture2D` pro Orquestrador. | Requer um Input Component. Não se esqueça de habilitar a *Rear Camera*. |

---

### Resumo Perfeito de Dependências & Passos de Execução:
Para garantir seu teste:
1. Pressione **Play** no Unity (com Aspect Ratio de Phone configurado).
2. O **`BackgroundServiceManager`** e o **`SmallPrinceTTS`** iniciarão nas sombras ativando as notificações primárias.
3. Clicar em "Usar Câmera" via UI fará o **`HudController`** ativar o **`SmartphoneCameraSource`**.
4. O **`MainSystemOrchestrator`** sugará esse Frame e alternará cálculos pesados de **`YoloInferenceManager`** e **`MidasInferenceManager`** no escalonador dos 14 frames lógicos.
5. Em caso de perigo imediato (`DangerScore = 10`), o **`Decision`** cuspirá um comando prioritário.
6. O Orquestrador o formatará para uma frase humana: *"Carro a 2 passos. Pare!"*.
7. O **`VoiceDirectorService`** fará a injeção instantânea na via principal e a caixa de som do celular reproduzirá o caminho seguro. O teste flui magicamente.
