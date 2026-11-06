## Análise dos resultados

**1. O melhor fitness correspondeu à rota mais eficiente?**

Não. Os resultados mostram que um fitness maior não representa necessariamente uma rota mais eficiente:

- **Resultado A:** Fitness médio de 315,34 e eficiência média de 104,03%.
- **Resultado C:** Fitness médio de 305,19 e eficiência média de 107,20%.

Mesmo com um fitness menor, o Resultado B apresentou uma eficiência superior. Isso demonstra que a pontuação de fitness não está diretamente relacionada à eficiência da rota.

**2. Em qual cenário de validação o agente teve pior desempenho? Por quê?**

O cenário B apresentou o pior desempenho, pois teve a menor taxa de eficiência. Isso aconteceu devido à presença de várias curvas, que exigiram mais correções de direção e fizeram o agente percorrer uma distância maior.

**3. Qual alteração no fitness poderia induzir um comportamento indesejado?**

Aumentar excessivamente a recompensa por progresso ou distância percorrida poderia incentivar o agente a realizar movimentos repetitivos ou percorrer caminhos longos para acumular pontos, em vez de chegar ao objetivo pelo caminho mais eficiente.

**4. O genoma salvo reproduziu o comportamento esperado após fechar e reabrir a cena?**

Sim. Após fechar e reabrir a cena, o genoma salvo foi carregado e reproduziu o comportamento esperado, demonstrando que a persistência dos dados funcionou corretamente.

Assets/Scenes/SampleScene
