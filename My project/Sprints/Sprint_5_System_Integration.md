# Sprint 5: Integração de Sistema e Testes em Campo

## Visão Geral
Unificação de todos os módulos desenvolvidos nas sprints anteriores em um único fluxo de trabalho e realização de testes de validação no mundo real.

## Objetivos
- Unificar Streaming + AR + IA + Heatmap em uma única cena.
- Realizar testes de estresse de rede e hardware.
- Validar a precisão da detecção em ambientes industriais/educacionais controlados.

## Ferramentas e Pacotes
- **Unity Profiler**: Para identificar gargalos de CPU/GPU.
- **ADB (Android Debug Bridge)**: Para logs e depuração em tempo real no dispositivo.

## Scripts a Implementar
1. `MainSystemOrchestrator.cs`: O "cérebro" do app que liga/desliga módulos e gerencia o estado global (Idle, Streaming, Calibrating, Warning).
2. `DiagnosticOverlay.cs`: UI de overlay para engenheiros verem KPIs técnicos (FPS, Mbps, Detecções por seg).
3. `DataLogger.cs`: Salva logs de performance e eventos de risco para análise posterior.

## Tarefas
- [ ] Criar a cena `Main_Live_AR` com todos os componentes.
- [ ] Testar reconexão automática em caso de queda de Wi-Fi.
- [ ] Validar falsos positivos na detecção de colisão.
- [ ] Coleta de feedback de ergonomia da interface AR.
