namespace Scriptorium.Core.Services;

/// <summary>
/// Detects whether a directory name represents a numbered television season.
/// </summary>
public interface ISeasonFolderDetector
{
    /// <summary>Returns the detected season number, or <see langword="null"/> when the name is not a season folder.</summary>
    int? DetectSeasonNumber(string folderName);
}
