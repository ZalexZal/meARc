using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The question on a marker, and what happens when it is answered.
///
/// The screen never decides anything. It sends what the reader typed or picked to
/// check_answer and draws whatever comes back, which is the only arrangement in
/// which a correct answer is not already on the phone. That is also why a failure
/// to reach the server is drawn as "couldn't check" and never as a wrong answer:
/// silence from the server is not a verdict, and treating it as one would deduct
/// somebody's points for walking into a basement.
///
/// The four creator rules land here as four different endings. Retry re-arms.
/// Lock counts down. Deduct shows the score moving. Skip closes the question for
/// good. The rule is announced before the first attempt, not sprung afterwards -
/// somebody about to lose five points for a guess is entitled to know that.
/// </summary>
public class MarkerQuestionView : MonoBehaviour
{
    [Tooltip("The page's own scroll. Sent to the bottom after a verdict, so the " +
             "bubble is not answered off the bottom of the screen.")]
    public ScrollRect scroll;

    [Header("The question")]
    [Tooltip("\"Creator rule: wait\" and friends. Hidden when the rule is Retry, " +
             "which is the one that costs nothing and needs no warning.")]
    public GameObject ruleHolder;

    public TextMeshProUGUI ruleLabel;
    public TextMeshProUGUI promptLabel;

    [Header("Choice")]
    public RectTransform optionsParent;
    public AnswerOptionView optionPrefab;

    [Header("Open")]
    public GameObject openHolder;
    public TMP_InputField openField;
    public TextMeshProUGUI openPlaceholder;

    [Header("Controls")]
    public GameObject checkHolder;
    public Button checkButton;
    public TextMeshProUGUI checkLabel;

    [Header("The answer")]
    public GameObject bubbleHolder;
    public Image bubbleFill;
    public Image bubbleEdge;
    public TextMeshProUGUI bubbleLabel;

    [Tooltip("Counts down while the reader is locked out.")]
    public GameObject lockHolder;

    public TextMeshProUGUI lockLabel;

    [Tooltip("The score, when a wrong answer costs points.")]
    public GameObject scoreHolder;

    public TextMeshProUGUI scoreLabel;

    [Header("Finished")]
    [Tooltip("Takes over the whole page once the marker can do nothing more.")]
    public GameObject finishedHolder;

    public TextMeshProUGUI finishedTitle;
    public TextMeshProUGUI finishedBody;
    public Button finishedButton;
    public TextMeshProUGUI finishedButtonLabel;

    readonly List<AnswerOptionView> _options = new List<AnswerOptionView>();

    /// <summary>
    /// Options the server has already rejected. They keep their cross and stay
    /// unpressable for the rest of the visit - re-offering an answer known to be
    /// wrong is how somebody spends a second deduction on it.
    /// </summary>
    readonly HashSet<int> _wrong = new HashSet<int>();

    BuildingData _marker;
    MarkerTask _task;
    int _chosen = -1;
    bool _waiting;
    float _tick;

    /// <summary>Raised when the reader is done here and wants the sheet closed.</summary>
    public System.Action onDone;

    void Awake()
    {
        if (checkButton != null)
            checkButton.onClick.AddListener(Submit);

        if (finishedButton != null)
            finishedButton.onClick.AddListener(Finish);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Relabel;
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Relabel;
    }

    // -------------------------------------------------------------------- open

    public void Show(BuildingData marker)
    {
        _marker = marker;
        _task = marker != null ? marker.task : null;
        _chosen = -1;
        _waiting = false;
        _wrong.Clear();

        if (_task == null || !_task.IsUsable)
            return;

        BuildOptions();

        if (promptLabel != null)
            promptLabel.text = _task.Prompt;

        bool open = _task.Kind == MarkerTaskKind.Open;

        Set(openHolder, open);
        Set(optionsParent != null ? optionsParent.gameObject : null, !open);

        if (openField != null)
            openField.SetTextWithoutNotify(string.Empty);

        if (openPlaceholder != null)
            openPlaceholder.text = AppStrings.Get("task.openHint");

        ShowRule();
        Draw();
    }

    /// <summary>
    /// The creator's rule, said up front. Retry says nothing because there is
    /// nothing to warn about.
    /// </summary>
    void ShowRule()
    {
        WrongAnswerRule rule = _task.Rule;
        bool worth = rule != WrongAnswerRule.Retry;

        Set(ruleHolder, worth);

        if (!worth || ruleLabel == null)
            return;

        ruleLabel.text = AppStrings.Get(
            rule == WrongAnswerRule.Lock ? "task.lockCap" :
            rule == WrongAnswerRule.Deduct ? "task.dedCap" : "task.skipCap");
    }

