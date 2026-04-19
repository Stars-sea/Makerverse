namespace ActivityService.DTOs;

public record CommentResponseDto(
    string Id,
    string ActivityId,
    string PublisherId,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    ulong Votes
) {
    public static CommentResponseDto FromModel(Models.Comment comment) {
        return new CommentResponseDto(
            comment.Id,
            comment.ActivityId,
            comment.PublisherId,
            comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt,
            comment.Votes
        );
    }
}
