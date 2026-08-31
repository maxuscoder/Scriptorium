namespace Scriptorium.App.Models;

/// <summary>
/// User preferences persisted locally between application launches.
/// </summary>
public sealed class ApplicationSettings
{
    public string Theme { get; set; } = "System";

    public List<string> LibraryFolders { get; set; } = [];

    public bool OpenLastLibraryOnStartup { get; set; } = true;

    /// <summary>Gets or sets the preferred layout for media cards in the library.</summary>
    public string LibraryLayout { get; set; } = "Grid";
}
