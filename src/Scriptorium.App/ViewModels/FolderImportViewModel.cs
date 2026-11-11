using System.IO;
using Scriptorium.App.Collections;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels;

/// <summary>Discovers supported files off the UI thread and owns their reviewable classifications.</summary>
public sealed class FolderImportViewModel : ViewModelBase
{
    private bool _isLoading;
    private string _status = "Choose a folder to review its media files.";

    public FolderImportViewModel(string folderPath = "") => FolderPath = folderPath;

    public string FolderPath { get; }
    public string FolderName => string.IsNullOrEmpty(FolderPath) ? "Folder overview" : new DirectoryInfo(FolderPath).Name;
    public BatchObservableCollection<ImportMediaFileViewModel> Files { get; } = [];
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool CanImport => !IsLoading && Files.Count > 0;
    public string ImportLabel => $"Add {Files.Count:N0} file{(Files.Count == 1 ? "" : "s")}";
    public string Summary => $"{Files.Count:N0} media file{(Files.Count == 1 ? "" : "s")} found";
    public string MovieCount => CountLabel(MediaType.Movie, "movie");
    public string TvShowCount => CountLabel(MediaType.TvShow, "TV episode");
    public string TutorialCount => CountLabel(MediaType.Tutorial, "tutorial");

    public async Task LoadAsync(IFileSystemService fileSystem, IMediaFormatService formats, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        OnPropertyChanged(nameof(CanImport));
        Status = "Looking for supported media in this folder and its subfolders…";
        try
        {
            var result = await Task.Run(() =>
            {
                var skipped = 0;
                var paths = fileSystem.EnumerateFiles([FolderPath], cancellationToken, onError: (_, _) => skipped++);
                var files = paths.Where(path => formats.IsSupportedExtension(Path.GetExtension(path)))
                    .OrderBy(path => Path.GetRelativePath(FolderPath, path), StringComparer.OrdinalIgnoreCase)
                    .Select(path => new ImportMediaFileViewModel(path, FolderPath)).ToArray();
                return (Files: files, Skipped: skipped);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var oldFile in Files) oldFile.PropertyChanged -= OnFileChanged;
            Files.ReplaceRange(result.Files);
            foreach (var file in Files) file.PropertyChanged += OnFileChanged;
            Status = Files.Count == 0
                ? "No supported media files found. Try another folder."
                : result.Skipped > 0
                    ? "Some locations could not be read. Only the files listed below will be added."
                    : "Types are suggested from filenames and folders. You can change each one before adding.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Status = "This folder could not be read. Check its permissions or choose another folder.";
        }
        finally
        {
            IsLoading = false;
            NotifyCountsChanged();
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(ImportLabel));
        }
    }

    public ImportFolderSelection CreateSelection() => new(FolderPath, MediaType.Movie,
        Files.Select(file => new ImportMediaFileSelection(file.Path, file.MediaType, file.SuggestedMediaType)).ToArray());

    private void OnFileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportMediaFileViewModel.MediaType)) NotifyCountsChanged();
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(MovieCount));
        OnPropertyChanged(nameof(TvShowCount));
        OnPropertyChanged(nameof(TutorialCount));
    }

    private string CountLabel(MediaType mediaType, string label)
    {
        var count = Files.Count(file => file.MediaType == mediaType);
        return $"{count:N0} {label}{(count == 1 ? "" : "s")}";
    }
}

public sealed class ImportMediaFileViewModel : ViewModelBase
{
    private MediaType _mediaType;

    public ImportMediaFileViewModel(string path, string folderPath)
    {
        Path = path;
        RelativePath = System.IO.Path.GetRelativePath(folderPath, path);
        var suggestion = MediaFileClassifier.Suggest(path, folderPath);
        SuggestedMediaType = suggestion.Type;
        _mediaType = suggestion.Type;
        SuggestionReason = suggestion.Reason;
    }

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string RelativePath { get; }
    public string Location => System.IO.Path.GetDirectoryName(RelativePath) is { Length: > 0 } directory ? directory : "Folder root";
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public MediaType SuggestedMediaType { get; }
    public string SuggestionReason { get; }
    public IReadOnlyList<MediaTypeChoice> MediaTypes => MediaTypeOptions.All;
    public MediaType MediaType
    {
        get => _mediaType;
        set
        {
            if (SetProperty(ref _mediaType, value)) OnPropertyChanged(nameof(ClassificationNote));
        }
    }
    public string ClassificationNote => MediaType == SuggestedMediaType ? SuggestionReason : "Type changed by you";
}
