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

## Phase 2: native shell

MainWindow uses the real Windows non-client title bar (SingleBorderWindow,
CanResize, no per-pixel transparency). Windows supplies the small application icon,
caption, caption buttons, system menu, Snap support, drag/resize hit testing and
maximized work-area behavior. There is no WindowChrome replacement or manual
WM_GETMINMAXINFO override. The window's actual outer corners belong to DWM.

WindowBackdropController decorates the HWND using documented DWM attributes:
DWMWA_USE_IMMERSIVE_DARK_MODE (20), DWMWA_WINDOW_CORNER_PREFERENCE (33, ROUND),
and DWMWA_SYSTEMBACKDROP_TYPE (38, MAINWINDOW). Mica requires Windows 11 build
22621 or newer; rounded corners require build 22000. Unsupported attributes are
best effort: failed HRESULTs never prevent the window from opening. Mica also
requires transparency effects, a local session and high contrast to be off.
DwmExtendFrameIntoClientArea extends the backdrop through the transparent sidebar;
content pages retain their opaque background. Failure to enable either backdrop
or extended frame resets the backdrop and uses Brush.SurfaceHeader. DWM can
independently simplify its material for power or inactive-window conditions.

The controller refreshes on preference, theme and composition changes, tracks
light/dark app brushes through a dynamic-resource dependency property, and removes
its HWND hook and system-preference subscription when the window closes. It does
not intercept navigation, input, playback, DPI changes or sizing. The application
manifest requests PerMonitorV2 with PerMonitor/legacy DPI declarations as fallback;
WPF owns scaling, and shell dimensions remain device-independent pixels.

Shell.xaml groups the shell geometry, Fluent/MDL2 icon fallback, styles and motion.
Its explicit Buttons.xaml dependency avoids deferred StaticResource lookup failures
inside navigation templates. Controls.xaml merges this dictionary first. Sidebar
width is 232 DIPs, switching to 72 below a 1000-DIP window width. Settings is docked
at the bottom; primary navigation scrolls independently if space is constrained.
There are no changes to ShellViewModel, NavigationItem, commands or page templates.
The existing English navigation labels select the five icon glyphs in the style;
if localization is introduced, use a stable presentation key for this mapping.

Brush.NavigationSelected is white at 4% opacity in dark mode and black at 4% in
light mode. Orange marks selected icons and the 3-DIP indicator. OpacityTransition
animates independent overlays (120 ms hover/press, 150 ms selection), using the
shared easing and respecting Windows animation/high-contrast preferences. There
are no navigation scale or layout animations. Existing keyboard focus and disabled
states are inherited from Button.Base.

ShellResourceChecks runs inside the existing single WPF test Application. It
resolves the real ShellViewModel, invokes every actual sidebar Button command at
1200, 800 and 1000 DIPs, checks the content binding and selected item, traces binding
errors, verifies native window style flags and minimize/maximize/restore, and tests
live light/dark theme changes. It uses isolated paths and does not start App or scan
media. Fallback eligibility is tested for Windows 10, early Windows 11, modern
Windows 11, remote sessions, high contrast and disabled transparency. Client-only
render previews are written to ignored test output as shell-expanded.png and
shell-compact.png. These previews do not capture DWM's native title bar or Mica.

Manual release checks still needed: native caption hover/Snap menu, wallpaper-based
Mica appearance with transparency on/off, dragging between monitors with different
DPI, and fallback appearance on Windows 10/early Windows 11. The desktop automation
helper was unavailable during this phase; no native screenshot was captured.

API references:
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmextendframeintoclientarea

## Phase 4: Home

HomePage now hosts a compact HomeHero and MediaShelf sections instead of Library's
wrapping grid. The hero chooses the latest available unfinished item; if there is
none, it uses recent available history. The primary action is "Continue in player"
and passes the existing media view model to OpenMediaCommand. It opens the existing
player/details page with saved playback preferences; it does not introduce autoplay.
Shared cards retain their truthful Open actions. The hero's contextual menu reuses
open and favorite commands. The toolbar keeps Refresh as a small secondary action.

MainWindowViewModel uses existing bounded repository queries: 24 recent unfinished
candidates, 12 recent additions, 12 favorites, and 10 history entries. Continue
watching filters unavailable/zero-position candidates and displays up to 12 items;
the hero is not repeated in that shelf. All shelves are omitted when empty. Home
refreshes on entry and on playback/favorite notifications, coalesces overlapping
reads, and honors ShowContinueWatching immediately. Empty, initial-loading and
failure states are separate; failed refreshes retain previously loaded content.
No database schema, scanning, playback engine or details navigation was changed.

HomeHero uses ThumbnailCache's existing 480x270 decoded preview. No full-resolution
artwork is loaded for the hero and there is no live blur. An ImageBrush provides
the backdrop without allowing the image's natural dimensions to inflate the hero;
artwork heroes have a 280-DIP minimum and the normal copy fits within that height.
Long titles can grow naturally for accessibility. Missing or unusable artwork
produces a compact themed surface. Versioned loading prevents stale artwork after
selection changes, and unload clears cached image references and subscriptions.

MediaShelf uses horizontal ListBox virtualization/recycling, pixel scrolling and
the Phase 3 MediaCard, explicitly retaining grid presentation regardless of the
Library layout setting. Arrows appear on hover/keyboard focus when overflowing.
Shift+wheel and native WM_MOUSEHWHEEL scroll horizontally; vertical wheel input
continues scrolling Home. Arrows use a short animation with reduced-motion support.
The native hook is local to the owning window and removed on unload. Shelf padding
leaves room for card scale/shadows at viewport edges. This is a curated, bounded
Home surface, not a replacement for browsing the full Library.

Home.xaml contains page/hero/shelf spacing, heights, scroll fraction and gradients.
HomeResourceChecks exercises the actual Home XAML against isolated SQLite data:
empty and error states, populated/empty sections, ordering and size caps, favorites
persistence, completion-driven hero changes, preference changes, existing command
parameters and saved position, horizontal virtualization and scrolling, widths
640/960/1440, and binding traces. Generated home-1120.png and home-640.png use test
artwork in ignored output. Existing playback and details-navigation tests continue
to cover the underlying commands and engine; no user media/database is used.
