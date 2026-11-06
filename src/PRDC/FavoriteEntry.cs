using System;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PRDC;

internal enum FavoriteOrphanStatus
{
    Valid,
    MissingSource,
    MissingNode
}

internal sealed class FavoriteResolutionResult
{
    public FavoriteOrphanStatus Status { get; init; }
    public IConnectionSource? Source { get; init; }
    public XElement? ServerElement { get; init; }
    public bool IsFallbackMatch { get; init; }
    public string? ResolutionMessage { get; init; }
    public string? FailureReason => ResolutionMessage;

    public bool IsValid => Status == FavoriteOrphanStatus.Valid && Source is not null && ServerElement is not null;

    public static FavoriteResolutionResult ValidResult(IConnectionSource source, XElement serverElement, bool isFallback = false, string? message = null) =>
        new() { Status = FavoriteOrphanStatus.Valid, Source = source, ServerElement = serverElement, IsFallbackMatch = isFallback, ResolutionMessage = message };

    public static FavoriteResolutionResult MissingSourceResult(string message, IConnectionSource? source = null) =>
        new() { Status = FavoriteOrphanStatus.MissingSource, Source = source, ResolutionMessage = message };

    public static FavoriteResolutionResult MissingNodeResult(IConnectionSource source, string message) =>
        new() { Status = FavoriteOrphanStatus.MissingNode, Source = source, ResolutionMessage = message };
}

internal sealed class FavoriteEntry
{
    [JsonPropertyName("favoriteId")]
    public Guid FavoriteId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("sourceId")]
    public Guid SourceId { get; set; }

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty; // "RdgFile" ou "GuacamolePortal"

    [JsonPropertyName("originalDisplayName")]
    public string OriginalDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("customAlias")]
    public string? CustomAlias { get; set; }

    [JsonPropertyName("orderIndex")]
    public int OrderIndex { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("locator")]
    public ServerLocator Locator { get; set; } = new();

    [JsonIgnore]
    public string EffectiveDisplayName => !string.IsNullOrWhiteSpace(CustomAlias) ? CustomAlias : OriginalDisplayName;
}

