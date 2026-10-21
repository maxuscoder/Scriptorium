using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer("AppLifetimeCollectionOrderer", "Scriptorium.App.Tests")]

// WPF application shutdown is process-wide: windows created afterwards cannot
// reliably load templates or native hosts, even on another STA dispatcher.
public sealed class AppLifetimeCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> collections) =>
        collections.OrderBy(collection => collection.DisplayName == "Z App lifetime" ? 1 : 0);
}

[CollectionDefinition("Native WPF UI", DisableParallelization = true)]
public sealed class NativeWpfUiCollection { }

[CollectionDefinition("Z App lifetime", DisableParallelization = true)]
public sealed class AppLifetimeCollection { }
