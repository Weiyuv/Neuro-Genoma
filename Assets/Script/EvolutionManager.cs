
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

public sealed class EvolutionManager : MonoBehaviour
{
    [Header("Cena de Treino")]
    [SerializeField] private EvolvingShip shipPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform goal;

    [Header("Cenarios de Validacao")]
    [SerializeField] private Transform[] validationGoals = new Transform[3];

    [Header("NavMesh")]
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 3f;
    [SerializeField] private bool drawReferencePath = true;

    [Header("Evolucao")]
    [SerializeField, Min(4)] private int populationSize = 30;
    [SerializeField, Min(1)] private int eliteCount = 3;
    [SerializeField, Range(0f, 1f)] private float mutationRate = 0.08f;
    [SerializeField, Min(0f)] private float mutationStrength = 0.25f;
    [SerializeField, Min(1f)] private float episodeSeconds = 15f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Velocidade")]
    [SerializeField, Min(1f)] private float simulationSpeed = 3f;

    [Header("Fitness")]
    [SerializeField] private float progressWeight = 10f;
    [SerializeField] private float goalBonus = 100f;
    [SerializeField] private float collisionPenalty = -50f;
    [SerializeField] private float timePenalty = 1f;

    private readonly List<EvolvingShip> ships = new();
    private readonly List<Genome> population = new();
    private readonly List<Vector3> referenceCorners = new();

    private System.Random random;

    private float elapsed;
    private int finishedCount;

    private bool testMode;
    private bool validationMode;

    private Genome testGenome;
    private Genome bestGenome;

    private int validationIndex;
    private float[] validationWeights;
    private readonly float[] validationDistances = new float[3];

    private readonly List<ValidationResult> validationResults = new();

    private class ValidationResult
    {
        public string Scenario;
        public bool ReachedGoal;
        public bool Collided;
        public float Time;
        public float Distance;
        public float Efficiency;
    }

    public int Generation { get; private set; }

    public float BestFitness { get; private set; }

    public float AverageFitness { get; private set; }

    public float ProgressWeight => progressWeight;
    public float GoalBonus => goalBonus;
    public float CollisionPenalty => collisionPenalty;
    public float TimePenalty => timePenalty;

    public int PopulationSize => populationSize;
    public float MutationRate => mutationRate;
    public float MutationStrength => mutationStrength;
    public int Seed => randomSeed;

    public bool IsTestingSavedGenome => testMode;
    public bool IsValidating => validationMode;

    public float ReferenceDistance { get; private set; }
    public bool HasReferencePath { get; private set; }

    private void Start()
    {
        if (shipPrefab == null || spawnPoint == null || goal == null)
        {
            Debug.LogError(
                "[Evolution] Configure Ship Prefab, Spawn Point e Goal."
            );

            enabled = false;
            return;
        }

        random = new System.Random(randomSeed);

        CalculateTrainingPath();

        for (int i = 0; i < populationSize; i++)
        {
            population.Add(
                new Genome(NeuralNetwork.WeightCount, random)
            );
        }

        BeginGeneration();

        Debug.Log(
            $"[Evolution] Treino iniciado. Seed: {randomSeed} | População: {populationSize}"
        );
    }

    private void CalculateTrainingPath()
    {
        referenceCorners.Clear();

        ReferenceDistance = 0f;
        HasReferencePath = false;

        if (!TryCalculatePath(
            spawnPoint.position,
            goal.position,
            out NavMeshPath path,
            out float distance))
        {
            Debug.LogError(
                "[NavMesh] Não foi possível calcular a rota do cenário de treino."
            );

            return;
        }

        referenceCorners.AddRange(path.corners);

        ReferenceDistance = distance;
        HasReferencePath = distance > 0f;

        Debug.Log(
            $"[NavMesh] Rota de treino: {ReferenceDistance:F2}m"
        );
    }

    private bool TryCalculatePath(
        Vector3 startPosition,
        Vector3 endPosition,
        out NavMeshPath path,
        out float distance)
    {
        path = new NavMeshPath();
        distance = 0f;

        bool startFound = NavMesh.SamplePosition(
            startPosition,
            out NavMeshHit startHit,
            navMeshSampleDistance,
            NavMesh.AllAreas
        );

        bool endFound = NavMesh.SamplePosition(
            endPosition,
            out NavMeshHit endHit,
            navMeshSampleDistance,
            NavMesh.AllAreas
        );

        if (!startFound || !endFound)
            return false;

        bool calculated = NavMesh.CalculatePath(
            startHit.position,
            endHit.position,
            NavMesh.AllAreas,
            path
        );

        if (!calculated ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            distance += Vector3.Distance(
                corners[i - 1],
                corners[i]
            );
        }

        return distance > 0f;
    }

