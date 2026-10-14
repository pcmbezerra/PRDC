# RDG compatibility

PRDC reads and writes XML documents with an `RDCMan` root and one or more `file` elements.

## Supported fields

| Element | Supported behavior |
| --- | --- |
| `file/properties/name` | Display and edit the RDG file name |
| `group/properties/name` | Display and edit group names |
| `server/properties/displayName` | Display and edit the server label |
| `server/properties/name` | Display and edit the RDP host name |
| `properties/expanded` | Persist file and group expansion state |
| Nested `group` and `server` elements | Display, add, edit, move through creation hierarchy, and remove |

## Preservation behavior

PRDC keeps the loaded `XDocument` as the source of truth. Unknown attributes and elements are retained unless their owning node is removed. Saving can normalize indentation and line endings.

## Current limitations

- Credentials profiles are preserved but not edited.
- Gateway, display, local-resource, and connection settings are preserved but not edited.
- Smart groups are not projected in the tree.
- Full parity with every RDCMan schema variation is not guaranteed.

Back up important `.rdg` files before editing them with any new tool. Do not publish RDG files containing private infrastructure data.
