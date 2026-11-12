using System.Security.Cryptography;
using Scriptorium.App.Services;
using System.IO;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class MomentClipExportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exports_without_changing_source_or_overwriting_existing_files(bool reencode)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("SCRIPTORIUM_TEST_FFMPEG");
        if (string.IsNullOrEmpty(ffmpeg)) return; // Optional integration test; see documentation.
        var dir = Path.Combine(Path.GetTempPath(), "scriptorium-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.mkv");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "audio-preview.mp4"), source);
            var original = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var destination = Path.Combine(dir, "clip.mkv");
            var service = new MomentClipExportService();
            await service.ExportAsync(ffmpeg, source, destination, 1, 3, reencode, CancellationToken.None);
            Assert.True(new FileInfo(destination).Length > 0);
            Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(source)));
            var existing = await File.ReadAllBytesAsync(destination);
            await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(ffmpeg, source, destination, 1, 3, reencode, CancellationToken.None));
            Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
            await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(ffmpeg, source, source, 1, 3, reencode, CancellationToken.None));
            Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(source)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(ffmpeg, source, Path.Combine(dir, "invalid.mkv"), 3, 8, reencode, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(dir, "invalid.mkv")));
            Assert.Empty(Directory.GetFiles(dir, ".scriptorium-export-*.mkv"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Rejects_unbounded_or_reversed_ranges_before_starting_tools()
    {
        var service = new MomentClipExportService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync("", "", "", 5, 5, true, CancellationToken.None));
    }
}
