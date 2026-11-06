using Cinedex.Core.Titles;

namespace Cinedex.App.ViewModels;

/// <summary>Une suggestion de « Vous aimerez aussi » : le titre proposé et la raison du rapprochement.</summary>
public sealed record SimilarTitle(TitleSummary Title, string Reason);
