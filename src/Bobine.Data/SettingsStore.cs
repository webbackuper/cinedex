using Dapper;

namespace Bobine.Data;

/// <summary>Réglages propres à un catalogue, en clé-valeur. Les secrets n'y sont jamais stockés.</summary>
public sealed class SettingsStore(CatalogDatabase database)
{
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var connection = database.Open();
        return connection.ExecuteScalar<string?>("SELECT value FROM setting WHERE key = @key;", new { key });
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
