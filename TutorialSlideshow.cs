using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A tutorial menu that rotates through your tutorial graphics like a slideshow, with a
/// text box underneath explaining each one. Opened from its own Tutorial button.
///
/// SETUP:
///   Canvas
///     TutorialButton          (Button -> Open Button)
///     TutorialMenu            (LerpPanel, Start Hidden -> Panel)
///       SlideImage            (Image, Preserve Aspect recommended -> Slide Image)
///       CaptionText           (TMP_Text -> Caption Text)
///       PrevButton / NextButton   (optional)
///       Dots                  (optional empty RectTransform -> Dot Container)
///       CloseButton           (optional)
///
/// Fill in Slides with an image and its explanation text each. The slideshow advances on
/// its own every Seconds Per Slide (looping back to the start), cross-fading between slides.
/// Prev/Next or clicking a dot jumps directly, and pauses the auto-advance for a bit so the
/// player can read. Everything runs on unscaled time, so it works while the game is paused.
/// </summary>
public class TutorialSlideshow : MonoBehaviour
{
    [System.Serializable]
    public class Slide
    {
        public Sprite image;
        [TextArea(2, 6)] public string text;
        [Tooltip("Seconds this slide stays up before auto-advancing. 0 = use Seconds Per Slide.")]
        public float durationOverride = 0f;
    }

    [Header("Wiring")]
    [Tooltip("The dedicated Tutorial button that opens the slideshow. Auto-wired.")]
    public Button openButton;
    [Tooltip("The slideshow menu.")]
    public LerpPanel panel;
    [Tooltip("Optional close button. Auto-wired.")]
    public Button closeButton;
    [Tooltip("Where each slide's graphic is shown.")]
    public Image slideImage;
    [Tooltip("Where each slide's explanation text is shown.")]
    public TMP_Text captionText;
    public Button prevButton;
    public Button nextButton;
    [Tooltip("Optional - e.g. \"3 / 8\".")]
    public TMP_Text counterText;

    [Header("Slides")]
    public List<Slide> slides = new List<Slide>();

    [Header("Rotation")]
    [Tooltip("Advance to the next slide automatically.")]
    public bool autoRotate = true;
    public float secondsPerSlide = 5f;
    [Tooltip("After the last slide, go back to the first. Off = stop on the last slide.")]
    public bool loop = true;
    [Tooltip("After the player presses Prev/Next or a dot, wait this long before auto-rotating again.")]
    public float pauseAfterManualNav = 10f;
    [Tooltip("Cross-fade time between slides (half fading out, half fading in).")]
    public float fadeDuration = 0.35f;
    [Tooltip("Start from the first slide every time the menu is opened.")]
    public bool restartOnOpen = true;

