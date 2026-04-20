namespace AccountService.Services;

public sealed record StoredAvatar(
    string ObjectKey,
    string ContentType,
    long Size,
    string Version
);
