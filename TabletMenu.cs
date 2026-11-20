using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An iPad-style device that slides up when you press a button and holds three "apps" on
/// tabs: Deck (DeckStatsPanel), Combos (ActiveComboPanel) and Popularity (PopularityPanel).
///
/// SCENE SETUP (suggested hierarchy):
///   Canvas
///     TabletButton              (Button - opens/closes the tablet; wire into openButton)
///     TabletBackdrop            (optional full-screen transparent Button behind the tablet; closes it)
///     Tablet                    (LerpPanel, Motion = SlideFromBottom; your iPad frame Image)
///       Screen                  (the "glass" area)
///         TitleText             (optional TMP_Text - shows the current tab's name)
///         DeckPage              (DeckStatsPanel's LerpPanel - Motion = None or ScaleOnly, stretch to fill Screen)
///         CombosPage            (ActiveComboPanel's LerpPanel - same)
///         PopularityPage        (PopularityPanel's LerpPanel - same)
///       TabBar                  (dock along the bottom of the screen)
///         DeckTab / CombosTab / PopularityTab   (Buttons)
///       CloseButton             (optional)
///
/// Leave clickOutsideBackdrop EMPTY on DeckStatsPanel and ActiveComboPanel when they live in
/// the tablet - the tablet's own backdrop replaces them - and set their LerpPanels to
/// Start Hidden. ActiveComboPanel.toggleButton can be left empty too (its badge can sit on
/// the Combos tab or the tablet button instead).
///
/// SCREEN ANIMATION (optional): add a TabletScreen (with an Animator built by Soda > Tablet
/// Screen Animator Builder) and drag it into Screen. The tablet then slides up with the screen
/// Off, plays TurningOn, and only once it's On do the tab buttons (everything in the screen's
/// Show When On list) and the page appear. Closing hides them, plays TurningOff, then slides down.
///
/// HOME SCREEN: with Open To Home Screen on (the default), the tablet opens with no page
/// showing - just the iPad image and the app buttons - and every page starts hidden. With
/// Tap Open App To Close on, pressing the button for the page that's already open closes
/// that page and goes back to the home screen.
///
/// Player-chosen deletions (the selective claw) open the tablet on the Deck tab
/// automatically if DeckStatsPanel.tablet points at this.
/// </summary>
public class TabletMenu : MonoBehaviour
{
    public enum Tab { Deck, Combos, Popularity }

    [Header("Tablet")]
    [Tooltip("The tablet itself. SlideFromBottom motion gives the 'pull it up' feel.")]
    public LerpPanel tabletPanel;
    [Tooltip("Opens/closes the tablet. Auto-wired, no OnClick entry needed.")]
    public Button openButton;
    [Tooltip("Optional close button on the tablet. Auto-wired.")]
    public Button closeButton;
    [Tooltip("Optional full-screen transparent Button behind the tablet that closes it when clicked. Auto-wired " +
             "and toggled on/off with the tablet.")]
    public Button backdrop;
    [Tooltip("Optional - shows the current tab's title.")]
    public TMP_Text titleText;
    [Tooltip("If on, the tablet opens with NO page showing - just the iPad image and the app buttons - and every " +
             "page starts hidden. Pick an app to open its page. If off, it opens on Default Tab (or the last tab).")]
    public bool openToHomeScreen = true;
    [Tooltip("If on, pressing the button for the page that's already open closes it, back to the home screen.")]
    public bool tapOpenAppToClose = true;
    [Tooltip("Title shown on the home screen (no page open). Leave empty for no title.")]
    public string homeTitle = "";
    [Tooltip("Tab shown when the tablet is opened with the button. Ignored while Open To Home Screen is on.")]
    public Tab defaultTab = Tab.Deck;
    [Tooltip("If on, opening with the button goes back to whichever tab was open last time instead of Default Tab. " +
             "Ignored while Open To Home Screen is on.")]
    public bool rememberLastTab = true;
    [Tooltip("Freezes Time.timeScale while the tablet is open (LerpPanels animate on unscaled time, so the tablet still animates).")]
    public bool pauseTimeWhileOpen = false;

