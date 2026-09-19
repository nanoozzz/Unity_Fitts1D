using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Second, experimenter-only screen.
///
///   Display 1 (participant):  task and UI exactly as before, minus TrialText and TimerText.
///   Display 2 (experimenter): the same task and UI, WITH TrialText and TimerText.
///
/// No other script changes. UIController keeps writing TrialText / TimerText as before; this component
///   1. renders the task on the experimenter display with a camera that copies the participant camera
///      every frame (so it follows whatever TaskCameraFramer does),
///   2. clones the participant Canvas onto the experimenter display (its scripts stripped) and copies
///      texts, colours and visibility from the original every frame,
///   3. hides TrialText and TimerText on the participant display only.
///
/// Setup: add this component to the Canvas (or any object) of the FittsExperiment scene.
/// The participant's monitor must be the OS primary monitor (Unity's Display 1).
/// In the Editor, open a second Game view and set it to "Display 2" to see the experimenter view.
/// Disable or remove the component to return to a single screen.
/// </summary>
public class ExperimenterDisplay : MonoBehaviour
{
    [Tooltip("Participant camera. Empty: Camera.main.")]
    public Camera participantCamera;

    [Tooltip("Participant UI. Empty: the UIController found in the scene.")]
    public UIController ui;

    [Tooltip("Unity display index of the experimenter screen: 0 = Display 1, 1 = Display 2.")]
    public int experimenterDisplay = 1;

    [Tooltip("Widen the experimenter view when its monitor is narrower than the participant's, so no target is cut off.")]
    public bool keepFullWidth = true;

    private Canvas participantCanvas;
    private Camera expCamera;
    private GameObject expCanvasObject;
    private Transform[] srcNodes, dstNodes;
    private Graphic[] srcGraphics, dstGraphics;
    private TMP_Text[] srcTexts, dstTexts;
    private readonly List<Graphic> experimenterOnly = new List<Graphic>();
    private bool ready;

    void Start()
    {
        if (ui == null) ui = FindAnyObjectByType<UIController>();
        if (participantCamera == null) participantCamera = Camera.main;
        participantCanvas = (ui != null) ? ui.GetComponentInParent<Canvas>() : null;
        if (participantCamera == null || participantCanvas == null)
        {
            Debug.LogError("[ExperimenterDisplay] Participant camera or UIController canvas not found: disabled.");
            enabled = false;
            return;
        }

        // Open the experimenter monitor (standalone player; the Editor shows it in a "Display 2" Game view)
        if (experimenterDisplay < Display.displays.Length)
        {
            if (!Display.displays[experimenterDisplay].active) Display.displays[experimenterDisplay].Activate();
        }
        else if (!Application.isEditor)
        {
            Debug.LogWarning($"[ExperimenterDisplay] {Display.displays.Length} display(s) detected: there is no " +
                             $"Display {experimenterDisplay + 1} for the experimenter view.");
        }

        // 1. Task: a second camera that follows the participant camera
        expCamera = new GameObject("Experimenter Camera").AddComponent<Camera>();
        FollowParticipantCamera();

        // 2. UI: script-free copy of the participant canvas, drawn on the experimenter display
        expCanvasObject = CloneWithoutScripts(participantCanvas.gameObject);
        expCanvasObject.name = participantCanvas.gameObject.name + " (experimenter)";
        expCanvasObject.GetComponent<Canvas>().targetDisplay = experimenterDisplay;

        srcNodes = participantCanvas.GetComponentsInChildren<Transform>(true);
        dstNodes = expCanvasObject.GetComponentsInChildren<Transform>(true);
        srcGraphics = participantCanvas.GetComponentsInChildren<Graphic>(true);
        dstGraphics = expCanvasObject.GetComponentsInChildren<Graphic>(true);
        srcTexts = participantCanvas.GetComponentsInChildren<TMP_Text>(true);
        dstTexts = expCanvasObject.GetComponentsInChildren<TMP_Text>(true);
        if (srcNodes.Length != dstNodes.Length || srcGraphics.Length != dstGraphics.Length ||
            srcTexts.Length != dstTexts.Length)
        {
            Debug.LogError("[ExperimenterDisplay] The canvas copy does not match the original: disabled.");
            Destroy(expCanvasObject);
            Destroy(expCamera.gameObject);
            enabled = false;
            return;
        }

        // 3. Trial number and time: experimenter screen only
        foreach (TMP_Text t in new[] { ui.trialText, ui.timerText })
        {
            if (t != null) experimenterOnly.Add(t);
            else Debug.LogWarning("[ExperimenterDisplay] UIController.trialText or timerText is not assigned.");
        }

        ready = true;
        ApplySplit(true);
    }

