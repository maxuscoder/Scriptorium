using Microsoft.Extensions.Logging;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class OperationMetricsTests
{
    [Fact]
    public void Slow_operations_are_logged_as_warnings_with_structured_details()
    {
        var logger = new CapturingLogger();
        var metrics = new OperationMetrics(
            new PerformanceOptions(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero),
            logger);

        using (var timing = metrics.Start("Search.Query"))
        {
            timing.SetTag("ResultCount", 12);
            timing.SetOutcome("Success");
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Search.Query", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ResultCount", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Success", entry.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<OperationMetrics>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
