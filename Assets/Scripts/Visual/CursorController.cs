using UnityEngine;

public class CursorController : MonoBehaviour
{
    //const float SCALE = 1f;
    [Header("References")]
    public Camera mainCamera;

    [Header("Input")]
    public bool useMouse = true;

    [Header("Workspace in metres")]
    public float minX = -0.4f;
    public float maxX = 0.4f;

    public float minY = -0.0f;
    public float maxY = 0.0f;

    private Vector2 position;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        position = transform.position;

        Debug.Log(
            "CursorController started. Initial position = " +
            position
        );
        Debug.Log("Workspace X = [" + minX + ", " + maxX + "]");
    }

    void Update()
    {
        if (useMouse)
        {
            UpdateFromMouse();
        }
    }

    void UpdateFromMouse()
    {
        // Get mouse position in screen pixels
        Vector3 mouseScreenPosition = Input.mousePosition;

        // IMPORTANT:
        // Give ScreenToWorldPoint the correct distance
        // from the camera to the z=0 plane.
        mouseScreenPosition.z =
            -mainCamera.transform.position.z;

        Vector3 worldPosition =
            mainCamera.ScreenToWorldPoint(
                mouseScreenPosition
            );

        position = new Vector2(
            worldPosition.x,
            worldPosition.y
        );

        // Keep cursor inside experimental workspace
        position.x = Mathf.Clamp(
            position.x,
            minX,
            maxX
        );

        position.y = Mathf.Clamp(
            position.y,
            minY,
            maxY
        );

        transform.position =
            new Vector3(
                position.x,
                position.y,
                0f
            );

        //Debug.Log(
        //    "Mouse world position = " +
        //    worldPosition
        //);
    }

    public Vector2 GetPosition()
    {
        return position;
    }

    public void SetPosition(Vector2 newPosition)
    {
        position = newPosition;

        transform.position =
            new Vector3(
                position.x,
                position.y,
                0f
            );
    }

    public float DistanceFrom(Vector2 target)
    {
        return Vector2.Distance(
            position,
            target
        );
    }
}