    [Header("Dots (optional)")]
    [Tooltip("Empty RectTransform - one clickable dot per slide is generated into it.")]
    public RectTransform dotContainer;
    public Vector2 dotSize = new Vector2(14f, 14f);
    public float dotSpacing = 8f;
    public Color activeDotColor = Color.white;
    public Color inactiveDotColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("Optional sprite for the dots (e.g. a circle). Squares if empty.")]
    public Sprite dotSprite;

    public bool IsOpen => panel != null && panel.IsShown;
    public int CurrentIndex => _index;

    private int _index;
    private float _timer;
    private float _manualPauseUntil;
    private Coroutine _fade;
    private readonly List<Image> _dots = new List<Image>();

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Toggle);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (prevButton != null) prevButton.onClick.AddListener(() => Step(-1, manual: true));
        if (nextButton != null) nextButton.onClick.AddListener(() => Step(+1, manual: true));
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        if (panel == null)
        {
            Debug.LogError($"[TutorialSlideshow] '{name}' has no panel (LerpPanel) assigned.", this);
            return;
        }
        if (slides.Count == 0)
            Debug.LogWarning($"[TutorialSlideshow] '{name}' has no slides - add some in the Inspector.", this);

        panel.Show();
        BuildDots();
        if (restartOnOpen) _index = 0;
        _timer = 0f;
        _manualPauseUntil = 0f;
        ShowSlide(_index, instant: true);
    }

    public void Close()
    {
        if (panel != null && panel.IsShown) panel.Hide();
    }

    /// <summary>Jump straight to a slide (e.g. from your own buttons). Pauses auto-rotation like Prev/Next.</summary>
    public void GoTo(int index)
    {
        if (slides.Count == 0) return;
        _manualPauseUntil = Time.unscaledTime + pauseAfterManualNav;
        _timer = 0f;
        ShowSlide(Mathf.Clamp(index, 0, slides.Count - 1), instant: false);
    }

    private void Update()
    {
        if (!IsOpen || !autoRotate || slides.Count < 2) return;
        if (Time.unscaledTime < _manualPauseUntil) return;

        _timer += Time.unscaledDeltaTime;
        var slide = slides[_index];
        float duration = slide.durationOverride > 0f ? slide.durationOverride : secondsPerSlide;
        if (_timer >= duration)
        {
            _timer = 0f;
            if (!loop && _index >= slides.Count - 1) return;
            Step(+1, manual: false);
        }
    }

    private void Step(int direction, bool manual)
    {
        if (slides.Count == 0) return;
        if (manual)
        {
            _manualPauseUntil = Time.unscaledTime + pauseAfterManualNav;
            _timer = 0f;
        }

        int next = _index + direction;
        if (next >= slides.Count) next = loop ? 0 : slides.Count - 1;
        if (next < 0) next = loop ? slides.Count - 1 : 0;
        if (next == _index) return;
        ShowSlide(next, instant: false);
    }

    private void ShowSlide(int index, bool instant)
    {
        if (slides.Count == 0)
        {
            if (captionText != null) captionText.text = "";
            if (slideImage != null) slideImage.enabled = false;
            return;
        }

        _index = index;
        if (_fade != null) StopCoroutine(_fade);

        if (instant || fadeDuration <= 0f || !isActiveAndEnabled)
        {
            ApplySlide(slides[_index]);
            SetAlpha(1f);
        }
        else
        {
            _fade = StartCoroutine(CrossFade(slides[_index]));
        }

        RefreshNav();
    }

    private IEnumerator CrossFade(Slide slide)
    {
        float half = fadeDuration * 0.5f;
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            SetAlpha(1f - t / half);
            yield return null;
        }
        ApplySlide(slide);
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            SetAlpha(t / half);
            yield return null;
        }
        SetAlpha(1f);
        _fade = null;
    }

    private void ApplySlide(Slide slide)
    {
        if (slideImage != null)
        {
            slideImage.enabled = slide.image != null;
            slideImage.sprite = slide.image;
        }
        if (captionText != null) captionText.text = slide.text;
    }

    private void SetAlpha(float a)
    {
        if (slideImage != null) { var c = slideImage.color; c.a = a; slideImage.color = c; }
        if (captionText != null) { var c = captionText.color; c.a = a; captionText.color = c; }
    }

    private void RefreshNav()
    {
        bool many = slides.Count > 1;
        if (counterText != null)
        {
            counterText.gameObject.SetActive(many);
            counterText.text = $"{_index + 1} / {slides.Count}";
        }
        if (prevButton != null) prevButton.interactable = loop || _index > 0;
        if (nextButton != null) nextButton.interactable = loop || _index < slides.Count - 1;

        for (int i = 0; i < _dots.Count; i++)
            _dots[i].color = i == _index ? activeDotColor : inactiveDotColor;
    }

    private void BuildDots()
    {
        if (dotContainer == null) return;

        var layout = dotContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null)
        {
            layout = dotContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }
        layout.spacing = dotSpacing;

        while (_dots.Count < slides.Count)
        {
            int dotIndex = _dots.Count;
            var go = new GameObject($"Dot{dotIndex + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(dotContainer, false);
            ((RectTransform)go.transform).sizeDelta = dotSize;
            var img = go.GetComponent<Image>();
            if (dotSprite != null) img.sprite = dotSprite;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None; // RefreshNav sets the colors
            btn.onClick.AddListener(() => GoTo(dotIndex));
            _dots.Add(img);
        }

        for (int i = 0; i < _dots.Count; i++)
            _dots[i].gameObject.SetActive(i < slides.Count);
    }
}
