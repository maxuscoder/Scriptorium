using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Scriptorium.App.Services;

/// <summary>Stores transient notifications and logs exception details outside the UI.</summary>
public sealed class NotificationService : INotificationService
{
    private const int MaximumVisibleNotifications = 4;
    private readonly ObservableCollection<NotificationItemViewModel> _items = [];
    private readonly Dictionary<NotificationItemViewModel, CancellationTokenSource> _dismissals = [];
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        _dispatcher = ApplicationDispatcher();
    }

    public IReadOnlyList<NotificationItemViewModel> Items => _items;

    public void Show(string message, NotificationSeverity severity = NotificationSeverity.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        RunOnUiThread(() => Add(message, severity));
    }

    public void Report(Exception exception, string message, NotificationSeverity severity = NotificationSeverity.Error)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _logger.Log(severity == NotificationSeverity.Error ? LogLevel.Error : LogLevel.Warning,
            exception, "{NotificationMessage}", message);
        Show(message, severity);
    }

    private void Add(string message, NotificationSeverity severity)
    {
        while (_items.Count >= MaximumVisibleNotifications)
        {
            Dismiss(_items[0]);
        }

        var item = new NotificationItemViewModel(message, severity, Dismiss);
        _items.Add(item);
        var cancellation = new CancellationTokenSource();
        _dismissals.Add(item, cancellation);
        _ = DismissAfterDelayAsync(item, cancellation.Token);
    }

    private async Task DismissAfterDelayAsync(NotificationItemViewModel item, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(item.Severity switch
            {
                NotificationSeverity.Error => TimeSpan.FromSeconds(9),
                NotificationSeverity.Warning => TimeSpan.FromSeconds(7),
                _ => TimeSpan.FromSeconds(5)
            }, cancellationToken);
            await _dispatcher.InvokeAsync(() => Dismiss(item));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Dismiss(NotificationItemViewModel item)
    {
        if (_dismissals.Remove(item, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _items.Remove(item);
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = _dispatcher.InvokeAsync(action);
    }

    private static Dispatcher ApplicationDispatcher() =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
}
