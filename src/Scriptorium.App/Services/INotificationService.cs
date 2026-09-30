namespace Scriptorium.App.Services;

/// <summary>Publishes dismissible, non-blocking user notifications.</summary>
public interface INotificationService
{
    IReadOnlyList<NotificationItemViewModel> Items { get; }

    void Show(string message, NotificationSeverity severity = NotificationSeverity.Information);

    void Report(Exception exception, string message, NotificationSeverity severity = NotificationSeverity.Error);
}
