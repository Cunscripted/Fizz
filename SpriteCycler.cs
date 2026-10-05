using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cycles a UI Image through a list of sprites, crossfading between them.
/// Attach to a GameObject with an Image component, then assign your sprites in the Inspector.
/// A hidden overlay Image is created automatically as a child to handle the fade.
/// </summary>
[RequireComponent(typeof(Image))]
public class SpriteCycler : MonoBehaviour
{
    [Tooltip("Sprites to cycle through, in order.")]
    [SerializeField] private Sprite[] sprites = new Sprite[3];

    [Tooltip("Seconds each sprite stays fully visible before the next fade starts.")]
    [SerializeField, Min(0f)] private float holdTime = 1f;

    [Tooltip("Seconds the crossfade takes.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.5f;

    [Tooltip("Shape of the fade over time (default is a smooth ease in/out).")]
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Also fade the current sprite out while the next fades in. " +
             "Turn this on if your sprites have transparent areas; leave it off for opaque sprites to avoid a brightness dip mid-fade.")]
    [SerializeField] private bool fadeOutCurrent = false;

    [Tooltip("Keep cycling while the game is paused (Time.timeScale = 0).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("Pick a random sprite each time instead of going in order (never repeats the current one).")]
    [SerializeField] private bool randomOrder = false;

    [Tooltip("Restart from the first sprite whenever this object is re-enabled.")]
    [SerializeField] private bool resetOnEnable = true;

    private Image baseImage;
    private Image overlayImage;
    private Color baseColor;
    private int currentIndex;
    private Coroutine cycleRoutine;

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private void Awake()
    {
        baseImage = GetComponent<Image>();
        baseColor = baseImage.color;
        CreateOverlay();
    }

    private void OnEnable()
    {
        if (resetOnEnable) currentIndex = 0;
        ShowInstant(currentIndex);
        cycleRoutine = StartCoroutine(CycleLoop());
    }

    private void OnDisable()
    {
        if (cycleRoutine != null) StopCoroutine(cycleRoutine);
        cycleRoutine = null;
        ResetFadeState();
    }

    private IEnumerator CycleLoop()
    {
        while (true)
        {
            // Hold on the current sprite.
            float t = 0f;
            while (t < holdTime)
            {
                t += DeltaTime;
                yield return null;
            }

            if (sprites == null || sprites.Length < 2)
            {
                yield return null;
                continue;
            }

            int nextIndex = PickNext();
            yield return Crossfade(nextIndex);
            currentIndex = nextIndex;
        }
    }

    private IEnumerator Crossfade(int nextIndex)
    {
        SyncOverlaySettings();
        overlayImage.sprite = sprites[nextIndex];
        overlayImage.enabled = true;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += DeltaTime;
            float p = fadeCurve.Evaluate(Mathf.Clamp01(elapsed / fadeDuration));
            SetAlpha(overlayImage, baseColor.a * p);
            if (fadeOutCurrent) SetAlpha(baseImage, baseColor.a * (1f - p));
            yield return null;
        }

        // Commit: the base image takes the new sprite and the overlay hides again.
        baseImage.sprite = sprites[nextIndex];
        ResetFadeState();
    }

    private int PickNext()
    {
        if (randomOrder)
        {
            int next = Random.Range(0, sprites.Length - 1);
            if (next >= currentIndex) next++; // skip the current sprite
            return next;
        }
        return (currentIndex + 1) % sprites.Length;
    }

    private void CreateOverlay()
    {
        var go = new GameObject("CrossfadeOverlay", typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling(); // draw right above the base image, below any other children

        overlayImage = go.AddComponent<Image>();
        overlayImage.raycastTarget = false;
        overlayImage.enabled = false;
    }

    // Mirror the base image's look so the overlay lines up exactly.
    private void SyncOverlaySettings()
    {
        overlayImage.material = baseImage.material;
        overlayImage.type = baseImage.type;
        overlayImage.preserveAspect = baseImage.preserveAspect;
        overlayImage.fillCenter = baseImage.fillCenter;
        overlayImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
    }

    private void ResetFadeState()
    {
        if (overlayImage != null) overlayImage.enabled = false;
        if (baseImage != null) baseImage.color = baseColor;
    }

    private static void SetAlpha(Image img, float a)
    {
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    private void ShowInstant(int index)
    {
        if (sprites == null || sprites.Length == 0) return;
        currentIndex = Mathf.Clamp(index, 0, sprites.Length - 1);
        if (sprites[currentIndex] != null) baseImage.sprite = sprites[currentIndex];
        ResetFadeState();
    }

    /// <summary>Change hold and fade times at runtime.</summary>
    public void SetTiming(float hold, float fade)
    {
        holdTime = Mathf.Max(0f, hold);
        fadeDuration = Mathf.Max(0f, fade);
    }

    /// <summary>Jump straight to a specific sprite (no fade) and restart the cycle.</summary>
    public void ShowSprite(int index)
    {
        if (cycleRoutine != null) StopCoroutine(cycleRoutine);
        ShowInstant(index);
        if (isActiveAndEnabled) cycleRoutine = StartCoroutine(CycleLoop());
    }
}