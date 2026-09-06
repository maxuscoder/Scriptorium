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

The player uses WPF's built-in `MediaPlayer` and `VideoDrawing`. This adds no native package or external player installation and lets the inline and fullscreen views share a decoded video without reopening it. `ScrubbingEnabled` presents a frame while paused. Supported codecs depend on Windows; an unavailable file or unsupported video produces an inline error and disables playback. Missing-file checks performed after a native media failure run off the UI dispatcher, keeping slow local or network paths from blocking the application. Technical failures are logged with their classification and path; the details page keeps its **Back to library** action available. See Microsoft's [VideoDrawing guide](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-play-media-using-a-videodrawing) and [multimedia overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/multimedia-overview).

- `IVideoPlayback` / `WpfVideoPlayback` isolate the native engine, local-path validation, video drawing, media events, and resource release.
- `VideoPlayerViewModel` owns loading, play/pause/replay, status, elapsed time, commands, and playback persistence. `SetMedia` binds a path, media ID, duration, and saved starting position; `Activate` opens it when the view loads; `Deactivate` stops the timer, detaches events, closes the engine, and flushes the latest position. While playing, progress is sampled by the existing 500 ms UI timer but written at most once every five seconds when the whole-second position changes. Changing media also flushes the previous session before disposing it.
- `VideoPlayer` is the reusable presentation control. Code-behind handles view lifecycle, keyboard input, transient action feedback, and fullscreen window management. Closing fullscreen preserves playback; unloading the owning inline control or closing the application window releases it.
- `MovieDetailsPageViewModel` supplies the current movie. Loading a preview does not update playback history. First playback retains the existing last-played update, and saved positions are respected (completed movies start at zero). Periodic, end-of-media, and close-time position updates are persisted through `IPlaybackProgressService`; playback at 95% or more of a known runtime is marked completed and refreshes the library through its saved-progress notification. The **Reset progress** action clears the saved position and completion state.

The implementation currently connects the player to movie details. The control and engine can also be bound to lesson or episode selection in later work.

## Verification

Run `dotnet test Scriptorium.sln` on Windows with the .NET 9 SDK and Windows media support enabled. The separate `Scriptorium.App.Tests` project keeps WPF checks out of the existing platform-independent Core/Infrastructure tests.

The tests cover paused initialization, resume without resetting position, end/replay, invalid or unavailable media, replacing a session, idempotent cleanup, actual local MP4 decoding and file release, rendered preview pixels, fullscreen continuity, Escape, and navigation cleanup. UI checks load theme resources in an invisible test window and never start the application or access its database. A rendered page is written to the test output directory as `movie-preview.png`.

To run the screen test with another local video, set `SCRIPTORIUM_PLAYBACK_TEST_FILE` to its absolute path, then run:

```powershell
dotnet test tests/Scriptorium.App.Tests/Scriptorium.App.Tests.csproj --filter FullyQualifiedName~VideoPlayerViewTests
```

The generated fixture is a four-second H.264 video with no audio or third-party content. It was created using:

```text
ffmpeg -f lavfi -i testsrc2=size=96x64:rate=15 -t 4 -c:v libx264 -pix_fmt yuv420p -movflags +faststart preview.mp4
```

FFmpeg is only needed to regenerate the fixture, not to run the app or tests.
