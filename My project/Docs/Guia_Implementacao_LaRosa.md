# 🌹 App La Rosa - Guia de Implementação e Uso (Arquitetura XR)

Este documento detalha o funcionamento, implementação e configuração do novo ecossistema de Percepção e Navegação desenvolvido para o App La Rosa. O sistema integra inteligência artificial de borda, realidade aumentada e fusão de sensores (IMU) para fornecer orientação espacial de alta precisão para pessoas com deficiência visual.

---

## 1. Visão Geral da Arquitetura

O sistema foi refatorado em componentes de responsabilidade única que operam em conjunto:

1. **Percepção Visual:** O `RaycastScanner` traduz os mapas de profundidade (Depth Anything V2) em distâncias métricas reais.
2. **Posicionamento e Odometria:** O `OdometryTracker` utiliza o giroscópio e acelerômetro do celular, combinados com dados espaciais (ARCore/ARKit), para rastrear passos e detectar inércia.
3. **Navegação Física:** O `ARCheckpointPlacer` ancora pontos tridimensionais no mundo real (chão) utilizando Realidade Aumentada. O `RouteProgressTracker` atua como a bússola que guia o usuário por esses pontos.
4. **Tomada de Decisão:** O `MainSystemOrchestrator` e a classe `Decision` interpretam onde o usuário está e quão longe estão os obstáculos, emitindo comandos vocais adequados e acionando o hardware (Servo Bússola Rosa).

---

## 2. Componentes e Configuração

Abaixo, os detalhes de cada classe, como utilizá-las dentro da cena Unity e as configurações do Inspector.

### 2.1. `OdometryTracker.cs` (Pedômetro e Movimento)

**Propósito:** Ratrear se o usuário está caminhando ativamente ou parado. Ele calcula os passos, a distância percorrida e a velocidade em tempo real. Essencial para evitar que a voz do guia repita comandos enquanto o usuário está fisicamente parado.

**Como Integrar na Cena:**
- Anexe a um GameObject vazio (ex: `XR_Sensors`).
- Conecte este componente no slot indicado do `MainSystemOrchestrator`.

**Configurações do Inspector:**
- **Step Peak Threshold:** (Padrão: 0.30) - Sensibilidade do acelerômetro. Se o app estiver registrando passos sozinho, aumente (ex: 0.40). Para passos muito lentos (idosos), diminua (ex: 0.20).
- **Min Step Interval:** (Padrão: 0.30s) - Tempo mínimo entre a contagem de dois passos para evitar picos duplos.
- **Idle Timeout Segundos:** (Padrão: 2.0s) - Quanto tempo sem pisar para o sistema considerar que o usuário "parou".
- **Passos Para Desbloquear:** (Padrão: 1) - Quantos passos o usuário precisa dar após receber um comando evasivo ("Desvie para a esquerda") para que o sistema possa emitir novos comandos laterais.
- **Use ARCore Fusion:** (Verdadeiro ou Falso) - Se ativado, lê o sensor 6DoF do AR Foundation para anular o drift inercial do celular.
- **AR Movement Threshold:** (Padrão: 0.05 m/s) - Qual a velocidade de locomoção corporal percebida pelo ARCore para configurar que o corpo está em deslocamento.

---

### 2.2. `RaycastScanner.cs` (A Bengala Virtual)

**Propósito:** Transforma as saídas de Inteligência Artificial em dados físicos. Mapeia um "*Danger Score*" (0-10) em metros para saber se o perigo está a 1 metro ou 5 metros.

**Como Integrar na Cena:**
- Anexe a um GameObject central ou ao mesmo objeto da IA.
- O `DepthAIManager` ou `MidasInferenceManager` empurra dados para ele chamando `.Scan(resultado)`.
- A classe `Decision` lê `.ObterMidasCalibrado()`.

**Configurações do Inspector:**
- **Distancia Maxima (Caminho Livre):** (Padrão: 7.0m) - Distância representada pelo score de perigo zero.
- **Distancia Minima (Colisão Iminente):** (Padrão: 0.3m) - Distância mais curta representada por um score máximo.
- **Curvatura Conversao:** (Padrão: 0.35) - Agressividade da escala. Deixe em 0.35 para ambientes apertados e fechados.
- **Limiar Perigo Imediato:** (Padrão: 0.8m) - Toda vez que tiver um obstáculo a menos de 0.8 metros, ele emite sinal vermelho que obriga o Sistema Orquestrador a emitir "PARE".

---

### 2.3. `RouteProgressTracker.cs` (Lógica de Navegação e Checkpoints)

**Propósito:** Mantém o controle de onde o usuário está, onde é o próximo objetivo e a que distância ele se encontra. Este script comanda o atuador (Servo) para apontar ativamente.

**Como Integrar na Cena:**
- Adicione a um GameObject global (ex: `NavigationSystem`).
- Arraste a classe `OdometryTracker` para o campo correspondente nela, se desejar confirmação de chegada por passos.

