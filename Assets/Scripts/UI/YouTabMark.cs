using TMPro;
using UnityEngine;

/// <summary>
/// The You tab's icon: a person for a guest, your initials once you have an
/// account - so the tab reads as you rather than as one more settings screen.
///
/// Both marks sit in the tab controller's icon list, so whichever is showing is
/// coloured with the tab like any other icon.
/// </summary>
public class YouTabMark : MonoBehaviour
{
    public GameObject personIcon;
    public TextMeshProUGUI initials;

    void OnEnable()
    {
        SupabaseAuth.OnSessionChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Draw;
    }

    void Draw()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool named = auth != null && auth.IsSignedIn && !auth.IsAnonymous;

        if (personIcon != null)
            personIcon.SetActive(!named);

        if (initials != null)
        {
            initials.gameObject.SetActive(named);

            if (named)
                initials.text = ProfileView.Initials(auth.DisplayName, auth.Email);
        }
    }
}