    private void FixedUpdate()
    {
        if (ships.Count == 0)
            return;

        if (!HasActiveShips())
            return;

        elapsed += Time.fixedDeltaTime * simulationSpeed;

        if (elapsed >= episodeSeconds)
        {
            for (int i = 0; i < ships.Count; i++)
            {
                if (ships[i].gameObject.activeSelf && ships[i].IsActive)
                    ships[i].Timeout();
            }
        }
    }

    private bool HasActiveShips()
    {
        for (int i = 0; i < ships.Count; i++)
        {
            if (ships[i].gameObject.activeSelf && ships[i].IsActive)
                return true;
        }

        return false;
    }

    private void BeginGeneration()
    {
        validationMode = false;
        testMode = false;
        testGenome = null;

        elapsed = 0f;
        finishedCount = 0;

        while (ships.Count < population.Count)
        {
            ships.Add(
                Instantiate(
                    shipPrefab,
                    spawnPoint.position,
                    spawnPoint.rotation,
                    transform
                )
            );
        }

        for (int i = 0; i < population.Count; i++)
        {
            ships[i].gameObject.SetActive(true);

            ships[i].Begin(
                this,
                population[i],
                goal,
                spawnPoint.position,
                spawnPoint.rotation
            );
        }

        for (int i = population.Count; i < ships.Count; i++)
        {
            ships[i].gameObject.SetActive(false);
        }
    }

    public void ReportFinished(EvolvingShip ship)
    {
        if (validationMode)
        {
            OnValidationShipFinished(ship);
            return;
        }

        if (testMode)
        {
            if (AllShipsFinished())
            {
                PrintEpisodeReport("TESTE DO GENOMA SALVO");

                Debug.Log(
                    "[Evolution] Teste concluído. A evolução não foi executada."
                );
            }

            return;
        }

        finishedCount++;

        if (finishedCount >= population.Count)
            Evolve();
    }

    private bool AllShipsFinished()
    {
        for (int i = 0; i < ships.Count; i++)
        {
            if (ships[i].gameObject.activeSelf && ships[i].IsActive)
                return false;
        }

        return true;
    }

    // =========================================================
    // EVOLUCAO
    // =========================================================

    private void Evolve()
    {
        for (int i = 0; i < population.Count; i++)
            population[i].Fitness = ships[i].Fitness;

        population.Sort(
            (a, b) => b.Fitness.CompareTo(a.Fitness)
        );

        BestFitness = population[0].Fitness;

        float totalFitness = 0f;

        for (int i = 0; i < population.Count; i++)
            totalFitness += population[i].Fitness;

        AverageFitness = totalFitness / population.Count;

        if (bestGenome == null || BestFitness > bestGenome.Fitness)
            bestGenome = population[0].Clone();

        PrintEpisodeReport($"GERAÇÃO {Generation}");

        List<Genome> next = new List<Genome>(populationSize);

        int protectedCount = Mathf.Min(
            eliteCount,
            population.Count
        );

        for (int i = 0; i < protectedCount; i++)
            next.Add(population[i].Clone());

        while (next.Count < populationSize)
        {
            Genome parentA = TournamentPick();
            Genome parentB = TournamentPick();

            Genome child = Genome.Crossover(
                parentA,
                parentB,
                random
            );

            child.Mutate(
                random,
                mutationRate,
                mutationStrength
            );

            next.Add(child);
        }

        population.Clear();
        population.AddRange(next);

        Generation++;

        BeginGeneration();
    }

    private Genome TournamentPick()
    {
        const int tournamentSize = 3;

        Genome best = population[random.Next(population.Count)];

        for (int i = 1; i < tournamentSize; i++)
        {
            Genome contender = population[random.Next(population.Count)];

            if (contender.Fitness > best.Fitness)
                best = contender;
        }

        return best;
    }

    // =========================================================
    // RELATORIO DO TREINO
    // =========================================================

