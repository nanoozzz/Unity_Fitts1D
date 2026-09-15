using System;
using System.IO;
using UnityEngine;

public class CSVLogger : MonoBehaviour
{
    private string filePath;

    public void StartNewFile()
    {
        string timestamp =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss"
            );

        filePath =
            Path.Combine(
                Application.persistentDataPath,
                "Fitts_" + timestamp + ".csv"
            );

        string header =
            "timestamp," +
            "block," +
            "round," +
            "trial," +
            "group," +
            "trial_in_group," +
            "A," +
            "W," +
            "ID," +
            "target_x," +
            "target_y," +
            "start_x," +
            "start_y," +
            "end_x," +
            "end_y," +
            "movement_time," +
            "error\n";

        File.WriteAllText(
            filePath,
            header
        );

        Debug.Log(
            "Logging data to: " + filePath
        );
    }

    public void LogTrial(
        string block,
        int round,
        Trial trial,
        Vector2 targetPosition,
        Vector2 startPosition,
        Vector2 endPosition,
        float movementTime,
        bool error)
    {
        string line =
            DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss.fff"
            ) + "," +

            block + "," +

            round + "," +

            trial.index + "," +

            trial.group + "," +

            trial.trialInGroup + "," +

            trial.A.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            trial.W.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            trial.ID.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            targetPosition.x.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            targetPosition.y.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            startPosition.x.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            startPosition.y.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            endPosition.x.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            endPosition.y.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            movementTime.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ) + "," +

            error + "\n";

        File.AppendAllText(
            filePath,
            line
        );
    }

    public string GetFilePath()
    {
        return filePath;
    }
}