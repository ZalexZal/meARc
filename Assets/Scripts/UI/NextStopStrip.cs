using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Where you came from, where you are, and what is next — the bus-stop strip that
/// replaced the "nearest to you" card at the bottom of the map.
///
/// The card it replaced answered proximity: what happens to be closest. That is
/// rarely the reader's question. On a trail the question is "where am I up to and
/// where do I go next", and a scatter of dots cannot answer it. This can, and it
/// does it in the place the reader is already looking.
///
/// Two behaviours, one component:
///
///   Guided and Capture the flag follow the creator's order. Next is the lowest
///   <see cref="BuildingData.step"/> not yet found, and the strip can say
///   "Stop 3 of 9" because there is an n to say.
///
///   Freefall has no order by definition, so next is simply the nearest marker not
///   yet opened. The row loses its "Stop n of 9" and keeps everything else, which
///   is why this is one component with a variable rather than two components.
///
/// Which of the two applies is read off the trail's mode, not off the data.
///
/// It used to be inferred - any marker carrying a step meant the trail was
/// ordered - which was a reasonable guess while nothing set steps. It stopped
/// being reasonable when the creator began numbering every stop on creation:
/// under the old rule the first marker placed on a Freefall trail turned it into
/// a guided one, and the creator was never asked. Mode is the thing the creator
/// actually chose; a step is data that may or may not be there.
///
/// A blank mode still means guide, as it does everywhere else, and a guided
/// trail whose creator never set an order falls through to nearest-unopened on
/// its own, because there is no route to draw.
/// </summary>
public class NextStopStrip : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("Opens a marker's sheet when the next row is tapped.")]
    public MapTabController mapTab;

    [Header("Previous")]
    [Tooltip("Hidden at the start of a trail, when there is nothing behind you.")]
    public GameObject previousRow;

    [Tooltip("Opens the stop just found. A row naming a stop that did nothing " +
             "when tapped read as broken.")]
    public Button previousButton;
    public Image previousKnob;
    public Image previousWire;
    public TextMeshProUGUI previousName;
    public TextMeshProUGUI previousMeta;

    [Header("Here")]
    public TextMeshProUGUI hereName;
    public TextMeshProUGUI hereMeta;

    [Tooltip("The line from 'here' down to 'next'. Off when the trail is finished.")]
    public GameObject hereWire;

    [Header("Next")]
    [Tooltip("Hidden when everything has been found.")]
    public GameObject nextRow;
    public Button nextButton;
    public Image nextKnob;
    public TextMeshProUGUI nextName;
    public TextMeshProUGUI nextMeta;
    public TextMeshProUGUI nextDistance;
    public BearingArrow nextArrow;

    BuildingData _next;
    BuildingData _previous;

    void Awake()
    {
        if (nextButton != null)
            nextButton.onClick.AddListener(OpenNext);

        if (previousButton != null)
            previousButton.onClick.AddListener(OpenPrevious);
    }

    void OnEnable()
    {
        BuildingDatabase.OnBuildingsChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        TrailService.OnTrailsChanged += Refresh;

        if (locationService != null)
            locationService.OnPoseUpdated += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        BuildingDatabase.OnBuildingsChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        TrailService.OnTrailsChanged -= Refresh;

        if (locationService != null)
            locationService.OnPoseUpdated -= Refresh;
    }

    /// <summary>The marker the next row is pointing at, or null.</summary>
    public BuildingData Next { get { return _next; } }

    void OpenNext()
    {
        if (_next != null && mapTab != null)
            mapTab.Focus(_next);
    }

    void OpenPrevious()
    {
        if (_previous != null && mapTab != null)
            mapTab.Focus(_previous);
    }

    // ------------------------------------------------------------------ drawing

    public void Refresh()
    {
        List<BuildingData> all = new List<BuildingData>(BuildingDatabase.AllBuildings);

        if (all.Count == 0)
        {
            Blank();
            return;
        }

        // Freefall is unordered by definition, so it ignores steps even when the
        // markers carry them - which they now always do, because the creator
        // numbers every stop as it is placed.
        List<BuildingData> route = new List<BuildingData>();

        if (!IsFreefall)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].step > 0)
                    route.Add(all[i]);
            }
        }

        route.Sort((a, b) => a.step.CompareTo(b.step));

        if (route.Count > 0)
            DrawRoute(route);
        else
            DrawNearest(all);
    }

    /// <summary>
    /// Whether the open trail is the one mode that has no order.
    ///
    /// Anything else - guide, ctf, educational, and a blank mode from a row that
    /// predates the column - follows the creator's sequence. Defaulting the
    /// unknown case to ordered is the safer way round: a trail that should have
    /// been ordered and is not merely loses its "Stop 3 of 9", whereas the
    /// reverse sends a Freefall walker to a stop the creator never meant to send
    /// them to first.
    /// </summary>
    static bool IsFreefall
    {
        get
        {
            TrailService trails = TrailService.Instance;
            if (trails == null || trails.Selected == null)
                return false;

            return string.Equals(trails.Selected.mode, "freefall",
                                 System.StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A trail with an order: next is the next step nobody has found.</summary>
    void DrawRoute(List<BuildingData> route)
    {
        BuildingData next = null;
        BuildingData previous = null;
        int nextIndex = -1;
        int found = 0;

        for (int i = 0; i < route.Count; i++)
        {
            if (DiscoveryTracker.IsDiscovered(route[i]))
            {
                found++;

                // The last one found, not the one before the next: a stop that was
                // skipped is not behind the reader, it is missed.
                if (next == null)
                    previous = route[i];

                continue;
            }

            if (next == null)
            {
                next = route[i];
                nextIndex = i;
            }
        }

        SetPrevious(previous);

        if (next == null)
        {
            Complete(route.Count);
            return;
        }

        SetHere(found, route.Count);

        SetNext(next, string.Format(AppStrings.Get("strip.stopOf"),
                                    nextIndex + 1, route.Count));
    }

    /// <summary>Freefall: next is whatever is closest that has not been opened.</summary>
    void DrawNearest(List<BuildingData> all)
    {
        BuildingData next = null;
        double best = double.MaxValue;
        int found = 0;

        bool located = locationService != null && locationService.HasValidPose;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null)
                continue;

            if (DiscoveryTracker.IsDiscovered(all[i]))
            {
                found++;
                continue;
            }

            if (!located)
            {
                // No position yet, so nothing can be "closest". Take the first
                // unfound one so the row says something true rather than nothing.
                if (next == null)
                    next = all[i];

                continue;
            }

            double distance = locationService.DistanceMetresTo(all[i]);

            if (distance >= 0d && distance < best)
            {
                best = distance;
                next = all[i];
            }
        }

        // No previous row in freefall. There is no sequence, so "the one before"
        // has no meaning that is not invented - and the only honest way to compute
        // it would be a timestamp DiscoveryTracker deliberately does not keep.
        SetPrevious(null);

        if (next == null)
        {
            Complete(all.Count);
            return;
        }

        SetHere(found, all.Count);
        SetNext(next, AppStrings.Get("strip.closest"));
    }

    // ------------------------------------------------------------------- rows

    void SetPrevious(BuildingData previous)
    {
        _previous = previous;
        bool show = previous != null;

        if (previousRow != null && previousRow.activeSelf != show)
            previousRow.SetActive(show);

        if (!show)
            return;

        if (previousName != null)
            previousName.text = previous.DisplayName;

        if (previousMeta != null)
        {
            previousMeta.text = previous.step > 0
                ? string.Format(AppStrings.Get("strip.stopFound"), previous.step)
                : AppStrings.Get("strip.found");
        }

        if (previousKnob != null)
            previousKnob.color = ThemeManager.Get(ThemeRole.Accent);

        if (previousWire != null)
            previousWire.color = ThemeManager.Get(ThemeRole.Accent);
    }

    void SetHere(int found, int total)
    {
        if (hereName != null)
            hereName.text = AppStrings.Get("strip.here");

        if (hereMeta != null)
        {
            hereMeta.text = found > 0
                ? string.Format(AppStrings.Get("strip.foundOf"), found, total)
                : AppStrings.Get("strip.nothingYet");
        }

        if (hereWire != null && !hereWire.activeSelf)
            hereWire.SetActive(true);
    }

    void SetNext(BuildingData next, string meta)
    {
        _next = next;

        if (nextRow != null && !nextRow.activeSelf)
            nextRow.SetActive(true);

        if (nextName != null)
            nextName.text = next.DisplayName;

        if (nextMeta != null)
            nextMeta.text = meta;

        if (nextKnob != null)
            nextKnob.color = MarkerPalette.Resolve(next);

        if (nextArrow != null)
            nextArrow.SetTarget(next);

        if (nextDistance == null)
            return;

        if (locationService == null || !locationService.HasValidPose)
        {
            nextDistance.text = string.Empty;
            return;
        }

        double metres = locationService.DistanceMetresTo(next);

        nextDistance.text = metres < 0d
            ? string.Empty
            : AppStrings.FormatDistance(metres, !locationService.HasPrecisePose);
    }

    /// <summary>Everything found. No next row, and the here row says so.</summary>
    void Complete(int total)
    {
        _next = null;

        if (nextRow != null && nextRow.activeSelf)
            nextRow.SetActive(false);

        if (hereWire != null && hereWire.activeSelf)
            hereWire.SetActive(false);

        if (hereName != null)
            hereName.text = string.Format(AppStrings.Get("strip.allFound"), total);

        if (hereMeta != null)
            hereMeta.text = AppStrings.Get("strip.complete");
    }

    /// <summary>No markers at all - no trail chosen, or nothing loaded yet.</summary>
    void Blank()
    {
        _next = null;

        if (previousRow != null && previousRow.activeSelf)
            previousRow.SetActive(false);

        if (nextRow != null && nextRow.activeSelf)
            nextRow.SetActive(false);

        if (hereWire != null && hereWire.activeSelf)
            hereWire.SetActive(false);

        bool hasTrail = TrailService.Instance != null &&
                        !string.IsNullOrEmpty(TrailService.Instance.SelectedId);

        if (hereName != null)
        {
            hereName.text = hasTrail
                ? AppStrings.Get("strip.here")
                : AppStrings.Get("trails.choose");
        }

        if (hereMeta != null)
            hereMeta.text = hasTrail ? AppStrings.Get("strip.loading") : string.Empty;
    }
}
