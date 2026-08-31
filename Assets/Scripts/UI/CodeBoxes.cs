using System.Text;
using TMPro;
using UnityEngine;
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
/// </summary>
public class CodeBoxes : MonoBehaviour
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int Length = 6;

    [Tooltip("The real input. Invisible - its own text is transparent.")]
    public TMP_InputField field;

    [Tooltip("One per character, left to right.")]
    public TextMeshProUGUI[] labels;

    [Tooltip("The box behind each character, lit as the reader reaches it.")]
    public Image[] boxes;

    [Tooltip("Called once six valid characters are in. The reader should not have " +
             "to hunt for a button after typing the last one.")]
    // Initialised here on purpose, the same trap SheetDragHandle hit: Unity only
    // creates a UnityEvent instance when it deserializes the component, so straight
    // after AddComponent this field is still null and the builder's own attempt to
    // add a listener throws.
    public UnityEngine.Events.UnityEvent onComplete = new UnityEngine.Events.UnityEvent();

    Color _idle;
    Color _active;
    Color _filled;

    void Awake()
    {
        _idle = ThemeManager.Get(ThemeRole.Line);
        _active = ThemeManager.Get(ThemeRole.Accent);
        _filled = ThemeManager.Get(ThemeRole.TextSecondary);

        if (field != null)
        {
            field.characterLimit = Length;
            field.onValueChanged.AddListener(HandleChanged);
        }
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += Recolour;
        Recolour();
        Render();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Recolour;
    }

    public string Code
    {
        get { return field != null ? field.text : string.Empty; }
    }

    public bool IsComplete
    {
        get { return Code.Length == Length; }
    }

    public void Clear()
    {
        if (field != null)
            field.SetTextWithoutNotify(string.Empty);

        Render();
    }

    /// <summary>Puts the keyboard up without the reader having to find the field.</summary>
    public void Focus()
    {
        if (field == null)
            return;

        field.Select();
        field.ActivateInputField();
    }

    void HandleChanged(string typed)
    {
        string cleaned = Clean(typed);

        // SetTextWithoutNotify, or filtering a character would re-enter this method
        // and the field would fight itself over the caret position.
        if (cleaned != typed && field != null)
            field.SetTextWithoutNotify(cleaned);

        Render();

        if (cleaned.Length == Length && onComplete != null)
            onComplete.Invoke();
    }

    static string Clean(string typed)
    {
        if (string.IsNullOrEmpty(typed))
            return string.Empty;

        StringBuilder kept = new StringBuilder(Length);

        for (int i = 0; i < typed.Length && kept.Length < Length; i++)
        {
            char c = char.ToUpperInvariant(typed[i]);

            if (Alphabet.IndexOf(c) >= 0)
                kept.Append(c);
        }

        return kept.ToString();
    }

    void Recolour()
    {
        _idle = ThemeManager.Get(ThemeRole.Line);
        _active = ThemeManager.Get(ThemeRole.Accent);
        _filled = ThemeManager.Get(ThemeRole.TextSecondary);
        Render();
    }

    public void Render()
    {
        string code = Code;

        for (int i = 0; i < Length; i++)
        {
            if (labels != null && i < labels.Length && labels[i] != null)
                labels[i].text = i < code.Length ? code[i].ToString() : string.Empty;

            if (boxes == null || i >= boxes.Length || boxes[i] == null)
                continue;

            // Three states, so the reader can see where they are: filled, the one
            // being typed into, and not yet reached.
            boxes[i].color = i < code.Length ? _filled
                           : i == code.Length ? _active
                           : _idle;
        }
    }
}
