# Soda Deck Builder

A Balatro-style deckbuilder in Unity 2D: mix flavor "additives" to brew sodas, chase a
score requirement each round, and grow your collection with new additives and
permanent "syrup" upgrades.

This README is the setup guide and the design doc both. Read **Design notes & caveats**
and **Common setup gotchas** before you build further on top of this - several of them
came from real bugs we hit and fixed along the way.

---

## Folder structure

```
SodaDeckBuilder/
├── Scripts/
│   ├── FlavorType.cs               Flavor enum (Sour/Sweet/Savory/Smooth/Fizzy/Aftertaste) + color palette
│   ├── AdditiveData.cs             ScriptableObject "card" template (the static, shared data)
│   ├── AdditiveInstance.cs         Runtime per-run wrapper around a template (bonuses live here)
│   ├── ScoringContext.cs           Bundle passed into effects: cup, owned collection, belt counts, FX anchors
│   ├── ScoreEvent.cs               One scoring step (points/mult/xmult/buff/added-to-deck) for FX playback
│   ├── FlavorComboRule.cs          ScriptableObject synergy rule (e.g. "Sour + Fizzy -> +2 mult")
│   ├── ModifierData.cs             ScriptableObject "syrup" (permanent upgrade) template
│   ├── ModifierManager.cs          Offers/applies syrups, tracks ongoing flavor rules
│   ├── SodaScoringManager.cs       Runs one scoring pass: additives -> syrups -> combos -> total (+ events)
│   ├── ConveyorBelt.cs             Arranges & circulates cards; Y-depth sorts them; pins its own draw order
│   ├── ConveyorBeltShaderController.cs  Drives the belt's tread shader, auto-synced to actual belt rotation
│   ├── AdditiveCard.cs             Draggable card: pickup/follow/drop, tilt, hover/drag-to-reveal, click-to-pick
│   ├── SodaBottle.cs               Drop zone; holds the cup (max 5), enforces capacity
│   ├── AdditiveDetailPanel.cs      TMP-based UI panel that shows hover/drag details + icon
│   ├── FloatingScoreText.cs        A single rising/fading score popup
│   ├── ScoreFXPlayer.cs            Plays a scored soda's events back one at a time (text + per-category sound)
│   ├── SodaColorController.cs      Feeds the cup's flavor mix, bubble size/opacity, color intensity into the shader
│   ├── ShopManager.cs              Round-end offer of 4 additives, efficiency-weighted odds
│   ├── ShopMenuController.cs       Lerping shop panel - spawns clickable offer cards, wires picks back to RoundManager
│   ├── ModifierOfferButton.cs      A single clickable syrup offer entry (name/description/Button)
│   ├── ModifierMenuController.cs   Lerping syrup-offer panel, same pattern as the shop
│   ├── DeckStatsPanel.cs           Lists every owned additive - opened from the pause menu
│   ├── PauseMenuController.cs      Pause button + lerping pause menu (Resume / Main Menu / Deck Stats)
│   ├── LerpPanel.cs                Reusable animated show/hide panel (slide + scale + fade) all menus share
│   └── RoundManager.cs             The game loop state machine
├── Editor/
│   ├── SodaEditorFields.cs         Shared conditional field-drawing logic + "Use Asset Name" buttons
│   ├── SodaCustomInspectors.cs     Custom Inspectors for the 3 asset types
│   └── SodaEditorWindow.cs         Soda > Additive & Modifier Editor window (resizable split panel)
└── Shaders/
    ├── SodaLiquid.shader           Color blend + reactive bubbling; adjustable bubble size/opacity, color intensity
    └── ConveyorBeltShader.shader   Procedural scrolling tread pattern, adjustable direction/speed (Built-in RP)
```

---

## Requirements

- **TextMeshPro.** `AdditiveDetailPanel`, `FloatingScoreText`, and `ModifierOfferButton`
  use `TMP_Text` (`TextMeshProUGUI`), not legacy `UnityEngine.UI.Text`. If your project
  has never used TMP before, Unity will prompt you to import **TMP Essentials** the
  first time you add a `TextMeshPro - Text (UI)` component - do that before wiring up
  any of these prefabs.