    [Header("Screen (optional)")]
    [Tooltip("Optional - the tablet's screen animation (Off -> TurningOn -> On). When set, the tab buttons and pages " +
             "only appear once the screen reaches On, and closing plays the screen off before the tablet slides away. " +
             "Put the tab bar / title / close button in the screen's Show When On list.")]
    public TabletScreen screen;
    [Tooltip("Wait for the tablet to finish sliding up before the screen starts turning on.")]
    public bool waitForSlideInBeforePowerOn = true;

    [Header("App Buttons")]
    [Tooltip("Hide the Deck / Combos / Popularity tab buttons (plus anything in Extra App Buttons) until the screen " +
             "is On. Handled here automatically - you don't need to put the tab buttons in the screen's Show When On list.")]
    public bool hideAppButtonsUntilScreenOn = true;
    [Tooltip("Any other buttons or button bars that should only appear once the screen is On (e.g. the whole app " +
             "button row, a close button). Don't put the screen itself, or anything containing it, in here.")]
    public List<GameObject> extraAppButtons = new List<GameObject>();

    [Header("Deck App")]
    public DeckStatsPanel deckStatsPanel;
    public Button deckTabButton;
    public string deckTitle = "Deck";

    [Header("Combos App")]
    public ActiveComboPanel activeComboPanel;
    public Button combosTabButton;
    public string combosTitle = "Active Combos";

    [Header("Popularity App")]
    public PopularityPanel popularityPanel;
    public Button popularityTabButton;
    public string popularityTitle = "Popularity";

