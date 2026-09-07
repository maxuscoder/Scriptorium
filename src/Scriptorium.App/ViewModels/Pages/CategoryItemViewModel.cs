using System.Windows.Media;
using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Provides editable presentation state for a category on the Categories page.
/// </summary>
public sealed class CategoryItemViewModel : ViewModelBase
{
    private string _name;
    private string _color;
    private bool _isSelected;

    public CategoryItemViewModel(Category category, IReadOnlyCollection<MediaItem> assignedMedia)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(assignedMedia);

        Id = category.Id;
        _name = category.Name;
        _color = category.Color;
        MediaCount = assignedMedia.Count;
        MovieCount = assignedMedia.Count(mediaItem => mediaItem.MediaType == MediaType.Movie);
        TvEpisodeCount = assignedMedia.Count(mediaItem => mediaItem.MediaType == MediaType.TvShow);
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(IsValidColor));
            }
        }
    }

    public bool IsValidColor => TryParseColor(Color, out _);

    public Brush ColorBrush => CreateColorBrush(Color);

    public int MediaCount { get; }

    public int MovieCount { get; }

    public int TvEpisodeCount { get; }

    public string MediaCountText => $"{MediaCount} media item{(MediaCount == 1 ? string.Empty : "s")}";

    /// <summary>Gets the type breakdown shown with the category's total.</summary>
    public string MediaTypeSummaryText
    {
        get
        {
            if (MediaCount == 0)
            {
                return "No media assigned yet";
            }

            var summaries = new List<string>(3);
            if (MovieCount > 0)
            {
                summaries.Add($"\U0001F3AC Movies ({MovieCount})");
            }

            if (TvEpisodeCount > 0)
            {
                summaries.Add($"\U0001F4FA TV episodes ({TvEpisodeCount})");
            }

            return string.Join("  ·  ", summaries);
        }
    }

    /// <summary>Gets or sets whether this category is the active category in the browser.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static Brush CreateColorBrush(string color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(color) is Color parsedColor)
            {
                var brush = new SolidColorBrush(parsedColor);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
            // Fall through to the neutral fallback for legacy or invalid values.
        }

        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 75, 8));
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (FormatException)
        {
            // Invalid values are shown with the neutral fallback brush.
        }

        return false;
    }
}
