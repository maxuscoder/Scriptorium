using Scriptorium.Core.Models;

namespace Scriptorium.App.Services;

/// <summary>
/// Contains the folder and media classification chosen during library import.
/// </summary>
public sealed record ImportFolderSelection(string FolderPath, MediaType MediaType,
    IReadOnlyList<ImportMediaFileSelection>? Files = null);

/// <summary>A reviewed media file and its independently selected classification.</summary>
public sealed record ImportMediaFileSelection(string Path, MediaType MediaType, MediaType SuggestedMediaType);
