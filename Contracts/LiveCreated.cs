namespace Contracts;

public record LiveCreated(
    string LiveId,
    string Title,
    List<string> Tags,
    DateTime Created,
    DateTime? Started
);
