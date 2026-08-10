namespace Scriptorium.Core.Models;

/// <summary>
/// Represents one season of a television show.
/// </summary>
public class Season
{
    /// <summary>Gets or sets the unique identifier for the season.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the identifier of the television show that owns this season.</summary>
    public Guid TVShowId { get; set; }

    /// <summary>Gets or sets the television show that owns this season.</summary>
    public required TVShow TVShow { get; set; }

    /// <summary>Gets or sets the season's number within its television show.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episodes in the season.</summary>
    public List<Episode> Episodes { get; set; } = [];
}
