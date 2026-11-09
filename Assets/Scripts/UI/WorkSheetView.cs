using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The line of work as a sheet: opened from the Settings row to change it, and
/// put up once after sign-in for an account that has never answered.
///
/// There is no "Later". The answer is required, but the sheet can still be
/// closed - its X, the dim behind it, a swipe down - and closing saves nothing,
/// so the question comes back at the next launch. A required question with no
/// way out is a door people force by uninstalling; one that only nags once a
/// launch costs a tap.
/// </summary>
public class WorkSheetView : MonoBehaviour
{
    public WorkChoiceView choice;

    [Tooltip("The sheet's title: \"What do you do?\" when asked, \"Change line of " +
             "work\" from Settings.")]
    public TextMeshProUGUI title;

    [Tooltip("Why it is asked. Shown either way.")]
    public TextMeshProUGUI askLine;

    public Button saveButton;

    [Tooltip("Says what went wrong. Empty and hidden when idle.")]
    public TextMeshProUGUI statusLabel;

    bool _busy;
    string _titleKey = "work.title";

    void Awake()
    {
        if (saveButton != null)
            saveButton.onClick.AddListener(Save);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Paint;

        if (choice != null)
            choice.OnChosen += ClearStatus;

        Paint();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Paint;

        if (choice != null)
            choice.OnChosen -= ClearStatus;
    }

    /// <summary>From the Settings row: the current answer already chosen.</summary>
    public void OpenToChange()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        Show("work.change", auth != null ? auth.Work : null);
    }

    /// <summary>After sign-in, for an account that has never answered: nothing chosen.</summary>
    public void Ask()
    {
        Show("work.title", null);
    }

    void Show(string titleKey, string current)
    {
        _titleKey = titleKey;
        _busy = false;
        gameObject.SetActive(true);

        if (choice != null)
            choice.Select(current);

        ClearStatus();
        Paint();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Paint()
    {
        if (title != null)
            title.text = AppStrings.Get(_titleKey);

        if (askLine != null)
            askLine.text = AppStrings.Get("work.ask");

        if (saveButton != null)
            saveButton.interactable = !_busy;
    }

    void Save()
    {
        if (_busy)
            return;

        string work = choice != null ? choice.Selected : null;

        if (!LineOfWork.IsValid(work))
        {
            Say("err.work");
            return;
        }

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        _busy = true;
        Paint();

        // On the auth service, which is never switched off: closing the sheet
        // mid-save would otherwise stop the coroutine before it answered.
        auth.StartCoroutine(auth.SetWork(work, Done));
    }

    void Done(string error)
    {
        _busy = false;
        Paint();

        if (!string.IsNullOrEmpty(error))
        {
            Say(error == "err.work" ? error : "work.saveFailed");
            return;
        }

        Close();
        ProximityToast.Say(AppStrings.Get("work.saved"));
    }

    void Say(string key)
    {
        if (statusLabel == null)
            return;

        statusLabel.text = AppStrings.Get(key);
        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
        statusLabel.gameObject.SetActive(true);
    }

    void ClearStatus()
    {
        if (statusLabel == null)
            return;

        statusLabel.text = string.Empty;
        statusLabel.gameObject.SetActive(false);
    }
}
