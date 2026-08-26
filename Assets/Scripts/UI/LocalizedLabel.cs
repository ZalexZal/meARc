using TMPro;
using UnityEngine;

/// <summary>
/// Sets a text object from an <see cref="AppStrings"/> key and keeps it in sync
/// when the language changes. This is the component that will be replaced by
/// Localization's own LocalizeStringEvent once the package is installed.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedLabel : MonoBehaviour
{
    [Tooltip("Key into AppStrings, e.g. \"tab.camera\".")]
    public string key;

    TMP_Text _text;

    void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        if (_text == null)
            _text = GetComponent<TMP_Text>();

        AppStrings.OnLanguageChanged += Apply;
        Apply();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Apply;
    }

    public void Apply()
    {
        if (_text != null && !string.IsNullOrWhiteSpace(key))
            _text.text = AppStrings.Get(key);
    }
}
