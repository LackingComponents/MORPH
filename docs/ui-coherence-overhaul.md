# UI Coherence Overhaul — OrthoPlanner

## Problem

The app has one theme file (`DarkTheme.xaml`) that defines 10 named colors, but the actual UI uses **20+ different hardcoded hex values** across MainWindow.xaml and dialog windows. Same-purpose controls (steppers, textboxes, labels, toggles, dividers) are styled inconsistently. The result: UI elements that should feel like the same system instead read as hand-crafted one-offs.

**The surgery stepper style is the canonical one** — transparent bg, `∧`/`∨` chevrons with horizontal stretch, SemiBold 13pt centered value text, numeric input guard. Everything else converges toward it.

---

## Token System — Expanded Palette

Add these to `DarkTheme.xaml`. Every new token displaces a hardcoded hex value somewhere.

| Token name | Hex | Replaces | Used by |
|---|---|---|---|
| `AccentBright` | `#FF1B98E0` | `#FF1B98E0` (8×), `#FF1E80B4` (2×) | Osteotomy buttons, NHP "DONE", active toggle check, tree highlight |
| `AccentBrightHover` | `#FF1B6AB0` | `#FF1B6AB0` (2×) | Secondary osteotomy buttons |
| `InputBg` | `#FF30343D` | `#FF30343D` (34×) | TextBox bg, toggle bg, stepper bg (old), separator fills |
| `CardBg` | `#FF1E222A` | `#FF1E222A` (9×), `#FF21252B` (6×) | Surgery boxes, NHP popup, info panel, smoothing panels |
| `SubtleText` | `#FFA0AAB5` | `#FFA0AAB5` (44×) | Secondary labels — replaces the current `TextSecondary` role |
| `HoverBg` | `#FF2B3240` | `#FF2B3240` | Tab hover |
| `ActiveBg` | `#FF3B4559` | `#FF3B4559` | Tab active/pressed |
| `DeepBg` | `#FF101418` | `#FF101418` (4×), `#FF0C1018` (2×), `#FF0E1420` (6× across dialogs) | Histogram backgrounds, enlarged overlay, dialog bg |

**Existing tokens to keep unchanged:** `BgDark`, `BgMedium`, `BgLight`, `BgHover`, `Accent` (slate-blue), `AccentHover`, `TextPrimary`, `Border`, `Success`, `Warning`.

**Retire `TextSecondary` (`#FF6E7F90`)** — it's only used 2× in MainWindow and is visually too muted for labels. `SubtleText` (`#FFA0AAB5`) is what's actually in use everywhere. Keep the brush key but remap it to `#FFA0AAB5` value. The 2 existing uses at lines 1322 and 1347 already look correct with `#FF6E7F90`; they'll improve slightly with the lighter value.

---

## Step 1 — Shared Numeric Stepper Styles → DarkTheme.xaml

Move the surgery stepper pattern into `DarkTheme.xaml` as shared styles. Improve them:

### `NumericStepperButtonStyle` (TargetType=RepeatButton)

Replace both `SurgeryStepperButtonStyle` and NHP's `VerticalStepperButtonStyle`.

Current surgery style:
- Transparent bg, `∧`/`∨` content, `ScaleTransform ScaleX=2.5`, `FontSize=14`, `Height=18`

Improvements:
- Add `Cursor="Hand"`
- Add hover: on `IsMouseOver`, shift Foreground to `{StaticResource AccentBrightBrush}` — gives tactile feedback without a bg fill
- Add `IsEnabled` opacity 0.4 trigger (matches global button pattern)
- Template: `Border Background="Transparent"` → swap to `AccentBrightBrush` foreground on hover

