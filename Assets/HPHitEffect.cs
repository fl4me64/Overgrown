using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Attach to the HP circle GameObject (top-right UI).
///
/// Call PlayHitEffect(newHP) from MowerController.TakeDamage instead of
/// toggling the old hitTextBox. This script runs its own unscaled coroutine
/// so it works correctly while Time.timeScale == 0.
///
/// Sequence (all inside the existing 1-second freeze window):
///   1. Circle vanishes from the top-right (instant).
///   2. It appears beside the player, HP number decrements, circle shakes.
///   3. It slides in a straight line back to its home position.
/// </summary>

public class HPHitEffect : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The RectTransform of this HP circle.")]
    public RectTransform hpCircleRect;

    [Tooltip("The TextMeshProUGUI inside the circle showing the HP number.")]
    public TextMeshProUGUI hpText;

    [Tooltip("The player's world-space Transform (the white cube).")]
    public Transform playerTransform;

    [Tooltip("The Canvas that owns this UI element.")]
    public Canvas canvas;

    [Tooltip("The Image component of the HP circle (for color changes).")]
    public UnityEngine.UI.Image hpCircleImage;

    [Header("Timing")]
    [Tooltip("Gap between vanish and reappear.")]
    public float pauseBeforeAppear = 0.1f;
    [Tooltip("How long the circle shakes next to the player. Game is frozen for this entire duration.")]
    public float shakeTime = 0.2f;
    [Tooltip("How long the circle stays still at the player's side after shaking (game is live).")]
    public float pauseAfterShake = 1.0f;
    [Tooltip("How long the slide back to the corner takes (game is live).")]
    public float slideBackDuration = 1.0f;

    [Header("Shake")]
    public float shakeRadius = 14f;
    public float shakeFrequency = 22f;

    [Header("Player Offset (canvas pixels)")]
    public Vector2 playerOffset = new Vector2(80f, 55f);

    [Header("HP Colors")]
    public Color colorNormal = new Color(0.13f, 0.80f, 0.13f, 1f); // green  (3 HP)
    public Color colorWarning = new Color(1.00f, 0.55f, 0.00f, 1f); // orange (2 HP)
    public Color colorDanger = new Color(0.90f, 0.10f, 0.10f, 1f); // red    (1 HP)

    [Header("Heartbeat (1 HP)")]
    [Tooltip("Seconds between each heartbeat pulse.")]
    public float heartbeatInterval = 3.0f;
    [Tooltip("How much the circle scales up at the peak of each pulse.")]
    public float heartbeatScale = 1.25f;
    [Tooltip("Duration of the full heartbeat animation (expand + contract).")]
    public float heartbeatDuration = 0.35f;

    private Vector2 _homePos;
    private Vector3 _homeScale;
    private bool _playing;
    private Coroutine _heartbeatCoroutine;
    private int _currentHP = -1;

    void Awake()
    {
        if (hpCircleRect != null)
        {
            _homePos = hpCircleRect.anchoredPosition;
            _homeScale = hpCircleRect.localScale;
        }            
    }

    public void PlayHitEffect(int newHP, MowerController player)
    {
        if (_playing) return;
        _currentHP = newHP;
        StartCoroutine(Sequence(newHP, player));
    }

    private IEnumerator Sequence(int newHP, MowerController player)
    {
        _playing = true;

        StopHeartbeat();

        // ── 1. Vanish from corner ─────────────────────────────────────────────
        hpCircleRect.gameObject.SetActive(false);
        yield return new WaitForSecondsRealtime(pauseBeforeAppear);

        // ── 2. Appear beside the player + shake (game frozen) ─────────────────
        Vector2 nearPlayer = ScreenToAnchoredPos(playerTransform.position) + playerOffset;
        hpCircleRect.anchoredPosition = nearPlayer;
        hpCircleRect.localScale = _homeScale;
        hpCircleRect.gameObject.SetActive(true);

        if (hpText != null)
            hpText.text = newHP.ToString();

        SetCircleColor(newHP);

        float t = 0f;
        while (t < shakeTime)
        {
            float angle = t * shakeFrequency * Mathf.PI * 2f;
            float radius = shakeRadius * Mathf.Sin(angle);
            Vector2 shake = new Vector2(Mathf.Cos(angle * 1.3f), Mathf.Sin(angle * 0.9f)) * radius;
            hpCircleRect.anchoredPosition = nearPlayer + shake;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        hpCircleRect.anchoredPosition = nearPlayer;

        // ── Unfreeze — everything below runs while gameplay is live ───────────
        if (player != null)
        {
            player.OnShakeComplete();
        }

        // ── 3. Hold still at player's side for pauseAfterShake seconds ────────
        yield return new WaitForSecondsRealtime(pauseAfterShake);

        // ── 4. Slide back to corner ───────────────────────────────────────────
        float s = 0f;
        while (s < slideBackDuration)
        {
            float lerp = s / slideBackDuration;
            lerp = lerp * lerp * (3f - 2f * lerp);
            hpCircleRect.anchoredPosition = Vector2.Lerp(nearPlayer, _homePos, lerp);
            s += Time.unscaledDeltaTime;
            yield return null;
        }
        hpCircleRect.anchoredPosition = _homePos;
        hpCircleRect.localScale = _homeScale;

        _playing = false;

        if (newHP == 1)
            StartHeartbeat();
    }

    private void SetCircleColor(int hp)
    {
        if (hpCircleImage == null) return;
        switch (hp)
        {
            case 1:
            case 0:
                hpCircleImage.color = colorDanger; break;
            case 2: hpCircleImage.color = colorWarning; break;
            default: hpCircleImage.color = colorNormal; break;
        }
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
    }

    /// <summary>
    /// Call this when the player heals or the scene ends, to stop pulsing.
    /// </summary>
    public void StopHeartbeat()
    {
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }
        // Restore scale in case we stopped mid-pulse.
        if (hpCircleRect != null)
            hpCircleRect.localScale = _homeScale;
    }

    private IEnumerator HeartbeatLoop()
    {
        while (true)
        {
            // Wait between beats (unscaled so it works if time is paused).
            yield return new WaitForSecondsRealtime(heartbeatInterval);

            bool isGameActive = GameManager.Instance == null || GameManager.Instance.IsGameActive;

            // Only pulse when the hit animation is not running.
            if (isGameActive && Time.timeScale > 0f && !_playing)
                yield return StartCoroutine(HeartbeatPulse());
        }
    }

    private IEnumerator HeartbeatPulse()
    {
        // Two quick beats like a real heartbeat: lub-dub.
        yield return StartCoroutine(SingleBeat(0.55f)); // lub  (slightly larger)
        yield return new WaitForSecondsRealtime(0.08f);
        yield return StartCoroutine(SingleBeat(0.45f)); // dub  (slightly smaller)
    }

    private IEnumerator SingleBeat(float fraction)
    {
        // fraction = share of heartbeatDuration this beat consumes.
        float beatDuration = heartbeatDuration * fraction;
        float half = beatDuration * 0.5f;

        // Expand.
        float e = 0f;
        while (e < half)
        {
            if (Time.timeScale == 0f || (GameManager.Instance != null && !GameManager.Instance.IsGameActive))
            {
                hpCircleRect.localScale = _homeScale;
                yield break;
            }

            float lerp = e / half;
            lerp = Mathf.Sin(lerp * Mathf.PI * 0.5f); // ease-out
            hpCircleRect.localScale = Vector3.Lerp(_homeScale, _homeScale * heartbeatScale, lerp);
            e += Time.unscaledDeltaTime;
            yield return null;
        }

        // Contract.
        float c = 0f;
        while (c < half)
        {
            if (Time.timeScale == 0f || (GameManager.Instance != null && !GameManager.Instance.IsGameActive))
            {
                hpCircleRect.localScale = _homeScale;
                yield break;
            }

            float lerp = c / half;
            lerp = 1f - Mathf.Sin((1f - lerp) * Mathf.PI * 0.5f); // ease-in
            hpCircleRect.localScale = Vector3.Lerp(_homeScale * heartbeatScale, _homeScale, lerp);
            c += Time.unscaledDeltaTime;
            yield return null;
        }

        hpCircleRect.localScale = _homeScale;
    }

    private Vector2 ScreenToAnchoredPos(Vector3 worldPos)
    {
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : canvas.worldCamera;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPos);

        RectTransform parentRect = hpCircleRect.parent as RectTransform;
        if (parentRect == null)
            parentRect = canvas.GetComponent<RectTransform>();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, screenPoint, cam, out Vector2 localPos);

        return localPos;
    }

    private void OnDisable()
    {
        StopHeartbeat();
    }
}