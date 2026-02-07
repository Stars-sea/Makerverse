namespace Contracts;

public record ActivityUpdated(
    string ActivityId,
    string Title,
    string Content,
    string[] Tags
);
