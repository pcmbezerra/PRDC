using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PRDC;

internal sealed class RdgDocument
{
    private readonly XDocument _xml;

    private RdgDocument(XDocument xml, string? filePath)
    {
        _xml = xml;
        FilePath = filePath;
    }

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    public IReadOnlyList<RdgNode> Roots => _xml.Root?
        .Elements()
        .Select(RdgNode.Create)
        .Where(node => node?.Kind == RdgNodeKind.File)
        .Cast<RdgNode>()
        .ToArray() ?? [];

    public static RdgDocument CreateNew()
    {
        XDocument xml = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("RDCMan",
                new XAttribute("programVersion", "2.93"),
                new XAttribute("schemaVersion", "3"),
                new XElement("file",
                    new XElement("credentialsProfiles"),
                    new XElement("properties",
                        new XElement("expanded", "True"),
                        new XElement("name", "PRDC"))),
                new XElement("connected"),
                new XElement("favorites"),
                new XElement("recentlyUsed")));

        return new RdgDocument(xml, null);
    }

    public static RdgDocument Load(string filePath)
    {
        XDocument xml = XDocument.Load(filePath, LoadOptions.SetLineInfo);

        if (xml.Root is null || xml.Root.Name.LocalName != "RDCMan")
        {
            throw new InvalidDataException("O arquivo não possui um elemento raiz RDCMan.");
        }

        if (!xml.Root.Elements().Any(element => element.Name.LocalName == "file"))
        {
            throw new InvalidDataException("O arquivo não contém um nó file do RDCMan.");
        }

        return new RdgDocument(xml, Path.GetFullPath(filePath));
    }

    public RdgNode AddGroup(RdgNode parent, string name)
    {
        EnsureContainer(parent);

        XElement element = new("group",
            new XElement("properties",
                new XElement("expanded", "True"),
                new XElement("name", name.Trim())));

        parent.Element.Add(element);
        IsDirty = true;
        return RdgNode.Create(element)!;
    }

    public RdgNode AddServer(RdgNode parent, string displayName, string hostName)
    {
        EnsureContainer(parent);

        XElement element = new("server",
            new XElement("properties",
                new XElement("displayName", displayName.Trim()),
                new XElement("name", hostName.Trim())));

        parent.Element.Add(element);
        IsDirty = true;
        return RdgNode.Create(element)!;
    }

    public void UpdateGroup(RdgNode node, string name)
    {
        if (node.Kind is not (RdgNodeKind.File or RdgNodeKind.Group))
        {
            throw new InvalidOperationException("O nó selecionado não é um arquivo ou grupo.");
        }

        SetProperty(node.Element, "name", name.Trim());
        IsDirty = true;
    }

    public void UpdateServer(RdgNode node, string displayName, string hostName)
    {
        if (node.Kind != RdgNodeKind.Server)
        {
            throw new InvalidOperationException("O nó selecionado não é um servidor.");
        }

        SetProperty(node.Element, "displayName", displayName.Trim());
        SetProperty(node.Element, "name", hostName.Trim());
        IsDirty = true;
    }

    public void SetExpanded(RdgNode node, bool expanded)
    {
        if (node.Kind == RdgNodeKind.Server || node.IsExpanded == expanded)
        {
            return;
        }

        SetProperty(node.Element, "expanded", expanded ? "True" : "False");
        IsDirty = true;
    }

    public void Remove(RdgNode node)
    {
        if (node.Kind == RdgNodeKind.File)
        {
            throw new InvalidOperationException("O arquivo RDG não pode ser removido pela árvore.");
        }

        node.Element.Remove();
        IsDirty = true;
    }

    public void Save(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        XmlWriterSettings settings = new()
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };

        using XmlWriter writer = XmlWriter.Create(fullPath, settings);
        _xml.Save(writer);
        FilePath = fullPath;
        IsDirty = false;
    }

    private static void EnsureContainer(RdgNode node)
    {
        if (node.Kind is not (RdgNodeKind.File or RdgNodeKind.Group))
        {
            throw new InvalidOperationException("Servidores só podem ser adicionados a arquivos ou grupos.");
        }
    }

    private static void SetProperty(XElement owner, string name, string value)
    {
        XElement? properties = RdgNode.FindChild(owner, "properties");
        if (properties is null)
        {
            properties = new XElement("properties");
            owner.AddFirst(properties);
        }

        XElement? property = RdgNode.FindChild(properties, name);
        if (property is null)
        {
            properties.Add(new XElement(name, value));
        }
        else
        {
            property.Value = value;
        }
    }
}
