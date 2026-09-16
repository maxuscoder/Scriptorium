using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Scriptorium.App.Collections;

/// <summary>
/// An observable collection that can replace its contents with one collection-change
/// notification. This prevents WPF from laying out every item while a page is loading.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var replacement = items.ToArray();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
