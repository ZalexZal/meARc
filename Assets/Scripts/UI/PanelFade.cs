using System.Collections;
using UnityEngine;

/// <summary>
/// A short fade and rise when a panel appears. Deliberately restrained - long
/// enough to soften the cut, short enough that nobody waiting to use the app ever
/// notices it as a delay.
///
/// TODO: swap to DOTween once the package is installed.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PanelFade : MonoBehaviour
{
    [Tooltip("Seconds. Above about 0.25 this starts to feel like waiting.")]
    public float duration = 0.18f;

    [Tooltip("How far the panel rises as it fades in, in reference pixels.")]
    public float rise = 14f;

    CanvasGroup _group;
    RectTransform _rect;
    Vector2 _restPosition;
    Coroutine _running;

    void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        _rect = (RectTransform)transform;

        // Captured once, before anything animates, so repeated shows always return
        // to the position the layout intended rather than drifting.
        _restPosition = _rect.anchoredPosition;
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (_running != null)
            StopCoroutine(_running);

        _running = StartCoroutine(Play());
    }

    void OnDisable()
    {
        // Left mid-fade by a tab switch, the panel would otherwise come back
        // half-transparent and offset.
        if (_group != null)
            _group.alpha = 1f;

        if (_rect != null)
            _rect.anchoredPosition = _restPosition;

        _running = null;
    }

    IEnumerator Play()
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1f - Mathf.Pow(1f - t, 3f); // OutCubic

            _group.alpha = t;
            _rect.anchoredPosition = _restPosition + new Vector2(0f, rise * (1f - t));

            yield return null;
        }

        _group.alpha = 1f;
        _rect.anchoredPosition = _restPosition;
        _running = null;
    }
}
