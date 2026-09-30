using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaBrowsePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Paged_browse_materializes_a_small_page_instead_of_the_full_library()
    {
        const int itemCount = 6000;
        const int pageSize = 80;
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-browse-benchmark-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var repository = new MediaItemRepository(new BenchmarkDbContextFactory(options));
            await repository.AddRangeAsync(Enumerable.Range(0, itemCount).Select(index => new MediaItem
            {
                Title = $"Sample title {index:D6}",
                Path = $"C:\\Benchmark\\sample-{index:D6}.mp4",
                MediaType = (MediaType)(index % 3),
                DateAdded = DateTimeOffset.UnixEpoch.AddMinutes(index)
            }));

            string queryPlan;
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.ExecuteSqlRawAsync("ANALYZE;");
                await context.Database.OpenConnectionAsync();
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = """
                    EXPLAIN QUERY PLAN
                    SELECT "Id" FROM "MediaItems"
                    WHERE "MediaType" = 2
                    ORDER BY (CASE WHEN "TitleOverride" IS NOT NULL AND trim("TitleOverride") <> ''
                        THEN trim("TitleOverride") ELSE "Title" END) COLLATE NOCASE, "Id"
                    LIMIT 80;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                var planDetails = new List<string>();
                while (await reader.ReadAsync())
                {
                    planDetails.Add(reader.GetString(3));
                }

                queryPlan = string.Join(" | ", planDetails);
            }

            Assert.Contains("IX_MediaItems_MediaType_DisplayTitle", queryPlan, StringComparison.Ordinal);

            var browseQuery = new MediaItemBrowseQuery
            {
                MediaTypes = [MediaType.Movie],
                SortOrder = MediaItemBrowseSortOrder.Ascending,
                PageSize = pageSize
            };

            // Warm EF's query compilation and SQLite's page cache before collecting timings.
            _ = await repository.GetAllAsync();
            _ = await repository.GetBrowsePageAsync(browseQuery, includeTotalCount: false);

            var fullLoadSamples = new List<double>();
            var pagedQuerySamples = new List<double>();
            for (var sample = 0; sample < 3; sample++)
            {
                var fullLoadTimer = Stopwatch.StartNew();
                var fullLibrary = await repository.GetAllAsync();
                var baselinePage = fullLibrary
                    .Where(item => item.MediaType == MediaType.Movie)
                    .OrderBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                    .Take(pageSize)
                    .ToArray();
                fullLoadTimer.Stop();
                fullLoadSamples.Add(fullLoadTimer.Elapsed.TotalMilliseconds);

                var pageTimer = Stopwatch.StartNew();
                var page = await repository.GetBrowsePageAsync(browseQuery, includeTotalCount: false);
                pageTimer.Stop();
                pagedQuerySamples.Add(pageTimer.Elapsed.TotalMilliseconds);
                Assert.Equal(baselinePage.Select(item => item.Id), page.Items.Select(item => item.Id));
            }

            output.WriteLine($"Synthetic SQLite library: {itemCount:N0} rows; page size: {pageSize}.");
            output.WriteLine($"Full load + in-memory filter/sort median: {Median(fullLoadSamples):F1} ms.");
            output.WriteLine($"Database-filtered page median: {Median(pagedQuerySamples):F1} ms.");
            output.WriteLine($"Rows materialized per browse request: {itemCount:N0} -> {pageSize}.");
            output.WriteLine($"SQLite query plan: {queryPlan}");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static double Median(IReadOnlyList<double> samples) => samples.Order().ElementAt(samples.Count / 2);

    private sealed class BenchmarkDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScriptoriumDbContext(options));
    }
}