- Canvas UI throughout - everything interactive is `RectTransform`-driven (see **Setup
  in Unity** below).

---

## Setup in Unity

1. **Import everything.** Drop `Scripts/` and `Shaders/` anywhere under `Assets/`.
   `Editor/` must stay in a folder literally named `Editor` (anywhere in the
   hierarchy) - that's what tells Unity to exclude it from builds.

2. **Set up the Canvas.**
   - `GameObject > UI > Canvas`. Render Mode: **Screen Space - Camera** or
     **Screen Space - Overlay**, either works.
   - Add a **Canvas Scaler** set to **Scale With Screen Size** with a reference
     resolution matching your target layout - this is what keeps the belt/bottle
     proportions correct across window sizes (`ConveyorBelt`'s radii are
     percentages of its own `RectTransform`, which the scaler resizes correctly).
   - Confirm an **EventSystem** exists (Unity adds one automatically with your first Canvas).

3. **Create the flavor palette.**
   `Assets > Create > Soda > Flavor Palette` -> tweak colors if you like, then
   assign it to your `SodaColorController`.

4. **Create some additives, combo rules, and syrups.**
   Open **Soda > Additive & Modifier Editor** from the menu bar. Pick a tab,
   hit **+ Create New**, and fill in the fields - the form only shows what's
   relevant to the effect type you pick. Drag the panel divider to resize the
   list/detail split. Use **Sync ALL Names to Assets** to bulk-rename the name
   field on every asset in the current tab to match its `.asset` file name.

