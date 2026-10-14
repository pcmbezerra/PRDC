using System.Xml.Linq;

namespace PRDC;

internal enum RdgNodeKind
{
    File,
    Group,
    Server
}

internal sealed class RdgNode(XElement element, RdgNodeKind kind)
{
    public XElement Element { get; } = element;

    public RdgNodeKind Kind { get; } = kind;

    public string Name
    {
        get
        {
            XElement? properties = FindChild(Element, "properties");
            string? displayName = FindChild(properties, "displayName")?.Value;
            string? name = FindChild(properties, "name")?.Value;

            return Kind == RdgNodeKind.Server
                ? FirstNonEmpty(displayName, name, "Servidor")
                : FirstNonEmpty(name, Kind == RdgNodeKind.File ? "Arquivo RDG" : "Grupo");
        }
    }

    public string HostName =>
        FindChild(FindChild(Element, "properties"), "name")?.Value.Trim() ?? string.Empty;

    public bool IsExpanded =>
        bool.TryParse(FindChild(FindChild(Element, "properties"), "expanded")?.Value, out bool expanded)
        && expanded;

    public IReadOnlyList<RdgNode> Children => Element
        .Elements()
        .Select(Create)
        .Where(node => node is not null)
        .Cast<RdgNode>()
        .ToArray();

    public static RdgNode? Create(XElement element)
    {
        return element.Name.LocalName switch
        {
            "file" => new RdgNode(element, RdgNodeKind.File),
            "group" => new RdgNode(element, RdgNodeKind.Group),
            "server" => new RdgNode(element, RdgNodeKind.Server),
            _ => null
        };
    }

    internal static XElement? FindChild(XContainer? container, string localName)
    {
        return container?.Elements().FirstOrDefault(
            element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}
