# Sprint 4: Mapa de Calor e Predição de Colisão

## Visão Geral
Criação do sistema principal de segurança que transforma as detecções da Sprint 3 em uma representação visual de risco (Heatmap) baseada na distância e vetor de movimento.

## Objetivos
- Calcular níveis de risco dinâmicos via vetores de movimento.
- Implementar Shader de Heatmap para visualização espacial.
- Gerar alertas preventivos baseados no tempo para impacto (Time-to-Collision).

## Ferramentas e Pacotes
- **Shader Graph**: Para criar o efeito visual de mapa de calor em transparência.
- **Physics Raycasting**: Para verificação de oclusão e profundidade.
- **VFX Graph**: Opcional para efeitos de faísca/alerta em áreas críticas.

## Scripts a Implementar
1. `RiskCalculator.cs`: Calcula a "Intensidade de Risco" (f(D, V, Dir)) para cada objeto detectado.
2. `HeatmapGenerator.cs`: Gerencia uma malha ou sistema de pontos que alimenta o shader de risco.
3. `CollisionPredictor.cs`: Projeta os vetores de movimento do usuário e dos objetos para detectar interseções futuras no tempo.
4. `CollisionHeatmap.shader`: Shader que renderiza gradientes (Verde -> Amarelo -> Vermelho) no chão ou sobreposto ao vídeo.

## Tarefas
- [ ] Implementar a fórmula matemática de Risco Euclidiana 3D.
- [ ] Criar mesh dinâmica que acompanha o piso detectado pelo ARCore.
- [ ] Desenvolver o shader de gradiente de calor sensível à proximidade.
- [ ] Integrar feedback háptico e sonoro no Android para alertas de nível "Vermelho".
