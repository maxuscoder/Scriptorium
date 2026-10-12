using System.Collections.Concurrent;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Finds artwork beside a video, preferring its basename over shared folder artwork.</summary>
public sealed class LocalArtworkFinder
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    private readonly ConcurrentDictionary<string, ArtworkDirectory> _directories = new(StringComparer.OrdinalIgnoreCase);

    public string? Find(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(directory)) return null;
        var info = new DirectoryInfo(directory);
        if (!info.Exists) return null;
        var stamp = info.LastWriteTimeUtc.Ticks;
        if (!_directories.TryGetValue(directory, out var cached) || cached.LastWriteTicks != stamp)
        {
            var files = Directory.EnumerateFiles(directory)
                .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToLookup(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);
            cached = new ArtworkDirectory(stamp, files);
            _directories[directory] = cached;
            if (_directories.Count > 64)
                foreach (var key in _directories.Keys)
                    if (!string.Equals(key, directory, StringComparison.OrdinalIgnoreCase))
                    { _directories.TryRemove(key, out _); break; }
        }
        foreach (var basename in new[] { Path.GetFileNameWithoutExtension(mediaPath), "poster", "folder" })
        foreach (var extension in Extensions)
        {
            var match = cached.Files[basename + extension].FirstOrDefault(File.Exists);
            if (match is not null) return match;
        }
        return null;
    }

    private sealed record ArtworkDirectory(long LastWriteTicks, ILookup<string, string> Files);
}
