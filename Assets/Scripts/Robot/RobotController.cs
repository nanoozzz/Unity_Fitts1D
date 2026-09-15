using UnityEngine;

public class RobotController : MonoBehaviour
{
    public CursorController cursor;

    public Vector2 CurrentPosition
    {
        get
        {
            if (cursor != null)
                return cursor.GetPosition();

            return Vector2.zero;
        }
    }

    public void MoveToOrigin(Vector2 origin)
    {
        if (cursor != null)
        {
            cursor.SetPosition(origin);
        }
    }

    public void MoveToPosition(Vector2 position)
    {
        if (cursor != null)
        {
            cursor.SetPosition(position);
        }
    }

    public void MoveToPosition3()
    {
        // Placeholder for the actual robot command.

        Debug.Log(
            "Robot: Move automatically to position 3"
        );
    }
}