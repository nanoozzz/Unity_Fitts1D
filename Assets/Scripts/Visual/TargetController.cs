using UnityEngine;

public class TargetController : MonoBehaviour
{
    [Header("References")]
    public SpriteRenderer spriteRenderer;
    public CircleCollider2D targetCollider;

    private float width;
    private Vector2 targetPosition;
    private Color baseColour;

    void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (targetCollider == null)
            targetCollider = GetComponent<CircleCollider2D>();

        if (spriteRenderer != null)
            baseColour = spriteRenderer.color;

        gameObject.SetActive(false);
    }

    public void SetColour(Color c) 
    { 
        if (spriteRenderer != null) 
            spriteRenderer.color = c; 
    }
    public void ResetColour() 
    { 
        if (spriteRenderer != null) 
            spriteRenderer.color = baseColour; 
    }

    public void SetTarget(Vector2 position, float W)
    {
        targetPosition = position;
        width = W;

        transform.position = new Vector3(
            position.x,
            position.y,
            0f
        );

        /*
         * We explicitly calculate the scale required
         * to make the sprite W metres wide.
         */
        float spriteDiameter =
            spriteRenderer.sprite.bounds.size.x;
        Debug.Log("Sprite Diameter " + spriteDiameter);

        float scale =
            W / spriteDiameter;
        Debug.Log("scale: " + scale);

        transform.localScale = new Vector3(
            scale,
            scale,
            1f
        );

        /*
         * The CircleCollider2D is also explicitly
         * configured to have W diameter.
         *
         * CircleCollider2D radius is half the diameter.
         */
        targetCollider.radius = 0.5f * spriteDiameter;

        targetCollider.offset = Vector2.zero;

        gameObject.SetActive(true);

        Debug.Log(
            "TARGET SET" +
            "\nPosition = " + targetPosition +
            "\nW = " + W +
            "\nSprite native diameter = " +
                spriteDiameter +
            "\nScale = " + scale +
            "\nCollider diameter = " + W
        );
    }

    public bool IsCursorInside(Vector2 cursorPosition)
    {
        /*
         * Use the actual world-space collider.
         */
        return targetCollider.OverlapPoint(cursorPosition);
    }

    public Vector2 GetPosition()
    {
        return targetPosition;
    }

    public float GetWidth()
    {
        return width;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }
}