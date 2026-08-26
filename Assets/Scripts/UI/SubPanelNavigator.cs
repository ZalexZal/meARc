using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One level of drill-down inside a tab: a root view and a set of sub-pages, each
/// reachable by a row and returned from by a back button.
/// </summary>
public class SubPanelNavigator : MonoBehaviour
{
    [Header("References")]
    public GameObject rootView;
    public List<GameObject> pages = new List<GameObject>();

    [Header("Back")]
    public Button backButton;
    public TextMeshProUGUI backLabel;

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(ShowRoot);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += ApplyLabels;
        ApplyLabels();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= ApplyLabels;
    }

    public void ShowRoot()
    {
        if (rootView != null)
            rootView.SetActive(true);

        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null)
                pages[i].SetActive(false);
        }

        if (backButton != null)
            backButton.gameObject.SetActive(false);
    }

    public void ShowPage(GameObject page)
    {
        if (page == null)
            return;

        if (rootView != null)
            rootView.SetActive(false);

        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null)
                pages[i].SetActive(pages[i] == page);
        }

        if (backButton != null)
            backButton.gameObject.SetActive(true);
    }

    void ApplyLabels()
    {
        if (backLabel != null)
            backLabel.text = AppStrings.Get("settings.back");
    }
}