    void BuildOptions()
    {
        if (optionsParent == null || optionPrefab == null)
            return;

        int wanted = _task.Kind == MarkerTaskKind.Choice ? _task.OptionCount : 0;

        while (_options.Count < wanted)
        {
            AnswerOptionView option = Instantiate(optionPrefab, optionsParent);
            _options.Add(option);

            int index = _options.Count - 1;   // captured per option, not per loop
            if (option.button != null)
                option.button.onClick.AddListener(() => Choose(index));
        }

        for (int i = 0; i < _options.Count; i++)
        {
            bool used = i < wanted;
            _options[i].gameObject.SetActive(used);

            if (used)
                _options[i].Bind(i, _task.OptionAt(i));
        }
    }

    // ------------------------------------------------------------- the reader

    void Choose(int index)
    {
        if (_waiting || State().IsFinished || State().IsLocked)
            return;

        if (_wrong.Contains(index))
            return;

        _chosen = index;

        for (int i = 0; i < _options.Count; i++)
        {
            if (!_options[i].gameObject.activeSelf)
                continue;

            _options[i].Set(i == index ? AnswerOptionView.State.Chosen
                          : _wrong.Contains(i) ? AnswerOptionView.State.Wrong
                          : AnswerOptionView.State.Idle);
        }

        // A tap on an option is the answer. Making somebody then find a Check
        // button underneath is a second decision they did not ask to make.
        Submit();
    }

    public void Submit()
    {
        if (_waiting || _marker == null || _task == null)
            return;

        MarkerTaskService.MarkerProgress progress = State();
        if (progress.IsFinished || progress.IsLocked)
            return;

        string answer = _task.Kind == MarkerTaskKind.Open
            ? (openField != null ? openField.text : string.Empty)
            : (_chosen >= 0 ? _task.OptionAt(_chosen) : null);

        if (string.IsNullOrWhiteSpace(answer))
        {
            if (_task.Kind == MarkerTaskKind.Open && openField != null)
                openField.ActivateInputField();

            return;
        }

        MarkerTaskService service = MarkerTaskService.Instance;
        if (service == null)
        {
            Bubble("task.offline", ThemeRole.TextSecondary);
            return;
        }

        _waiting = true;
        Draw();

        service.CheckAnswer(_marker.buildingId, answer, Verdict);
    }

    void Verdict(MarkerTaskService.Result result)
    {
        _waiting = false;

        if (result == null || result.verdict == MarkerTaskService.Verdict.Unavailable)
        {
            // Not a wrong answer. Everything the reader chose stays as it was, and
            // they can simply press again.
            Bubble("task.offline", ThemeRole.TextSecondary);
            ReArm();
            ShowTheAnswer();
            Draw();
            return;
        }

        bool right = result.verdict == MarkerTaskService.Verdict.Correct;

        MarkAnswered(right);
        ShowTheAnswer();

        if (right)
        {
            string text = !string.IsNullOrWhiteSpace(result.feedback)
                ? result.feedback
                : AppStrings.Get("task.correct");

            int points = result.progress != null ? result.progress.points : 0;
            if (points > 0)
                text += "  " + string.Format(AppStrings.Get("task.awarded"), points);

            Bubble(text, ThemeRole.StatusGood, false);
            Draw();
            return;
        }

        WrongAnswerRule rule = _task.Rule;
        int deducted = result.progress != null ? result.progress.lastDeduction : 0;

        string message = !string.IsNullOrWhiteSpace(result.feedback)
            ? result.feedback
            : rule == WrongAnswerRule.Deduct && deducted > 0
                ? string.Format(AppStrings.Get("task.dedMsg"), deducted)
                : AppStrings.Get("task.wrong");

        Bubble(message, ThemeRole.StatusPoor, false);
        Draw();
    }

    /// <summary>
    /// Scrolls to where the reply is. A long question with four options can push
    /// the bubble below the fold, and a verdict nobody sees is a screen that looks
    /// like it did nothing.
    /// </summary>
    void ShowTheAnswer()
    {
        if (scroll != null)
            scroll.verticalNormalizedPosition = 0f;
    }

    /// <summary>
    /// Puts the options back for another try. Anything already rejected keeps its
    /// cross rather than being quietly offered again.
    /// </summary>
    void ReArm()
    {
        for (int i = 0; i < _options.Count; i++)
        {
            if (!_options[i].gameObject.activeSelf)
                continue;

            _options[i].Set(_wrong.Contains(i) ? AnswerOptionView.State.Wrong
                                               : AnswerOptionView.State.Idle);
        }

        _chosen = -1;
    }

    void MarkAnswered(bool right)
    {
        if (!right && _chosen >= 0)
            _wrong.Add(_chosen);

        // Two of the four rules let the reader carry on, so for those the untried
        // options go back to being answerable rather than greying out with the
        // rest. Muting everything after one wrong answer is right for Lock and for
        // Skip, and it is what made the rule actually named Retry answerable once.
        WrongAnswerRule rule = _task != null ? _task.Rule : WrongAnswerRule.Retry;
        bool carriesOn = !right &&
                         (rule == WrongAnswerRule.Retry || rule == WrongAnswerRule.Deduct);

        for (int i = 0; i < _options.Count; i++)
        {
            if (!_options[i].gameObject.activeSelf)
                continue;

            // Only the chosen one is judged. Marking the others would be marking
            // the right answer, which is the thing this whole design avoids.
            if (i == _chosen)
            {
                _options[i].Set(right ? AnswerOptionView.State.Right
                                      : AnswerOptionView.State.Wrong);
                continue;
            }

            _options[i].Set(_wrong.Contains(i) ? AnswerOptionView.State.Wrong
                          : carriesOn ? AnswerOptionView.State.Idle
                          : AnswerOptionView.State.Muted);
        }

        if (carriesOn)
            _chosen = -1;
    }

