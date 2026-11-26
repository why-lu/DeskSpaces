namespace DeskSpaces;

public sealed record ClipboardEntry(Guid SourceWorkspaceId, Guid OriginalEntryId, DesktopEntry Entry);
