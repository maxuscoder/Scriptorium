using System.IO;
using Microsoft.EntityFrameworkCore;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Repositories;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.App.Tests;

public sealed class CategoriesPageViewModelTests
{
    [Fact]
    public async Task Category_commands_preserve_collection_media_and_tile_presentation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-categories-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

        try
        {
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var contextFactory = new TestDbContextFactory(options);
            ICategoryRepository categoryRepository = new CategoryRepository(contextFactory);
            var mediaRepository = new MediaItemRepository(contextFactory);
            var categoryService = new CategoryService(categoryRepository, mediaRepository);
            var createDialog = new RecordingCreateCategoryDialog(new CreateCategoryDialogResult("Learning", "#6B46C1"));
            var confirmationDialog = new AcceptingConfirmationDialog();
            using var viewModel = new CategoriesPageViewModel(
                categoryRepository,
                categoryService,
                confirmationDialog,
                createDialog,
                mediaRepository,
                new FavoriteService(mediaRepository));

            await ((AsyncRelayCommand)viewModel.CreateCategoryCommand).ExecuteAsync();
            await viewModel.RefreshAsync();

            var category = Assert.Single(viewModel.Categories);
            Assert.Equal("Learning", category.Name);
            Assert.Equal("#6B46C1", category.Color);
            Assert.Equal(0, category.MediaCount);
            Assert.Equal(string.Empty, category.MediaTypeSummaryText);

            var media = new MediaItem
            {
                Title = "Network fundamentals",
                Path = @"C:\\Courses\\network-fundamentals.mp4",
                ThumbnailPath = @"C:\\Artwork\\network-fundamentals.png",
                MediaType = MediaType.Tutorial
            };
            await mediaRepository.AddAsync(media);
            Assert.True(await categoryService.AssignToMediaAsync(media.Id, category.Id));
            await viewModel.RefreshAsync();

            category = Assert.Single(viewModel.Categories);
            Assert.Equal(1, category.MediaCount);
            Assert.Equal(@"C:\\Artwork\\network-fundamentals.png", category.PreviewThumbnailPath);
            Assert.True(viewModel.HasSelectedCategory);
            Assert.True(viewModel.HasMediaItems);
            Assert.Equal("Network fundamentals", Assert.Single(viewModel.MediaItems).Title);

            category.Name = "Courses";
            category.Color = "#2563EB";
            await ((AsyncRelayCommand)viewModel.RenameCategoryCommand).ExecuteAsync(category);
            await viewModel.RefreshAsync();
            category = Assert.Single(viewModel.Categories);
            Assert.Equal("Courses", category.Name);
            Assert.Equal("#2563EB", category.Color);
            Assert.Equal("Category 'Courses' saved.", viewModel.StatusMessage);

            await ((AsyncRelayCommand)viewModel.DeleteCategoryCommand).ExecuteAsync(category);
            await viewModel.RefreshAsync();
            Assert.Empty(viewModel.Categories);
            Assert.False(viewModel.HasSelectedCategory);
            Assert.Null((await mediaRepository.GetByIdAsync(media.Id))!.CategoryId);
            Assert.Equal(1, confirmationDialog.ConfirmationCount);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingCreateCategoryDialog(CreateCategoryDialogResult result) : ICreateCategoryDialog
    {
        public CreateCategoryDialogResult? Show(IReadOnlyCollection<string> existingCategoryNames) => result;
    }

    private sealed class AcceptingConfirmationDialog : IConfirmationDialog
    {
        public int ConfirmationCount { get; private set; }

        public bool Confirm(string message, string title)
        {
            ConfirmationCount++;
            return true;
        }
    }
}
