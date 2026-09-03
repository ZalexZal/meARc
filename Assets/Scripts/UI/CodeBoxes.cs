using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Six boxes over one hidden input field.
///
/// The field is real and does all the work - focus, the on-screen keyboard, the
/// system's own paste - and is simply not drawn. The boxes are a picture of its
/// contents, which is why they cannot get out of step with it.
///
/// Codes come from generate_join_code, whose alphabet leaves out 0, O, 1, I and L
/// so nothing in a code is ambiguous when it is read off a whiteboard. Anything
/// outside that alphabet is refused as it is typed rather than accepted and
/// rejected later by the server, which would send the reader looking for a typo
/// they did not make.
///
/// The filtering happens in onValidateInput and nowhere else. That is not a style
/// preference. The first version cleaned the string inside onValueChanged and
/// wrote it back with SetTextWithoutNotify, and that scrambled every code after
/// about the fourth character: TMP_InputField.LateUpdate compares its own m_Text
/// against the Android keyboard's buffer and, whenever the two differ, rebuilds
/// m_Text from the keyboard and moves the caret to wherever the native field's
/// selection is. Writing a corrected string back from onValueChanged makes them
/// differ on purpose - the keyboard holds "abcd", we hold "ABCD" - so the two
/// overwrite each other once a frame while the caret drifts out from under the
/// reader. onValidateInput is called from inside that same rebuild, and from
/// Append for a hardware key, so the character is corrected before it is ever
/// stored, TMP writes the result back to the keyboard itself, and there is only
/// ever one authority on what the text is.
/// </summary>
public class CodeBoxes : MonoBehaviour
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int Length = 6;

    /// <summary>One box: the surface, its edge, the letter, and the caret.</summary>
    [System.Serializable]
    public class Cell
    {
        public Image fill;
        public Image outline;
        public TextMeshProUGUI letter;

        [Tooltip("The blinking bar. Only ever shown in the box being typed into.")]
        public RectTransform caret;
    }

    [Tooltip("The real input. Invisible - its own text is transparent.")]
    public TMP_InputField field;

    [Tooltip("Left to right, six of them.")]
    public Cell[] cells;

    [Tooltip("Called once six valid characters are in. The reader should not have " +
             "to hunt for a button after typing the last one.")]
    // Initialised here on purpose, the same trap SheetDragHandle hit: Unity only
    // creates a UnityEvent instance when it deserializes the component, so straight
    // after AddComponent this field is still null and the builder's own attempt to
    // add a listener throws.
    public UnityEngine.Events.UnityEvent onComplete = new UnityEngine.Events.UnityEvent();

    Color _line;
    Color _accent;
    Color _surfaceAlt;
    Color _text;

    Coroutine _focusing;
    float _blink;

    void Awake()
    {
        CacheColours();

        if (field == null)
            return;

        // contentType first: its setter reapplies the line type, keyboard type and
        // validation that go with the preset, so anything set before it is undone.
        field.contentType = TMP_InputField.ContentType.Custom;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.inputType = TMP_InputField.InputType.Standard;
        field.keyboardType = TouchScreenKeyboardType.ASCIICapable;
        field.characterValidation = TMP_InputField.CharacterValidation.None;
        field.characterLimit = Length;
        field.richText = false;
        field.restoreOriginalTextOnEscape = false;

        // We draw the boxes. Without this Android floats its own edit field over
        // the keyboard and the reader ends up looking at two of them.
        field.shouldHideMobileInput = true;

        field.onValidateInput = ValidateChar;
        field.onValueChanged.AddListener(HandleChanged);
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Recolour;
        CacheColours();
        _blink = 0f;
        Render();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Recolour;
        _focusing = null;
    }

    void Update()
    {
        if (cells == null)
            return;

        // Half a second lit, half dark. Only the box being typed into has a caret,
        // so this is one object going on and off rather than a repaint.
        float was = _blink;
        _blink += Time.unscaledDeltaTime;

        if (Mathf.FloorToInt(was * 2f) != Mathf.FloorToInt(_blink * 2f))
            RenderCarets();
    }

    public string Code
    {
        get { return field != null && field.text != null ? field.text : string.Empty; }
    }

    public bool IsComplete
    {
        get { return Code.Length == Length; }
    }

    /// <summary>True while the reader is actually typing into this.</summary>
    public bool HasFocus
    {
        get { return field != null && field.isFocused; }
    }

    public void Clear()
    {
        // Safe here, unlike inside onValueChanged: this is a deliberate reset when
        // the screen opens, and SetText syncs the soft keyboard's own buffer with
        // it rather than leaving the two to argue about it every frame.
        if (field != null)
            field.SetTextWithoutNotify(string.Empty);

        Render();
    }

    /// <summary>Puts the keyboard up without the reader having to find the field.</summary>
    public void Focus()
    {
        if (field == null || !isActiveAndEnabled || !Application.isPlaying)
            return;

        if (_focusing != null)
            StopCoroutine(_focusing);

        _focusing = StartCoroutine(FocusSoon());
    }

    /// <summary>
    /// One frame late, on purpose. The panel holding this was enabled in the same
    /// call that asks for focus, and a Select on an object the EventSystem has not
    /// seen yet is quietly dropped - which is a code screen with no keyboard.
    /// </summary>
    IEnumerator FocusSoon()
    {
        yield return null;

        if (field != null && isActiveAndEnabled)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(field.gameObject);

            field.ActivateInputField();

            int end = field.text != null ? field.text.Length : 0;
            field.caretPosition = end;
            field.selectionAnchorPosition = end;
            field.selectionFocusPosition = end;
        }

        _blink = 0f;
        Render();
        _focusing = null;
    }

    /// <summary>
    /// Called by TMP before a character is stored, on both the hardware-key path
    /// and the soft-keyboard rebuild. Returning the null character refuses it.
    /// </summary>
    char ValidateChar(string soFar, int index, char typed)
    {
        if (soFar != null && soFar.Length >= Length)
            return '\0';

        // Accepted case-insensitively and returned EXACTLY as typed.
        //
        // Returning char.ToUpperInvariant(typed) brought the scrambling back, by
        // the same route the class comment describes. Join codes are lowercase
        // now, so a reader types "m", the Android keyboard's buffer holds "m",
        // and uppercasing made our text hold "M". TMP_InputField.LateUpdate
        // compares the two, finds them different, and rebuilds its text from the
        // keyboard while moving the caret to the native selection - once a frame,
        // for every character after the first few. Filtering in onValidateInput
        // was never the whole fix; not changing the character is.
        //
        // The boxes uppercase for display, and join_world compares with upper()
        // on both sides, so nothing downstream cares what case arrives.
        return Alphabet.IndexOf(char.ToUpperInvariant(typed)) >= 0 ? typed : '\0';
    }

    /// <summary>
    /// Redraws, and nothing else. Writing to the field from here is what broke it.
    /// </summary>
    void HandleChanged(string current)
    {
        _blink = 0f;
        Render();

        if (current != null && current.Length == Length && onComplete != null)
            onComplete.Invoke();
    }

    void CacheColours()
    {
        _line = ThemeManager.Get(ThemeRole.Line);
        _accent = ThemeManager.Get(ThemeRole.Accent);
        _surfaceAlt = ThemeManager.Get(ThemeRole.SurfaceAlt);
        _text = ThemeManager.Get(ThemeRole.TextPrimary);
    }

    void Recolour()
    {
        CacheColours();
        Render();
    }

    public void Render()
    {
        if (cells == null)
            return;

        string code = Code;
        int at = code.Length;

        for (int i = 0; i < cells.Length; i++)
        {
            Cell cell = cells[i];
            if (cell == null)
                continue;

            bool filled = i < at;

            // The box being typed into, whether or not the keyboard is up. A reader
            // who tapped away still needs to see where they got to.
            bool here = i == at;

            if (cell.letter != null)
            {
                // Uppercased here and only here. The field holds exactly what the
                // keyboard sent, because rewriting it as it is typed is what makes
                // TMP and the keyboard disagree and scramble the code; the boxes
                // are a picture of that text and a picture may be tidied.
                cell.letter.text = filled
                    ? char.ToUpperInvariant(code[i]).ToString()
                    : string.Empty;
                cell.letter.color = _text;
            }

            // A filled box steps up to the line colour and an empty one stays at
            // the recessed one, so progress through the six reads at arm's length
            // rather than only close enough to make out the letters.
            //
            // Not Surface for the filled state, which is what it was: Surface is
            // the colour of the card these are sitting on, so a box disappeared
            // the moment it had a letter in it and the letters floated on nothing.
            if (cell.fill != null)
                cell.fill.color = filled ? _line : _surfaceAlt;

            if (cell.outline != null)
                cell.outline.color = here ? _accent : _line;
        }

        RenderCarets();
    }

    void RenderCarets()
    {
        if (cells == null)
            return;

        int at = Code.Length;
        bool typing = HasFocus;
        bool lit = Mathf.FloorToInt(_blink * 2f) % 2 == 0;

        for (int i = 0; i < cells.Length; i++)
        {
            Cell cell = cells[i];
            if (cell == null || cell.caret == null)
                continue;

            bool show = typing && i == at && lit;

            if (cell.caret.gameObject.activeSelf != show)
                cell.caret.gameObject.SetActive(show);

            Image bar = cell.caret.GetComponent<Image>();
            if (bar != null)
                bar.color = _accent;
        }
    }
}
