namespace AccountService.Options;

public sealed class AvatarOptions {
    public string BucketName { get; set; } = "avatars";
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
    public int CacheMaxAgeSeconds { get; set; } = 3600;
}
