using Scriptorium.Core.Models;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>Represents one category choice for a media item, including the uncategorized choice.</summary>
public sealed class MediaCategoryOptionViewModel
{
    public MediaCategoryOptionViewModel(Guid? id, string name, Category? category = null)
    {
        Id = id;
        Name = name;
        Category = category;
    }

    public Guid? Id { get; }

    public string Name { get; }

    /// <summary>Gets the text used by the shared ComboBox display template.</summary>
    public string DisplayName => Name;

    internal Category? Category { get; }
}
