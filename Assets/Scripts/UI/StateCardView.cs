using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The card shown when there is nothing to show: still locating, still loading,
/// nothing nearby, the fetch failed, or no connection at all.
///
/// One component with five configurations rather than five screens, because the
/// shape of the answer is always the same - what happened, what it means, and the
/// one thing worth trying. The action button is never decorative: it is hidden
/// outright when there is nothing useful to press, which is the difference between
/// an empty state and a dead end.
/// </summary>
public class StateCardView : MonoBehaviour
{
    public enum Kind
    {
        Hidden,

        /// <summary>No trail joined, so there is nothing to be near.</summary>
        Empty,

        /// <summary>
        /// On a trail, standing away from it. Not the same thing as Empty at all:
        /// the markers exist, they are simply somewhere else, and offering to open
        /// a trail list to somebody who has already opened one is no help.
        /// </summary>
        FarAway,

        /// <summary>Still waiting for a first fix.</summary>
        Locating,

        /// <summary>Position is known; the marker list is on its way.</summary>
        Loading,

        /// <summary>The list could not be loaded and there is no cache to fall back on.</summary>
        Failed,

        /// <summary>No connection at all.</summary>
        Offline
    }

    [Header("Parts")]
    public GameObject root;

    [Tooltip("One icon per state. A single generic glyph makes five different " +
             "situations look like the same shrug.")]
    public Image icon;
    public Sprite locatingIcon;
    public Sprite loadingIcon;
    public Sprite emptyIcon;
    public Sprite failedIcon;
    public Sprite offlineIcon;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public Button actionButton;
    public TextMeshProUGUI actionLabel;

    [Header("Behaviour")]
    [Tooltip("Radius named in the empty state's wording, so it matches what was searched.")]
    public double radiusMetres = 500d;

    /// <summary>What the action button does. Set by whoever owns this card.</summary>
    public Action onAction;

    Kind _kind = Kind.Hidden;

    public Kind Current { get { return _kind; } }

    void Awake()
    {
        if (actionButton != null)
            actionButton.onClick.AddListener(RaiseAction);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Render;
        Render();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Render;
    }

    public void Show(Kind kind)
    {
        _kind = kind;
        Render();
    }

    public void Hide()
    {
        Show(Kind.Hidden);
    }

    void Render()
    {
        if (root != null)
            root.SetActive(_kind != Kind.Hidden);

        if (_kind == Kind.Hidden)
            return;

        string title;
        string body;
        string action;
        ThemeRole tint;
        Sprite glyph;

        switch (_kind)
        {
            case Kind.Locating:
                title = "state.locating.title";
                body = "state.locating.body";
                action = null;
                tint = ThemeRole.StatusNone;
                glyph = locatingIcon;
                break;

            case Kind.Loading:
                title = "state.loading.title";
                body = "state.loading.body";
                action = null;
                tint = ThemeRole.StatusNone;
                glyph = loadingIcon;
                break;

            case Kind.Failed:
                title = "state.failed.title";
                body = "state.failed.body";
                action = "state.retry";
                tint = ThemeRole.StatusPoor;
                glyph = failedIcon;
                break;

            case Kind.Offline:
                title = "state.offline.title";
                body = "state.offline.body";
                action = "state.retry";
                tint = ThemeRole.StatusFair;
                glyph = offlineIcon;
                break;

            case Kind.FarAway:
                title = "state.far.title";
                body = "state.far.body";
                // Nothing to press. The card below already points at the next
                // marker, and a button here would be a second thing to read
                // before doing the one thing that helps: walking.
                action = null;
                tint = ThemeRole.TextMuted;
                glyph = emptyIcon;
                break;

            default:
                title = "state.empty.title";
                body = "state.empty.body";
                action = "state.widen";
                tint = ThemeRole.TextMuted;
                glyph = emptyIcon;
                break;
        }

        // The far state names the radius it actually searched, so "nothing here"
        // is a fact the reader can check rather than a shrug. Which line carries
        // the number is a translator's choice - in both languages today it is
        // the title - so the radius goes wherever the placeholder is rather than
        // into a line assumed to hold one. It was going into the body, which has
        // no placeholder, and the title shipped a literal "{0}" to the reader.
        if (titleText != null)
            titleText.text = WithRadius(AppStrings.Get(title));

        if (bodyText != null)
            bodyText.text = WithRadius(AppStrings.Get(body));

        if (icon != null)
        {
            icon.color = ThemeManager.Get(tint);

            if (glyph != null)
                icon.sprite = glyph;
        }

        bool hasAction = action != null;

        if (actionButton != null)
            actionButton.gameObject.SetActive(hasAction);

        if (hasAction && actionLabel != null)
            actionLabel.text = AppStrings.Get(action);
    }

    void RaiseAction()
    {
        if (onAction != null)
            onAction();
    }

    /// <summary>
    /// Puts the searched radius into a line that asks for it, and leaves alone
    /// one that does not.
    /// </summary>
    string WithRadius(string text)
    {
        return !string.IsNullOrEmpty(text) && text.Contains("{0}")
            ? string.Format(text, AppStrings.FormatDistance(radiusMetres))
            : text;
    }
}
