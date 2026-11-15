using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// You → Send feedback: what a tester, or anybody, wants the person running
/// meARc to know.
///
/// It lands in the reviewer's queue on the website with the app version and
/// the phone model, which is what turns "the camera is black" into something
/// that can be fixed - and the page says so before anything is sent. Google
/// asks, before a new app may go to production, what its closed test's
/// testers said; this is where they say it.
///
/// Guests may send. A 13-year-old whose parent has not agreed may not - the
/// server refuses, the same trigger that keeps them out of trails - and the
/// page then says who their parent can write to instead. Replying by email is
/// offered only to an account that has an email to reply to.
/// </summary>
public class FeedbackView : MonoBehaviour
{
    public TMP_InputField messageField;
    public TextMeshProUGUI attachedLabel;
    public GameObject replyRow;
    public Toggle replyToggle;
    public Button sendButton;
    public TextMeshProUGUI statusLabel;

    bool _busy;

    void Awake()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(Send);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        Say(null, ThemeRole.TextMuted);
        Draw();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
    }

    static string Device
    {
        get { return SystemInfo.deviceModel + ", " + SystemInfo.operatingSystem; }
    }

    /// <summary>Public for the preview harness, where OnEnable never runs.</summary>
    public void Draw()
    {
        if (attachedLabel != null)
        {
            attachedLabel.text = string.Format(AppStrings.Get("feedback.attached"),
                                               Application.version, SystemInfo.deviceModel);
        }

        SupabaseAuth auth = SupabaseAuth.Instance;
        bool hasEmail = auth != null && !auth.IsAnonymous && !string.IsNullOrEmpty(auth.Email);

        if (replyRow != null)
            replyRow.SetActive(hasEmail);
    }

    void Send()
    {
        if (_busy)
            return;

        string text = messageField != null ? messageField.text.Trim() : string.Empty;

        if (text.Length == 0)
        {
            Say("feedback.empty", ThemeRole.StatusPoor);
            return;
        }

        StartCoroutine(Post(text));
    }

    IEnumerator Post(string text)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (config == null || auth == null || !auth.IsSignedIn)
        {
            Say("report.offline", ThemeRole.StatusPoor);
            yield break;
        }

        _busy = true;
        Say("account.working", ThemeRole.TextMuted);

        bool reply = replyToggle != null && replyToggle.isOn &&
                     replyRow != null && replyRow.activeSelf;

        string body = "{\"p_message\":\"" + Postgrest.Json(text) + "\"" +
                      ",\"p_source\":\"app\"" +
                      ",\"p_app_version\":\"" + Postgrest.Json(Application.version) + "\"" +
                      ",\"p_device\":\"" + Postgrest.Json(Device) + "\"" +
                      ",\"p_place\":\"you\"" +
                      ",\"p_reply_ok\":" + (reply ? "true" : "false") + "}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/send_feedback"), body, null))
        {
            yield return request.SendWebRequest();
            _busy = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // What the server said, in the reader's language - never its raw
                // English, and never "check your connection" for a refusal.
                string said = Postgrest.Said(request);

                Say(said == null ? "report.offline"
                    : said.Contains("parent needs to agree") ? "feedback.parent"
                    : said.Contains("a lot of feedback") ? "feedback.tooMany"
                    : "report.failed", ThemeRole.StatusPoor);
                yield break;
            }
        }

        if (messageField != null)
            messageField.text = string.Empty;

        Say("feedback.sent", ThemeRole.StatusGood);
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
