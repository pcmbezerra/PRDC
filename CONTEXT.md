# PRDC

PRDC manages RDG server definitions and independent Remote Desktop sessions.

## Language

**Server**:
An RDP endpoint defined in the RDG document.
_Avoid_: Connection, session

**Session tab**:
The local workspace that owns one Remote Desktop session for a server.
_Avoid_: Server tab, connection tab

**Connect**:
Start a Remote Desktop session for the selected server.

**Reconnect**:
Replace the current Remote Desktop transport for a session tab and connect again to the same server.

**Disconnect**:
End the client transport while preserving the Windows session on the server and the local session tab.

**Log off**:
End the authenticated Windows session on the server while preserving the local session tab.
_Avoid_: Disconnect

**Close tab**:
Remove the local session tab, disconnecting its transport when necessary without logging off the Windows session.
_Avoid_: Log off
