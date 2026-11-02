using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Cinedex.Core.Localization;

/// <summary>Une langue de l'interface : code (fr, en, zh-Hans…), nom dans la langue elle-même, sens d'écriture.</summary>
public sealed record UiLanguage(string Code, string NativeName, bool RightToLeft = false)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// Traduction de l'interface. Le texte français sert de clé : <c>Tr.T("Réglages")</c> renvoie « Settings » en anglais,
/// et le texte français tel quel s'il n'a pas (encore) de traduction. Les dictionnaires sont des fichiers JSON
/// embarqués (Localization/Lang/&lt;code&gt;.json), un par langue, de la forme { "texte français": "traduction" }.
/// </summary>
public static class Tr
{
    public const string SourceLanguage = "fr";

    /// <summary>Les 16 langues les plus parlées (anglais, mandarin, hindi, espagnol, français, arabe…).</summary>
    public static IReadOnlyList<UiLanguage> Languages { get; } =
    [
        new("fr", "Français"),
        new("en", "English"),
        new("zh-Hans", "中文（简体）"),
        new("hi", "हिन्दी"),
        new("es", "Español"),
        new("ar", "العربية", RightToLeft: true),
        new("bn", "বাংলা"),
        new("pt", "Português"),
        new("ru", "Русский"),
        new("ur", "اردو", RightToLeft: true),
        new("id", "Bahasa Indonesia"),
        new("de", "Deutsch"),
        new("ja", "日本語"),
        new("mr", "मराठी"),
        new("te", "తెలుగు"),
        new("tr", "Türkçe"),
    ];

    private static IReadOnlyDictionary<string, string> _strings = FrozenDictionary<string, string>.Empty;

    public static UiLanguage Current { get; private set; } = Languages[0];

    public static bool IsRightToLeft => Current.RightToLeft;

    /// <summary>Langue proposée au premier lancement : celle du système si elle est disponible, sinon l'anglais.</summary>
    public static string SystemDefault()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hans";
        }

        return Languages.FirstOrDefault(l => l.Code == culture.TwoLetterISOLanguageName)?.Code ?? "en";
    }

    /// <summary>Choisit la langue de l'interface (à faire avant d'ouvrir les fenêtres).</summary>
    public static void Use(string? code)
    {
        Current = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        _strings = Current.Code == SourceLanguage ? FrozenDictionary<string, string>.Empty : Load(Current.Code);
        try
        {
            var culture = CultureInfo.GetCultureInfo(Current.Code);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Culture inconnue du système (mode invariant) : seuls les textes changent.
        }
    }

    /// <summary>Texte traduit ; le texte français s'il n'a pas de traduction.</summary>
    public static string T(string french) =>
        french is not null && _strings.TryGetValue(french, out var translated) ? translated : french!;

    /// <summary>Vrai si ce texte a une traduction dans la langue courante (pour l'interface : ne toucher qu'aux textes connus).</summary>
    public static bool TryTranslate(string french, out string translated)
    {
        if (french is not null && _strings.TryGetValue(french, out var t) && !string.Equals(t, french, StringComparison.Ordinal))
        {
            translated = t;
            return true;
        }

        translated = french!;
        return false;
    }

    /// <summary>Format traduit puis rempli : <c>Tr.F("{0} titre(s)", n)</c>.</summary>
    public static string F(string frenchFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(frenchFormat), args);

    /// <summary>Dictionnaire brut d'une langue (pour les tests de complétude).</summary>
    public static IReadOnlyDictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cinedex.Core.Localization.Lang.{code}.json");
        if (stream is null)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        try
        {
            return (JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? []).ToFrozenDictionary(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return FrozenDictionary<string, string>.Empty;
        }
    }
}
