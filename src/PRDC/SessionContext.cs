using System;
using System.Xml.Linq;

namespace PRDC;

internal sealed class SessionContext
{
    public Guid SessionId { get; } = Guid.NewGuid();

    public Guid SourceId { get; }

    public string SourceDisplayName { get; internal set; }

    public XElement ServerElement { get; }

    public RdpSession Session { get; }

    public SessionContext(
        Guid sourceId,
        string sourceDisplayName,
        XElement serverElement,
        RdpSession session)
    {
        SourceId = sourceId;
        SourceDisplayName = sourceDisplayName ?? throw new ArgumentNullException(nameof(sourceDisplayName));
        ServerElement = serverElement ?? throw new ArgumentNullException(nameof(serverElement));
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }
}

