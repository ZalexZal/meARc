using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// The people this reader has blocked, and the way to take a block back.
///
/// Nobody here is named. The list says what each block was made from - the
/// creator of a trail, the founder of a team, a player - because that is what
/// the reader saw, and it is all they need to recognise it. The rows come
/// straight from user_blocks, which row-level security limits to the reader's
/// own; unblocking deletes the row, under the same policy.
/// </summary>
public class BlockedListView : MonoBehaviour
{
    public const int MaxRows = 12;

    public TextMeshProUGUI emptyLabel;
    public GameObject[] rows = new GameObject[MaxRows];
    public TextMeshProUGUI[] rowLabels = new TextMeshProUGUI[MaxRows];
    public Button[] unblockButtons = new Button[MaxRows];
    public TextMeshProUGUI statusLabel;

    [Serializable]
    class Row
    {
        public string blocked_id;
        public string via;
        public string label;
    }

    [Serializable]
    class RowList
    {
        public Row[] items;
    }

    Row[] _rows = new Row[0];
    bool _busy;

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        ReportSheetView.OnBlocked += Load;
        Load();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
        ReportSheetView.OnBlocked -= Load;
    }

    void Load()
    {
        if (isActiveAndEnabled)
            StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        SupabaseAuth auth = SupabaseAuth.Instance;

        _rows = new Row[0];
        Say(null, ThemeRole.TextMuted);

        if (config == null || auth == null || !auth.IsSignedIn)
        {
            Draw();
            yield break;
        }

        string url = config.Endpoint("/rest/v1/user_blocks?select=blocked_id,via,label" +
                                     "&order=created_at.desc&limit=" + MaxRows);

        using (UnityWebRequest request = SupabaseAuth.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Blocked] Could not read the list: " + Postgrest.Said(request));
                Say("report.offline", ThemeRole.StatusPoor);
                Draw();
                yield break;
            }

            try
            {
                _rows = JsonUtility.FromJson<RowList>("{\"items\":" +
                                                      request.downloadHandler.text + "}").items
                        ?? new Row[0];
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Blocked] Unreadable list: " + exception.Message);
            }
        }

        Draw();
    }

    void Draw()
    {
        if (emptyLabel != null)
            emptyLabel.gameObject.SetActive(_rows.Length == 0);

        for (int i = 0; i < rows.Length; i++)
        {
            bool used = i < _rows.Length;

            if (rows[i] != null)
                rows[i].SetActive(used);

            if (!used)
                continue;

            Row row = _rows[i];

            if (rowLabels[i] != null)
                rowLabels[i].text = string.Format(AppStrings.Get("blocked.via." + row.via),
                                                  row.label);

            if (unblockButtons[i] != null)
            {
                string id = row.blocked_id;
                unblockButtons[i].onClick.RemoveAllListeners();
                unblockButtons[i].onClick.AddListener(delegate { Unblock(id); });
            }
        }
    }

    void Unblock(string blockedId)
    {
        if (_busy || string.IsNullOrEmpty(blockedId))
            return;

        StartCoroutine(Remove(blockedId));
    }

    IEnumerator Remove(string blockedId)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
            yield break;

        _busy = true;
        Say("account.working", ThemeRole.TextMuted);

        string url = config.Endpoint("/rest/v1/user_blocks?blocked_id=eq." +
                                     UnityWebRequest.EscapeURL(blockedId));

        using (UnityWebRequest request = SupabaseAuth.Delete(url))
        {
            yield return request.SendWebRequest();

            _busy = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Say("report.failed", ThemeRole.StatusPoor);
                yield break;
            }
        }

        // Their public trails come back to the library; the trails this reader
        // left when blocking do not rejoin themselves.
        Say("blocked.undone", ThemeRole.StatusGood);
        yield return Fetch();
        Say("blocked.undone", ThemeRole.StatusGood);
    }

    void Say(string key, ThemeRole role)
    {
        if (statusLabel == null)
            return;

        statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(key));
        statusLabel.text = string.IsNullOrEmpty(key) ? string.Empty : AppStrings.Get(key);
        statusLabel.color = ThemeManager.Get(role);
    }
}
