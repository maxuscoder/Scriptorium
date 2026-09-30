namespace Scriptorium.Core.Services;

/// <summary>Raised when a second library scan is requested while one is active.</summary>
public sealed class ScanAlreadyRunningException : InvalidOperationException
{
    public ScanAlreadyRunningException()
        : base("A library scan is already in progress.")
    {
    }
}