    // After RemoteExperimentController / UIController have updated the participant UI in Update()
    void LateUpdate()
    {
        if (!ready) return;
        FollowParticipantCamera();

        for (int i = 0; i < srcNodes.Length; i++)
        {
            if (srcNodes[i] == null || dstNodes[i] == null) continue;
            bool active = srcNodes[i].gameObject.activeSelf;
            if (dstNodes[i].gameObject.activeSelf != active) dstNodes[i].gameObject.SetActive(active);
        }
        for (int i = 0; i < srcGraphics.Length; i++)
        {
            if (srcGraphics[i] == null || dstGraphics[i] == null) continue;
            bool visible = srcGraphics[i].enabled || experimenterOnly.Contains(srcGraphics[i]);
            if (dstGraphics[i].enabled != visible) dstGraphics[i].enabled = visible;
            if (dstGraphics[i].color != srcGraphics[i].color) dstGraphics[i].color = srcGraphics[i].color;
        }
        for (int i = 0; i < srcTexts.Length; i++)
        {
            if (srcTexts[i] == null || dstTexts[i] == null) continue;
            if (dstTexts[i].text != srcTexts[i].text) dstTexts[i].text = srcTexts[i].text;
        }
    }

    void OnEnable() { if (ready) ApplySplit(true); }
    void OnDisable() { if (ready) ApplySplit(false); }

    void OnDestroy()
    {
        if (!ready) return;
        ApplySplit(false);
        if (expCanvasObject != null) Destroy(expCanvasObject);
        if (expCamera != null) Destroy(expCamera.gameObject);
    }

    // on: two screens (trial number and time hidden from the participant); off: back to one screen
    private void ApplySplit(bool on)
    {
        foreach (Graphic g in experimenterOnly)
            if (g != null) g.enabled = !on;
        if (expCanvasObject != null) expCanvasObject.SetActive(on);
        if (expCamera != null) expCamera.gameObject.SetActive(on);
    }

    private void FollowParticipantCamera()
    {
        expCamera.CopyFrom(participantCamera);      // projection, background, culling mask and transform
        expCamera.targetDisplay = experimenterDisplay;
        expCamera.ResetAspect();                    // aspect of the experimenter display, not the participant's
        if (keepFullWidth && participantCamera.orthographic && expCamera.aspect > 0f &&
            expCamera.aspect < participantCamera.aspect)
            expCamera.orthographicSize = participantCamera.orthographicSize * participantCamera.aspect / expCamera.aspect;
    }

    // Copy of a hierarchy with every project script (UIController, this component, ...) and the raycaster
    // removed. It is created under an inactive parent, so none of the copied scripts runs Awake/OnEnable/Start.
    private static GameObject CloneWithoutScripts(GameObject original)
    {
        var holder = new GameObject("ExperimenterDisplay holder");
        holder.SetActive(false);
        GameObject copy = Instantiate(original, holder.transform, false);
        var projectAssembly = typeof(ExperimenterDisplay).Assembly;
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && (mb.GetType().Assembly == projectAssembly || mb is GraphicRaycaster))
                DestroyImmediate(mb);
        copy.transform.SetParent(null, false);
        Destroy(holder);
        return copy;
    }
}