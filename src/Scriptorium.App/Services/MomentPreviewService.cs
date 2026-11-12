using System.IO;
using System.Globalization;
using Scriptorium.Infrastructure;

namespace Scriptorium.App.Services;

/// <summary>Stores moment previews separately from the disposable artwork cache.</summary>
public sealed class MomentPreviewService
{
    private readonly LibVlcThumbnailGenerator generator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string DirectoryPath;

    public MomentPreviewService(LibVlcThumbnailGenerator generator, DatabaseLocation database)
        : this(generator, Path.Combine(Path.GetDirectoryName(database.FilePath)!, "MomentPreviews")) { }

    internal MomentPreviewService(LibVlcThumbnailGenerator generator, string directoryPath)
    {
        this.generator = generator;
        DirectoryPath = directoryPath;
    }
    private string MetadataPath(Guid id) => Path.Combine(DirectoryPath, $"{id:N}.txt");

    public (string? Path, long Seconds) Get(Guid id, long defaultSeconds)
    {
        try
        {
            var lines = File.ReadAllLines(MetadataPath(id));
            if (lines.Length == 2 && long.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
                seconds >= 0 && Path.GetFileName(lines[1]) == lines[1])
            {
                var path = Path.Combine(DirectoryPath, lines[1]);
                return (File.Exists(path) ? path : null, seconds);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return (null, defaultSeconds);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var image = Get(id, 0).Path;
            if (image is not null) File.Delete(image);
            File.Delete(MetadataPath(id));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { _gate.Release(); }
    }

    public async Task<string?> GenerateAsync(Guid id, string source, long? duration, long seconds)
    {
        await _gate.WaitAsync();
        string? output = null;
        try
        {
            if (seconds < 0 || (duration is > 0 && seconds >= duration) || !File.Exists(source)) return null;
            Directory.CreateDirectory(DirectoryPath);
            var oldPath = Get(id, seconds).Path;
            var filename = $"{id:N}-{Guid.NewGuid():N}.jpg";
            output = Path.Combine(DirectoryPath, filename);
            if (!await generator.GenerateFrameAsync(source, duration, output, seconds)) return null;
            var temporaryMetadata = MetadataPath(id) + ".tmp";
            await File.WriteAllLinesAsync(temporaryMetadata, [seconds.ToString(CultureInfo.InvariantCulture), filename]);
            File.Move(temporaryMetadata, MetadataPath(id), true);
            output = null;
            if (oldPath is not null) try { File.Delete(oldPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return Path.Combine(DirectoryPath, filename);
        }
        finally
        {
            if (output is not null) try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
