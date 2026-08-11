namespace Scriptorium.Core.Services;

/// <summary>
/// Associates a discovered media file path with the library folder that supplied it.
/// </summary>
public sealed record MediaFileCandidate(Guid LibraryFolderId, string Path);
