using System.Diagnostics;
using System.IO;

namespace Scriptorium.App.Services;

/// <summary>Uses the operating system's registered media application to open an item.</summary>
public sealed class SystemMediaPlaybackLauncher : IMediaPlaybackLauncher
{
    /// <inheritdoc />
    public Task<bool> LaunchAsync(MediaPlaybackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = request.FilePath,
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }
}
