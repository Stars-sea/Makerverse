namespace Contracts;

public record ActivityCreated(
    string ActivityId,
    string Title,
    string Content,
    string[] Tags,
    DateTime CreatedAt
);
