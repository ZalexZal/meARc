using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The blurred map behind the way-in screens.
///
/// Alex asked for the map to show through and be blurred: the reader is on their
/// way in, so showing where they are going is right, and the blur is what says
/// "not yet". A live Gaussian blur of a moving map would cost more than the map
/// does, so this takes one picture of the screen when a door opens and blurs that.
/// It is a still, which is correct - nothing behind a modal should be moving.
///
/// The blur is four halving blits. Each one bilinearly averages four texels, so by
/// the fourth the image is a sixteenth of the screen and every output pixel has
/// gathered a wide neighbourhood; drawn back at full size it reads as a soft blur
/// rather than as a pixellated one, because the sampler interpolates on the way up
/// as well as on the way down.
///
/// Every part of this can fail - a device that will not read the framebuffer, a
/// scene with nothing rendered yet, the editor in batch mode. So the scrim
/// underneath is not decoration: it is what guarantees the card on top is legible
/// whether or not the picture ever arrives. If the capture fails the reader gets a
/// dimmed map, which is a worse version of the same idea rather than a broken one.
/// </summary>
public class FrostedBackdrop : MonoBehaviour
{
    [Tooltip("Shows the blurred picture. Left off when there is no picture.")]
    public RawImage image;

    [Tooltip("Sits under the image and is always drawn. What keeps the card readable.")]
    public Graphic scrim;

    [Tooltip("How many times the picture is halved. Four is a sixteenth of the screen.")]
    [Range(1, 6)]
    public int halvings = 4;

    [Tooltip("Longest side of the first capture. Bigger costs more and blurs less.")]
    public int captureLimit = 1024;

    RenderTexture _blurred;
    bool _capturing;

    void Awake()
    {
        if (image != null)
            image.enabled = false;
    }

    void OnDestroy()
    {
        Release();
    }

    /// <summary>
    /// Takes the picture, then calls back.
    ///
    /// The callback is the whole contract: whatever is going to be drawn in front
    /// of this has to wait for the shutter, because the shutter is at the end of
    /// the current frame and anything shown before then is in the photograph. It
    /// always runs - on a device with no blur, in the editor, on a second call
    /// while the first is still going - or the door it is holding back never opens.
    /// </summary>
    public void Capture(System.Action then)
    {
        if (_capturing || !isActiveAndEnabled || !Application.isPlaying)
        {
            Done(then);
            return;
        }

        StartCoroutine(Grab(then));
    }

    public void Clear()
    {
        if (image != null)
            image.enabled = false;

        Release();
    }

    IEnumerator Grab(System.Action then)
    {
        _capturing = true;

        // Out of our own photograph.
        //
        // The caller switches this object on before asking for the capture, so
        // without this the scrim is already over the map when the shutter fires.
        // The blur then contains it and the scrim is drawn over the blur a second
        // time; two coats of 0.80 leave four percent of the map, which is a black
        // rectangle where the map was meant to show through.
        //
        // Only the scrim is saved and restored. Blur sets image.enabled itself on
        // every path it can take - true with a texture, false without one - so
        // restoring that would only fight it.
        bool scrimWas = scrim != null && scrim.enabled;

        if (scrim != null)
            scrim.enabled = false;

        if (image != null)
            image.enabled = false;

        // The framebuffer only holds a finished frame at the end of one.
        yield return new WaitForEndOfFrame();

        try
        {
            Blur();
        }
        catch (System.Exception exception)
        {
            // Not worth an error. The scrim already covers this case, and a device
            // that will not hand over its framebuffer is not a bug we can fix here.
            Debug.LogWarning("[Frost] No blur on this device: " + exception.Message);

            if (image != null)
                image.enabled = false;
        }

        if (scrim != null)
            scrim.enabled = scrimWas;

        _capturing = false;
        Done(then);
    }

    static void Done(System.Action then)
    {
        if (then != null)
            then();
    }

    void Blur()
    {
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);

        if (width < 8 || height < 8 || image == null)
            return;

        // Scale the first capture down towards captureLimit before blurring, so the
        // cost does not grow with the resolution of the phone.
        float shrink = Mathf.Min(1f, captureLimit / (float)Mathf.Max(width, height));

        RenderTexture full = RenderTexture.GetTemporary(width, height, 0);
        RenderTexture current = null;

        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);

            int w = Mathf.Max(2, Mathf.RoundToInt(width * shrink));
            int h = Mathf.Max(2, Mathf.RoundToInt(height * shrink));

            current = RenderTexture.GetTemporary(w, h, 0);
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(full, current);

            for (int i = 0; i < halvings; i++)
            {
                w = Mathf.Max(2, w / 2);
                h = Mathf.Max(2, h / 2);

                RenderTexture next = RenderTexture.GetTemporary(w, h, 0);
                next.filterMode = FilterMode.Bilinear;

                Graphics.Blit(current, next);
                RenderTexture.ReleaseTemporary(current);
                current = next;
            }

            // Copied out of the temporary pool, because a temporary is only ours
            // until the next call and this one has to survive being looked at.
            Release();
            _blurred = new RenderTexture(current.width, current.height, 0)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Graphics.Blit(current, _blurred);

            image.texture = _blurred;
            image.enabled = true;

            // CaptureScreenshotIntoRenderTexture writes in the graphics API's own
            // orientation, which is upside down wherever the origin is at the top.
            image.uvRect = SystemInfo.graphicsUVStartsAtTop
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
        }
        finally
        {
            if (current != null)
                RenderTexture.ReleaseTemporary(current);

            RenderTexture.ReleaseTemporary(full);
        }
    }

    void Release()
    {
        if (_blurred == null)
            return;

        if (image != null && image.texture == _blurred)
            image.texture = null;

        _blurred.Release();
        Destroy(_blurred);
        _blurred = null;
    }
}