```xml
<Style x:Key="NumericStepperButtonStyle" TargetType="RepeatButton">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
    <Setter Property="Height" Value="18" />
    <Setter Property="MinWidth" Value="20" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RepeatButton">
                <Border x:Name="Bd" Background="Transparent">
                    <ContentPresenter x:Name="Arrow" HorizontalAlignment="Center" VerticalAlignment="Center">
                        <ContentPresenter.LayoutTransform>
                            <ScaleTransform ScaleX="2.5" ScaleY="1.0" />
                        </ContentPresenter.LayoutTransform>
                    </ContentPresenter>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Arrow" Property="TextElement.Foreground"
                                Value="{StaticResource AccentBrightBrush}" />
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.4" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

### `NumericValueTextBoxStyle` (TargetType=TextBox)

Replace both `SurgeryTextBoxStyle` and the inline NHP TextBox style.

Current surgery style:
- Transparent bg, White fg, Center, SemiBold 13pt, 44×30, numeric guard EventSetters

Improvements:
- Add focused state: `BorderBrush="{StaticResource AccentBrightBrush}"` + `BorderThickness="0,0,0,2"` bottom accent line (subtle, no box). This requires a template since the default TextBox template draws a border rect — use a `Border` with `CornerRadius="2"` bottom-only accent.
- Actually simpler: use `BorderThickness="0"` + a `Border` wrapper that shows a 2px bottom line on focus. Implementation below keeps it minimal — just add a bottom-border highlight via template trigger.

```xml
<Style x:Key="NumericValueTextBoxStyle" TargetType="TextBox">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="TextAlignment" Value="Center" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Height" Value="30" />
    <Setter Property="Width" Value="44" />
    <Setter Property="Margin" Value="0" />
    <Setter Property="CaretBrush" Value="{StaticResource AccentBrightBrush}" />
    <!-- EventSetters cannot go in global ResourceDictionary styles (no code-behind).
         These will be added as inline handlers in MainWindow.xaml since that's where
         the handlers live. Alternatively, move handlers to an attached behavior. -->
</Style>
```

**Note on EventSetters:** WPF `EventSetter` can only live inside a style that's in the same XAML file as the handler code-behind. Since `DarkTheme.xaml` is a `ResourceDictionary` with no code-behind, the numeric guard EventSetters (`PreviewTextInput`, `DataObject.Pasting`, `LostFocus`) must stay on the inline styles in `MainWindow.xaml`, or we use an attached-behavior pattern. **Decision: keep EventSetters inline in MainWindow.xaml** — add them to a `BasedOn` style override that inherits from `NumericValueTextBoxStyle`. This is one line per style and is the standard WPF approach for code-behind handlers.

### `NumericStepperRowStyle` (TargetType=StackPanel)

```xml
<Style x:Key="NumericStepperRowStyle" TargetType="StackPanel">
    <Setter Property="Orientation" Value="Horizontal" />
    <Setter Property="HorizontalAlignment" Value="Center" />
    <Setter Property="VerticalAlignment" Value="Center" />
</Style>
```

---

## Step 2 — Refactor NHP Popup to Match Surgery Pattern

Current NHP layout (lines 226–365):
- 2-column Grid per row: label left, textbox+arrows right
- `VerticalStepperButtonStyle` with `▲`/`▼` (bold block arrows), `Width=20`, `Height=12`, `Background=#FF30343D`
- TextBox `Width=50`, `Background=#FF30343D`, `Foreground=White`, no numeric guard
- Section headers: "Translations (mm)", "Rotations (°)" in `#FFA0AB5`

New NHP layout — converge to surgery's column-based arrangement:

### Structural change

The NHP popup is narrow (Width=220). The surgery pattern puts label above the value row in centered columns. For NHP, the label-on-the-left / value-on-the-right is actually better for the narrow popup — keep that structure. But the **value+arrows interaction pattern** must match surgery.

Per-row layout becomes:
```
Label text left          [  0.3  ] [∧]
                                  [∨]
```

### Specific changes per NHP row

For each of the 6 rows (Lat, Ant, Vert, Pitch, Roll, Yaw):

