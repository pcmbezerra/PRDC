using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PRDC;

internal sealed class ServerLocator
{
    // Campos RDG
    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("nodePath")]
    public string? NodePath { get; set; }

    [JsonPropertyName("hostName")]
    public string? HostName { get; set; }

    // Campos Guacamole
    [JsonPropertyName("portalId")]
    public Guid? PortalId { get; set; }

    [JsonPropertyName("dataSource")]
    public string? DataSource { get; set; }

    [JsonPropertyName("connectionIdentifier")]
    public string? ConnectionIdentifier { get; set; }

    // Comum
    [JsonPropertyName("originalDisplayName")]
    public string? OriginalDisplayName { get; set; }

    public static ServerLocator FromRdgServer(string? sourcePath, XElement serverElement)
    {
        ArgumentNullException.ThrowIfNull(serverElement);

        string nodePath = BuildNodePath(serverElement);
        string hostName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "name")?.Value?.Trim() ?? string.Empty;
        string displayName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "displayName")?.Value?.Trim() ?? hostName;

        return new ServerLocator
        {
            SourcePath = sourcePath,
            NodePath = nodePath,
            HostName = hostName,
            OriginalDisplayName = displayName
        };
    }

    public static ServerLocator FromGuacamoleReference(Guid portalId, GuacamoleConnectionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new ServerLocator
        {
            PortalId = portalId,
            DataSource = reference.DataSource,
            ConnectionIdentifier = reference.ConnectionIdentifier,
            OriginalDisplayName = reference.DisplayName
        };
    }

    public static string BuildNodePath(XElement serverElement)
    {
        ArgumentNullException.ThrowIfNull(serverElement);

        List<string> segments = [];
        XElement? current = serverElement;
        while (current is not null)
        {
            string localName = current.Name.LocalName;
            if (localName is "server" or "group")
            {
                XElement? prop = RdgNode.FindChild(current, "properties");
                string? name = RdgNode.FindChild(prop, "displayName")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = RdgNode.FindChild(prop, "name")?.Value;
                }
                if (!string.IsNullOrWhiteSpace(name))
                {
                    segments.Add(name.Trim());
                }
            }
            else if (localName == "file")
            {
                break;
            }
            current = current.Parent;
        }

        segments.Reverse();
        return "/" + string.Join("/", segments);
    }
}

