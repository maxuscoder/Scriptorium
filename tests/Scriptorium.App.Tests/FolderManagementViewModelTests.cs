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

public sealed class FolderManagementViewModelTests
{
    [Fact]
    public async Task Importing_a_folder_persists_it_through_the_repository()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-folders-{Guid.NewGuid():N}.db");
        var folderPath = Path.Combine(Path.GetTempPath(), $"scriptorium-import-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(folderPath);
            var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
                .Options;
            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            var folderRepository = new LibraryFolderRepository(new TestDbContextFactory(options));
            var importDialog = new RecordingImportFolderDialog(
                new ImportFolderSelection(folderPath, MediaType.Tutorial));
            string? statusMessage = null;
            var viewModel = new FolderManagementViewModel(
                importDialog,
                new AcceptingConfirmationDialog(),
                folderRepository,
                new LibraryFolderValidator(),
                new MediaItemRepository(new TestDbContextFactory(options)),
                () => Task.CompletedTask,
                message => statusMessage = message,
                () => false);

            await ((AsyncRelayCommand)viewModel.ImportFolderCommand).ExecuteAsync();

            var storedFolder = Assert.Single(await folderRepository.GetAllAsync());
            Assert.Equal(folderPath, storedFolder.Path);
            Assert.Equal(MediaType.Tutorial, storedFolder.MediaType);
            Assert.Equal("Library folder added.", statusMessage);
        }
        finally
        {
            File.Delete(databasePath);
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingImportFolderDialog(ImportFolderSelection selection) : IImportFolderDialog
    {
        public ImportFolderSelection SelectFolder(string? initialDirectory = null) => selection;
    }

    private sealed class AcceptingConfirmationDialog : IConfirmationDialog
    {
        public bool Confirm(string message, string title) => true;
    }
}
