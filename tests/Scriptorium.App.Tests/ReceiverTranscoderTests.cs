using System.Diagnostics;
using System.IO;
using Scriptorium.App.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public class ReceiverTranscoderTests
{
    [Fact]
    public async Task ConvertsLocalVideoAndDeletesTemporaryOutput()
    {
        var ffmpeg = MomentClipExportService.FindInstalledFfmpeg();
        if (ffmpeg is null) return; // FFmpeg is an optional user-installed dependency.
        var source = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scriptorium-test-{Guid.NewGuid():N}.mp4");
        string? output = null;
        try
        {
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "color=size=64x64:rate=10", "-t", "0.5", "-c:v", "mpeg4", source }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await error);
            using (var converted = await new ReceiverTranscoder().ConvertAsync(source))
            {
                output = converted.Path;
                Assert.True(File.Exists(output));
                Assert.True(new FileInfo(output).Length > 0);
            }
            Assert.False(File.Exists(output));
        }
        finally { File.Delete(source); if (output is not null) File.Delete(output); }
    }
}