    private void PrintEpisodeReport(string title)
    {
        StringBuilder report = new StringBuilder();

        int reachedCount = 0;
        int collisionCount = 0;
        int validEfficiencyCount = 0;

        float totalTime = 0f;
        float totalDistance = 0f;
        float totalEfficiency = 0f;
        float totalFitness = 0f;

        for (int i = 0; i < populationSize; i++)
        {
            EvolvingShip ship = ships[i];

            if (ship.ReachedGoal)
                reachedCount++;

            if (ship.Collided)
                collisionCount++;

            totalTime += ship.EpisodeTime;
            totalDistance += ship.RealDistance;
            totalFitness += ship.Fitness;

            if (ship.ReachedGoal &&
                ship.RealDistance > 0f &&
                HasReferencePath)
            {
                totalEfficiency +=
                    ReferenceDistance / ship.RealDistance;

                validEfficiencyCount++;
            }
        }

        report.AppendLine("");
        report.AppendLine("==============================================");
        report.AppendLine($"          RELATÓRIO - {title}");
        report.AppendLine("==============================================");

        report.AppendLine("                 MÉDIAS GERAIS");
        report.AppendLine("----------------------------------------------");

        report.AppendLine($"Seed: {randomSeed}");
        report.AppendLine($"Geração: {Generation}");
        report.AppendLine($"Ships: {populationSize}");

        report.AppendLine(
            $"Chegaram ao alvo: {reachedCount}/{populationSize}"
        );

        report.AppendLine(
            $"Colidiram: {collisionCount}/{populationSize}"
        );

        report.AppendLine(
            $"Tempo médio: {totalTime / populationSize:F2}s"
        );

        report.AppendLine(
            $"Distância real média: {totalDistance / populationSize:F2}m"
        );

        report.AppendLine(
            $"Distância de referência: " +
            (HasReferencePath ? $"{ReferenceDistance:F2}m" : "INDISPONÍVEL")
        );

        report.AppendLine(
            validEfficiencyCount > 0
                ? $"Eficiência média: {totalEfficiency / validEfficiencyCount:P2}"
                : "Eficiência média: INDISPONÍVEL"
        );

        report.AppendLine(
            $"Fitness médio: {totalFitness / populationSize:F2}"
        );

        report.AppendLine($"Melhor fitness: {BestFitness:F2}");

        report.AppendLine("----------------------------------------------");
        report.AppendLine("              RESULTADOS INDIVIDUAIS");
        report.AppendLine("----------------------------------------------");

        for (int i = 0; i < populationSize; i++)
        {
            EvolvingShip ship = ships[i];

            string efficiency = "INVÁLIDA";

            if (ship.ReachedGoal &&
                ship.RealDistance > 0f &&
                HasReferencePath)
            {
                efficiency =
                    $"{ReferenceDistance / ship.RealDistance:P2}";
            }

            report.AppendLine(
                $"Ship {i + 1:00} | " +
                $"Chegou: {ship.ReachedGoal} | " +
                $"Colidiu: {ship.Collided} | " +
                $"Tempo: {ship.EpisodeTime:F2}s | " +
                $"Distância: {ship.RealDistance:F2}m | " +
                $"Eficiência: {efficiency} | " +
                $"Fitness: {ship.Fitness:F2}"
            );
        }

        report.AppendLine("==============================================");

        Debug.Log(report.ToString());
    }

    // =========================================================
    // VALIDACAO DOS TRES CENARIOS
    // =========================================================

