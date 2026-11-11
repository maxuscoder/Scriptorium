# Embedded video playback

Home's featured card autoplays a 20-second sample starting at 10% of the media's actual runtime once each time Home becomes visible. It starts muted, with pause/resume and mute/unmute controls at the bottom right, and restores the thumbnail when the sample ends (or the file ends sooner). Navigation releases the preview and returning repeats the same sample from its 10% starting point. Unavailable or unplayable media, including files with an unknown runtime, retain their thumbnail. The top-right heart toggles favorites directly.

`HomeHeroPreviewViewModel` uses the existing playback factory in a separate session, without playback-history, progress, or preference writes. Its timer uses the media position, so paused time does not consume the sample. The view keeps its copy and controls in LibVLC's foreground overlay while video is attached, then restores its regular WPF layout after playback. Native decoding resources are released asynchronously when playback ends or the page becomes hidden.

Movie details now show a paused video preview above the title. Play/Pause resumes the current session, Replay restarts a finished video, and Fullscreen opens the same session in a borderless window. Use the exit button or Escape to return; F11 toggles fullscreen while the player has keyboard focus. A brief centered icon confirms playback, seek, volume, and mute actions.

Tutorial and TV details use a compact Library breadcrumb with progress, metadata editing, and an action menu. The player leads the page, with Play/Resume/Pause/Replay, Next, and Favorite beneath it. Course content and episodes appear in a collapsible side panel with their own scroll area on wide windows, and stack below the player on narrower windows. The TV season selector browses seasons without interrupting the current episode; sequential navigation and automatic continuation reveal the playing episode's season. Previous, completion, continue-learning/watching, and TV progress-reset actions remain available in the header menu. Editing, resizing, and hiding the content panel keep the same player mounted.

Scrolling over the inline video or its controls scrolls the details page. Media details disable the viewport opacity fade around native video, and clip both the video and its foreground overlay to the visible scroll area while keeping the same playback surface mounted. Fullscreen wheel input stays within the fullscreen player.

Native video dimensions are rounded from the local player size, independently of its screen position. This prevents fractional display scaling from changing the render height by one pixel on each scroll. Pure moves preserve the retained video frame, skip redundant native resize notifications, and update clipping without erasing the paused frame.

The foreground overlay paints opaque black bars using the display aspect ratio of VLC's native picture window, including sample aspect ratio and rotation. A hidden or unavailable picture stays fully covered in black, so the native gray background cannot show through. The adapter listens for native picture creation, visibility, and geometry changes, including changes after the initial preview has paused, and briefly follows the resulting layout while it settles. This keeps pillarbox and letterbox areas black on initial entry, return navigation, scrolling, and resizing, without repainting the paused Direct3D frame or polling continuously while paused.

The volume slider and Up/Down shortcuts support **0–200%**. **100%** is the original level; higher values use LibVLC's software amplification for quiet movies. The player displays the selected percentage and remembers it for subsequent videos and app launches. Settings → Playback → Default volume supports the same range. Mute/unmute restores the selected boost. Start around 125% and increase as needed; high boosts can distort loud scenes.

## Local subtitles

The player discovers `.srt`, `.vtt` (WebVTT), `.ass`, and `.ssa` sidecars in the video's own folder whenever a media session opens. Names match case-insensitively: `Movie.mp4` can use `Movie.srt`, `Movie.en.srt`, `Movie.en.forced.srt`, or `Movie ro.srt`. A dot or space must separate a language/variant suffix; `Episode 1` does not match `Episode 10`. If multiple video names share a prefix, the longest matching video name owns the subtitle. Discovery does not search subfolders or download files.

The **Subtitle track** selector lists loaded external and embedded tracks, with **Subtitles off** available explicitly. Selection changes take effect in the current session, including fullscreen. Turning subtitles on or off saves the existing `SubtitlesEnabled` preference; subsequent videos select the first available track when enabled. Opening a video without subtitles preserves that preference.

Discovery and preliminary validation run off the UI thread and cancel when a session is stopped, replaced, or released. Empty, unreadable, oversized (over 16 MiB), and invalid files without a recognizable timed cue are skipped. BOM-marked UTF-8/UTF-16 files are supported. LibVLC performs the final format parsing; only tracks it exposes appear in the menu. Missing subtitles leave video playback available. Hovering the subtitle selector shows availability or skipped-file information, including when the selector is disabled. Sidecars are never modified or persisted in the library database; reopen the media to discover added or renamed files.

SRT, WebVTT, ASS, and SSA are covered by native decoding/selection tests. Other formats, including bitmap subtitle pairs (`.idx`/`.sub`), remain outside automatic discovery because their pairing and validation need separate handling.

