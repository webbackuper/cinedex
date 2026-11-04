using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Cinedex.Data;

/// <summary>
/// Images du catalogue, rangées par empreinte SHA-256 (pas de doublon, nom de fichier non contrôlé par la source).
/// Seuls les formats JPEG, PNG et WebP sont acceptés, reconnus à leur signature et non à leur extension.
/// </summary>
public sealed class ImageStore(CatalogDatabase database, string imagesDirectory)
{
    public const int MaxBytes = 15 * 1024 * 1024;

    public string ImagesDirectory { get; } = Path.GetFullPath(imagesDirectory);

    /// <summary>Enregistre l'image (si nouvelle) et renvoie son identifiant.</summary>
    public long Save(ReadOnlySpan<byte> content, string? sourceUrl = null)
    {
        var extension = DetectExtension(content) ?? throw new InvalidDataException("Format d'image non pris en charge.");
        if (content.Length > MaxBytes)
        {
            throw new InvalidDataException("Image trop volumineuse.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var relative = Path.Combine(hash[..2], hash + extension);
        var full = Path.Combine(ImagesDirectory, relative);
        if (!File.Exists(full))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var temp = full + ".tmp";
            File.WriteAllBytes(temp, content.ToArray());
            File.Move(temp, full, overwrite: true);
        }

        var (width, height) = ReadDimensions(content);
        using var connection = database.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO image (sha256, rel_path, width, height, source_url) VALUES (@hash, @relative, @width, @height, @sourceUrl)
            ON CONFLICT (sha256) DO UPDATE SET source_url = coalesce(image.source_url, excluded.source_url)
            RETURNING id;
            """,
            new { hash, relative = relative.Replace('\\', '/'), width, height, sourceUrl });
    }

    /// <summary>Associe une image à un titre pour un rôle (affiche, fond...), en remplaçant l'éventuelle précédente.</summary>
    public void Attach(long titleId, long imageId, string role)
    {
        using var connection = database.Open();
        Attach(connection, null, titleId, imageId, role);
    }

    internal static void Attach(SqliteConnection connection, SqliteTransaction? transaction, long titleId, long imageId, string role)
    {
        connection.Execute(
            "DELETE FROM title_image WHERE title_id = @titleId AND role = @role;",
            new { titleId, role },
            transaction);
        connection.Execute(
            "INSERT INTO title_image (title_id, image_id, role) VALUES (@titleId, @imageId, @role);",
            new { titleId, imageId, role },
            transaction);
    }

    /// <summary>Retire l'image d'un titre pour un rôle ; le fichier reste en réserve jusqu'au nettoyage des images.</summary>
    public void Remove(long titleId, string role)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM title_image WHERE title_id = @titleId AND role = @role;", new { titleId, role });
    }

    /// <summary>Enregistre une image lue sur le disque et l'associe au titre ; renvoie son chemin dans la réserve.</summary>
    public string? SetFromFile(long titleId, string path, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = File.ReadAllBytes(path);
        if (content.Length > MaxBytes)
        {
            throw new InvalidOperationException(Cinedex.Core.Localization.Tr.F("Image trop lourde ({0} Mo au plus).", MaxBytes / 1024 / 1024));
        }

        Attach(titleId, Save(content), role);
        return PathFor(titleId, role);
    }

    public bool HasImage(long titleId, string role)
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM title_image WHERE title_id = @titleId AND role = @role);",
            new { titleId, role });
    }

    /// <summary>Chemin absolu de l'image d'un titre pour un rôle, ou null.</summary>
    public string? PathFor(long titleId, string role)
    {
        using var connection = database.Open();
        var relative = connection.ExecuteScalar<string?>(
            """
            SELECT i.rel_path FROM title_image ti JOIN image i ON i.id = ti.image_id
            WHERE ti.title_id = @titleId AND ti.role = @role;
            """,
            new { titleId, role });
        return relative is null ? null : ResolveInside(relative);
    }

    /// <summary>Associe une image à une collection (affiche ou décor), en remplaçant l'éventuelle précédente.</summary>
    public void AttachToCollection(long collectionId, long imageId, string role)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM title_image WHERE collection_id = @collectionId AND role = @role;", new { collectionId, role }, transaction);
        connection.Execute(
            "INSERT INTO title_image (collection_id, image_id, role) VALUES (@collectionId, @imageId, @role);",
            new { collectionId, imageId, role },
            transaction);
        transaction.Commit();
    }

    public bool HasCollectionImage(long collectionId, string role)
    {
        using var connection = database.Open();
        return connection.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM title_image WHERE collection_id = @collectionId AND role = @role);",
            new { collectionId, role });
    }

    /// <summary>Résout un chemin relatif en refusant toute sortie du dossier des images.</summary>
    public string? ResolveInside(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(ImagesDirectory, relative));
        var root = ImagesDirectory.EndsWith(Path.DirectorySeparatorChar) ? ImagesDirectory : ImagesDirectory + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    internal static string? DetectExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    /// <summary>Lit largeur et hauteur dans l'en-tête PNG ou JPEG (SOF), sans décoder l'image.</summary>
    internal static (int? Width, int? Height) ReadDimensions(ReadOnlySpan<byte> data)
    {
        if (DetectExtension(data) == ".png" && data.Length >= 24)
        {
            return (ReadBigEndian32(data[16..20]), ReadBigEndian32(data[20..24]));
        }

        if (DetectExtension(data) == ".jpg")
        {
            var i = 2;
            while (i + 9 < data.Length && data[i] == 0xFF)
            {
                var marker = data[i + 1];
                var length = (data[i + 2] << 8) | data[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
                }

                if (length < 2)
                {
                    break;
                }

                i += 2 + length;
            }
        }

        return (null, null);
    }

    private static int ReadBigEndian32(ReadOnlySpan<byte> b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
}
