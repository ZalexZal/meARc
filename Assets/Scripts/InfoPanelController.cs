using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InfoPanelController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panelRoot;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI metaText;
    public TextMeshProUGUI descriptionText;
    public GameObject learnMoreButton;

    [Header("Dismiss")]
    [Tooltip("Full-screen transparent catcher behind the sheet. Tapping it closes " +
             "the sheet, which is what people expect from a bottom sheet.")]
    public GameObject scrim;
    public Button scrimButton;

    [Header("Optional")]
    [Tooltip("Slides the sheet in and out. Without one the panel just appears.")]
    public SheetAnimator sheet;

    public TextMeshProUGUI learnMoreLabel;
    public TextMeshProUGUI closeLabel;

    /// <summary>Raised before the sheet opens, so the Camera tab can close Nearby.</summary>
    public Action onWillOpen;

    private string currentLink;
    private BuildingData currentBuilding;

    void Awake()
    {
        if (scrimButton != null)
            scrimButton.onClick.AddListener(ClosePanel);

        if (scrim != null)
            scrim.SetActive(false);

        // With an animator the sheet stays enabled and simply sits offscreen, so
        // its own controller keeps running. Without one, fall back to hiding it.
        if (sheet != null)
            sheet.SnapClosed();
        else if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += RefreshLabels;
        RefreshLabels();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= RefreshLabels;
    }

    public void ShowPanel(BuildingData data)
    {
        if (data == null)
        {
            Debug.LogWarning("InfoPanelController: ShowPanel called with no BuildingData.");
            return;
        }

        if (onWillOpen != null)
            onWillOpen();

        currentBuilding = data;

        RenderBuilding(data);
        RefreshLabels();

        // Counts the moment the reader sees it, whether they arrived from an AR
        // marker, the Nearby list or a search result.
        DiscoveryTracker.MarkDiscovered(data);

        if (scrim != null)
            scrim.SetActive(true);

        if (sheet != null)
            sheet.Open();
        else if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    /// <summary>
    /// Every field is read through its language-aware property, so switching
    /// language re-renders the whole sheet rather than only its chrome.
    /// </summary>
    void RenderBuilding(BuildingData data)
    {
        titleText.text = data.DisplayName;
        metaText.text = BuildMetaLine(data);
        descriptionText.text = data.Description;
        currentLink = data.ExternalLink;

        learnMoreButton.SetActive(!string.IsNullOrWhiteSpace(currentLink));
    }

    // Joins only the fields that are actually filled in, so a building missing an
    // architect doesn't render as "1979  ·    ·  Block".
    string BuildMetaLine(BuildingData data)
    {
        List<string> parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(data.YearBuilt)) parts.Add(data.YearBuilt);
        if (!string.IsNullOrWhiteSpace(data.Architect)) parts.Add(data.Architect);

        string category = data.category != null ? data.category.DisplayName : data.buildingType;
        if (!string.IsNullOrWhiteSpace(category)) parts.Add(category);

        return string.Join("  ·  ", parts);
    }

    void RefreshLabels()
    {
        if (learnMoreLabel != null)
            learnMoreLabel.text = AppStrings.Get("info.learnMore");

        if (closeLabel != null)
            closeLabel.text = AppStrings.Get("info.close");

        // Re-render the open sheet in the new language rather than leaving stale
        // text until the reader closes and reopens it.
        if (currentBuilding != null && titleText != null)
            RenderBuilding(currentBuilding);
    }

    public void OpenExternalLink()
    {
        if (string.IsNullOrWhiteSpace(currentLink))
            return;

        Application.OpenURL(NormalizeUrl(currentLink));
    }

    // Android refuses to open a URL with no scheme, so "example.com" silently does
    // nothing. Assume https for any link entered without one.
    static string NormalizeUrl(string url)
    {
        url = url.Trim();

        if (url.Contains("://") || url.StartsWith("mailto:"))
            return url;

        return "https://" + url;
    }

    public void ClosePanel()
    {
        if (scrim != null)
            scrim.SetActive(false);

        if (sheet != null)
            sheet.Close();
        else
            panelRoot.SetActive(false);
    }
}
