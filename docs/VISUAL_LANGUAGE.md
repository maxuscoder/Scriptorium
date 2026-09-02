# Scriptorium visual language

Scriptorium is a calm, dark, media-library desktop application. The interface uses a restrained blue accent, clear information hierarchy, and a small set of reusable tokens. New UI must consume these resources rather than introduce literal colors, sizes, radii, or shadows.

## Application shell

The shell has three layers: a persistent navigation rail, a contextual header, and the page canvas. On normal widths, the navigation rail is 256 px wide and includes the Scriptorium identity, section label, full navigation labels, and a small library-status card. Below 1060 px it becomes a 76 px icon rail, while the search field reduces from 400 px to 260 px; all commands and destinations remain available.

The header is 80 px high and places a small contextual label above the current page title. Global search sits on the right and always keeps its visible search glyph and placeholder. Pages supply their own content and actions beneath the header; do not duplicate global navigation or search inside a page.

## Spacing and layout

The base unit is 4 px. `Space.1` through `Space.8` represent 4, 8, 12, 16, 24, 32, 48, and 64 px. For `Margin` and `Padding`, use the matching `Spacing.XXS` through `Spacing.3XL` resources.

Use 8–16 px within a compact component, 24 px inside a card, 32 px around a page, and 48–64 px only between major sections or empty-state content. Keep related controls at 8 px and separate unrelated groups at 16–24 px. Page content starts with `Spacing.XL` unless a compact page deliberately needs less.

## Typography

Use Segoe UI throughout. Available sizes are Caption 12, Label 13, Body Small 14, Body 15, Subheading 18, Heading 24, Display 32, and Hero 40 px.

| Use | Resource / weight |
| --- | --- |
| App name | `Text.Brand`, bold |
| Page title | `Text.PageTitle`, semibold |
| Section heading | `Text.SectionTitle`, semibold |
| Long-form or supporting copy | `Text.Body`, regular |
| Empty-state supporting copy | `Text.Body.Center`, regular |
| Field label | `Text.Label`, semibold |
| Metadata and help | `Text.Caption`, regular |

Use `TextPrimary` for headings and important values, `TextSecondary` for normal supporting text, and `TextMuted` for metadata. Do not use body text as a heading or rely on all caps for hierarchy.

## Color

Surfaces are ordered from `Brush.Background` to `Brush.Surface`, `Brush.SurfaceElevated`, and `Brush.SurfaceOverlay`. Use `Brush.SurfaceHeader` only for persistent chrome. Boundaries use `Brush.Border`; reserve `Brush.BorderStrong` for a selected, active, or emphasized boundary.

`Brush.Accent` is the primary action, selection, link, and progress color; `Brush.AccentStrong` is its hover state and `Brush.AccentSurface` is its selected background. Feedback must use semantic brushes: `Success`, `Warning`, or `Danger` with the equivalent `*Surface` brush for a contained status. `Brush.FocusRing` is exclusively for keyboard focus. Never use status colors as decoration or as the sole way to communicate a state.

## Shape and elevation

Use `CornerRadius.XS` (4 px) for small internal elements, `S` (6 px) for buttons, inputs, badges, and tags, `M` (8 px) for cards, `L` (12 px) for dialogs, `XL` (16 px) for prominent feature surfaces, and `Pill` only for switches or slim indicators.

Elevation is a hierarchy signal, not ornament: `Elevation.0` for inline content, `Elevation.1` for cards, `Elevation.2` for transient menus or popovers, and `Elevation.3` for dialogs. Keep one elevation change between adjacent layers where possible. The shared `Card` and `Dialog.Surface` styles already apply the appropriate level.

## Motion

Use `Motion.Duration.Instant` (100 ms) for immediate feedback, `Fast` (150 ms) for hover and focus, `Standard` (200 ms) for common state changes, `Deliberate` (300 ms) for panels and dialogs, and `Slow` (450 ms) only for large, explanatory transitions. Use `Motion.Easing.Enter`, `Exit`, and `Standard` to match direction.

Animate opacity, color, and a small positional change (at most 8 px). Do not animate layout repeatedly, autoplay decorative motion, or delay feedback. Any new animation must honor Windows reduced-motion settings; when it is enabled, show the resulting state immediately.

## Icons

Use one icon family with an approximately 2 px visual stroke at 20 px. Draw compact inline icons at `Icon.Size.Compact` (16 px), default action icons at `Icon.Size.Default` (20 px), navigation icons at `Icon.Size.Navigation` (24 px), and empty-state or feature icons at `Icon.Size.Feature` (32 px). Place icon-only actions in a `Button.Icon` container; its visible hit target is 40 px and must have a tooltip or accessible name. Pair an icon with text for destructive, ambiguous, or infrequent actions.

## Component rules

Start with the shared styles: `Button.Primary` for one main action per context, `Button.Secondary` for alternatives, `Button.Destructive` only after clear consequence, and `Button.Icon` for icon-only actions. Use `Toggle.Switch` for binary inclusion or preference states that should read as on/off rather than a labeled checkbox. Use `Input.TextBox` and `Input.ComboBox` for fields, `Card` for grouped content, `Dialog.Surface` for modal content, `Badge` for compact status labels, and `Divider` to separate dense groups.

Components must have default, hover, keyboard-focus, disabled, and (where relevant) selected, error, and loading states. Preserve a 32 px minimum hit target; use 40 px for icon-only controls and comfortable text inputs. Keep the primary action visually unique, communicate disabled states with both opacity and unavailable behavior, and ensure every focusable custom component visibly uses `Brush.FocusRing`.

When a needed variant cannot be composed from an existing resource, add the variant to the theme resources and document its role here before using it in a page.
