using System.Diagnostics;
using System.IO;
using Scriptorium.Core.Services;

namespace Scriptorium.App.Services;

/// <summary>Uses the operating system's registered media application to open an item.</summary>
public sealed class SystemMediaPlaybackLauncher(IPrivatePathGuard? privatePaths = null) : IMediaPlaybackLauncher
{
    /// <inheritdoc />
    public async Task<bool> LaunchAsync(MediaPlaybackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.OpenPrivateStream is not null) return false;

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return false;
        }
        if (privatePaths is not null && await privatePaths.IsPrivatePathAsync(request.FilePath, cancellationToken)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = request.FilePath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