**Configurações do Inspector:**
- **User Transform:** O transform 3D central do jogador (Câmera do celular).
- **Radius Tolerance:** (Padrão: 1.5m) - O quão perto você precisa chegar de um checkpoint para que ele seja contabilizado como "Alcançado".
- **Use GPS Fallback:** Se Verdadeiro, em vez de ler marcadores AR no chão, ele vai buscar as latitudes e longitudes geradas pelo `NavigationManager`. Útil apenas para trajetos outdoor gigantes; mantenha *Falso* para interiores ou rotatórias. 
- **Use Odometry Confirmation:** Se verdadeiro, apenas entrar no raio de alcance não é o suficiente; o giroscópio do usuário tem que confirmar que N passos foram dados (isso impede que ruídos do sistema o transportem de um local para o outro incorretamente).

---

### 2.4. `ARCheckpointPlacer.cs` (Ancoramento no Mundo Físico)

**Propósito:** Interagir com o chão (Planos) lidos pela câmera do celular. Responsável pelo processo onde o usuário fisicamente pisa no ambiente, mira para um canto e cria a Rota em tempo de execução.

**Como Integrar na Cena:**
- Apenas coloque no GameObject principal da UI. 
- Ele necessita dos scripts base do AR Foundation da sua cena da Unity (`ARRaycastManager`, `ARPlaneManager` e `ARAnchorManager`). Arraste-os para os slots respectivos. 
- Coloque uma referência ao `RouteProgressTracker` no Inspector dele.

**Como o Fluxo Acontece no App via Interface (HUD):**
1. O usuário toca em **[ESTABELECER CHECKPOINTS]**. Isso vai habilitar a matriz de planos rastreando o piso.
2. Onde houver piso plano, surgirá um *Preview Visual (bolinha semi transparente)*.
3. Tocando em **[MARCAR CHECKPOINT]**, o item atual ganha peso e uma "Âncora AR" tridimensional trava essa bolinha real no ambiente. Ela não flutua e nem sofre variação da câmera.
4. Repita a marcação de 2 a 5 checkpoints.
5. Clica em **[INICIAR]**. Os planos somem, a matriz trava para poupar poder de processamento do celular e a pista é enviada inteira ao `RouteProgressTracker` para ele começar a te orientar com comandos como: "vire a esquerda", "caminho livre".

*Nota de compilação:* Esse código está perfeitamente protegido por `#if UNITY_AR_FOUNDATION_PRESENT`. Caso o Plugin "AR Foundation" não esteja instalado pelo *Package Manager*, o projeto funcionará sem erros, mas colocará os pontos a 1 metro flutuando em frente à tela em vez de mapear a calçada.

---

### 2.5. `MainSystemOrchestrator.cs` (Central de Decisão)

**Propósito:** Ponderar entre "Eu posso continuar indo aos checkpoints em paz?" ou "Tem um lixo ou degrau na minha frente pedindo prioridade para não me acidentar?". 

O `Orquestrador` obedece limites fixos. Sempre que o Raycast der ordem de "IsImmediateDanger", ele pausa totalmente a trilha, invadindo os áudios paralisando o Guia ("Pare! Perigo."). O Odometry atua neste momento garantindo que, até você se mover `x passos` de inércia, nenhum novo comando evasivo será despejado sobre sua audição.

**Como usa:** Requer a UI, os Inferences, o `RaycastScanner` e a `Decision` nos slots da Scene. Não precisa lidar diretamente com ele a menos que precise dar Toggle de quais IA estão operantes ou debugar logs.

---

## 3. Guia Rápido de Solução de Problemas

- **Não aparece o chão nem a Esfera Verde de Marcação AR:**
  Se o app compilar, confira se nas configurações da Build você marcou dependência de ARKit/ARCore com permissões de Câmera da Unity, se a iluminação está boa, ou se de fato você importou o package *AR Foundation*. A HUD precisa mostrar "📍 AR: SUPERFÍCIE DETECTADA" e não "MODO POCKET".
  
- **Voz robótica repetitiva quando estou parado:**
  Abra o `OdometryTracker` na Scene. O `velocidade atualMs` tá se mexendo sozinho sem você pisar? É ruído magnético ou vibração. Aumente a variável `VarianciaMovimento` e o `Step Peak Threshold` até que o estado estabilize como **"PARADO"**.

- **Comandos "Vire" não surtem efeito no Bússola Hardware:**
  Abra a ferramenta do firmware. Certifique-se nas abas de conexão da engine que o HUD "Conectou". O atuador exige um IP fixo (geralmente `192.168.43.50`). Na variável Pública do TrackProgrees, o Toggle `calcularAnguloBussola` precisa estar verificado.

- **Checkpoints ignorados:**
  Raio de captura menor do que 0.5 metros podem ser impossíveis de acionar por GPS Drift. Estale a propriedade `Radius Tolerance` do `RouteProgressTracker` para no mínimo `1.2`. Somado a isso, desmarque `User Odometry Confirmation` se estiver testando com pouco espaço.
