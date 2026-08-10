namespace Scriptorium.Core.Models;

/// <summary>
/// Represents one season of a television show.
/// </summary>
public class Season
{
    /// <summary>Gets or sets the season's number within its television show.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episodes in the season.</summary>
    public List<Episode> Episodes { get; set; } = [];
}
