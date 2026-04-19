namespace ActivityService.DTOs;

public record ActivityResponseDto(
    string Id,
    string PublisherId,
    string? LinkedLiveId,
    string Title,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    ulong Votes,
    ulong ViewCount,
    List<string> TagSlugs
) {
    public static ActivityResponseDto FromModel(Models.Activity activity) {
        return new ActivityResponseDto(
            activity.Id,
            activity.PublisherId,
            activity.LinkedLiveId,
            activity.Title,
            activity.Content,
            activity.CreatedAt,
            activity.UpdatedAt,
            activity.Votes,
            activity.ViewCount,
            activity.TagSlugs
        );
    }
}