    public void StartThreeScenarioValidation()
    {
        if (validationMode)
        {
            Debug.LogWarning("[Validation] A validação já está em andamento.");
            return;
        }

        if (bestGenome == null)
        {
            Debug.LogError(
                "[Validation] Ainda não existe um melhor genoma de uma geração concluída. " +
                "Deixe a evolução terminar pelo menos uma geração."
            );

            return;
        }

        if (validationGoals == null || validationGoals.Length != 3)
        {
            Debug.LogError(
                "[Validation] Configure exatamente três Goals no Inspector."
            );

            return;
        }

        for (int i = 0; i < 3; i++)
        {
            if (validationGoals[i] == null)
            {
                Debug.LogError(
                    $"[Validation] Goal do cenário {i + 1} não foi configurado."
                );

                return;
            }

            if (!TryCalculatePath(
                spawnPoint.position,
                validationGoals[i].position,
                out NavMeshPath path,
                out float distance))
            {
                Debug.LogError(
                    $"[Validation] Cenário {i + 1} não possui rota completa na NavMesh. " +
                    "Validação cancelada."
                );

                return;
            }

            validationDistances[i] = distance;
        }

        // Copia os pesos uma única vez.
        // Todos os cenários usarão exatamente este mesmo genoma.
        validationWeights = (float[])bestGenome.Weights.Clone();

        validationResults.Clear();

        validationIndex = 0;
        validationMode = true;
        testMode = false;

        elapsed = 0f;

        // Garante que existe pelo menos um Ship.
        if (ships.Count == 0)
        {
            ships.Add(
                Instantiate(
                    shipPrefab,
                    spawnPoint.position,
                    spawnPoint.rotation,
                    transform
                )
            );
        }

        // Desativa todos os Ships, exceto o primeiro.
        for (int i = 1; i < ships.Count; i++)
            ships[i].gameObject.SetActive(false);

        Debug.Log(
            "[Validation] Iniciando validação com o melhor genoma da geração " +
            Generation + "."
        );

        BeginValidationScenario();
    }

    private void BeginValidationScenario()
    {
        if (!validationMode || validationIndex >= 3)
            return;

        EvolvingShip ship = ships[0];

        Transform scenarioGoal = validationGoals[validationIndex];

        ReferenceDistance = validationDistances[validationIndex];
        HasReferencePath = ReferenceDistance > 0f;

        elapsed = 0f;

        Genome validationGenome = new Genome(
            NeuralNetwork.WeightCount,
            random
        );

        Array.Copy(
            validationWeights,
            validationGenome.Weights,
            validationWeights.Length
        );

        ship.gameObject.SetActive(true);

        ship.Begin(
            this,
            validationGenome,
            scenarioGoal,
            spawnPoint.position,
            spawnPoint.rotation
        );

        Debug.Log(
            $"[Validation] Cenário {validationIndex + 1}/3 iniciado. " +
            $"Distância de referência: {ReferenceDistance:F2}m"
        );
    }

    private void OnValidationShipFinished(EvolvingShip ship)
    {
        ValidationResult result = new ValidationResult();

        result.Scenario = $"Cenário {validationIndex + 1}";

        result.ReachedGoal = ship.ReachedGoal;
        result.Collided = ship.Collided;
        result.Time = ship.EpisodeTime;
        result.Distance = ship.RealDistance;
        result.Efficiency = ship.Efficiency;

        validationResults.Add(result);

        validationIndex++;

        if (validationIndex < 3)
        {
            BeginValidationScenario();
        }
        else
        {
            FinishThreeScenarioValidation();
        }
    }

    private void FinishThreeScenarioValidation()
    {
        validationMode = false;

        PrintValidationTable();

        ships[0].gameObject.SetActive(false);

        validationWeights = null;
    }

    private void PrintValidationTable()
    {
        StringBuilder report = new StringBuilder();

        int successfulScenarios = 0;

        report.AppendLine("");
        report.AppendLine("==============================================================");
        report.AppendLine("             RESULTADO DA VALIDACAO");
        report.AppendLine("==============================================================");

        report.AppendLine(
            $"Genoma utilizado: Melhor genoma da geração {Generation}"
        );

        report.AppendLine(
            $"Fitness do melhor genoma: {bestGenome.Fitness:F2}"
        );

        report.AppendLine(
            $"Seed do treinamento: {randomSeed}"
        );

        report.AppendLine("");

        report.AppendLine(
            "CENARIO | CHEGOU | COLISAO | TEMPO | DISTANCIA | EFICIENCIA"
        );

        report.AppendLine(
            "--------------------------------------------------------------"
        );

        for (int i = 0; i < validationResults.Count; i++)
        {
            ValidationResult result = validationResults[i];

            bool success =
                result.ReachedGoal && !result.Collided;

            if (success)
                successfulScenarios++;

            string efficiency =
                result.Efficiency >= 0f
                    ? result.Efficiency.ToString("P2")
                    : "INVALIDA";

            report.AppendLine(
                $"{result.Scenario,-9} | " +
                $"{(result.ReachedGoal ? "SIM" : "NAO"),-6} | " +
                $"{(result.Collided ? "SIM" : "NAO"),-7} | " +
                $"{result.Time,5:F2}s | " +
                $"{result.Distance,8:F2}m | " +
                $"{efficiency}"
            );
        }

        report.AppendLine(
            "--------------------------------------------------------------"
        );

        report.AppendLine(
            $"Cenários concluídos sem colisão: {successfulScenarios}/3"
        );

        report.AppendLine("");

        if (successfulScenarios >= 2)
        {
            report.AppendLine(
                "RESULTADO FINAL: APROVADO"
            );

            report.AppendLine(
                "O melhor genoma alcançou o alvo sem colisão em pelo menos 2 dos 3 cenários."
            );
        }
        else
        {
            report.AppendLine(
                "RESULTADO FINAL: REPROVADO"
            );

            report.AppendLine(
                "O melhor genoma não alcançou o alvo sem colisão em pelo menos 2 dos 3 cenários."
            );
        }

        report.AppendLine("==============================================================");

        Debug.Log(report.ToString());
    }

