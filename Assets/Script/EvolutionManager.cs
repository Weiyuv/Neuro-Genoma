
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

    private Genome testGenome;
    private Genome bestGenome;

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

    private void Update()
    {
        Time.timeScale = Mathf.Max(0.01f, simulationSpeed);
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
        if (ships.Count == 0 || !HasActiveShips())
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
    // RELATORIO
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
    // GENOMA SALVO
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
        if (!testMode)
            return;

        testMode = false;
        testGenome = null;

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
    }
}