    [Header("Tab Highlight")]
    [Tooltip("Tint applied to the selected tab button's graphic.")]
    public Color selectedTabColor = Color.white;
    [Tooltip("Tint applied to the other tab buttons' graphics.")]
    public Color unselectedTabColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("Selected tab grows to this scale (1 = no change).")]
    public float selectedTabScale = 1.12f;

    public bool IsOpen => tabletPanel != null && tabletPanel.IsShown;
    public Tab CurrentTab { get; private set; }

    /// <summary>True while one of the app pages is open (false on the home screen).</summary>
    public bool HasPageOpen => IsPageShown(Tab.Deck) || IsPageShown(Tab.Combos) || IsPageShown(Tab.Popularity);

    private float _timeScaleBeforeOpen = 1f;
    private Coroutine _powerRoutine;
    private bool _closing;
    private int _appButtonsVisible = -1; // -1 = not applied yet, 0 = hidden, 1 = shown
    private Tab? _pendingTab;            // tab to show once the screen is on; null = home screen
    private int _lastTabRequestFrame = -1; // see ShowTab - one tab press per frame
    private Tab _lastTabRequest;

    private void Awake()
    {
        CurrentTab = defaultTab;
        if (openButton != null) openButton.onClick.AddListener(Toggle);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backdrop != null)
        {
            backdrop.onClick.AddListener(Close);
            backdrop.gameObject.SetActive(false);
        }
        if (deckTabButton != null) deckTabButton.onClick.AddListener(() => ShowTab(Tab.Deck));
        if (combosTabButton != null) combosTabButton.onClick.AddListener(() => ShowTab(Tab.Combos));
        if (popularityTabButton != null) popularityTabButton.onClick.AddListener(() => ShowTab(Tab.Popularity));

        // Tell the pages they live in the tablet, so their own toggles/backdrops stop closing them.
        if (deckStatsPanel != null) deckStatsPanel.tablet = this;
        if (activeComboPanel != null) activeComboPanel.tablet = this;
        if (popularityPanel != null) popularityPanel.tablet = this;

        ReportDoubleWiring();
        SyncAppButtons();
    }

    private void Start()
    {
        // Every page starts closed, so the first open shows the bare home screen. Done in Start
        // (not Awake) so each page's LerpPanel has finished its own Awake first.
        if (openToHomeScreen)
        {
            deckStatsPanel?.panel?.Hide();
            activeComboPanel?.panel?.Hide();
            popularityPanel?.panel?.Hide();
            HighlightAllTabs(null);
            if (titleText != null) titleText.text = homeTitle;
        }
    }

    /// <summary>
    /// The tab buttons are wired here automatically. Any EXTRA OnClick entries on them (set up in the
    /// Inspector before the tablet existed, e.g. DeckStatsPanel.Toggle or LerpPanel.Toggle) run on the
    /// same click - typically closing the page again the instant it opens. List them so they can be removed.
    /// </summary>
    private void ReportDoubleWiring()
    {
        void Check(Button b, string tabName)
        {
            if (b == null) return;
            int n = b.onClick.GetPersistentEventCount();
            for (int i = 0; i < n; i++)
            {
                var target = b.onClick.GetPersistentTarget(i);
                string method = b.onClick.GetPersistentMethodName(i);
                if (target == null || string.IsNullOrEmpty(method)) continue;
                Debug.LogWarning($"[TabletMenu] The {tabName} tab button '{b.name}' also has an OnClick entry in the " +
                                 $"Inspector: {target.GetType().Name}.{method} on '{target.name}'. TabletMenu already handles " +
                                 "this button - remove that entry, or it may close the page right after opening it.", b);
            }
        }
        Check(deckTabButton, "Deck");
        Check(combosTabButton, "Combos");
        Check(popularityTabButton, "Popularity");

        // Each page needs its OWN LerpPanel. If two tabs share one (or a page uses the tablet's own
        // LerpPanel / a parent of the other pages), switching tabs hides the page you just opened.
        var pages = new List<(string name, LerpPanel lp)>();
        if (deckStatsPanel != null && deckStatsPanel.panel != null) pages.Add(("Deck", deckStatsPanel.panel));
        if (activeComboPanel != null && activeComboPanel.panel != null) pages.Add(("Combos", activeComboPanel.panel));
        if (popularityPanel != null && popularityPanel.panel != null) pages.Add(("Popularity", popularityPanel.panel));
        foreach (var (pageName, lp) in pages)
        {
            if (lp == tabletPanel)
                Debug.LogWarning($"[TabletMenu] The {pageName} page's Panel is the TABLET's own LerpPanel - hiding that page " +
                                 "hides the whole tablet. Give the page its own child object with its own LerpPanel.", lp);
            foreach (var (otherName, other) in pages)
            {
                if (other == lp) continue;
                if (IsSelfOrAncestor(lp.gameObject, other.transform))
                    Debug.LogWarning($"[TabletMenu] The {otherName} page sits INSIDE the {pageName} page's LerpPanel ('{lp.name}'), " +
                                     $"so opening {otherName} hides it again. Make the pages siblings, each with its own LerpPanel.", other);
            }
        }
        for (int i = 0; i < pages.Count; i++)
            for (int j = i + 1; j < pages.Count; j++)
                if (pages[i].lp == pages[j].lp)
                    Debug.LogWarning($"[TabletMenu] The {pages[i].name} and {pages[j].name} pages use the SAME LerpPanel " +
                                     $"('{pages[i].lp.name}') - opening one hides the other, which is the same object. " +
                                     "Give each page its own LerpPanel.", pages[i].lp);

        if (activeComboPanel != null && activeComboPanel.toggleButton != null &&
            (activeComboPanel.toggleButton == deckTabButton || activeComboPanel.toggleButton == combosTabButton ||
             activeComboPanel.toggleButton == popularityTabButton))
        {
            Debug.Log($"[TabletMenu] ActiveComboPanel's Toggle Button is also a tablet tab button - that's fine now " +
                      "(it just opens the Combos tab), but you can clear it.", activeComboPanel);
        }
    }

    private void LateUpdate() => SyncAppButtons();

    /// <summary>
    /// Shows the app buttons only while the tablet is open AND its screen is On (and not closing).
    /// Checked every frame, so it stays right no matter which path opened/closed the tablet.
    /// </summary>
    private void SyncAppButtons()
    {
        bool visible = !hideAppButtonsUntilScreenOn ||
                       (IsOpen && !_closing && (screen == null || screen.IsOn));
        int state = visible ? 1 : 0;
        if (state == _appButtonsVisible) return;
        _appButtonsVisible = state;

        SetActiveSafe(deckTabButton != null ? deckTabButton.gameObject : null, visible);
        SetActiveSafe(combosTabButton != null ? combosTabButton.gameObject : null, visible);
        SetActiveSafe(popularityTabButton != null ? popularityTabButton.gameObject : null, visible);
        foreach (var go in extraAppButtons) SetActiveSafe(go, visible);
    }

    private void SetActiveSafe(GameObject go, bool active)
    {
        if (go == null) return;
        // Never hide the tablet or its screen by accident (e.g. if a whole panel got listed here).
        if (!active && tabletPanel != null && IsSelfOrAncestor(go, tabletPanel.transform)) return;
        if (!active && screen != null && IsSelfOrAncestor(go, screen.transform)) return;
        go.SetActive(active);
    }

    private static bool IsSelfOrAncestor(GameObject candidate, Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.gameObject == candidate) return true;
        return false;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else if (openToHomeScreen) OpenHome();
        else Open(rememberLastTab ? CurrentTab : defaultTab);
    }

    /// <summary>Slides the tablet up showing the home screen - no page open, just the app buttons.</summary>
    public void OpenHome() => OpenCore(null, true);

    /// <summary>Slides the tablet up and shows the given tab.</summary>
    public void Open(Tab tab) => Open(tab, true);

    /// <summary>
    /// Slides the tablet up and selects a tab. showPage = false only highlights the tab without
    /// opening its page - used when the caller opens the page itself (DeckStatsPanel.ShowForDeletion).
    /// </summary>
    public void Open(Tab tab, bool showPage) => OpenCore(tab, showPage);

    /// <summary>tab = null opens to the home screen.</summary>
    private void OpenCore(Tab? tab, bool showPage)
    {
        if (tabletPanel == null)
        {
            Debug.LogError($"[TabletMenu] '{name}' has no tabletPanel assigned.", this);
            return;
        }
        if (_closing) return; // let the power-off / slide-down finish first

        bool wasOpen = IsOpen;
        if (!IsOpen)
        {
            tabletPanel.Show();
            if (backdrop != null)
            {
                backdrop.gameObject.SetActive(true);
                // Sit directly behind the tablet: covers everything else (belt, buttons...) so a
                // click there closes the tablet, while clicks ON the tablet still reach it.
                if (backdrop.transform.parent == tabletPanel.transform.parent)
                {
                    backdrop.transform.SetSiblingIndex(tabletPanel.transform.GetSiblingIndex());
                }
            }
            if (pauseTimeWhileOpen)
            {
                _timeScaleBeforeOpen = Time.timeScale;
                Time.timeScale = 0f;
            }
        }

        if (screen == null)
        {
            ShowTabOrHome(tab, showPage);
            return;
        }

        if (!showPage && tab.HasValue)
        {
            // Someone else is opening the page themselves right now (e.g. the selective claw's
            // DeckStatsPanel.ShowForDeletion) - it can't wait for the turn-on animation.
            if (_powerRoutine != null) { StopCoroutine(_powerRoutine); _powerRoutine = null; }
            screen.PowerOnInstant();
            SyncAppButtons();
            SelectTab(tab.Value, false);
            return;
        }

        if (wasOpen && screen.IsOn)
        {
            ShowTabOrHome(tab, true);
            return;
        }

        _pendingTab = tab;
        if (tab.HasValue) CurrentTab = tab.Value;
        if (_powerRoutine == null) _powerRoutine = StartCoroutine(PowerOnThenShow());
    }

    private IEnumerator PowerOnThenShow()
    {
        if (waitForSlideInBeforePowerOn)
            while (tabletPanel.IsAnimating) yield return null;

        bool ready = false;
        screen.PowerOn(() => ready = true);
        while (!ready) yield return null;

        _powerRoutine = null;
        SyncAppButtons();
        ShowTabOrHome(_pendingTab, true); // may have changed while it was turning on
    }

    private void ShowTabOrHome(Tab? tab, bool showPage)
    {
        if (tab.HasValue) SelectTab(tab.Value, showPage);
        else ShowHome();
    }

    /// <summary>
    /// Closes whichever page is open and leaves the tablet up on its home screen (the iPad image
    /// and app buttons). Does nothing while the player must pick a card to delete.
    /// </summary>
    public void ShowHome()
    {
        if (IsLockedOnDeck()) return;
        deckStatsPanel?.Hide();
        activeComboPanel?.Hide();
        popularityPanel?.Hide();
        if (titleText != null) titleText.text = homeTitle;
        HighlightAllTabs(null);
    }

    /// <summary>Switches tabs. Opens the tablet first if it's closed.</summary>
    public void ShowTab(Tab tab)
    {
        // One click can reach here more than once in the same frame: the tab button's own listener
        // plus ActiveComboPanel's Toggle Button (when it's the same button) or an old Inspector OnClick
        // entry like DeckStatsPanel.Toggle. Without this, the first call closes the open page and the
        // second instantly reopens it - so "tap the open tab to close it" looked like it did nothing.
        if (_lastTabRequestFrame == Time.frameCount && _lastTabRequest == tab) return;
        _lastTabRequestFrame = Time.frameCount;
        _lastTabRequest = tab;

        if (!IsOpen) { Open(tab); return; }
        if (screen != null && !screen.IsOn) return; // tabs only work once the screen is on
        if (tab == CurrentTab && IsPageShown(tab))
        {
            // Pressing the open page's own button closes it, back to the home screen.
            if (tapOpenAppToClose) ShowHome();
            return;
        }
        if (IsLockedOnDeck() && tab != Tab.Deck) return; // mid-deletion and cancelling isn't allowed
        SelectTab(tab, true);
    }

    public void Close()
    {
        if (!IsOpen || _closing) return;
        if (IsLockedOnDeck()) return; // the player has to pick a card to delete first

        if (_powerRoutine != null) { StopCoroutine(_powerRoutine); _powerRoutine = null; }

        deckStatsPanel?.Hide();
        activeComboPanel?.Hide();
        popularityPanel?.Hide();

        if (screen != null)
        {
            // Buttons vanish immediately, the screen plays its off animation, THEN the tablet slides away.
            _closing = true;
            SyncAppButtons();
            screen.PowerOff(SlideAway);
        }
        else
        {
            SlideAway();
        }
    }

    private void SlideAway()
    {
        _closing = false;
        tabletPanel.Hide();
        SyncAppButtons();
        if (backdrop != null) backdrop.gameObject.SetActive(false);
        if (pauseTimeWhileOpen) Time.timeScale = _timeScaleBeforeOpen;
    }

    private bool IsLockedOnDeck() =>
        deckStatsPanel != null && deckStatsPanel.IsChoosingDeletion && !deckStatsPanel.allowCancelDeletion;

    private bool IsPageShown(Tab tab) => tab switch
    {
        Tab.Deck => deckStatsPanel != null && deckStatsPanel.IsShown,
        Tab.Combos => activeComboPanel != null && activeComboPanel.IsShown,
        _ => popularityPanel != null && popularityPanel.IsShown
    };

    private void SelectTab(Tab tab, bool showPage)
    {
        CurrentTab = tab;

        // Hide the pages that aren't selected (each Hide() is a no-op if that page isn't open).
        if (tab != Tab.Deck) deckStatsPanel?.Hide();
        if (tab != Tab.Combos) activeComboPanel?.Hide();
        if (tab != Tab.Popularity) popularityPanel?.Hide();

        if (showPage)
        {
            switch (tab)
            {
                case Tab.Deck: deckStatsPanel?.Show(); break;
                case Tab.Combos: activeComboPanel?.Show(); break;
                case Tab.Popularity: popularityPanel?.Show(); break;
            }
        }

        if (titleText != null)
            titleText.text = tab switch { Tab.Deck => deckTitle, Tab.Combos => combosTitle, _ => popularityTitle };

        HighlightAllTabs(tab);
    }

    /// <summary>Highlights the given tab's button; null = home screen, none highlighted.</summary>
    private void HighlightAllTabs(Tab? selected)
    {
        HighlightTab(deckTabButton, selected == Tab.Deck);
        HighlightTab(combosTabButton, selected == Tab.Combos);
        HighlightTab(popularityTabButton, selected == Tab.Popularity);
    }

    private void HighlightTab(Button button, bool selected)
    {
        if (button == null) return;
        if (button.targetGraphic != null) button.targetGraphic.color = selected ? selectedTabColor : unselectedTabColor;
        button.transform.localScale = Vector3.one * (selected ? selectedTabScale : 1f);
    }
}