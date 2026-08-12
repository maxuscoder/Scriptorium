using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

/// <summary>
/// Associates a discovered media file path and classification with the library folder that supplied it.
/// </summary>
public sealed record MediaFileCandidate(Guid LibraryFolderId, MediaType MediaType, string Path);
