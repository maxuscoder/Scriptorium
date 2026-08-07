using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

public interface ISettingsService
{
    ApplicationSettings Settings { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
