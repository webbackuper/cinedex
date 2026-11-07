using Microsoft.Data.Sqlite;

namespace Cinedex.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cinedex-tests", UniqueName());
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>
    /// Un nom unique fait de lettres seulement. Le scanner reprend le nom du dossier quand celui du fichier
    /// ne porte pas d'année : un identifiant à chiffres finissait par en contenir une par hasard, et le titre
    /// attendu devenait le nom du dossier, une fois sur dix.
    /// </summary>
    private static string UniqueName() =>
        string.Concat(Guid.NewGuid().ToString("N").Select(c => char.IsAsciiDigit(c) ? (char)('g' + (c - '0')) : c));

    public void Dispose()
    {
        // Les connexions mises en pool gardent les fichiers ouverts sous Windows.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
