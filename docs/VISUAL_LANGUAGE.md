# Scriptorium: Fluent Cinema

Phase 1 defines the global visual system. Page composition, navigation, media queries,
scanning, persistence, and playback commands are unchanged. Existing page-specific
layout literals will be migrated with their respective page redesigns; do not add
new visual literals to views.

## Resource architecture

App.xaml retains its existing merge order: Colors, Typography, Layout, Elevation,
Motion, Controls, then Library. Controls.xaml is a compatibility entry point that
merges Buttons, Surfaces, Inputs, Toggles, Menus, and Chrome. Keep dependencies before
their consumers. Existing keys remain available to existing views.

| Dictionary | Responsibility |
| --- | --- |
| Theme/Colors.xaml | Dark semantic colors and brushes, including artwork/playback roles |
| Theme/Typography.xaml | Font fallback, type scale, named text roles |
| Theme/Layout.xaml | Spacing, semantic padding/margins, radii, border widths, dimensions |
| Theme/Elevation.xaml | Shared shadows; no per-view shadow definitions |
| Theme/Motion.xaml | Durations, easing, and movement distances |
| Theme/Buttons.xaml | Buttons, navigation states, keyboard focus, color swatches |
| Theme/Surfaces.xaml | Surfaces, cards, dialogs, badges, search frame, section headers |
| Theme/Inputs.xaml | Text/search fields, ComboBoxes and item presentation |
| Theme/Toggles.xaml | Checkboxes, switches, filter toggles, chips, radio styling |
| Theme/Menus.xaml | Tooltips, contextual actions, checked items, submenus, separators |
| Theme/Chrome.xaml | Existing caption controls and scrollbars; shell dimensions preserved |
| Resources/Library.xaml | Library-specific compositions and compatibility aliases |

Use DynamicResource for brushes so live theme switching works. Use StaticResource
for dimensions and styles, except cross-dictionary styles inside detached popup
templates, which use dynamic lookup. Library.SearchField, Library.ComboBox,
Library.FilterToggle, and Library.RowIconToggle compose global resources rather
than copying templates.

ThemeService retains the existing Light/System behavior and the light palette.
Every new theme role must exist in both Colors.xaml and ThemeService.LightColors;
the service captures the matching dark brushes and replaces frozen brush resources
when themes change. Artwork scrims and playback black remain dark in both themes.

## Color roles

| Role | Dark color |
| --- | --- |
| Background | #0B0B0D |
| Surface / SurfaceHeader | #101012 |
| SurfaceElevated | #17171A |
| SurfaceOverlay (hover) | #1E1E22 |
| SurfacePressed | #26262B |
| Border / BorderStrong | white at alpha 12 / 1C |
| TextPrimary / TextSecondary / TextMuted | #F5F5F7 / #A7A7AD / #73737A |
| Accent / AccentStrong (hover) | #FF9D00 / #FFAD24 |
| AccentMuted / AccentBorder | orange at alpha 18 / 55 |
| Success / Danger | #63D297 / #FF6B6B |

WPF eight-digit colors use **#AARRGGBB**. The supplied CSS-style #FFFFFF12
therefore becomes #12FFFFFF; #FF9D0018 becomes #18FF9D00.

Orange means primary action, active selection, playback progress, or focus. Routine
hover uses neutral surfaces. Metadata uses secondary/muted text. Inactive favorite
icons are neutral; active favorites are orange. Use TextOnAccent and TextOnDanger
for readable filled-button labels, not the primary text brush. User category colors
and selectable swatch values are data, not theme colors.

## Typography and spacing

FontFamily.Application prefers Segoe UI Variable Text, falling back to Segoe UI.
Named roles are: page title 32 semibold; section 22 semibold; panel 20; subsection 17;
card title 16 semibold; body/secondary 14; metadata 12; labels 13; controls 14.
Use Text.PageTitle, Text.SectionTitle, Text.CardTitle, Text.Body,
Text.Secondary, Text.Metadata, and Text.Label. Section.Header is a content
control style for a section title; Text.SectionHeader is the text-only equivalent.

Existing Space.* and Spacing.* keys remain compatible. The main spacing steps
are 8, 12, 16, 24, 32, 40, and 48. Space.40 is a double; Spacing.40 is a Thickness.
Use semantic Padding.* / Margin.* roles for reusable component geometry.
Do not put Double resources into GridLength properties: use the corresponding
GridLength tokens. Add a semantic token when a genuinely new component needs one.

Radii: Radius.Control 6; Radius.Input 8; Radius.Surface 10; Radius.Card 12.
Round switch tracks/thumbs only where the control's shape warrants it. Legacy
corner-radius keys remain available. Cards are flat by default; use Elevation.2
for flyouts and Elevation.3 for dialogs, not a shadow on every nested surface.

## Component states and behavior

- Buttons: Button.Primary, Secondary, Destructive, Text, and Icon share
  a template, dimensions, disabled opacity, and keyboard focus. Secondary hover
  does not use an orange outline.
- Focus.Keyboard uses WPF's keyboard-focus adorner without shifting content.
  Inputs additionally show their focus border. Validation uses Brush.Danger.
- Input.SearchTextBox is the inner field; pair it with Input.SearchField when
  a search icon/placeholder is supplied by the containing view.
- ComboBoxes retain dropdown scrolling and support DisplayMemberPath,
  SelectedValuePath, explicit ComboBoxItems, and ItemTemplate (including playback
  speed formatting). No command or selection semantics belong in their styles.
- Chip is an interactive ToggleButton; Badge is noninteractive information.
  Badge.Active is reserved for active state.
- Menu.Item.Destructive marks destructive context actions. Standard menu
  keyboard routing, commands, check state, and submenu behavior are retained.
- Implicit button, input, checkbox, radio, tooltip, and menu styles cover otherwise
  unstyled standard controls. Existing explicit keys remain supported.

## Motion

Durations are Instant 100 ms, Fast 150 ms, Standard 200 ms, Deliberate 300 ms,
and Slow 450 ms. Enter/Exit/Standard cubic easing is centralized. Do not animate
layout dimensions or hardcode animation offsets in views.

MotionBehavior.HoverLift uses the shared distance (-2 DIPs), Fast timing, and
easing. It checks Windows client-area animation and high-contrast settings for
each interaction and clears its animation when the control unloads/recycles.
The Page.Enter compatibility style intentionally has no ancestor transform:
LibVLC native video hosts must keep stable coordinates. Popup transitions are
instant. Button/toggle states update immediately.

## Verification

Build the complete solution, then run its tests. The existing WPF playback test
also invokes DesignSystemResourceChecks in its single STA Application. This
loads real resource dictionaries, renders controls, checks ComboBox names and
ItemTemplate output, opens dropdowns/context menus/submenus/tooltips, traces binding
errors, tests live light/dark brush replacement, and verifies command invocation.
It does not start Scriptorium or access the user's database. A generated
fluent-cinema-resources.png in the test project's ignored build output supports
visual inspection. Playback/fullscreen continuity is verified afterward.
