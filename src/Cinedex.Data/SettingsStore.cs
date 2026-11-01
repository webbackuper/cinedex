using Dapper;

namespace Cinedex.Data;

/// <summary>Réglages propres à un catalogue, en clé-valeur. Les secrets n'y sont jamais stockés.</summary>
public sealed class SettingsStore(CatalogDatabase database)
{
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var connection = database.Open();
        return connection.ExecuteScalar<string?>("SELECT value FROM setting WHERE key = @key;", new { key });
    }

    /// <summary>Tous les réglages, pour les reprendre dans un nouveau catalogue.</summary>
    public IReadOnlyDictionary<string, string?> All()
    {
        using var connection = database.Open();
        return connection.Query<(string Key, string? Value)>("SELECT key, value FROM setting;").ToDictionary(r => r.Key, r => r.Value);
    }

    public void Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var connection = database.Open();
        connection.Execute(
            "INSERT INTO setting (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;",
            new { key, value });
    }
}
