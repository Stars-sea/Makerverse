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

public record SimplifiedActivityResponseDto(
    string Id,
    string PublisherId,
    string Title,
    string ShortContent,
    DateTime CreatedOrUpdatedAt,
    ulong Votes,
    ulong ViewCount,
    List<string> TagSlugs
) {
    private const int MaxContentLength = 100;

    public static SimplifiedActivityResponseDto FromModel(Models.Activity activity) {
        string shortContent = activity.Content.Length > MaxContentLength
            ? activity.Content[..MaxContentLength] + "..."
            : activity.Content;
        
        return new SimplifiedActivityResponseDto(
            activity.Id,
            activity.PublisherId,
            activity.Title,
            shortContent,
            activity.UpdatedAt ?? activity.CreatedAt,
            activity.Votes,
            activity.ViewCount,
            activity.TagSlugs.Take(4).ToList()
        );
    }
}
