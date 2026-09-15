using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Globalization;



public class TrialSequence : MonoBehaviour
{
    const int COULUMN_PER_CSV_FILE = 4;
    const int TRIALS_PER_BLOCK = 45;
    public List<Trial> trials = new List<Trial>();

    public void LoadCSV(TextAsset csvFile)
    {
        trials.Clear();

        if (csvFile == null)
        {
            Debug.LogError("No .csv file assigned.");
            return;
        }

        string[] lines = csvFile.text.Split("\n");

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            string[] values = line.Split(",");

            if (values.Length < COULUMN_PER_CSV_FILE)
                continue;

            int index = int.Parse(values[0]);
            int group = 1 + (index - 1) / TRIALS_PER_BLOCK;
            int trialInGroup = 1 + (index - 1) % TRIALS_PER_BLOCK;

            float A = float.Parse(values[1], CultureInfo.InvariantCulture);
            float W = float.Parse(values[2], CultureInfo.InvariantCulture);
            float ID = float.Parse(values[3], CultureInfo.InvariantCulture);

            Trial trial = new Trial(index, group, trialInGroup, A, W, ID);

            trials.Add(trial);
        }

        Debug.Log("Loaded " + trials.Count + " trials.");
    }

    public Trial GetTrial(int index)
    {
        if (index < 0 || index >= trials.Count)
            return null;

        return trials[index];
    }

    public int Count()
    {
        return trials.Count;
    }
}
