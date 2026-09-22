using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The first-run walkthrough, and the thing Settings replays.
///
/// Four cards, skippable from the first one. It explains the two things nothing
/// else can: that markers are pinned to real coordinates rather than floating in
/// front of the camera, and that the camera is somewhere the reader goes rather
/// than something the app does to them.
///
/// It is shown once, recorded in PlayerPrefs. Replaying it does not clear that -
/// somebody who asked to see it again has not become a new user.
///
/// This component is on a holder that is always active, and what it shows and
/// hides is a child. A component that deactivates its own object never reaches
/// Start, and Start is where the first run is decided.
/// </summary>
public class TutorialFlow : MonoBehaviour
{
    const string PrefKey = "tutorial_seen";

    [System.Serializable]
    public class Page
    {
        public GameObject root;
        public TextMeshProUGUI title;
        public TextMeshProUGUI body;

        [Tooltip("Key prefix, e.g. tutorial.1 - the title and body are read from " +
                 "<prefix>.title and <prefix>.body.")]
        public string key;
    }

    [Header("Parts")]
    [Tooltip("The overlay, not this object. See the note on this class.")]
    public GameObject root;
    public List<Page> pages = new List<Page>();
    public List<Image> dots = new List<Image>();

    public Button nextButton;
    public TextMeshProUGUI nextLabel;
    public Button skipButton;
    public TextMeshProUGUI skipLabel;
    public Button backButton;
    public TextMeshProUGUI backLabel;

    /// <summary>
    /// Raised when the first run of the tutorial ends, by Next or by Skip.
    /// Not on a replay from Settings - by then the reader has long since been
    /// asked everything that follows it.
    /// </summary>
    public static event System.Action OnFirstFinished;

    /// <summary>True while the tutorial is on screen.</summary>
    public static bool Showing { get; private set; }

    public static bool HasBeenSeen
    {
        get { return PlayerPrefs.GetInt(PrefKey, 0) == 1; }
    }

    int _page;

    void Awake()
    {
        if (nextButton != null)
            nextButton.onClick.AddListener(Next);

        if (skipButton != null)
            skipButton.onClick.AddListener(Finish);

        if (backButton != null)
            backButton.onClick.AddListener(Back);

        if (root != null)
            root.SetActive(false);
    }

    void Start()
    {
        // Start rather than Awake: the tab controller decides what is on screen in
        // its own Start, and opening over a panel that is about to change is how
        // a walkthrough ends up behind the map.
        // Not any more: the demo is the first run now. Kept for the preview
        // harness, which photographs these cards.

    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Render;
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Render;
    }

    /// <summary>Opens at the first card. Called on first run and by Settings.</summary>
    public void Open()
    {
        _page = 0;
        Showing = true;

        if (root != null)
            root.SetActive(true);

        Render();
    }

    public void Next()
    {
        if (_page >= pages.Count - 1)
        {
            Finish();
            return;
        }

        _page++;
        Render();
    }

    public void Back()
    {
        if (_page <= 0)
            return;

        _page--;
        Render();
    }

    public void Finish()
    {
        bool first = !HasBeenSeen;

        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();

        Showing = false;

        if (root != null)
            root.SetActive(false);

        if (first && OnFirstFinished != null)
            OnFirstFinished();
    }

    void Render()
    {
        for (int i = 0; i < pages.Count; i++)
        {
            Page page = pages[i];
            if (page == null)
                continue;

            if (page.root != null)
                page.root.SetActive(i == _page);

            if (i != _page)
                continue;

            if (page.title != null)
                page.title.text = AppStrings.Get(page.key + ".title");

            if (page.body != null)
                page.body.text = AppStrings.Get(page.key + ".body");
        }

        for (int i = 0; i < dots.Count; i++)
        {
            if (dots[i] == null)
                continue;

            // Size as well as colour, so the position in the sequence survives
            // being read by someone who cannot tell the two greys apart.
            bool here = i == _page;
            dots[i].color = ThemeManager.Get(here ? ThemeRole.Accent : ThemeRole.Line);

            // Through the LayoutElement, not the RectTransform: the row is a layout
            // group, and it overwrites sizeDelta on every one of its children.
            LayoutElement element = dots[i].GetComponent<LayoutElement>();
            if (element != null)
            {
                float width = here ? 34f : 14f;
                element.minWidth = width;
                element.preferredWidth = width;
            }
        }

        bool last = _page >= pages.Count - 1;

        if (nextLabel != null)
            nextLabel.text = AppStrings.Get(last ? "tutorial.done" : "tutorial.next");

        // Skip is only honest while there is something left to skip.
        if (skipButton != null)
            skipButton.gameObject.SetActive(!last);

        if (skipLabel != null)
            skipLabel.text = AppStrings.Get("tutorial.skip");

        if (backButton != null)
            backButton.gameObject.SetActive(_page > 0);

        if (backLabel != null)
            backLabel.text = AppStrings.Get("settings.back");
    }
}
