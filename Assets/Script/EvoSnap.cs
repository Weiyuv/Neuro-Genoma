
using System;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class EvolutionSnapshot
{
    public int version = 1;
    public int seed;
    public int generation;
    public float bestFitness;
    public float[] weights;
    public int populationSize;
    public float mutationRate;
    public float mutationStrength;
    public string saveDate;
}

public sealed class EvoSnap : MonoBehaviour
{
    private const string SaveKey = "neuroevolution.best.v1";

    [Header("Referências")]
    [SerializeField] private EvolutionManager evolutionManager;

    [Header("Interface")]
    [SerializeField] private Text statusText;

    public void SalvarMelhor()
    {
        if (evolutionManager == null)
        {
            Mostrar("EvolutionManager não encontrado.");
            return;
        }

        float[] weights = evolutionManager.GetBestWeights();

        if (weights == null || weights.Length != NeuralNetwork.WeightCount)
        {
            Mostrar("Não existe um genoma válido para salvar.");
            return;
        }

        EvolutionSnapshot snapshot = new EvolutionSnapshot
        {
            version = 1,
            seed = evolutionManager.Seed,
            generation = evolutionManager.Generation,
            bestFitness = evolutionManager.GetBestFitness(),
            weights = weights,
            populationSize = evolutionManager.PopulationSize,
            mutationRate = evolutionManager.MutationRate,
            mutationStrength = evolutionManager.MutationStrength,
            saveDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
        };

        string json = JsonUtility.ToJson(snapshot);

        PlayerPrefs.SetString(SaveKey, json);
        PlayerPrefs.Save();

        Mostrar("Melhor genoma salvo!");
    }

    public void CarregarMelhor()
    {
        if (evolutionManager == null)
        {
            Mostrar("EvolutionManager não encontrado.");
            return;
        }

        if (!PlayerPrefs.HasKey(SaveKey))
        {
            Mostrar("Nenhum save encontrado.");
            return;
        }

        EvolutionSnapshot snapshot;

        try
        {
            snapshot = JsonUtility.FromJson<EvolutionSnapshot>(
                PlayerPrefs.GetString(SaveKey)
            );
        }
        catch (Exception)
        {
            Mostrar("Save corrompido.");
            return;
        }

        if (snapshot == null || snapshot.weights == null)
        {
            Mostrar("Save inválido.");
            return;
        }

        if (snapshot.version != 1)
        {
            Mostrar("Versão incompatível.");
            return;
        }

        if (snapshot.weights.Length != NeuralNetwork.WeightCount)
        {
            Mostrar("Quantidade de pesos incompatível.");
            return;
        }

        evolutionManager.LoadAndTest(snapshot.weights);

        Mostrar(
            "Genoma carregado! Geração salva: " + snapshot.generation
        );
    }

    public void ApagarSave()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
        {
            Mostrar("Nenhum save para apagar.");
            return;
        }

        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();

        Mostrar("Save apagado com sucesso!");
    }

    public void EvoluirNovamente()
    {
        if (evolutionManager == null)
        {
            Mostrar("EvolutionManager não encontrado.");
            return;
        }

        evolutionManager.ResumeEvolution();

        Mostrar("Evolução retomada.");
    }

    private void Mostrar(string mensagem)
    {
        Debug.Log("[EvoSnap] " + mensagem);

        if (statusText != null)
            statusText.text = mensagem;
    }
}
