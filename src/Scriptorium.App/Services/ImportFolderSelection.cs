using Scriptorium.Core.Models;

namespace Scriptorium.App.Services;

/// <summary>
/// Contains the folder and media classification chosen during library import.
/// </summary>
public sealed record ImportFolderSelection(string FolderPath, MediaType MediaType);
