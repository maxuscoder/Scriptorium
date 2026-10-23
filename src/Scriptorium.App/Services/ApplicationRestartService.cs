using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Scriptorium.App.Services;

internal interface IApplicationRestartService
{
    void Restart();
    Task WaitForExitAsync(int processId, long processStartTimeUtcTicks);
}

internal sealed class ApplicationRestartService : IApplicationRestartService
{
    public void Restart()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable could not be located.");
        var startInfo = CreateStartInfo(executable, Environment.GetCommandLineArgs(),
            Assembly.GetEntryAssembly()?.Location);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Scriptorium could not be restarted.");
        Application.Current.Shutdown();
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string[] arguments, string? assemblyPath)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // A framework-dependent app can also be launched with dotnet Scriptorium.App.dll.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(assemblyPath
                ?? throw new InvalidOperationException("The application assembly could not be located."));
        }
        foreach (var argument in arguments.Skip(1)) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    public async Task WaitForExitAsync(int processId, long processStartTimeUtcTicks)
    {
        Process previous;
        try
        {
            previous = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return; // The instance that requested the reset has already exited.
        }
        using (previous)
        {
            if (previous.HasExited || previous.StartTime.ToUniversalTime().Ticks != processStartTimeUtcTicks) return;
            if (processId == Environment.ProcessId)
                throw new InvalidOperationException("App data can only be cleared after the previous instance closes.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await previous.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("Close the previous Scriptorium instance, then reopen the app to finish clearing its data.");
            }
        }
    }
}
