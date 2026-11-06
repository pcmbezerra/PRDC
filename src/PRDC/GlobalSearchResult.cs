namespace PRDC;

/// <summary>
/// Representa o resultado de uma consulta de busca no catálogo global (RFC-006).
/// Contém a entrada localizada e a pontuação (score) atribuída pelo algoritmo de ranqueamento.
/// </summary>
internal sealed class GlobalSearchResult
{
    /// <summary>
    /// Entrada correspondente no catálogo global.
    /// </summary>
    public required GlobalSearchEntry Entry { get; init; }

    /// <summary>
    /// Pontuação de relevância calculada para a consulta.
    /// </summary>
    public required int Score { get; init; }
}