## Keyboard shortcuts

When the player has focus, these shortcuts are available. They do not run while focus is in a text-entry control or a track/speed selector.

| Key | Action |
| --- | --- |
| Space | Play or pause |
| Left / Right | Seek backward / forward 5 seconds |
| Up / Down | Increase / decrease volume by 5% |
| F or F11 | Toggle fullscreen |
| M | Mute or restore audio |

## Implementation

The player uses the official `LibVLCSharp`, `LibVLCSharp.WPF`, and `VideoLAN.LibVLC.Windows` packages. This provides natural LibVLC support for MKV, MP4, AVI, WebM, and other formats without starting or embedding the VLC desktop application. An unavailable file or unplayable video produces an inline error and disables playback. Missing-file checks performed after a native media failure run off the UI dispatcher, keeping slow local or network paths from blocking the application. Technical failures are logged with their classification and path; the details page keeps its **Back to library** action available.

- `IVideoPlayback` is the engine-neutral session contract used by `VideoPlayerViewModel`. `LibVlcVideoPlayback` is the only playback implementation and contains media creation, local-path validation, playback events, seeking, volume/rate conversion, and resource release. LibVLC types do not cross into the ViewModel or domain layers.
- MP4, M4V, and MOV playback uses LibVLC's bundled FFmpeg (`avformat`) demuxer. The native VLC 3 MP4 demuxer can omit H.264 reference pictures when seeking within fragmented downloads, leaving artifacts until the next keyframe. Hardware decoding stays enabled; the input file is unchanged.
- `LibVlcRuntime` is a DI singleton. It selects the official packaged runtime directory for the process architecture, initializes LibVLCSharp's native loader once, creates one `LibVLC` instance for the application lifetime, and disposes that instance when the application service provider shuts down. Each preview session owns only its `MediaPlayer` and `Media` objects. Native session teardown is registered with the runtime and runs on a worker thread; runtime shutdown waits for pending releases before disposing LibVLC.
- `LibVlcVideoViewBinding` is the narrow rendering adapter between the opaque `IVideoOutput` token and LibVLCSharp.WPF's `VideoView`. Native decoding waits until the loaded view has a nonzero render HWND; the adapter retries attachment after layout if the template was created before its host window. This prevents LibVLC from creating a separate desktop output window during page startup. Releasing a session cancels an open that is still waiting for its surface. Fullscreen temporarily moves the existing Scriptorium player visual into a borderless window and returns it afterward. The same `VideoView`, native render target, `MediaPlayer`, and decoder remain in use; all playback decisions stay in the ViewModel.
- `VideoPlayerViewModel` owns loading, play/pause/replay, status, elapsed time, commands, and playback persistence. `SetMedia` binds a path, media ID, duration, and saved starting position; `Activate` opens it when the view loads; `Deactivate` stops the timer, detaches the rendering output and events, and queues native teardown and the latest position save without blocking navigation. `DeactivateAsync` also waits for those pending operations; application closing awaits all details-page players before disposing services. While playing, progress is sampled by the existing 500 ms UI timer but written at most once every five seconds when the whole-second position changes. Progress writes run in order on the thread pool, including the synchronous portion of SQLite calls. Saved-progress notifications are applied on the player’s dispatcher before a flush completes, so completion/reset actions cannot be overwritten by a delayed UI update. Changing media captures the previous session’s position before detaching it and queues its save and release. Episode switches and returning views wait asynchronously for the previous decoder to finish cleanup before opening a replacement on the same native surface. New selections supersede pending opens, and leaving the page or selecting unavailable media cancels them; old callbacks cannot affect the new session.
- `VideoPlayer` is the reusable presentation control. Code-behind handles view lifecycle, keyboard input, transient action feedback, and fullscreen window management. Closing fullscreen preserves playback; unloading the owning inline control or closing the application window releases it.
- `MovieDetailsPageViewModel` supplies the current movie, while `TutorialDetailsPageViewModel` and `TvShowDetailsPageViewModel` bind the same player to the selected lesson or episode. Loading a preview does not update playback history. First playback retains the existing last-played update, and saved positions are respected (completed items start at zero). Periodic, end-of-media, and close-time position updates are persisted through `IPlaybackProgressService`; playback at 95% or more of a known runtime is marked completed and refreshes the library through its saved-progress notification. Collection details pages select the first incomplete item when opened, preserve per-item resume positions, and expose sequential previous/next navigation. The **Reset progress** action clears the saved position and completion state.

The player is connected to movie details, tutorial lessons, and TV-show episodes. Collection details pages select the first incomplete item when opened, preserve per-item resume positions, and expose sequential previous/next navigation.

