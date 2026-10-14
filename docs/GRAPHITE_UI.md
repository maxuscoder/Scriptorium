# Frosted graphite UI

The redesign extends the existing WPF dictionaries and reusable controls. No new
framework, packages, data paths, or commands are introduced.

## Modified files

Paths below are relative to `src/Scriptorium.App/` unless noted.

| Files | Purpose |
| --- | --- |
| `Resources/Theme/Colors.xaml`, `Services/ThemeService.cs` | Shared surface roles and live Light/Dark/System gradients |
| `Resources/Theme/Chrome.xaml`, `Resources/Theme/Inputs.xaml` | Theme-aware graphite scrollbar and clearer input focus |
| `Resources/Theme/Shell.xaml`, `Views/MainWindow.xaml`, `Views/Controls/Sidebar.xaml` | Shared ambient backdrop, frosted sidebar, selected navigation and sharp logo |
| `Resources/Library.xaml`, `Views/Controls/Library/LibraryToolbar.xaml` | Unified controls, typography and aligned content margins |
| `Resources/Theme/MediaCards.xaml`, `Views/Controls/MediaCard.xaml`, `Views/Controls/MediaCard.xaml.cs` | Rounded surface, title-row overflow, hover feedback and removal of shadow/zoom work |
| `Views/Controls/AmbientBokehBackground.xaml` and `.xaml.cs` | Muted cool/warm lighting, shared diffuse textures and lighter motion |
| `Views/Controls/ContentAtmosphereBackground.cs`, `Resources/Theme/Colors.xaml` | Static graphite content surface and dark-only atmosphere tokens |
| `Views/Pages/CategoriesPage.xaml`, `Converters/ViewportContentHeightConverter.cs`, `Resources/Theme/Layout.xaml` | Empty-state centering within the remaining viewport and restrained collection icon |
| `tests/Scriptorium.App.Tests/{AmbientBokeh,MediaCard,ScrollBar,Shell,Library}ResourceChecks.cs` | Updated visual/interaction expectations and scaled screenshot capture |
| `tests/Scriptorium.App.Tests/ContentAtmosphereResourceChecks.cs`, `CategoriesResourceChecks.cs`, `VideoPlayerViewTests.cs` | Static surface, smoothness, caching, theme fallback, and empty-state layout coverage |
| `tests/Scriptorium.App.Tests/ThemeServiceTests.cs` | Frozen surface gradients and live theme replacement regression coverage |

## Surfaces and hierarchy

- `Theme/Colors.xaml` owns the graphite surface, selection, card and scrollbar
  colors. `ThemeService` supplies corresponding Light/System colors and replaces
  the shared frozen gradients during theme changes.
- `MainWindow` retains the sidebar's existing atmosphere, clipped to its column
  while preserving its original full-shell lighting coordinates. A separate,
  opaque `ContentAtmosphereBackground` draws the page surface, so its tones are
  independent of the sidebar. The sidebar styles and interactions are unchanged. Windows
  retains the native caption buttons, resizing, snap and existing Mica fallback.
- The content surface keeps its charcoal base and graphite center lift, with broad
  blue-gray illumination from beyond the upper-left edge. A six-level diagonal
  tonal shift and overlapping burnt-orange light from beyond the lower-right edge
  add visible, quiet depth. Warm-light design strength is 0.102, approximately 30%
  of the original 0.34 reference strength; this is not a calibrated luminance ratio.
  There are no separate glow disks. Dark-only atmosphere colors and strengths live in
  `Colors.xaml`; Light/System and high contrast retain the plain theme background.
- Categories uses the remaining viewport height to center its empty state below
  the header/status area. Populated collection and media grids remain together at
  their existing spacing. Short windows retain vertical scrolling when necessary.
- `Library.xaml` gives search, header actions, sorting, filter toggles and view
  controls a shared graphite surface. Existing wrapping and header breakpoints
  remain in use. The section/card left edge aligns with the header and toolbar.
- `MediaCards.xaml` and `MediaCard` provide a quiet rounded surface, neutral hover
  brightening, and an always available overflow button next to the title. The
  original artwork source, proportion-preserving fill, metadata bindings, tooltip
  truncation, favorite/open commands and keyboard interactions remain intact.
- The existing custom scrollbar retains its 16-DIP hit area and 5/7-DIP thumb.
  Its hover/drag states use graphite, keeping orange for selection and focus.

## Rendering cost

The content background is a single opaque, frozen 1024x768 texture, shared across
instances and stretched by WPF. It is generated once per palette, never on resize
or per frame. Sub-one-level stochastic rounding avoids quantization bands without
a visible noise overlay. It has no animation, blur, shader, opacity group or
additional child visuals.

The existing sidebar ambient field uses 12 layers instead of 24. All colors use the same smooth
bell profile, dithered before quantization into frozen, shared image brushes.
The field has an explicit 0.8 intensity multiplier; cool light predominates and
warm light is localized. Motion is render-only at 15 fps with reduced travel,
pauses when minimized, stops when detached, and respects Windows animation and
high-contrast preferences. Resizing repositions the existing textures.

Cards have no blur or shadow effect and do not enlarge on hover. Shared surface
brushes and existing color/opacity transitions supply feedback without layout
animation. The browser's recycling virtualization, row sizing, lazy pagination,
thumbnail priority, asynchronous loading and image cache are unchanged. The
reserved scrollbar gutter continues to prevent width changes as scrolling starts.

## Verification and remaining visual checks

Run `dotnet build Scriptorium.sln` and `dotnet test Scriptorium.sln`. The existing
STA resource checks exercise actual WPF controls, live theme changes, narrow and
wide layouts, 150% scaling, navigation, native playback, scrollbar input and
thumbnail/virtualization stability. The checks save PNG previews beneath
`tests/Scriptorium.App.Tests/bin/Debug/net9.0-windows/`.

Preview renders use isolated test data and do not capture the DWM caption or the
user's actual media library. Final manual review should include native title-bar
appearance, real thumbnails and long titles, mixed-monitor DPI transitions, and
interactive scrolling through a large library. The 0.8 lighting multiplier is a
design control, not a measured luminance match to the supplied screenshot.
