using System.Text.Json;
using Cinedex.Core.Localization;

namespace Cinedex.App.Services;

/// <summary>
/// Langue de l'interface, commune à tous les catalogues, mémorisée dans <c>language.json</c> à la racine des données.
/// Sans choix enregistré : la langue du système si Cinedex la propose, sinon l'anglais.
/// </summary>
public static class LanguagePreference
{
    private const string FileName = "language.json";

    public static string Load(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            if (File.Exists(path) && JsonSerializer.Deserialize<State>(File.ReadAllText(path)) is { Language: { } code } &&
                Tr.Languages.Any(l => l.Code == code))
            {
                return code;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fichier illisible : langue par défaut.
        }

        return Tr.SystemDefault();
    }

    public static void Save(string dataRoot, string code)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(new State(code)));
    }

    private sealed record State(string? Language);
}
