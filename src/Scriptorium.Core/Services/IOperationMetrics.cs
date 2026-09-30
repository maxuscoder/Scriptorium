namespace Scriptorium.Core.Services;

/// <summary>Records elapsed time and outcome for important application operations.</summary>
public interface IOperationMetrics
{
    /// <summary>Starts timing an operation. The returned scope logs when disposed.</summary>
    IOperationMetricScope Start(string operation);
}

/// <summary>Mutable details for one timed operation.</summary>
public interface IOperationMetricScope : IDisposable
{
    /// <summary>Adds a structured value to the operation log entry.</summary>
    void SetTag(string name, object? value);

    /// <summary>Sets the operation outcome, such as Success, Cancelled, or Failed.</summary>
    void SetOutcome(string outcome);
}
