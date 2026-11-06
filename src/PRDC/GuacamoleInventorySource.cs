using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PRDC;

internal sealed class GuacamoleInventorySource : IConnectionSource
{
    private readonly Dictionary<XElement, GuacamoleConnectionReference> _references = new(ReferenceEqualityComparer.Instance);
    private readonly string? _customDisplayName;
    private bool _isModifiedByUser;

    public Guid Id { get; }

    public ConnectionSourceKind Kind => ConnectionSourceKind.GuacamoleInventory;

    public RdgDocument Document { get; }

    public IReadOnlyList<RdgNode> Roots => Document.Roots;

    public bool IsReadOnly => false;

    public Guid? PortalId { get; }

    public bool IsMissing { get; }

    public GuacamoleInventorySource(
        RdgDocument document,
        IReadOnlyDictionary<XElement, GuacamoleConnectionReference>? references,
        Guid? id = null,
        string? displayName = null,
        Guid? portalId = null,
        bool isMissing = false)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Id = id ?? Guid.NewGuid();
        _customDisplayName = displayName;
        PortalId = portalId ?? Id;
        IsMissing = isMissing;

        if (!string.IsNullOrWhiteSpace(displayName) && Document.Roots.Count > 0)
        {
            Document.UpdateGroup(Document.Roots[0], displayName);
        }

        if (references is not null)
        {
            foreach (var (element, reference) in references)
            {
                var refWithPortal = reference.PortalId.HasValue
                    ? reference
                    : reference with { PortalId = Id };
                _references[element] = refWithPortal;
            }
        }
    }

    public static GuacamoleInventorySource FromImportResult(
        GuacamoleImportResult result,
        Guid? id = null,
        string? displayName = null,
        Guid? portalId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new GuacamoleInventorySource(result.Document, result.ConnectionReferences, id, displayName, portalId);
    }

    public static GuacamoleInventorySource CreateMissing(Guid portalId, string? displayName = null, Guid? id = null)
    {
        RdgDocument doc = RdgDocument.CreateNew();
        string baseName = string.IsNullOrWhiteSpace(displayName) ? "Portal Guacamole" : displayName;
        string missingName = $"{baseName} (não encontrado)";
        if (doc.Roots.Count > 0)
        {
            doc.UpdateGroup(doc.Roots[0], missingName);
        }

        return new GuacamoleInventorySource(doc, null, id ?? portalId, missingName, portalId, isMissing: true);
    }

    public string DisplayName => _customDisplayName ?? "Guacamole";

    public string? BaseUrl => _references.Values.FirstOrDefault()?.BaseUri.AbsoluteUri;

    public bool IsDirty => _isModifiedByUser;

    public IReadOnlyDictionary<XElement, GuacamoleConnectionReference> GuacamoleReferences => _references;

    public event EventHandler? DirtyChanged;

    public void MarkModified()
    {
        bool wasDirty = IsDirty;
        _isModifiedByUser = true;
        if (!wasDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkClean()
    {
        bool wasDirty = IsDirty;
        _isModifiedByUser = false;
        if (wasDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Save()
    {
        if (Document.FilePath is null)
        {
            throw new InvalidOperationException("O inventário do Guacamole não possui caminho de exportação RDG definido. Utilize SaveAs.");
        }

        bool wasDirty = IsDirty;
        Document.Save(Document.FilePath);
        _isModifiedByUser = false;
        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SaveAs(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        bool wasDirty = IsDirty;
        Document.Save(filePath);
        _isModifiedByUser = false;
        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveReferencesFor(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        XElement[] keys = _references.Keys
            .Where(key => ReferenceEquals(key, element)
                || key.Ancestors().Any(ancestor => ReferenceEquals(ancestor, element)))
            .ToArray();

        foreach (XElement key in keys)
        {
            _references.Remove(key);
        }
    }

    public void Dispose()
    {
        _references.Clear();
    }
}

