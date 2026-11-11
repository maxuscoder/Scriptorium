using Scriptorium.App.Models;

namespace Scriptorium.App.Services;

/// <summary>Configures text rendering before media opens and before the output is attached.</summary>
public interface ISubtitleAppearancePlayback
{
    bool ConfigureSubtitleAppearance(SubtitleAppearance appearance);
}
