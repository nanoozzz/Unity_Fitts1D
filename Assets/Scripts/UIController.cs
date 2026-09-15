using TMPro;
using UnityEngine;

public class UIController : MonoBehaviour
{
    [Header("Main UI")]
    public TMP_Text instructionText;
    public TMP_Text stateText;
    public TMP_Text trialText;
    public TMP_Text timerText;

    [Header("Rest UI")]
    public GameObject restPanel;
    public TMP_Text restText;
    public TMP_Text continueText;

    void Start()
    {
        HideRest();
    }

    public void ShowInstruction(string text)
    {
        if (instructionText != null)
            instructionText.text = text;
    }

    public void ShowState(string text)
    {
        if (stateText != null)
            stateText.text = text;
    }

    public void ShowTrial(int current, int total)
    {
        if (trialText != null)
            trialText.text =
                "Trial " + current + " / " + total;
    }

    public void ShowTimer(float time)
    {
        if (timerText != null)
            timerText.text =
                time.ToString("F1") + " s";
    }

    public void ShowRest(string message, float remaining)
    {
        if (restPanel != null)
            restPanel.SetActive(true);

        if (restText != null)
            restText.text =
                message +
                "\n\nRemaining: " +
                remaining.ToString("F0") +
                " s";

        if (continueText != null)
            continueText.text =
                "Press SPACE to continue";
    }

    public void HideRest()
    {
        if (restPanel != null)
            restPanel.SetActive(false);
    }
}