1. **TextBox**: replace inline style with `NumericValueTextBoxStyle` (with inline `BasedOn` override adding EventSetters + adjusting `Width` from 44→48 because NHP shows `F1` with longer labels). Remove hardcoded `Background="#FF30343D"`, `Foreground="White"`, `Width="50"`.
2. **Arrow buttons**: replace `VerticalStepperButtonStyle` with `NumericStepperButtonStyle`. Change content from `▲`/`▼` to `∧`/`∨`. Remove old `Style=` reference. Keep `Margin="0,0,0,1"` on the upper button.
3. **Arrow wrapper StackPanel**: add `VerticalAlignment="Center"` (same as surgery).
4. **Stepper row StackPanel** (horizontal): add `Style="{StaticResource NumericStepperRowStyle}"`.
5. Remove the `DarkStepperButtonStyle` and `VerticalStepperButtonStyle` from NHP `StackPanel.Resources` — they're now dead.
6. Add numeric guard EventSetters to the NHP TextBox override style.

### DONE button

Line 349–368: `Background="#FF1E80B4"` → `Background="{StaticResource AccentBrightBrush}"`. Remove inline `Foreground="White"` and `FontWeight="Bold"` — those should come from an `AccentButton` style or be set on the style.

### Popup border

Line 227: `Background="#FF21252B"` → `Background="{StaticResource CardBgBrush}"`, `BorderBrush="#FF30343D"` → `BorderBrush="{StaticResource BorderBrush}"`.

### Section headers

Line 270/309: `Foreground="#FFA0AAB5"` → `Foreground="{StaticResource SubtleTextBrush}"`.
Line 267: `Foreground="White"` → `Foreground="{StaticResource TextPrimaryBrush}"`.
Line 274 etc. per-row labels: `Foreground="White"` → `Foreground="{StaticResource TextPrimaryBrush}"`.

### Separator

Line 347: `Background="#FF30343D"` → `Background="{StaticResource InputBgBrush}"`.

---

## Step 3 — Refactor Surgery Panel to Use Shared Styles

Current surgery styles are local to `StackPanel.Resources` (lines 1469–1512).

1. Move `SurgeryStepperButtonStyle` → replaced by `NumericStepperButtonStyle` (from DarkTheme).
2. Move `SurgeryTextBoxStyle` → replaced by `NumericValueTextBoxStyle` + inline override for EventSetters.
3. Move `SurgeryStepperRowStyle` → replaced by `NumericStepperRowStyle` (from DarkTheme).
4. Remove the local `StackPanel.Resources` entirely.
5. Replace all `Style="{StaticResource SurgeryStepperButtonStyle}"` → `Style="{StaticResource NumericStepperButtonStyle}"`.
6. Replace all `Style="{StaticResource SurgeryTextBoxStyle}"` → `Style="{StaticResource NumericValueTextBoxStyle}"` (plus inline EventSetter override).
7. Replace all `Style="{StaticResource SurgeryStepperRowStyle}"` → `Style="{StaticResource NumericStepperRowStyle}"`.
8. Surgery box borders: `Background="#FF1E222A"` → `CardBgBrush`, `BorderBrush="#FF30343D"` → `BorderBrush`.
9. Surgery section labels "AP (Y)" etc.: `Foreground="#FFA0AAB5"` → `SubtleTextBrush`.
10. Surgery section headers "MAXILLA", "MANDIBLE" etc.: `Foreground="White"` → `TextPrimaryBrush`.
11. Surgery mode selector border: same CardBgBrush + BorderBrush swap.
12. Surgery mode context menu: remove inline `Background="#FF1E222A" BorderBrush="#FF30343D"` → let themed ContextMenu apply.

---

## Step 4 — Replace All Hardcoded Colors in MainWindow.xaml

Systematic find-replace. Every occurrence of these hex values:

