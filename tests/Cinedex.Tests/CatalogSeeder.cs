using Cinedex.Data;
using Dapper;

namespace Cinedex.Tests;

/// <summary>
/// Remplit un catalogue de test réaliste et volumineux (titres, exemplaires, genres, générique, fichiers,
/// visionnages, prêts, affiches) pour mesurer les performances et tester l'interface.
/// </summary>
internal static class CatalogSeeder
{
    public static void Seed(CatalogDatabase database, int titles, int people = 3000, int seed = 7)
    {
        var random = new Random(seed);
        string[] words = ["Nuit", "Ombre", "Retour", "Dernier", "Voyage", "Secret", "Ville", "Feu", "Silence", "Route", "Étoile", "Mer", "Rêve", "Loup", "Ciel", "Guerre", "Amour", "Héros", "Mystère", "Temps"];
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var genres = connection.Query<long>("SELECT id FROM genre;", transaction: transaction).ToList();
        var mediums = connection.Query<long>("SELECT id FROM ref_list WHERE kind = 'medium';", transaction: transaction).ToList();

        for (var p = 1; p <= people; p++)
        {
            connection.Execute("INSERT INTO person (id, name) VALUES (@p, @name);", new { p, name = $"Personne {p}" }, transaction);
        }

        for (var i = 0; i < 6; i++)
        {
            connection.Execute(
                "INSERT INTO image (id, sha256, rel_path) VALUES (@id, @sha, @path);",
                new { id = i + 1, sha = i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture), path = $"seed/{i}.jpg" },
                transaction);
        }

        for (var id = 1; id <= titles; id++)
        {
            var title = $"{words[random.Next(words.Length)]} {words[random.Next(words.Length)]} {id}";
            connection.Execute(
                """
                INSERT INTO title (id, kind, title, sort_title, year, runtime_minutes, user_rating, imdb_rating, overview, is_favorite)
                VALUES (@id, @kind, @title, lower(@title), @year, @runtime, @rating, @imdb, @overview, @favorite);
                """,
                new
                {
                    id,
                    kind = id % 15 == 0 ? "series" : "movie",
                    title,
                    year = 1940 + random.Next(86),
                    runtime = 80 + random.Next(100),
                    rating = random.Next(4) == 0 ? (double?)random.Next(1, 11) : null,
                    imdb = Math.Round(4 + (random.NextDouble() * 5), 1),
                    overview = $"Synopsis du film {id} : une histoire de {words[random.Next(words.Length)].ToLowerInvariant()}.",
                    favorite = random.Next(10) == 0 ? 1 : 0,
                },
                transaction);
            var copy = connection.ExecuteScalar<long>(
                "INSERT INTO copy (title_id, number, medium_id) VALUES (@id, @id, @medium) RETURNING id;",
                new { id, medium = mediums[random.Next(mediums.Count)] },
                transaction);
            foreach (var genre in genres.OrderBy(_ => random.Next()).Take(1 + random.Next(3)))
            {
                connection.Execute("INSERT INTO title_genre (title_id, genre_id) VALUES (@id, @genre);", new { id, genre }, transaction);
            }

            for (var position = 0; position < 6; position++)
            {
                connection.Execute(
                    "INSERT INTO credit (title_id, person_id, role, position) VALUES (@id, @person, @role, @position);",
                    new { id, person = 1 + random.Next(people), role = position == 0 ? "director" : "actor", position },
                    transaction);
            }

            if (random.Next(2) == 0)
            {
                connection.Execute(
                    "INSERT INTO media_file (copy_id, path, size_bytes, width, height, volume_label) VALUES (@copy, @path, @size, @width, @height, @volume);",
                    new { copy, path = $"Films/{title}.mkv", size = 1_000_000_000L + random.Next(int.MaxValue), width = 1920, height = random.Next(3) == 0 ? 2160 : 1080, volume = $"DISQUE{random.Next(5)}" },
                    transaction);
            }

            if (random.Next(3) == 0)
            {
                connection.Execute("INSERT INTO viewing (title_id) VALUES (@id);", new { id }, transaction);
            }

            if (random.Next(50) == 0)
            {
                connection.Execute("INSERT INTO loan (copy_id, borrower, loaned_at) VALUES (@copy, 'Paul', '2026-01-01');", new { copy }, transaction);
            }

            connection.Execute("INSERT INTO title_image (title_id, image_id, role) VALUES (@id, @image, 'poster');", new { id, image = 1 + random.Next(6) }, transaction);
        }

        transaction.Commit();
        connection.Execute("ANALYZE;");
    }
}