5. **Build the core gameplay scene, all under the Canvas:**
   - **ConveyorBelt** - a `RectTransform` with a **real, non-zero Width/Height**
     (see gotcha below), sized/anchored to whatever screen region should hold
     the belt.
   - **Belt background (optional)** - a separate UI `Image` sibling using a
     material with `Soda/ConveyorBeltShader`, plus `ConveyorBeltShaderController`
     (auto-syncs scroll speed/direction to the belt's actual rotation - see
     that script's header comment). Assign it to `ConveyorBelt.backgroundVisual`
     so the two don't fight over draw order (see gotcha below).
   - **SodaBottle** - a `RectTransform` (with your bottle art as a child Image)
     plus `SodaBottle.cs`. Assign a `stackAnchor` child `RectTransform` for
     where accepted cards visually pile up, and hook up a `SodaColorController`.
   - **Soda liquid visual** - a UI `Image` using a material with the
     `Soda/SodaLiquid` shader (plain white sprite, Raycast Target off), or a
     separate world-space Renderer. `SodaColorController` auto-detects either.
   - **AdditiveCard prefab** - a UI GameObject using a **fixed-point anchor**
     (see gotcha below) with an `Image` (Raycast Target ON) for `artworkImage`,
     and `AdditiveCard.cs`. `RoundManager` instantiates this per additive, as a
     child of the belt's `RectTransform`.
   - **Drag layer** - an empty `RectTransform` as the *last* sibling under the
     Canvas (renders on top of everything), assigned to each card's `dragLayer`.
   - **Detail panel** - a UI panel with TMP_Text fields for name/description/
     stats and an `Image` for the icon preview, plus `AdditiveDetailPanel.cs`.
     **Keep this script's own GameObject always active** - see gotcha below.
   - **Floating score text prefab** - a small UI object with a TMP_Text and a
     CanvasGroup, plus `FloatingScoreText.cs`.
   - **Score FX player** - a `ScoreFXPlayer` somewhere in the scene; assign the
     floating text prefab, a top-level Canvas `RectTransform` as `textLayer`,
     an `AudioSource`, and the 5 category audio clips (see **Sound categories** below).
   - **Brew button** - any UI `Button`; just drag it into `RoundManager.brewButton`,
     no manual `OnClick()` wiring needed.
   - **Managers** - one GameObject (or several) holding `SodaScoringManager`,
     `ShopManager`, `ModifierManager`, and `RoundManager`. Wire their public
     fields to each other and to the belt/bottle/prefab/scoreFX above.

6. **Build the menus (all use `LerpPanel`):**
   - **Shop** - a panel with `LerpPanel` + `ShopMenuController`. Assign an
     `AdditiveCard` prefab (draggable off - the controller sets this itself)
     and a container `RectTransform` for the offer cards to spawn into.
   - **Syrup offers** - same pattern with `ModifierMenuController` and a
     `ModifierOfferButton` prefab (Button + 2 TMP_Text fields) instead.
   - **Pause menu** - a panel with `LerpPanel` + `PauseMenuController`. Wire a
     pause `Button` in the HUD, plus Resume/Main Menu/Deck Stats buttons inside
     the panel.
   - **Deck stats** - a panel with `LerpPanel` + `DeckStatsPanel`, opened from
     the pause menu's Deck Stats button. Uses the same `AdditiveCard` prefab
     as the shop, draggable off.

7. **Fill in `RoundManager`:**
   - `startingAdditives` - the templates the player owns at the start of a run.
   - `cardsPerRound` (default 10), `baseScoreRequirement`, `difficultyGrowth`,
     `maxAttempts` (default 3), `modifierEveryNRounds` (default 5).
   - Populate `ShopManager.additivePool` with every additive obtainable during
     a run, and `ModifierManager.modifierPool` with every syrup.

---

## Sound categories

`ScoreFXPlayer` plays a different clip per scoring event category, with pitch
climbing across the whole sequence regardless of which clip plays:

| Clip | Plays for |
|---|---|
| `pointsClip` | A points-adding fire (base trigger, not a retrigger) |
| `multClip` | A mult/x-mult-adding fire (base trigger, not a retrigger) |
| `retriggerClip` | **Overrides** points/mult above - any fire that's the 2nd+ time an effect went off this scoring pass (from `baseRetriggerCount`, a syrup's flavor-retrigger bonus, or a combo's retrigger), regardless of whether it added points or mult |
| `buffClip` | `BuffRandomAdditivePoints/Mult` permanently boosting another owned additive |
| `addedToDeckClip` | `AddAdditiveToDeck` permanently adding a new additive |
| `fallbackClip` | Used if the category-specific clip above is left empty |

The floating text/color for a retriggered fire still shows the actual points/mult
value earned - only the *sound* changes for retriggers, not the number or its color.

---

## Common setup gotchas

These are real issues we hit while building this out - check here first if
something visual looks broken before assuming it's a code bug.

- **"Only one card seems to spawn."** `ConveyorBelt` computes its ellipse
  radius as a *percentage of its own RectTransform's size*. If that
  RectTransform has zero/near-zero width or height (collapsed anchors,
  `sizeDelta` 0), every card's target position collapses to the same point -
  all 10 spawn, they're just stacked exactly on top of each other. `ConveyorBelt`
  logs a warning at Start() if its size looks too small; give it a real size.

- **"Cards look contorted / stretched when reparented."** The card prefab's
  `RectTransform` needs a **fixed-point anchor** (anchor min == anchor max),
  not a stretch anchor. A card gets reparented three times over its life
  (belt -> drag layer -> bottle stack); if its anchors are inherited "stretch"
  values, it distorts to fill whatever percentage of its *new* parent's size
  those values work out to, every time. Fix: select the anchor preset icon on
  the card's RectTransform, hold **Alt+Shift**, click "middle center" - this
  converts to a fixed point while keeping the object visually in place - then
  set an explicit Width/Height.

- **"The detail panel never shows up."** By far the most common cause: the
  `AdditiveDetailPanel` script's own GameObject was disabled to "hide" the
  panel by default. Unity never calls `OnEnable()` on a disabled object, so
  the event subscriptions that make the whole thing work never happen. Keep
  the object this script lives on **always active**; toggle the separate
  `panelRoot` field instead. `OnEnable()` logs errors/warnings for missing
  reference fields, and `OnValidate()` (Editor-only, runs even while disabled)
  specifically warns if this object itself is turned off.

- **RectTransform showing Left/Top/Right/Bottom instead of Width/Height?**
  That means it's using stretch anchors (anchor min != anchor max), which
  derive size from the parent rather than taking an explicit size. See the
  "contorted" gotcha above for the fix.

- **"The belt/cards are rendering behind the belt's own background image."**
  `ConveyorBelt.alwaysRenderAtBack` keeps the belt behind *other* UI (bottle,
  buttons, panels), but if you also have a dedicated background/track image
  sibling (e.g. from `ConveyorBeltShaderController`), assign it to
  `ConveyorBelt.backgroundVisual` - otherwise both objects will fight over the
  very first sibling slot and the belt can end up behind its own background.

---

## The game loop

```
BeginRound()
  -> draw cardsPerRound additives from the owned collection onto the belt
  -> set the round's score requirement (baseScoreRequirement * difficultyGrowth^round)

Playing
  -> player drags additives from belt to bottle (max 5)
  -> picking up a card already in the bottle immediately frees that slot
  -> hovering ~0.5s, OR picking a card up at all, shows its details + icon
  -> press Brew -> OnBrewPressed()

Scoring (one attempt)
  -> build a ScoringContext (cup, owned collection, belt flavor counts, card-rect resolver)
  -> SodaScoringManager.ScoreSoda(context) - instant, deterministic, also returns
     an ordered ScoreEvent list recording every individual scoring step
  -> ScoreFXPlayer plays that list back: a floating popup + a category-specific
     sound per event, pitch climbing with each one - RoundManager waits for this
     before continuing
  -> total >= requirement?  -> round won
  -> total <  requirement, attempts left? -> bottle clears back to belt, try again
  -> total <  requirement, no attempts left? -> game over

Round won
  -> efficiency = attemptsRemaining / (EffectiveMaxAttempts - 1)   [1.0 = won first try]
  -> every Nth round: ModifierMenuController shows syrup offers first
  -> then: ShopMenuController shows 4 additive offers (efficiency-weighted odds)
  -> picks are added to the owned collection -> next BeginRound()

Anytime
  -> pause button -> PauseMenuController opens (Resume / Main Menu / Deck Stats)
  -> Deck Stats -> DeckStatsPanel lists every additive in RoundManager.OwnedAdditives
```

Attempts are **reusable within a round** - the same 10-card hand persists
across all 3 tries; a failed attempt just clears the bottle rather than
consuming the hand.

---

## Scoring order (what happens inside `ScoreSoda`)

1. For each additive in the cup: fire its effect `1 + retriggers` times
   (own retrigger count + any syrup-granted retrigger-for-flavor bonus),
   then add any flat flavor point/mult bonus from active syrups. Every fire
   appends a `ScoreEvent`, tagged `isRetrigger` for every fire after the first.
2. Apply syrup belt-scaling bonuses once each (based on what's still on the
   belt, not the cup).
3. Evaluate every `FlavorComboRule` against the finished cup; each rule that
   matches fires its bonus `EffectiveTriggerCount` times (normally 1, more
   with a retrigger-combo syrup); a `RetriggerMatchingAdditives` bonus tags
   its fires `isRetrigger` too.
4. Add any global mult bonus from syrups.
5. `total = points * mult`.

The whole pass is synchronous and instant - `ScoreFXPlayer` replays the
resulting event list afterward at its own pace, fully decoupled from the math.

---

## Additive effect types

| EffectType | What it does |
|---|---|
| `FlatPoints` / `FlatMult` / `XMult` | Simple, self-contained scoring |
| `RetriggerSelf` | Fires `1 + baseRetriggerCount` times, applying `amount` as points or mult (per `retriggerPayload`) **every** fire |
| `BuffRandomAdditivePoints` / `Mult` | Permanently buffs a random **other** owned additive |
| `PointsPerFlavorOnBelt` / `MultPerFlavorOnBelt` | Scales with how many additives of `scalingFlavor` are still on the belt |
| `AddAdditiveToDeck` | Adds `additiveToAdd` to your owned collection (once, unless `addToDeckOnlyOnce` is off) |

## Syrup (modifier) effect types

| ModifierEffectType | What it does |
|---|---|
| `BoostComboRule` | Permanently raises a specific combo's payout |
| `RetriggerComboRule` | Makes a specific combo fire extra times per soda |
| `GlobalMult` | Flat mult added to every soda from now on |
| `RetriggerForFlavor` | Cup additives of a flavor gain extra fires |
| `BoostFlavorPoints` / `BoostFlavorMult` | Cup additives of a flavor gain flat bonus points/mult |
| `ScalePerFlavorOnBelt` | Bonus scaling with belt-flavor count, added once per soda |
| `AddAdditiveToDeck` | Immediately adds a specific additive to your collection |
| `AddAttempt` | Permanently grants extra attempts per round for the rest of the run |

---

## Design notes & caveats

- **Never mutate `AdditiveData`/`FlavorComboRule`/`ModifierData` assets'
  serialized fields at runtime.** They're shared ScriptableObject assets -
  writing to a serialized field persists in the editor across play sessions.
  Runtime state belongs on `AdditiveInstance` (bonus fields) or on the
  `[NonSerialized]` runtime fields already on `FlavorComboRule`. Follow this
  pattern for any new mechanic you add.
- **Flavor-wide syrup bonuses are ongoing rules, not one-time stamps.**
  `BoostFlavorPoints/Mult`, `RetriggerForFlavor`, and belt-scaling live in
  `ModifierManager` and are re-checked every scoring pass, so additives
  bought *after* picking the syrup still benefit.
- **`BuffRandomAdditivePoints/Mult` can target any owned additive**,
  including ones not in this round's drawn hand (mirrors Balatro jokers
  buffing cards you're not currently holding).
- **Attempts are reusable, not consumed.** All 3 tries use the same 10-card
  hand; a failed attempt clears the bottle and lets you retry.
- **Card depth sorting is Y-position-based, belt-only.** Canvas UI has no
  per-object sorting layers like `SpriteRenderer` - `ConveyorBelt` re-sorts
  its children's sibling index by Y every frame instead (higher up = drawn
  further back). This doesn't apply to the bottle stack or the drag layer.
