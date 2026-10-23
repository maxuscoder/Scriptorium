namespace Scriptorium.Core.Services;

/// <summary>Extracts a small video preview without changing the source file.</summary>
public interface IThumbnailGenerator
{
    Task<bool> GenerateAsync(string sourcePath, long? runtimeSeconds, string outputPath,
        CancellationToken cancellationToken = default);
}
