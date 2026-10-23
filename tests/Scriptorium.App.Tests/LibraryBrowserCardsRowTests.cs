using Scriptorium.App.ViewModels.Pages;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class LibraryBrowserCardsRowTests
{
    [Fact]
    public void Cards_are_read_lazily_and_only_for_the_requested_row()
    {
        var source = new TrackingReadOnlyList(Enumerable.Range(0, 100).Cast<object>().ToArray());
        var row = new LibraryBrowserCardsRow(source, startIndex: 24, count: 4);

        Assert.Equal(0, source.ReadCount);
        Assert.Equal([24, 25, 26, 27], row.Cards.Cast<int>());
        Assert.Equal(4, source.ReadCount);
    }

    [Fact]
    public void Card_view_models_are_created_only_when_the_row_is_enumerated()
    {
        var source = new TrackingReadOnlyList(Enumerable.Range(0, 100).Cast<object>().ToArray());
        var createdCards = new List<int>();
        var row = new LibraryBrowserCardsRow(
            source,
            startIndex: 40,
            count: 2,
            createCardViewModel: item =>
            {
                createdCards.Add((int)item);
                return item;
            });

        Assert.Empty(createdCards);
        Assert.Equal([40, 41], row.Cards.Cast<int>());
        Assert.Equal([40, 41], createdCards);
    }

    [Fact]
    public void ReenumerationPreservesCardInstancesWithoutRecreatingOrReadingSource()
    {
        var source = new TrackingReadOnlyList(Enumerable.Range(0, 100).Cast<object>().ToArray());
        var creations = 0;
        var row = new LibraryBrowserCardsRow(source, 40, 2, _ => { creations++; return new object(); });
        Assert.Empty(row.RealizedCards);
        Assert.Equal(0, source.ReadCount);
        var first = row.Cards.ToArray();
        var second = row.Cards.ToArray();
        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
        Assert.Equal(2, creations);
        Assert.Equal(2, source.ReadCount);
        Assert.Same(first[0], row.RealizedCards[0]);
    }

    private sealed class TrackingReadOnlyList(IReadOnlyList<object> items) : IReadOnlyList<object>
    {
        public int ReadCount { get; private set; }

        public int Count => items.Count;

        public object this[int index]
        {
            get
            {
                ReadCount++;
                return items[index];
            }
        }

        public IEnumerator<object> GetEnumerator() => items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