- **`ConveyorBelt.alwaysRenderAtBack`** pins the belt to the first sibling
  slot in its parent every frame (cheap - only reorders if something knocked
  it out of place), so it can never end up rendered on top of other gameplay
  UI. Assign `backgroundVisual` if you have a separate belt-track image so
  the two don't contest the same slot.
- **`LerpPanel` animates on unscaled time**, specifically so the pause menu
  keeps animating smoothly even if `PauseMenuController.freezeTimeScale`
  sets `Time.timeScale` to 0.
- **Difficulty curve is a simple exponential**
  (`baseScoreRequirement * difficultyGrowth^round`) - tune `difficultyGrowth`
  by playtesting against your actual scoring numbers.
- Both shaders target the **Built-in Render Pipeline**; see the conversion
  notes at the bottom of each shader file for URP. Both work fine on a UI
  `Image` as-is - `SodaColorController`/`ConveyorBeltShaderController` clone
  the material at startup so they never edit the shared asset.

---

## Extending this

- **New additive effect**: add a case to `EffectType`, add any needed fields
  to `AdditiveData`, implement the case in `AdditiveInstance.ApplyEffect`
  (call `emit` if it should show a score popup/sound), and add its
  field-drawing case to `SodaEditorFields.DrawAdditive`.
- **New syrup effect**: same pattern in `ModifierEffectType`, `ModifierData`,
  `ModifierManager.ApplyModifier`, and `SodaEditorFields.DrawModifier`.
- **New flavor family**: add to the `FlavorType` enum and give it a color in
  `FlavorPalette` - everything else reads the enum generically.
- **New menu**: add a `LerpPanel` to your panel's RectTransform, write a small
  controller that calls `Show()`/`Hide()`, and hook it to whatever
  `RoundManager` event or button should trigger it - `ShopMenuController` and
  `ModifierMenuController` are the reference examples.