using System;
using System.Text.Json.Serialization;

namespace PRDC;

internal sealed record GuacamolePortalMetadata(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("baseUrl")] string BaseUrl,
    [property: JsonPropertyName("lastSyncUtc")] DateTime LastSyncUtc);

