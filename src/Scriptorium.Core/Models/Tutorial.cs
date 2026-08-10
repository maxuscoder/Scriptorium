namespace Scriptorium.Core.Models;

/// <summary>
/// Represents a tutorial in the media library.
/// </summary>
public class Tutorial : MediaItem
{
    /// <summary>Initializes a new instance of the <see cref="Tutorial"/> class.</summary>
    public Tutorial()
    {
        MediaType = MediaType.Tutorial;
    }
}
