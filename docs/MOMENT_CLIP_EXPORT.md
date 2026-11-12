# Exporting a moment as a clip

From **Saved Moments**, choose **Export clip** on a saved sequence. Point moments need an end time before export; use **Edit** to add one. Select a mode, an installed `ffmpeg.exe` (with `ffprobe.exe` in the same folder), and a new `.mkv` destination. Scriptorium does not install or download FFmpeg. Canceling any dialog leaves the moment unchanged.

- **Accurate boundaries** decodes and re-encodes video as MPEG-4 and audio as AAC. It can take longer and reduce quality. Cuts are limited by frame and audio timing; saved timestamps have whole-second precision.
- **Fast** copies streams without re-encoding. The result may include material outside the selected range, especially before the start, and is unsuitable for removing sensitive content precisely. Some codecs cannot be copied to Matroska. The app reports failure instead of silently changing modes.

The export includes the first video and, if present, the first audio stream. Other tracks, subtitles (including sidecars), chapters, and metadata are omitted. The export is a separate file: it does not change the source, saved moment, playback progress, or collections. It is not added to the library directly, although a later scan can discover it in a library folder.

The source is opened read-only and held against writes or replacement during export. Existing destination files are never overwritten, including the source and hard links to it. The app uses a temporary file in the destination folder, probes it before moving it to the requested name, and removes it on normal failure or cancellation. A forced termination or crash can leave a `.scriptorium-export-*.mkv` temporary file in that folder; it can be deleted manually. The export dialog shows indeterminate progress and supports cancellation.

The source is probed before export. Invalid or out-of-duration ranges, missing video, unavailable duration, and non-zero or unknown initial audio/video timestamps are rejected. This conservative check does not establish that all timestamps in a file are continuous or correct; unusual or damaged media can still fail or yield unexpected timing. Review the resulting clip, particularly when using fast mode. FFmpeg documents the [seek and stream-copy behavior](https://ffmpeg.org/ffmpeg.html), including that stream copy may retain content before a requested seek point.

FFmpeg is supplied by the user, so supported codecs and output quality depend on that installation. If FFmpeg is ever bundled, review the exact build and encoder licenses and distribution obligations; see [FFmpeg's license guidance](https://ffmpeg.org/legal.html).

## Verification

Run `dotnet test Scriptorium.sln` on Windows. To include the real FFmpeg integration checks, set `SCRIPTORIUM_TEST_FFMPEG` to the full path of `ffmpeg.exe` (with `ffprobe.exe` beside it) before running the tests. The integration checks exercise both modes, range validation, source preservation, existing destination protection, and temporary-file cleanup. Without that environment variable those integration checks return without invoking FFmpeg.
