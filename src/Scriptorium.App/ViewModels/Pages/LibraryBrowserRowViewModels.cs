namespace Scriptorium.App.ViewModels.Pages;

/// <summary>A section label in the unified, virtualized library browser.</summary>
public sealed record LibraryBrowserSectionRow(string Title);

/// <summary>A responsive row of media cards.</summary>
public sealed class LibraryBrowserCardsRow
{
    private readonly IReadOnlyList<object> _sourceItems;
    private readonly int _startIndex;
    private readonly Func<object, object>? _createCardViewModel;

    public LibraryBrowserCardsRow(
        IReadOnlyList<object> sourceItems,
        int startIndex,
        int count,
        Func<object, object>? createCardViewModel = null)
    {
        _sourceItems = sourceItems;
        _startIndex = startIndex;
        Count = count;
        _createCardViewModel = createCardViewModel;
    }

    public int Count { get; }

    /// <summary>Enumerates only this row's items when WPF realizes the row.</summary>
    public IEnumerable<object> Cards
    {
        get
        {
            for (var index = _startIndex; index < _startIndex + Count; index++)
            {
                var sourceItem = _sourceItems[index];
                yield return _createCardViewModel is null ? sourceItem : _createCardViewModel(sourceItem);
            }
        }
    }
}

/// <summary>Marker row for configured-folder management.</summary>
public sealed class LibraryBrowserFoldersRow;

/// <summary>Marker row for manual TV-show group management.</summary>
public sealed class LibraryBrowserTvShowGroupsRow;

/// <summary>Marker row for the library's empty state.</summary>
public sealed class LibraryBrowserEmptyRow;
