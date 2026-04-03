# GUIA DE IMPLEMENTAÇÃO E INTEGRAÇÃO NA UNITY — SPRINT 7

Este guia serve como a documentação definitiva e minuciosa para integrar todas as novas classes desenvolvidas na **Sprint 7** direto no seu projeto Unity. O aplicativo `LuckArkman XR` agora possui Foreground Services, Google Maps, Wi-Fi Hotspot de transição, TTS do "Pequeno Príncipe" e uso da câmera do celular.

Abaixo, detalhamos a configuração **passo a passo** de como a sua `Hierarchy` (Hierarquia de GameObjects) na Unity deve se parecer e como os "fios" dos componentes devem ser interligados via *Inspector*.

---

## 1. Configurações de Sistema ⚙️

Antes de mexer nos GameObjects, o motor da Unity precisa estar ciente das novas permissões exigidas pelo Android (como a permissão de abrir um Hotspot ou rodar com a tela desligada).

*   **Passo 1:** Vá até a aba superior `Edit` > `Project Settings` > `Player`.
*   **Passo 2:** Acesse as configurações de **Android** (ícone do robô verde) e expanda a aba **Publishing Settings**.
*   **Passo 3:** Ative a caixa **Custom Main Manifest**. A Unity deve confirmar o uso do arquivo em `Assets/Plugins/Android/AndroidManifest.xml` (este arquivo já foi criado e preenchido corretamente no nosso workflow).
*   **Passo 4:** Na aba **Resolution and Presentation**, verifique se *Run In Background* está ativado.

---

## 2. O Controlador de Fundo e Energia 🔋
*Vamos lidar com a permanência do aplicativo enquanto a tela bloqueia.*

Crie ou vá até um GameObject na cena (geralmente usamos o `[XRLock_Core]` ou algum `GlobalManager`).
*   **Adicione o Componente (Add Component):** `Background Service Manager` (Namespace: `LuckArkman.XR.Background`)
*   **Configuração no Inspector:** Não há nenhuma variável pública para preencher! O script cuida instantaneamente de chamar o Serviço Foreground Java escondido em *Plugins* que exibe a notificação de status e protege a CPU.
*   **Verifique:** Certifique-se de que o `BatteryOptimizer` também está no mesmo objeto (ou ativo na cena). Ele já foi atualizado para nunca dormir e coexistir com o Serviço de Fundo.

---

## 3. O Sistema de Câmera de Bolso (Smartphone Fallback) 📷
*Ativa a lente traseira do smartphone caso seu óculos físico XR não esteja pareado.*

Recomenda-se adicionar isto na Hierarquia da UI ou dentro de um GameObject gerenciador como o **[XRLock_UI]**.
*   **Adicione o Componente:** `Smartphone Camera Source` (Namespace: `LuckArkman.XR.UI`)
*   **Configuração no Inspector:**
    *   **Use Back Camera:** `Marcado (True)`. *(A câmera traseira deve ser usada de frente pro buraco)*
    *   **Target Width:** `640`
    *   **Target Height:** `480`
    *   **Target FPS:** `30`

---

## 4. O Sistema TTS "O Pequeno Príncipe" 🎙️
*Este é o narrador do nosso HUD. Ele intercepta as tomadas de decisões do script de perigo.*

Crie um novo **GameObject vazio** na sua hierarquia e renomeie-o para `[VoiceSystem]`.
Neste Game Object, você adicionará **dois componentes fundamentais**:

1.  **Adicione o Componente:** `Small Prince TTS` (Namespace: `LuckArkman.XR.Voice`)
    *   **Voice Pitch:** `1.3` (Gera uma voz mais fina/infantil. Ajuste testando).
    *   **Voice Rate:** `0.9` (Fala limpa e com pausas amenas).

2.  **Adicione o Componente:** `Voice Director Service` (Namespace: `LuckArkman.XR.Voice`)
    *   **Tts Engine:** Arraste o Componente *Small Prince TTS* do passo 1 que está no mesmo GameObject para este campo. Ele ligará a "Maestria de Filas" diretamente à "Garganta".

---

## 5. Google Maps Navigation 🗺️
*Módulo de Deep Linking Inteligente.*

Crie um GameObject na Hierarquia chamado `[NavigationSystem]`.
*   **Adicione o Componente:** `Google Maps Navigator` (Namespace: `LuckArkman.XR.Navigation`)
*   **Configuração no Inspector:**
    *   **Voice Director:** Arraste o objeto `[VoiceSystem]` da hierarquia que acabamos de montar no passo #4.
    *   **Travel Mode:** `w` (Walk/Caminhando).

---

## 6. O Sistema de Hotspot Automático 🌐
*Cria a Rede Wi-Fi em branco no celular para conectar o Óculos remotamente.*

Crie ou vá até um GameObject na Hierarquia (pode ser o `[Networking]`).
*   **Adicione o Componente:** `Hotspot Fallback Manager` (Namespace: `LuckArkman.XR.Networking`)
*   **Configuração no Inspector:**
    *   **Timeout Seconds:** `30` (Aguardará 30 seg após iniciar o jogo. Se o Esp32 não aparecer neste tempo, ativa a ponte de Hotspot no celular).
    *   **Discovery Manager:** Arraste e solte o já existente componente `Wifi Discovery Manager`.
    *   **Voice Director:** Arraste e solte novamente o objeto `[VoiceSystem]`.

---

## 7. Acoplando tudo na Tela Principal: O `HudController` 📱
*Aqui, todas as funções anteriores vão interagir visualmente com a nossa Interface de Usuário polida.*

Selecione o seu GameObject do **Main HUD** (Aquele que tem seu componente *UIDocument* e o *Hud Controller*).

*No Inspector deste objeto do **HudController** verifique e arraste:*
*   **Smartphone Camera:** Arraste o GameObject que contem nosso `Smartphone Camera Source` gerado no passo 3.
*   **Maps Navigator:** Arraste o `[NavigationSystem]` que possui nosso script `Google Maps Navigator` gerado no passo 5.

### Revisão no UI Builder:
Agora que tudo está conectado no Unity Inspector, abra o seu `MainHUD.uxml` com dois cliques para garantir as amarrações do código. Nossa edição avançada via `.uxml` e o `.uss` Glassmorphism já organizaram as nomenclaturas como "IRMÃOS" (*siblings*) na UI.

Certifique-se de que tem estes exatos nomes na janela lateral direita (aba *Identity -> Name* no UIBuilder):
1.  **Toggle (Caixa de Câmera):** Deve chamar-se precisamente `ToggleCameraSource`.
2.  **Text Field (Painel do Mapa):** Deve chamar-se precisamente `DestinationInput`.
3.  **Botão Naranja de Ir:** Deve chamar-se precisamente `NavigateButton`.

---

### 🎉 Arquitetura Pronta!
Com todos os GameObjects instanciados na Unity e referenciados, o motor cerebral e autônomo está amarrado.

Ao dar "Play", a HUD solicitará um IP ou ligará o Hotspot e o Painel vai surgir. Ao digitar um endereço para navegação, o App será mantido em Segundo Plano (Background Services), as câmeras (XR ou Telefone) vão ficar injetando Texturas no Sentis para o modelo de Yolo, que avaliará e mandará por Voice Priorits (Fala do Pequeno Príncipe) o grito de "*Atenção! Parar Imediatamente!*" por cima da voz da inteligência guia do próprio Google Maps.
