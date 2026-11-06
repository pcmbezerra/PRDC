using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace PRDC;

internal interface IConnectionSource : IDisposable
{
    Guid Id { get; }

    string DisplayName { get; }

    ConnectionSourceKind Kind { get; }

    bool IsDirty { get; }

    bool IsReadOnly { get; }

    RdgDocument Document { get; }

    IReadOnlyList<RdgNode> Roots => Document.Roots;

    IReadOnlyDictionary<XElement, GuacamoleConnectionReference> GuacamoleReferences { get; }

    event EventHandler? DirtyChanged;

    void Save();

    void SaveAs(string filePath);

    void MarkModified();

    void MarkClean();
}

