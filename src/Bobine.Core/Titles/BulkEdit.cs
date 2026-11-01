namespace Bobine.Core.Titles;

/// <summary>
/// Modification groupée : seuls les champs renseignés (non null) sont appliqués à la sélection,
/// comme dans l'édition multiple d'EMDB où seuls les champs modifiés sont mis à jour.
/// </summary>
public sealed record BulkEdit
{
    public bool? IsOwned { get; init; }

    public bool? IsFavorite { get; init; }

    public bool? IsShortlisted { get; init; }

    public bool? IsLocked { get; init; }

    public bool? IsSeen { get; init; }

    public string? MediumCode { get; init; }

    public string? Location { get; init; }

    /// <summary>Collection à affecter ; 0 retire de toute collection.</summary>
    public long? CollectionId { get; init; }

    public IReadOnlyList<string> AddTags { get; init; } = [];

    public IReadOnlyList<string> RemoveTags { get; init; } = [];

    public IReadOnlyList<string> AddGroups { get; init; } = [];
}

public sealed record CollectionInfo(long Id, string Name, int Count);

public sealed record LabelInfo(string Name, string? Color, int Count);
