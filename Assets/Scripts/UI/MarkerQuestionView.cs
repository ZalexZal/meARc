using System.Collections.Generic;
using System.Globalization;
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

    [Tooltip("What a correct answer is worth. Off on a guide, and off on a " +
             "question the creator priced at nothing.")]
    public GameObject worthHolder;
    public TextMeshProUGUI worthLabel;

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
    [Header("Hints")]
    [Tooltip("Shown only when the marker has hints and the trail scores. A walk " +
             "has none at all, and marker_detail returns none there.")]
    public GameObject hintsHolder;
    public Button hintsButton;
    public TextMeshProUGUI hintsLabel;

    [Tooltip("The card the button opens. Set by whoever builds the sheet.")]
    public HintSheetView hintSheet;

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
    CanvasGroup _checkFade;
    bool _waiting;
    float _tick;

    /// <summary>Raised when the reader is done here and wants the sheet closed.</summary>
    public System.Action onDone;

    [Header("Daylight v2")]
    [Tooltip("Shown once answered: on to the next stop, which closes the sheet.")]
    public Button nextButton;
    public TextMeshProUGUI nextLabel;

    [Tooltip("Shown while locked: the rest of the trail, which closes the sheet.")]
    public Button exploreButton;
    public TextMeshProUGUI exploreLabel;

    [Tooltip("The verdict's own heading: \"Right · +10 points\", \"Not right\".")]
    public TextMeshProUGUI bubbleTitle;
    public Image bubbleIcon;
    public Sprite rightSprite;
    public Sprite wrongSprite;
    public Sprite infoSprite;

    [Tooltip("The countdown ring on the lock card: red, emptying as the wait runs out.")]
    public Image lockArc;
    public TextMeshProUGUI lockTitle;

    /// <summary>Raised by Next stop and Explore: the reader is leaving the sheet.</summary>
    public System.Action onLeave;

    void Awake()
    {
        if (checkButton != null)
            checkButton.onClick.AddListener(Submit);

        // Typing has to redraw, or Check never wakes up.
        //
        // Draw decides whether Check is pressable from whether there is anything
        // to send, and for a typed answer that is the contents of this field -
        // but nothing called Draw when the contents changed. So the one question
        // kind that needs a keyboard had a Check button that stayed dim and dead
        // no matter what was written in it, and a free-text stop could not be
        // answered at all.
        if (openField != null)
        {
            openField.onValueChanged.AddListener(delegate { Draw(); });

            // The keyboard covers the bottom of the page, and the bottom of the
            // page is where Check is. Sending the page down as the field takes
            // focus puts the button just above the keyboard instead of under it.
            openField.onSelect.AddListener(delegate { ToTheBottom(); });
        }

        if (finishedButton != null)
            finishedButton.onClick.AddListener(Finish);

        if (nextButton != null)
            nextButton.onClick.AddListener(Leave);

        if (exploreButton != null)
            exploreButton.onClick.AddListener(Leave);
    }

    void Leave()
    {
        if (onLeave != null)
            onLeave();
        else
            Finish();
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

        if (hintsButton != null)
        {
            hintsButton.onClick.RemoveAllListeners();
            hintsButton.onClick.AddListener(OpenHints);
        }
        _task = marker != null ? marker.task : null;
        _chosen = -1;
        _waiting = false;
        _wrong.Clear();

        // Last time's answer belongs to last time.
        //
        // A walker refused for having no account made one, came back, and found
        // "a competitive trail needs a real account" still sitting under the
        // question - a sentence that was true ten minutes ago and is now simply
        // wrong.
        Set(bubbleHolder, false);

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
    ///
    /// Said as what happens to the reader, not as what the creator set. It read
    /// "Creator rule: wait" - which is the schema's word for it, in a sentence
    /// about whose rule it is, to somebody who only needs to know that getting
    /// this wrong means waiting. And the deduction now names its number: the
    /// question above says what it is worth and the hints below say what they
    /// cost, so the one figure left unsaid was the one for being wrong.
    /// </summary>
    void ShowRule()
    {
        // One line before the question: what it is worth and what a wrong
        // answer costs. "10 points · a wrong answer locks it for 5 minutes".
        var parts = new System.Collections.Generic.List<string>();

        TrailService trails = TrailService.Instance;
        bool scored = trails == null || trails.SelectedIsGame;

        if (scored && _task.points > 0)
            parts.Add(string.Format(AppStrings.Get("task.worthShort"), _task.points));

        WrongAnswerRule rule = _task.Rule;

        if (rule == WrongAnswerRule.Deduct)
            parts.Add(string.Format(AppStrings.Get("task.dedLine"), _task.deduct));
        else if (rule == WrongAnswerRule.Lock)
            parts.Add(string.Format(AppStrings.Get("task.lockLine"), Duration(_task.lock_seconds)));
        else if (rule == WrongAnswerRule.Skip)
            parts.Add(AppStrings.Get("task.skipLine"));

        bool any = parts.Count > 0;
        Set(ruleHolder, any);

        if (!any || ruleLabel == null)
            return;

        string line = string.Join(" · ", parts);
        ruleLabel.text = char.ToUpper(line[0]) + line.Substring(1);
    }

    /// <summary>"5 minutes", "1 minute", "30 seconds".</summary>
    static string Duration(int seconds)
    {
        if (seconds <= 0)
            seconds = 60;

        if (seconds % 60 == 0)
        {
            int minutes = seconds / 60;
            return minutes == 1 ? AppStrings.Get("time.minute")
                                : string.Format(AppStrings.Get("time.minutes"), minutes);
        }

        return string.Format(AppStrings.Get("time.seconds"), seconds);
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

        // Choosing is not sending.
        //
        // A tap used to be the answer, on the argument that a Check button
        // underneath is a second decision nobody asked to make. That holds on a
        // guided walk, where a wrong answer costs nothing. It does not hold on a
        // trail whose creator set a deduction and a cooldown: there a mis-tap on
        // a slab the width of the screen costs real points before the reader has
        // finished reading the option below it, and there is nothing to be said
        // to them afterwards.
        //
        // So the tap marks, and Check sends.
        Draw();
    }

    public void Submit()
    {
        if (_waiting || _marker == null || _task == null)
            return;

        MarkerTaskService.MarkerProgress progress = State();
        if (progress.IsFinished || progress.IsLocked)
            return;

        // A chosen option travels as its position, not as its words.
        //
        // check_answer reads p_submitted for a choice question as a list of
        // numbers - the indices of what was picked - and compares them with the
        // indices the creator ticked. This sent the option's text instead, and
        // the two only ever agreed by accident:
        //
        //   Options labelled "1", "2", "3" with the second ticked. Tapping the
        //   first sent "1", the server read index 1, and a wrong answer scored.
        //   That stop was then solved, so every later answer came back correct
        //   as well - which is what a field test sees as "everything is right".
        //
        //   Options labelled with words - "Paris", "London" - are worse and
        //   quieter. The server keeps only digits, so nothing survived, the
        //   chosen set was empty, and every answer was wrong. Those questions
        //   have never been answerable by anybody.
        //
        // Open answers are unaffected: those are compared as text, and text is
        // what they have always sent.
        string answer = _task.Kind == MarkerTaskKind.Open
            ? (openField != null ? openField.text : string.Empty)
            : (_chosen >= 0 ? _chosen.ToString(CultureInfo.InvariantCulture) : null);

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
            //
            // But say which of the two things happened. A refusal carries the
            // server's own sentence - "make or join a team first", "a
            // competitive trail needs a real account" - and those are things a
            // walker can do something about. Only silence is the offline case,
            // and reporting a refusal as one sent somebody to check the wifi
            // when the answer was on the screen all along.
            string said = result != null ? result.refusal : null;

            if (!string.IsNullOrWhiteSpace(said))
            {
                Verdict2(Postgrest.InWords(said), null, ThemeRole.StatusFair);

                // The team sheet can be closed on the way in, and nothing on a
                // question says where it went. Open it rather than describe it.
                if (said.Contains("team first") && DoorFlow.Instance != null)
                    DoorFlow.Instance.OpenTeam();
            }
            else
                Verdict2(AppStrings.Get("task.offline"), null, ThemeRole.TextSecondary);

            ReArm();
            ShowTheAnswer();
            Draw();
            return;
        }

        // Too soon after the last wrong answer. The server refused to judge this
        // one at all - it deliberately answers "wait" whatever was pressed, so
        // the cooldown cannot be used to probe for the right option.
        //
        // It was drawn as a wrong answer. Worse than a wrong message: the chosen
        // option was crossed out and filed in _wrong, and Choose() refuses
        // anything in _wrong, so a walker who tapped a wrong option and then the
        // right one inside the minute had the RIGHT one struck off for good. A
        // field test read that as "even the correct answer is marked wrong".
        //
        // So nothing is judged and nothing is crossed out. The service has
        // already set the lock, and Draw() shows its countdown; when it runs
        // out, the same option can be pressed again.
        if (result.verdict == MarkerTaskService.Verdict.Wait)
        {
            ReArm();
            ShowTheAnswer();
            Draw();
            return;
        }

        // A typed answer sent to the creator. Not wrong either - it has not been
        // looked at yet - and it was being drawn as a red "Wrong answer".
        if (result.verdict == MarkerTaskService.Verdict.PendingReview)
        {
            Verdict2(AppStrings.Get("task.review"), null, ThemeRole.StatusFair);
            ShowTheAnswer();
            Draw();
            return;
        }

        bool right = result.verdict == MarkerTaskService.Verdict.Correct;

        MarkAnswered(right);
        ShowTheAnswer();

        if (right)
        {
            // "Right · +10 points", and under it the creator's fact, which is
            // the reward for having got there.
            int points = result.progress != null ? result.progress.points : 0;
            string heading = points > 0
                ? string.Format(AppStrings.Get("task.rightPts"), points)
                : AppStrings.Get("task.right");

            Verdict2(heading, result.feedback, ThemeRole.StatusGood);
            Haptics.Confirm();
            Draw();
            return;
        }

        WrongAnswerRule rule = _task.Rule;
        int deducted = result.progress != null ? result.progress.lastDeduction : 0;

        string message = !string.IsNullOrWhiteSpace(result.feedback)
            ? result.feedback
            : rule == WrongAnswerRule.Deduct && deducted > 0
                ? string.Format(AppStrings.Get("task.dedMsg"), deducted)
                : null;

        Haptics.Reject();
        Shake(optionsParent != null && optionsParent.gameObject.activeInHierarchy
                  ? optionsParent : openHolder != null ? openHolder.transform as RectTransform : null);

        // A lock says itself on the lock card, with its countdown; a bubble
        // saying "not right" above it would be the same news twice.
        if (State().IsLocked)
            Set(bubbleHolder, false);
        else
            Verdict2(AppStrings.Get("task.notRight"), message, ThemeRole.StatusPoor);

        Draw();
    }

    /// <summary>
    /// Scrolls to where the reply is. A long question with four options can push
    /// the bubble below the fold, and a verdict nobody sees is a screen that looks
    /// like it did nothing.
    /// </summary>
    void ShowTheAnswer()
    {
        ToTheBottom();
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
    /// <summary>
    /// The hints button, and whether there is anything behind it.
    ///
    /// Hidden on a walk and on a marker with no hints. A walk is not a matter of
    /// drawing: marker_detail returns an empty list there, so the count is zero
    /// and this hides itself without needing to know why.
    ///
    /// Also hidden once the marker is solved. A hint for a question already
    /// answered is an offer to spend points on nothing.
    /// </summary>
    void DrawHints(MarkerTaskService.MarkerProgress progress)
    {
        int count = HintSheetView.CountFor(_marker);
        bool worth = count > 0 && !progress.solved && !progress.skipped;

        Set(hintsHolder, worth);

        if (!worth)
            return;

        if (hintsLabel != null)
            hintsLabel.text = AppStrings.Get("hint.title");
    }

    void OpenHints()
    {
        if (hintSheet != null && _marker != null)
            hintSheet.Open(_marker);
    }

    void Draw()
    {
        MarkerTaskService.MarkerProgress progress = State();

        DrawHints(progress);

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
            Set(nextButton != null ? nextButton.gameObject : null, false);
            Set(exploreButton != null ? exploreButton.gameObject : null, false);
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

        // What it is worth, beside what it is.
        //
        // The hints under this question have been priced in points since 0015 and
        // the question itself never was, so a walker could read "-5 points" and
        // have nothing to weigh it against. Both halves of the trade, or neither.
        //
        // Not on a guide: a guide asks questions and scores none of them, and a
        // number there would be a promise the server does not keep.
        TrailService trails = TrailService.Instance;
        bool scored = trails == null || trails.SelectedIsGame;
        bool worthSaying = scored && _task.points > 0;

        // Said on the policy line above the question now; the chip stays off.
        Set(worthHolder, false);

        // The footer: Check while it can be answered, the way on once it has
        // been, the rest of the trail while it is locked.
        Set(nextButton != null ? nextButton.gameObject : null, solved || progress.awaitingReview);
        Set(exploreButton != null ? exploreButton.gameObject : null, locked && !solved);

        if (nextLabel != null)
            nextLabel.text = AppStrings.Get("task.next");

        if (exploreLabel != null)
            exploreLabel.text = AppStrings.Get("task.explore");

        // Sent for review is answered too. Check stayed live after "Sent. The
        // trail's creator will check it", inviting a second copy for them to read.
        bool answerable = !locked && !solved && !progress.awaitingReview;

        // Shown for both kinds now. It was only ever drawn for a typed answer,
        // because a tap on an option was the answer.
        Set(checkHolder, answerable);

        if (checkLabel != null)
        {
            checkLabel.text = AppStrings.Get(_waiting ? "task.checking" : "task.check");
        }

        // Nothing picked, nothing to send. Dimmed rather than missing: a button
        // that appears when you choose is a button you have to notice appearing.
        bool something = _task.Kind == MarkerTaskKind.Open
            ? (openField != null && !string.IsNullOrWhiteSpace(openField.text))
            : _chosen >= 0;

        bool canSend = answerable && !_waiting && something;

        if (checkButton != null)
            checkButton.interactable = canSend;

        // Found here rather than in Awake, which does not run in the editor - the
        // harness draws this screen without ever playing it, and a fade that only
        // exists at runtime is a fade nobody can review.
        if (_checkFade == null && checkHolder != null)
        {
            _checkFade = checkHolder.GetComponent<CanvasGroup>();

            if (_checkFade == null)
                _checkFade = checkHolder.AddComponent<CanvasGroup>();
        }

        // Said in alpha, not in interactable.
        //
        // The button has no disabled tint - its colour comes from the theme, and
        // a colour written here would be painted over on the next theme event -
        // so switching interactable off changed nothing anybody could see. A
        // solid green Check under an untouched question is a button that looks
        // ready and does nothing, which is worse than no button at all.
        if (_checkFade != null)
            _checkFade.alpha = canSend ? 1f : 0.4f;

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

    /// <summary>Puts the end of the page on screen: the verdict, or Check.</summary>
    void ToTheBottom()
    {
        if (scroll != null)
            scroll.verticalNormalizedPosition = 0f;
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

    float _lockTotal;

    void UpdateCountdown(MarkerTaskService.MarkerProgress progress)
    {
        float left = Mathf.Max(0f, progress.SecondsLeft);

        // The whole wait, for the ring: the creator's setting where there is one,
        // and otherwise the longest this lock has been seen to have left.
        float total = _task != null && _task.lock_seconds > 0 ? _task.lock_seconds : 0f;
        _lockTotal = Mathf.Max(_lockTotal, total, left);

        if (lockArc != null)
        {
            lockArc.fillAmount = _lockTotal > 0f ? left / _lockTotal : 0f;
            lockArc.color = ThemeManager.Get(ThemeRole.BadDot);
        }

        if (lockTitle != null)
            lockTitle.text = AppStrings.Get("task.notRight");

        if (lockLabel == null)
            return;

        int whole = Mathf.CeilToInt(left);
        string clock = (whole / 60) + ":" + (whole % 60).ToString("00");
        string at = System.DateTime.Now.AddSeconds(whole).ToString("H:mm");

        lockLabel.text = string.Format(AppStrings.Get("task.lockBody"), clock, at);
    }

    /// <summary>
    /// The verdict under the options: a heading, and under it the reason or the
    /// fact, on a wash of the verdict's own colour.
    /// </summary>
    void Verdict2(string heading, string body, ThemeRole role)
    {
        if (bubbleTitle == null)
        {
            Bubble(string.IsNullOrWhiteSpace(body) ? heading : heading + "  " + body, role, false);
            return;
        }

        Set(bubbleHolder, true);
        bubbleTitle.text = heading;

        Color colour = ThemeManager.Get(role);
        bubbleTitle.color = role == ThemeRole.StatusGood ? colour : ThemeManager.Get(ThemeRole.TextPrimary);

        if (bubbleLabel != null)
        {
            bool hasBody = !string.IsNullOrWhiteSpace(body);
            Set(bubbleLabel.gameObject, hasBody);
            bubbleLabel.text = hasBody ? body : string.Empty;
            bubbleLabel.color = ThemeManager.Get(ThemeRole.TextSecondary);
        }

        if (bubbleIcon != null)
        {
            bubbleIcon.sprite = role == ThemeRole.StatusGood ? rightSprite
                              : role == ThemeRole.StatusPoor ? wrongSprite
                              : infoSprite;
            bubbleIcon.color = colour;
        }

        if (bubbleEdge != null)
            bubbleEdge.color = new Color(0f, 0f, 0f, 0f);

        if (bubbleFill != null)
        {
            Color wash = role == ThemeRole.StatusGood ? ThemeManager.Get(ThemeRole.GoodTint)
                       : role == ThemeRole.StatusPoor ? ThemeManager.Get(ThemeRole.BadTint)
                       : ThemeManager.Get(ThemeRole.SurfaceAlt);
            bubbleFill.color = wash;
        }
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

    /// <summary>
    /// "No": three quick side-to-side swings that die away in 0.35 s, as iOS
    /// shakes a wrong passcode. Position only, put back exactly at the end.
    /// </summary>
    void Shake(RectTransform target)
    {
        if (target != null && isActiveAndEnabled)
            StartCoroutine(ShakeRoutine(target));
    }

    System.Collections.IEnumerator ShakeRoutine(RectTransform target)
    {
        Vector2 home = target.anchoredPosition;
        const float length = 0.35f;
        for (float t = 0f; t < length; t += Time.unscaledDeltaTime)
        {
            float fade = 1f - t / length;
            target.anchoredPosition = home + new Vector2(Mathf.Sin(t * 60f) * 18f * fade, 0f);
            yield return null;
        }
        target.anchoredPosition = home;
    }
}
