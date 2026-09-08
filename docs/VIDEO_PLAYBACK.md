# Embedded video playback

Movie details now show a paused video preview above the title. Play/Pause resumes the current session, Replay restarts a finished video, and Fullscreen opens the same session in a borderless window. Use the exit button or Escape to return; F11 toggles fullscreen while the player has keyboard focus. A brief centered icon confirms playback, seek, volume, and mute actions.

## Keyboard shortcuts

When the player has focus, these shortcuts are available. They do not run while focus is in a text-entry control.

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
- `LibVlcRuntime` is a DI singleton. It selects the official packaged runtime directory for the process architecture, initializes LibVLCSharp's native loader once, creates one `LibVLC` instance for the application lifetime, and disposes that instance when the application service provider shuts down. Each preview session owns only its `MediaPlayer` and `Media` objects.
- `LibVlcVideoViewBinding` is the narrow rendering adapter between the opaque `IVideoOutput` token and LibVLCSharp.WPF's `VideoView`. Fullscreen temporarily moves the existing Scriptorium player visual into a borderless window and returns it afterward. The same `VideoView`, native render target, `MediaPlayer`, and decoder remain in use; all playback decisions stay in the ViewModel.
- `VideoPlayerViewModel` owns loading, play/pause/replay, status, elapsed time, commands, and playback persistence. `SetMedia` binds a path, media ID, duration, and saved starting position; `Activate` opens it when the view loads; `Deactivate` stops the timer, detaches events, closes the engine, and flushes the latest position. While playing, progress is sampled by the existing 500 ms UI timer but written at most once every five seconds when the whole-second position changes. Changing media also flushes the previous session before disposing it.
- `VideoPlayer` is the reusable presentation control. Code-behind handles view lifecycle, keyboard input, transient action feedback, and fullscreen window management. Closing fullscreen preserves playback; unloading the owning inline control or closing the application window releases it.
- `MovieDetailsPageViewModel` supplies the current movie, while `TutorialDetailsPageViewModel` and `TvShowDetailsPageViewModel` bind the same player to the selected lesson or episode. Loading a preview does not update playback history. First playback retains the existing last-played update, and saved positions are respected (completed items start at zero). Periodic, end-of-media, and close-time position updates are persisted through `IPlaybackProgressService`; playback at 95% or more of a known runtime is marked completed and refreshes the library through its saved-progress notification. Collection details pages select the first incomplete item when opened, preserve per-item resume positions, and expose sequential previous/next navigation. The **Reset progress** action clears the saved position and completion state.

The player is connected to movie details, tutorial lessons, and TV-show episodes. Collection details pages select the first incomplete item when opened, preserve per-item resume positions, and expose sequential previous/next navigation.

## Verification

Run `dotnet test Scriptorium.sln` on Windows with the .NET 9 SDK. The official LibVLC Windows runtime is restored and copied to the application/test output by NuGet. The separate `Scriptorium.App.Tests` project keeps WPF checks out of the existing platform-independent Core/Infrastructure tests.

The tests cover paused initialization, resume without resetting position, end/replay, invalid or unavailable media, replacing a session, idempotent cleanup, actual local MP4 decoding and file release, `VideoView` attachment, fullscreen continuity, Escape, and navigation cleanup. UI checks load theme resources in an invisible test window and never start the application or access its database.

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
