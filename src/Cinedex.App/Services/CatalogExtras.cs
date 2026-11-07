using Cinedex.Data;

namespace Cinedex.App.Services;

/// <summary>
/// Dépôts de lecture qui enrichissent la fenêtre principale sans lui être indispensables : rapprochements,
/// suggestions TMDb, bandes de la page d'accueil, chiffres du catalogue. Groupés pour qu'en ajouter un ne
/// rallonge pas la liste des paramètres du constructeur.
/// </summary>
public sealed record CatalogExtras(
    RecommendationRepository? Recommendations = null,
    SuggestionRepository? Suggestions = null,
    HomeRepository? Home = null,
    StatisticsRepository? Statistics = null,
    ReferenceListService? Lists = null,
    MaintenanceRepository? Maintenance = null);