Individual media cards in search and filtered library results open their corresponding details page in both grid and list layouts. Episode and lesson results select the clicked media item; opening a collection still selects its first incomplete item. Back returns to the library with the search and filters preserved.

## Audio and subtitle preferences

The player offers audio and subtitle track selectors. Explicit selections save the
chosen track for that media item in local settings. Movies and tutorials also learn
global language preferences. Choosing **Subtitles off** is remembered per item.
Library IDs retain choices after a file is renamed, and unindexed media uses its
file path.

For TV episodes, open the player's audio/subtitle settings (clock button) and choose
**Use selected tracks for series** to save the displayed audio and subtitles as
defaults for the entire show, across seasons. Series defaults use the persistent
show ID, keeping different shows separate. Later track changes are episode-specific
overrides; **Restore series tracks for this episode** clears those overrides.
Selection priority is episode choice, series default, then global preference, with
audio and subtitles resolved independently. TV choices do not alter global defaults.
Synchronization delays always remain specific to each episode.

Restoration runs when playback opens and when tracks arrive later. Track IDs are
checked against their names and languages so reassigned IDs cannot silently select
a different language. ISO language codes and common language names are normalized.
If preferred subtitles are unavailable, subtitles stay off; unavailable audio
preferences use the available native audio track. Missing or rejected tracks never
overwrite saved preferences or prevent video playback. A later track-list update
retries the saved choice. Tracks without language metadata can still be remembered
for their individual media item. VTT filename language hints are used when the
external tracks can be identified unambiguously.

Preference writes are debounced, survive disposal during navigation, and are awaited
by asynchronous player shutdown. Resetting settings clears the language, series,
and item choices along with the other preferences.

## Audio and subtitle synchronization

Open the clock button in the player toolbar to adjust **Subtitles** and **Audio**
independently. The Sync panel shows each signed delay in milliseconds. The minus
and plus buttons adjust by 50 ms; a typed value applies on Enter or when leaving
the field. Positive values play the track later, and negative values play it
earlier relative to video. Each track has a Reset button that returns its delay
to zero. Adjustments are limited to ten minutes in either direction.

Changes apply while playing or paused. Subtitle controls are enabled when a
subtitle track is selected, and audio controls when audio is available. Engines
without timing support disable these controls. Failed adjustments show an error
and retain the previous offset without interrupting playback.

Timing offsets are saved per media item alongside its track choices, using the
library ID or file path. Other media starts at zero unless it has its own saved
correction. Opening a file, changing tracks, and stop/replay restore its saved
offsets, including tracks discovered after opening. Resetting settings clears
these corrections. Older settings default to zero delay. Native tests verify
millisecond-to-microsecond conversion and adjustment during playback; UI checks
cover signed values, direct input, validation, keyboard input, and toolbar layout.

## Subtitle appearance

Open **Audio and subtitle settings** (the clock button) and use **Subtitle
appearance** to choose Small, Normal, Large, or Extra large text; text and outline
colors; and no, thin, normal, or thick outlines. The bottom-margin slider raises
subtitles by up to 200 video pixels where the format permits it. **Reset
appearance** restores the defaults. Preferences are saved locally for all videos,
and survive export/import; old settings use the defaults.

Changes preview in the current video after a short debounce. The logical player
keeps its position, play/pause/stopped/ended state, volume, speed, selected tracks,
and per-item timing corrections. Refreshing a style does not generate another
playback-start or completion event. Navigation cancels a pending preview.

VLC 3's media-level text-renderer options are not reliable for every bundled
renderer. Customized playback therefore uses an instance configured before its
input opens; that instance belongs to its player and is released with it. A live
change refreshes the native session while retaining the existing WPF player and
fullscreen visual. Default playback continues to use the shared runtime.

These controls target rendered text subtitles. Source-defined styles and absolute
positions can take precedence, and bitmap or burned-in subtitles cannot be
recolored or resized as text. Native frame tests verify font size, text color,
colored outlines, and vertical placement; regression tests cover persistence,
state restoration, failed configuration, and cancellation during navigation.

## Verification

Run `dotnet test Scriptorium.sln` on Windows with the .NET 10 SDK. The official LibVLC Windows runtime is restored and copied to the application/test output by NuGet. The separate `Scriptorium.App.Tests` project keeps WPF checks out of the existing platform-independent Core/Infrastructure tests.

