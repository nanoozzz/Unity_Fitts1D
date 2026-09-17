using UnityEngine;
using Fitts.Net;
using Fitts.Experiment;

namespace Fitts.Visual
{
    /// <summary>
    /// Sizes a sprite by its real-world diameter instead of a hand-tuned localScale.
    ///
    /// Drop this on the Cursor and on the Origin marker. It reads the sprite's native size and
    /// computes the scale that makes it render at exactly `diameterMetres` of robot travel:
    ///
    ///     localScale = diameterMetres * displayGain / sprite.bounds.size.x
    ///
    /// Why this matters rather than eyeballing a scale in the inspector. CORC validates a trial on
    /// |x - x_target| &lt;= W/2 evaluated at the HANDLE - a point, with no radius. What the
    /// participant sees is a cursor of some finite width over a target of width W, so their
    /// perceived tolerance is roughly (cursor + target). If the cursor is not small relative to W,
    /// the effective width they are actually aiming at is inflated - and inflated by a *larger
    /// proportion* at small W than at large W. That does not merely shift the Fitts intercept; it
    /// compresses the range of effective ID and bends the slope, which is the parameter the
    /// experiment exists to estimate.
    ///
    /// The scene as shipped had the cursor at localScale 0.01 on Unity's builtin Knob sprite
    /// (native diameter 2.56 units), i.e. a 2.56 cm cursor against target widths of 0.889, 1.0,
    /// 1.333 and 2.0 cm - wider than every target in the design.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class RealSizeSprite : MonoBehaviour
    {
        [Header("Real-world size")]
        [Tooltip("Diameter of this sprite in metres of robot travel. For the cursor, keep it well " +
                 "below the narrowest target width in the design (W_min = 0.889 cm here); 3 mm is a " +
                 "reasonable default. For the origin marker, matching the narrowest target reads well.")]
        public float diameterMetres = 0.003f;

        [Tooltip("Re-apply every frame. Only useful while tuning in the editor - leave off for a session.")]
        public bool continuousUpdate = false;

        [Header("Result (read only)")]
        public float nativeSpriteDiameter;
        public float appliedScale;
        public float worldDiameterMetres;

        private SpriteRenderer sr;
        private M2Link link;
        private float displayGain = 1f;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            link = FindFirstObjectByType<M2Link>();
        }

        void OnEnable()
        {
            if (link != null) link.OnSession += HandleSession;
        }

        void OnDisable()
        {
            if (link != null) link.OnSession -= HandleSession;
        }

        void Start() => Apply();

        void Update()
        {
            if (continuousUpdate) Apply();
        }

        // displayGain is CORC's scene-units-per-robot-metre. It should be 1; adopting it here means
        // the sprite stays physically correct even if a gain manipulation is ever introduced.
        private void HandleSession(SessionInfo s)
        {
            displayGain = (float)s.DisplayGain;
            Apply();
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null)
            {
                Debug.LogWarning($"[RealSizeSprite] {name} has no sprite; cannot size it.");
                return;
            }

            nativeSpriteDiameter = sr.sprite.bounds.size.x;
            if (nativeSpriteDiameter <= 0f) return;

            appliedScale = diameterMetres * displayGain / nativeSpriteDiameter;
            transform.localScale = new Vector3(appliedScale, appliedScale, 1f);

            // Report the size that actually reaches the screen, including any parent scaling.
            worldDiameterMetres = transform.lossyScale.x * nativeSpriteDiameter / Mathf.Max(displayGain, 1e-6f);

            if (Mathf.Abs(worldDiameterMetres - diameterMetres) > 1e-5f)
            {
                Debug.LogWarning(
                    $"[RealSizeSprite] {name} renders at {worldDiameterMetres * 100f:F2} cm, not the " +
                    $"requested {diameterMetres * 100f:F2} cm. A parent transform is scaling it " +
                    $"(lossyScale {transform.lossyScale.x:F4} vs localScale {appliedScale:F4}).");
            }
        }

        /// <summary>Rendered diameter in metres of robot travel, for cross-checks against W.</summary>
        public float WorldDiameterMetres => worldDiameterMetres;
    }
}