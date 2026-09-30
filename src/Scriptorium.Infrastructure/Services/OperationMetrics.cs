using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Writes low-overhead structured operation timings to the application log.</summary>
public sealed class OperationMetrics(
    PerformanceOptions options,
    ILogger<OperationMetrics>? logger = null) : IOperationMetrics
{
    public IOperationMetricScope Start(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return new Scope(logger, operation, options.GetThreshold(operation));
    }

    private sealed class Scope(
        ILogger? logger,
        string operation,
        TimeSpan slowThreshold) : IOperationMetricScope
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Dictionary<string, object?> _tags = new(StringComparer.Ordinal);
        private string _outcome = "Unknown";
        private int _disposed;

        public void SetTag(string name, object? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (Volatile.Read(ref _disposed) == 0)
            {
                _tags[name] = value;
            }
        }

        public void SetOutcome(string outcome)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
            if (Volatile.Read(ref _disposed) == 0)
            {
                _outcome = outcome;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopwatch.Stop();
            var elapsedMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;
            var logLevel = _stopwatch.Elapsed >= slowThreshold ? LogLevel.Warning : LogLevel.Debug;
            logger?.Log(
                logLevel,
                "Operation {Operation} completed in {ElapsedMilliseconds} ms with outcome {Outcome}. Metrics: {@Metrics}",
                operation,
                elapsedMilliseconds,
                _outcome,
                _tags);
        }
    }
}