The tests cover paused initialization, resume without resetting position, end/replay, invalid or unavailable media, replacing a session, idempotent cleanup, actual local MP4 decoding and file release, `VideoView` attachment, fullscreen continuity, Escape, and navigation cleanup. A TV-page navigation check holds native-style teardown and progress writes behind separate gates, verifies that the destination tab loads and accepts input within 250 ms, and tests a quick return while the previous session is still closing. Native tests verify asynchronous/idempotent release, runtime shutdown file release, and repeated episode switches without standalone VLC output windows. Controlled cleanup tests also verify that rapid selections open only the latest episode and navigation cancels pending opens. UI checks load theme resources in an invisible test window and never start the application or access its database.

Home preview checks cover the 10% starting point and 20-second sample across different runtimes, pause/resume, mute, repeat visits, short or unavailable videos, direct favorites, and unchanged watch progress. The native Home check also verifies the seek using the file's actual runtime, cropping, scroll clipping, and restoring isolated test artwork when the preview ends.

The packaged third-party versions and their package-supplied license references are recorded in `THIRD-PARTY-NOTICES.txt`, which is copied beside the application output. Distribution preparation must preserve notices accompanying the exact packages and runtime files. This integration structure is not, by itself, a guarantee of legal compliance.

To run the screen test with another local video, set `SCRIPTORIUM_PLAYBACK_TEST_FILE` to its absolute path, then run:

```powershell
dotnet test tests/Scriptorium.App.Tests/Scriptorium.App.Tests.csproj --filter FullyQualifiedName~VideoPlayerViewTests
```

The generated fixture is a four-second H.264 video with no audio or third-party content. It was created using:

```text
ffmpeg -f lavfi -i testsrc2=size=96x64:rate=15 -t 4 -c:v libx264 -pix_fmt yuv420p -movflags +faststart preview.mp4
```

FFmpeg is only needed to regenerate the fixture, not to run the app or tests.

`Fixtures/multilingual.mkv` combines the silent audio fixture with two audio tracks
and two embedded subtitle tracks tagged English and Romanian. Native tests verify
language restoration and stop/replay; regression tests cover per-item overrides,
save/reload, missing and late tracks, changed IDs, failed selections, and navigation.
With a `sample.srt` containing a cue from 00:00:00,100 to 00:00:03,800, regenerate it:

```powershell
ffmpeg -i tests/Scriptorium.App.Tests/Fixtures/audio-preview.mp4 -i sample.srt -map 0:v -map 0:a -map 0:a -map 1:s -map 1:s -c copy -c:s srt -metadata:s:a:0 language=eng -metadata:s:a:1 language=ron -metadata:s:s:0 language=eng -metadata:s:s:1 language=ron tests/Scriptorium.App.Tests/Fixtures/multilingual.mkv
```

`Fixtures/audio-preview.mp4` is a synthetic black video with silent AAC audio. The audio-volume test uses it to verify that the native output accepts 150% and 200% volume, mute, and range limits without producing sound. Regenerate it with:

```powershell
ffmpeg -f lavfi -i color=c=black:s=96x64:r=10:d=4 -f lavfi -i anullsrc=r=8000:cl=mono -t 4 -c:v libx264 -pix_fmt yuv420p -c:a aac -b:a 16k -shortest tests/Scriptorium.App.Tests/Fixtures/audio-preview.mp4
```

`Fixtures/fragmented-seek.mp4` is an eight-second synthetic H.264 fixture with two four-second GOPs and half-second MP4 fragments. Its large corner patch changes from red to green to blue to yellow every two seconds, including between keyframes. `FragmentedVideoSeekTests` seeks out of order while paused and playing, captures decoded frames, and checks those colors so stale references cannot pass by reporting the right playback time. Both cases fail with the native MP4 demuxer and pass with `avformat`.

Regenerate it from the repository root with:

```powershell
ffmpeg -f lavfi -i "testsrc2=size=192x108:rate=15" -t 8 -vf "drawbox=x=0:y=0:w=64:h=64:color=red:t=fill,drawbox=x=0:y=0:w=64:h=64:color=lime:t=fill:enable='gte(t,2)',drawbox=x=0:y=0:w=64:h=64:color=blue:t=fill:enable='gte(t,4)',drawbox=x=0:y=0:w=64:h=64:color=yellow:t=fill:enable='gte(t,6)',drawbox=x=160:y=0:w=32:h=32:color=lime:t=fill,drawbox=x=0:y=76:w=32:h=32:color=blue:t=fill" -c:v libx264 -g 60 -keyint_min 60 -sc_threshold 0 -bf 2 -pix_fmt yuv420p -movflags frag_keyframe+empty_moov+default_base_moof -frag_duration 500000 tests/Scriptorium.App.Tests/Fixtures/fragmented-seek.mp4
```
