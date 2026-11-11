using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creator or viewer, and a creator's line of work, as a sheet: opened from the
/// Settings row to change it, and put up once after sign-in for an account that
/// has never said how it uses AR Trail. The role's A5 stage sits on top and
/// follows the answer, as the website's sign-up stage does.
///
/// There is no "Later". The answer is required, but the sheet can still be
/// closed - its X, the dim behind it, a swipe down - and closing saves nothing,
/// so the question comes back at the next launch. A required question with no
/// way out is a door people force by uninstalling; one that only nags once a
/// launch costs a tap.
/// </summary>
public class WorkSheetView : MonoBehaviour
{
    [Tooltip("Creator or viewer. Switches the work rows on for a creator.")]
    public RoleChoiceView role;

    public WorkChoiceView choice;

    [Tooltip("The A5 stage over the choices: the role's sky, scene and words.")]
    public RoleStage stage;

    [Tooltip("The sheet's title: \"How will you use AR Trail?\" when asked, \"How " +
             "you use AR Trail\" from Settings.")]
    public TextMeshProUGUI title;

    [Tooltip("Why it is asked. Shown either way.")]
    public TextMeshProUGUI askLine;

    public Button saveButton;

    [Tooltip("Says what went wrong. Empty and hidden when idle.")]
    public TextMeshProUGUI statusLabel;

    bool _busy;
    string _titleKey = "role.question";

    void Awake()
    {
        if (saveButton != null)
            saveButton.onClick.AddListener(Save);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Paint;

        if (choice != null)
            choice.OnChosen += Chosen;

        if (role != null)
            role.OnChosen += Chosen;

        Paint();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Paint;

        if (choice != null)
            choice.OnChosen -= Chosen;

        if (role != null)
            role.OnChosen -= Chosen;
    }

    /// <summary>From the Settings row: the current answer already chosen.</summary>
    public void OpenToChange()
    {
        Show("role.settingsTitle");
    }

    /// <summary>
    /// After sign-in, for an account whose role was never asked. An account
    /// made by an older app may still have a line of work; that answer stands
    /// and comes up chosen, as a creator's.
    /// </summary>
    public void Ask()
    {
        Show("role.question");
    }

    void Show(string titleKey)
    {
        _titleKey = titleKey;
        _busy = false;
        gameObject.SetActive(true);

        SupabaseAuth auth = SupabaseAuth.Instance;
        string currentRole = auth != null ? auth.Role : null;
        string currentWork = auth != null ? auth.Work : null;

        if (!AccountRole.IsValid(currentRole) && LineOfWork.IsValid(currentWork))
            currentRole = AccountRole.Creator;

        if (role != null)
            role.Select(currentRole);

        if (choice != null)
            choice.Select(currentRole == AccountRole.Creator ? currentWork : null);

        ClearStatus();
        ShowBanner();
        Paint();
    }

    void Chosen()
    {
        ClearStatus();
        ShowBanner();
    }

    void ShowBanner()
    {
        if (stage != null)
            stage.Show(role != null ? role.Selected : null, choice != null ? choice.Selected : null);
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
            askLine.text = AppStrings.Get("role.footer");

        if (saveButton != null)
            saveButton.interactable = !_busy;
    }

    void Save()
    {
        if (_busy)
            return;

        string chosenRole = role != null ? role.Selected : null;
        string work = choice != null ? choice.Selected : null;

        if (!AccountRole.IsValid(chosenRole))
        {
            Say("err.role");
            return;
        }

        // A viewer is never asked what they do, so nothing is sent for it.
        if (chosenRole == AccountRole.Viewer)
            work = null;
        else if (!LineOfWork.IsValid(work))
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
        auth.StartCoroutine(auth.SetRole(chosenRole, work, Done));
    }

    void Done(string error)
    {
        _busy = false;
        Paint();

        if (!string.IsNullOrEmpty(error))
        {
            Say(error == "err.work" || error == "err.role" ? error : "work.saveFailed");
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
