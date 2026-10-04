using System.Collections;
using UnityEngine;

/// <summary>
/// Slides a bottom sheet in and out.
///
/// It moves the sheet rather than enabling and disabling it. Toggling the object
/// would stop its own controller from running - the controller usually lives on the
/// same GameObject - and a closed sheet parked below the screen receives no taps
/// anyway, because the graphic raycaster only tests inside the canvas rect.
///
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SheetAnimator : MonoBehaviour
{
    [Tooltip("Seconds for a full open or close.")]
    public float duration = 0.25f;

    [Tooltip("Y position when open, in the sheet's own anchored space.")]
    public float openY;

    [Tooltip("Extra distance below the closed position, so shadows clear the edge.")]
    public float closedPadding = 40f;

    RectTransform _rect;
    Coroutine _running;

    public bool IsOpen { get; private set; }

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    float ClosedY
    {
        get
        {
            if (_rect == null)
                _rect = GetComponent<RectTransform>();

            return openY - _rect.rect.height - closedPadding;
        }
    }

    /// <summary>Parks the sheet offscreen with no animation, for initial state.</summary>
    public void SnapClosed()
    {
        SetY(ClosedY);
        IsOpen = false;
    }

    public void Open()
    {
        if (IsOpen)
            return;

        IsOpen = true;
        Play(openY);
    }

    /// <summary>
    /// Animates back to the open position without changing state. Used when a drag
    /// is released short of the dismiss threshold.
    /// </summary>
    public void SettleOpen()
    {
        IsOpen = true;
        Play(openY);
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        Play(ClosedY);
    }

    void Play(float targetY)
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }

        // Coroutines can't run on an inactive object, so land on the value directly
        // rather than leaving the sheet stranded mid-slide.
        if (!gameObject.activeInHierarchy || !Application.isPlaying)
        {
            SetY(targetY);
            return;
        }

        _running = StartCoroutine(Slide(targetY));
    }

    /// <summary>Velocity handed over by a drag, in reference pixels a second.</summary>
    float _handover;

    /// <summary>Settles open carrying the speed the finger let go at.</summary>
    public void SettleOpen(float velocity)
    {
        _handover = velocity;
        SettleOpen();
    }

    /// <summary>
    /// A critically damped spring rather than a timed ease: it arrives and stops,
    /// and a flick carries its own speed into the settle instead of being
    /// restarted from rest - Daylight's sheet motion.
    /// </summary>
    IEnumerator Slide(float targetY)
    {
        float y = _rect.anchoredPosition.y;
        float v = _handover;
        _handover = 0f;

        const float k = 420f, c = 41f;

        while (true)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                float a = -k * (y - targetY) - c * v;
                v += a * h;
                y += v * h;
            }

            if (Mathf.Abs(y - targetY) < 0.5f && Mathf.Abs(v) < 5f)
                break;

            SetY(y);
            yield return null;
        }

        SetY(targetY);
        _running = null;
    }

    void SetY(float y)
    {
        if (_rect == null)
            _rect = GetComponent<RectTransform>();

        Vector2 position = _rect.anchoredPosition;
        position.y = y;
        _rect.anchoredPosition = position;
    }
}