    void Finish()
    {
        if (onDone != null)
            onDone();
    }

    // ------------------------------------------------------------------ drawing

    MarkerTaskService.MarkerProgress State()
    {
        MarkerTaskService service = MarkerTaskService.Instance;

        return service != null && _marker != null
            ? service.Progress(_marker.buildingId)
            : new MarkerTaskService.MarkerProgress();
    }

    /// <summary>
    /// Puts the page into whichever of its endings applies. Called after every
    /// answer and every second while a lock runs down.
    /// </summary>
    void Draw()
    {
        MarkerTaskService.MarkerProgress progress = State();

        bool finished = progress.skipped;
        bool locked = progress.IsLocked;
        bool solved = progress.solved;

        // Skip is the only rule that ends the marker without solving it, so it is
        // the only one that takes the whole page over.
        Set(finishedHolder, finished);

        if (finished)
        {
            if (finishedTitle != null)
                finishedTitle.text = AppStrings.Get("task.skipMsg");

            if (finishedBody != null)
                finishedBody.text = AppStrings.Get("task.skipBody");

            if (finishedButtonLabel != null)
                finishedButtonLabel.text = AppStrings.Get("task.backToMap");

            Set(checkHolder, false);
            Set(lockHolder, false);
            Set(scoreHolder, false);
            return;
        }

        Set(lockHolder, locked);
        if (locked)
            UpdateCountdown(progress);

        // Deduct is the one rule with a running total worth showing, and only once
        // something has actually been taken off.
        bool showScore = _task.Rule == WrongAnswerRule.Deduct && progress.attempts > 0;
        Set(scoreHolder, showScore);

        if (showScore && scoreLabel != null)
        {
            scoreLabel.text = progress.points + " " + AppStrings.Get("task.points");
        }

        bool answerable = !locked && !solved;

        Set(checkHolder, answerable && _task.Kind == MarkerTaskKind.Open);

        if (checkLabel != null)
        {
            checkLabel.text = AppStrings.Get(_waiting ? "task.checking" : "task.check");
        }

        if (checkButton != null)
            checkButton.interactable = answerable && !_waiting;

        if (openField != null)
            openField.interactable = answerable && !_waiting;

        // Solved or locked, nothing on the list is pressable. The options stay on
        // screen because the question and the reader's own answer are worth seeing.
        if (!answerable && !solved)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (!_options[i].gameObject.activeSelf || i == _chosen)
                    continue;

                _options[i].Set(_wrong.Contains(i) ? AnswerOptionView.State.Wrong
                                                   : AnswerOptionView.State.Muted);
            }
        }
    }

    void Update()
    {
        if (lockHolder == null || !lockHolder.activeSelf)
            return;

        _tick += Time.unscaledDeltaTime;
        if (_tick < 0.5f)
            return;

        _tick = 0f;

        MarkerTaskService.MarkerProgress progress = State();

        if (!progress.IsLocked)
        {
            // The wait is over. Back to a question the reader can answer, without
            // them having to close the sheet and open it again.
            ReArm();
            Set(bubbleHolder, false);
            Draw();
            return;
        }

        UpdateCountdown(progress);
    }

    void UpdateCountdown(MarkerTaskService.MarkerProgress progress)
    {
        if (lockLabel == null)
            return;

        int left = Mathf.CeilToInt(progress.SecondsLeft);
        string clock = (left / 60) + ":" + (left % 60).ToString("00");

        lockLabel.text = string.Format(AppStrings.Get("task.lockMsg"), clock);
    }

    void Bubble(string keyOrText, ThemeRole role, bool isKey = true)
    {
        Set(bubbleHolder, true);

        if (bubbleLabel != null)
            bubbleLabel.text = isKey ? AppStrings.Get(keyOrText) : keyOrText;

        Color colour = ThemeManager.Get(role);

        if (bubbleEdge != null)
            bubbleEdge.color = colour;

        if (bubbleFill != null)
        {
            // A tenth of the verdict colour behind the words, so the bubble reads as
            // good or bad at a glance without shouting a solid block of red at
            // somebody who simply guessed.
            Color wash = colour;
            wash.a = 0.14f;
            bubbleFill.color = wash;
        }

        if (bubbleLabel != null)
            bubbleLabel.color = ThemeManager.Get(ThemeRole.TextPrimary);
    }

    void Relabel()
    {
        if (_marker != null)
            Show(_marker);
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
