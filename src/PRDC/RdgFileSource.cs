using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;

namespace PRDC;

internal sealed class RdgFileSource : IConnectionSource
{
    private static readonly IReadOnlyDictionary<XElement, GuacamoleConnectionReference> EmptyReferences =
        new ReadOnlyDictionary<XElement, GuacamoleConnectionReference>(new Dictionary<XElement, GuacamoleConnectionReference>());

    private readonly string? _customDisplayName;
    private bool _isExplicitlyDirty;

    public Guid Id { get; }

    public ConnectionSourceKind Kind => ConnectionSourceKind.RdgFile;

    public RdgDocument Document { get; }

    public IReadOnlyList<RdgNode> Roots => Document.Roots;

    public bool IsReadOnly => false;

    public bool IsMissing { get; }

    public string? MissingFilePath { get; }

    public string? FilePath => Document.FilePath ?? MissingFilePath;

    public RdgFileSource(
        RdgDocument document,
        Guid? id = null,
        string? customDisplayName = null,
        string? missingFilePath = null,
        bool isMissing = false)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Id = id ?? Guid.NewGuid();
        _customDisplayName = customDisplayName;
        MissingFilePath = missingFilePath;
        IsMissing = isMissing;
    }

    public static RdgFileSource CreateNew(string? displayName = null)
    {
        RdgDocument doc = RdgDocument.CreateNew();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            RdgNode? root = doc.Roots.Count > 0 ? doc.Roots[0] : null;
            if (root is not null)
            {
                XElement? prop = root.Element.Element("properties");
                XElement? nameElem = prop?.Element("name");
                if (nameElem is not null)
                {
                    nameElem.Value = displayName;
                }
            }
        }

        return new RdgFileSource(doc, null, displayName);
    }

    public static RdgFileSource Load(string filePath, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new RdgFileSource(RdgDocument.Load(filePath), id);
    }

    public static RdgFileSource CreateMissing(string filePath, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        RdgDocument emptyDoc = RdgDocument.CreateNew();
        string fileName = Path.GetFileName(filePath);
        string missingName = $"{fileName} (não encontrado)";
        if (emptyDoc.Roots.Count > 0)
        {
            XElement? prop = emptyDoc.Roots[0].Element.Element("properties");
            XElement? nameElem = prop?.Element("name");
            if (nameElem is not null)
            {
                nameElem.Value = missingName;
            }
        }

        return new RdgFileSource(emptyDoc, id, missingName, filePath, isMissing: true);
    }

    public string DisplayName =>
        IsMissing
            ? $"{Path.GetFileName(FilePath ?? "Arquivo")} (não encontrado)"
            : (Document.FilePath is not null
                ? Path.GetFileName(Document.FilePath)
                : (_customDisplayName ?? "Novo arquivo"));

    public bool IsDirty => _isExplicitlyDirty || Document.IsDirty;

    public IReadOnlyDictionary<XElement, GuacamoleConnectionReference> GuacamoleReferences => EmptyReferences;

    public event EventHandler? DirtyChanged;

    public void Save()
    {
        if (Document.FilePath is null)
        {
            throw new InvalidOperationException("Não é possível salvar um documento sem caminho de arquivo definido. Utilize SaveAs.");
        }

        bool wasDirty = IsDirty;
        Document.Save(Document.FilePath);
        _isExplicitlyDirty = false;
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
        _isExplicitlyDirty = false;
        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkModified()
    {
        bool wasDirty = IsDirty;
        _isExplicitlyDirty = true;
        if (!wasDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkClean()
    {
        bool wasDirty = IsDirty;
        _isExplicitlyDirty = false;
        if (wasDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        // Recursos gerenciados em memória são liberados pelo GC.
    }
}