| Old hardcoded | New brush reference | Count |
|---|---|---|
| `#FFA0AAB5` | `{StaticResource SubtleTextBrush}` | 44 |
| `#FF30343D` | `{StaticResource InputBgBrush}` | 34 |
| `#FF1E222A` | `{StaticResource CardBgBrush}` | 9 |
| `#FF1B98E0` | `{StaticResource AccentBrightBrush}` | 8 |
| `#FF21252B` | `{StaticResource CardBgBrush}` | 6 |
| `#FF1B6AB0` | `{StaticResource AccentBrightHoverBrush}` | 2 |
| `#FFAAB4C0` | `{StaticResource TextPrimaryBrush}` | 4 |
| `#FF101418` | `{StaticResource DeepBgBrush}` | 4 |
| `#FF0C1018` | `{StaticResource DeepBgBrush}` | 2 |
| `#FF1E80B4` | `{StaticResource AccentBrightBrush}` | 2 |
| `#FF2B3240` | `{StaticResource HoverBgBrush}` | 1 (tab style) |
| `#FF3B4559` | `{StaticResource ActiveBgBrush}` | 2 |
| `#FF424D63` | `{StaticResource ActiveBgBrush}` | 1 (tab border) |
| `#FF404552` | `{StaticResource ActiveBgBrush}` | 1 (toggle hover) |
| `#FF3A4A5A` | `{StaticResource BorderBrush}` | 1 (toolbar divider) |
| `#FF282E3A` | `{StaticResource BorderBrush}` | 1 (measurement rect) |
| `#FF1A1F28` | `{StaticResource BgMediumBrush}` | 1 (measurement header bar) |
| `#FF0E1420` | `{StaticResource DeepBgBrush}` | (dialog windows) |
| `#888` | `{StaticResource TextSecondaryBrush}` | 2 (✕ buttons) |
| `#FF0FF……` alpha patterns | keep (these are semi-transparent overlays like `#0FFFFFFF`) | — |

