namespace PharmaERP.Infrastructure.Persistence.Entities;

/// <summary>
/// Infrastructure persistence entity for atomic, collision-proof document numbering.
/// Kept out of the business Domain layer as a persistence mechanism.
/// </summary>
public class DocumentSequence
{
    public string SequenceKey { get; set; } = string.Empty;

    public long LastNumber { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

