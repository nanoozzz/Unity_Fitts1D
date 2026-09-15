using UnityEngine;

/// <summary>
/// Draws the cursor. Two sources are supported and they are mutually exclusive:
///
///   Mouse simulation  - the cursor follows the mouse, clamped to the workspace. Development and
///                       piloting only: mouse position is sampled at frame rate and has no force
///                       feedback, so nothing measured in this mode is comparable to robot data.
///   Robot (external)  - the position is pushed in by RemoteExperimentController from the M2
///                       state stream. Update() must not touch it, or the displayed cursor would
///                       drift away from the position CORC is scoring.
/// </summary>
public class CursorController : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;

    [Header("Input")]
    [Tooltip("Mouse simulation. Automatically cleared when the robot drives the cursor.")]
    public bool useMouse = true;

    [Tooltip("Set by RemoteExperimentController: the position comes from the M2 over FLNL.")]
    public bool externalControl = false;

    [Header("Workspace in scene units (mouse simulation only)")]
    public float minX = -0.4f;
    public float maxX = 0.4f;

    public float minY = 0.0f;
    public float maxY = 0.0f;

    private Vector2 position;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        position = transform.position;

        Debug.Log($"[Cursor] Started at {position}. Workspace X = [{minX}, {maxX}]. " +
                  $"Source = {(externalControl ? "robot (FLNL)" : useMouse ? "mouse" : "none")}.");
    }

    void Update()
    {
        // The robot is authoritative when connected: never let the mouse move the cursor as well.
        if (externalControl) return;
        if (useMouse) UpdateFromMouse();
    }

    void UpdateFromMouse()
    {
        Vector3 mouseScreenPosition = Input.mousePosition;

        // Distance from the camera to the z = 0 plane.
        mouseScreenPosition.z = -mainCamera.transform.position.z;

        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(mouseScreenPosition);

        position = new Vector2(worldPosition.x, worldPosition.y);

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.y = Mathf.Clamp(position.y, minY, maxY);

        transform.position = new Vector3(position.x, position.y, 0f);
    }

    public Vector2 GetPosition()
    {
        return position;
    }

    /// <summary>
    /// Set the cursor position directly. In robot mode this is called once per frame with the
    /// transformed M2 end-effector position; no clamping or smoothing is applied, so that what is
    /// displayed is what was measured.
    /// </summary>
    public void SetPosition(Vector2 newPosition)
    {
        position = newPosition;
        transform.position = new Vector3(position.x, position.y, 0f);
    }

    public float DistanceFrom(Vector2 target)
    {
        return Vector2.Distance(position, target);
    }
}