    // =========================================================
    // SALVAR E TESTAR GENOMA
    // =========================================================

    public float[] GetBestWeights()
    {
        if (bestGenome != null)
            return (float[])bestGenome.Weights.Clone();

        if (population.Count == 0)
            return null;

        Genome best = population[0];

        for (int i = 1; i < population.Count; i++)
        {
            if (population[i].Fitness > best.Fitness)
                best = population[i];
        }

        return (float[])best.Weights.Clone();
    }

    public float GetBestFitness()
    {
        return bestGenome != null
            ? bestGenome.Fitness
            : BestFitness;
    }

    public void LoadAndTest(float[] weights)
    {
        if (weights == null ||
            weights.Length != NeuralNetwork.WeightCount)
        {
            Debug.LogError("[Evolution] Genoma incompatível.");
            return;
        }

        validationMode = false;

        testGenome = new Genome(
            NeuralNetwork.WeightCount,
            random
        );

        Array.Copy(
            weights,
            testGenome.Weights,
            weights.Length
        );

        testMode = true;

        elapsed = 0f;
        finishedCount = 0;

        while (ships.Count < populationSize)
        {
            ships.Add(
                Instantiate(
                    shipPrefab,
                    spawnPoint.position,
                    spawnPoint.rotation,
                    transform
                )
            );
        }

        ReferenceDistance = 0f;
        HasReferencePath = false;

        if (TryCalculatePath(
            spawnPoint.position,
            goal.position,
            out NavMeshPath path,
            out float distance))
        {
            ReferenceDistance = distance;
            HasReferencePath = true;
        }

        for (int i = 0; i < populationSize; i++)
        {
            ships[i].gameObject.SetActive(true);

            ships[i].Begin(
                this,
                testGenome.Clone(),
                goal,
                spawnPoint.position,
                spawnPoint.rotation
            );
        }

        for (int i = populationSize; i < ships.Count; i++)
            ships[i].gameObject.SetActive(false);

        Debug.Log(
            "[Evolution] Teste do genoma salvo iniciado."
        );
    }

    public void ResumeEvolution()
    {
        if (!testMode && !validationMode)
            return;

        testMode = false;
        validationMode = false;
        testGenome = null;
        validationWeights = null;

        Debug.Log("[Evolution] Retomando evolução.");

        BeginGeneration();
    }

    // =========================================================
    // CONFIGURACOES
    // =========================================================

    public void SetSimulationSpeed(float speed)
    {
        simulationSpeed = Mathf.Max(1f, speed);
    }

    public void SetMutation(float rate, float strength)
    {
        mutationRate = Mathf.Clamp01(rate);
        mutationStrength = Mathf.Max(0f, strength);
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmos()
    {
        if (!drawReferencePath || referenceCorners.Count < 2)
            return;

        Gizmos.color = Color.green;

        for (int i = 1; i < referenceCorners.Count; i++)
        {
            Gizmos.DrawLine(
                referenceCorners[i - 1],
                referenceCorners[i]
            );

            Gizmos.DrawSphere(
                referenceCorners[i],
                0.12f
            );
        }

        if (validationGoals != null)
        {
            Gizmos.color = Color.yellow;

            for (int i = 0; i < validationGoals.Length; i++)
            {
                if (validationGoals[i] != null)
                {
                    Gizmos.DrawWireSphere(
                        validationGoals[i].position,
                        0.5f
                    );
                }
            }
        }
    }
}
