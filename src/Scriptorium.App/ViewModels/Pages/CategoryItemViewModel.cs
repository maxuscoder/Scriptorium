using System.Windows.Media;
using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Provides editable presentation state for a category on the Categories page.
/// </summary>
public sealed class CategoryItemViewModel : ViewModelBase
{
    private string _name;
    private bool _isSelected;

    public CategoryItemViewModel(Category category, int mediaCount)
    {
        ArgumentNullException.ThrowIfNull(category);

        Id = category.Id;
        _name = category.Name;
        Color = category.Color;
        MediaCount = mediaCount;
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Color { get; }

    public Brush ColorBrush => CreateColorBrush(Color);

    public int MediaCount { get; }

    public string MediaCountText => $"{MediaCount} media item{(MediaCount == 1 ? string.Empty : "s")}";

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
}