**Special cases:**
- `Foreground="White"` on 30+ inline controls → `{StaticResource TextPrimaryBrush}` (White is #FFFFFFFF, TextPrimary is #FFD0D8E0 — close enough for a dark UI and more coherent)
- `Background="#30FFFFFF"` (surgery "＋ Plan" button) — keep as-is, it's a deliberate semi-transparent overlay
- `#FF505060` (occlusion plan inactive dot) — replace with `{StaticResource TextSecondaryBrush}` (close enough)
- `#FF1B98E0` used as `Foreground` on tree items (lines 969, 1003, 1027) → `{StaticResource AccentBrightBrush}`

---

## Step 5 — Normalize `PanelTabButtonStyle`

Lines 72–102. Currently all hardcoded. Refactor:

```xml
<Style x:Key="PanelTabButtonStyle" TargetType="RadioButton">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource SubtleTextBrush}" />
    <Setter Property="BorderThickness" Value="1,1,1,0" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Padding" Value="12,8" />
    <Setter Property="Cursor" Value="Hand" />
    <!-- IsMouseOver: Background → HoverBgBrush, Foreground → TextPrimaryBrush -->
    <!-- IsChecked: Background → ActiveBgBrush, BorderBrush → BorderBrush -->
</Style>
```

---

## Step 6 — Normalize `DarkToggleButtonStyle`

Lines 103–126. Currently:
- Base: `Background=#FF30343D` → `{StaticResource InputBgBrush}`
- Hover: `#FF404552` → `{StaticResource ActiveBgBrush}`
- Checked: `#FF1E80B4` → `{StaticResource AccentBrightBrush}`
- Foreground: `White` → `{StaticResource TextPrimaryBrush}`

Also: `GearToggleButtonStyle` (lines 128–162) already uses theme brushes correctly — no change needed. Make `DarkToggleButtonStyle` match its structure.

---

## Step 7 — Normalize Osteotomy Buttons

Lines 886–898. Replace inline `Background="#FF1B98E0"` / `Background="#FF1B6AB0"` with:

```xml
<Style x:Key="AccentBrightButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{StaticResource AccentBrightBrush}" />
    <Setter Property="Foreground" Value="#FFFFFFFF" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
```

Primary actions (Plan Le Fort 1, Plan BSSO, Plan Genioplasty) use `AccentBrightButton`.
Secondary actions (2-Piece Sagittal, 3-Piece Y-Cut) use a dimmer variant or just `BasedOn` default button with `AccentBrightHoverBrush`.

---

## Step 8 — Normalize Font Sizes for Secondary Labels

Current spread for what's functionally the same "secondary label" role:

| Location | Current | Target |
|---|---|---|
| NHP "Translations (mm)" | 10 | 10 |
| Smoothing "Parts Kept:" | 11 | 10 |
| Surgery "AP (Y)" | 9 | 10 |
| Measurement detail labels | 11 | 10 |
| Patient info "Name:" | 11 | 10 |

Create a `SecondaryLabel` style in DarkTheme:

```xml
<Style x:Key="SecondaryLabel" TargetType="TextBlock">
    <Setter Property="FontSize" Value="10" />
    <Setter Property="Foreground" Value="{StaticResource SubtleTextBrush}" />
</Style>
```

Apply everywhere the table above lists. Single-line changes per label.

---

## Step 9 — Normalize Dividers

Three different divider colors for the same visual purpose:

| Location | Current |统一 |
|---|---|---|
| NHP `Separator Background="#FF30343D"` | `#FF30343D` | `InputBgBrush` |
| Measurement `Rectangle Fill="#FF282E3A"` | `#FF282E3A` | `BorderBrush` |
| Toolbar `Background="#FF3A4A5A"` | `#FF3A4A5A` | `BorderBrush` |

**Decision:** All dividers use `{StaticResource BorderBrush}` except the NHP separator which is a thicker "section break" and uses `InputBgBrush`. Rationale: `BorderBrush` is the existing thin-line token; `InputBgBrush` is for filled areas.

---

## Step 10 — ✕ Button Color

Lines 47/63: `Foreground="#888"` → `{StaticResource TextSecondaryBrush}`.

Also applies to the duplicated pattern in "Imported Meshes" items (lines 839–845).

---

## Step 11 — Context Menu Inline Overrides

Line 32: `<ContextMenu Background="#FF1E222A" BorderBrush="#FF30343D">` — remove both overrides. The themed `ContextMenu` style already sets `Background="{StaticResource BgMediumBrush}"` and `BorderBrush="{StaticResource BorderBrush}"`.

Line 1618: Same in surgery occlusion context menu — remove inline overrides.

---

## Step 12 — Dialog Window Backgrounds

Across CondyleSplitWindow, MandibleAutorotationWindow, SplintPlannerWindow, SplintSequenceWindow, DentalAlignmentWindow:

All use `#FF0E1420` for window bg and `#FF0C1018` for viewport bg. Replace with `DeepBgBrush` and `BgDarkBrush` respectively.

These windows should also set:
```xml
Background="{StaticResource BgDarkBrush}"
Foreground="{StaticResource TextPrimaryBrush}"
```

The `#FF2ECC71` (green) on autorotation/splint windows → keep as semantic "success/good" indicator → `{StaticResource SuccessBrush}` (already in theme).

---

## Execution Order

1. **DarkTheme.xaml** — add new Color + Brush tokens, add `NumericStepperButtonStyle`, `NumericValueTextBoxStyle`, `NumericStepperRowStyle`, `AccentBrightButton`, `SecondaryLabel`
2. **MainWindow.xaml** — step 2 (NHP refactor), step 3 (surgery refactor), step 4 (global hex→brush), step 5 (tab style), step 6 (toggle style), step 7 (osteotomy buttons), step 8 (font sizes), step 9 (dividers), step 10 (✕ buttons), step 11 (context menus)
3. **Dialog windows** — step 12
4. **Build + test** after each step group

---

## Verification Checklist

- [ ] `dotnet build` — 0 errors after each step
- [ ] NHP popup: chevrons match surgery, values centered, numeric guard works
- [ ] Surgery panel: identical visual as before (chevrons, layout), just using shared styles
- [ ] No hardcoded `#FF` colors remain in MainWindow.xaml (except semi-transparent `#0F`/`#1F`/`#30` overlays and chart colors)
- [ ] Tab hover/active states look coherent with global hover/accent tokens
- [ ] Osteotomy buttons match accent system
- [ ] All secondary labels same font size
- [ ] Tool-options TextBoxes also get numeric guard (smoothing passes, parts kept) — extend `NumericValueTextBoxStyle`
- [ ] Context menus render with themed style, no inline overrides
