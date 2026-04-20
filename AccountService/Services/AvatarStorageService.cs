using ErrorOr;
using AccountService.Options;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace AccountService.Services;

public sealed class AvatarStorageService(
    IMinioClient minioClient,
    IOptions<AvatarOptions> options,
    ILogger<AvatarStorageService> logger
) {
    private static readonly (string ContentType, string Extension)[] AvatarFormats = [
        ("image/jpeg", "jpg"),
        ("image/png", "png"),
        ("image/webp", "webp")
    ];

    private string BucketName => options.Value.BucketName;

    public async Task<ErrorOr<StoredAvatar>> UploadAsync(string userId, IFormFile file, CancellationToken ct = default) {
        if (file.Length <= 0)
            return Error.Validation(description: "Avatar file is empty.");
        if (file.Length > options.Value.MaxFileSizeBytes)
            return Error.Validation(description: $"Avatar file exceeds {options.Value.MaxFileSizeBytes} bytes.");
        if (!options.Value.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return Error.Validation(description: "Avatar content type is not allowed.");

        string objectKey = BuildObjectKey(userId, file.ContentType);
        string version   = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        try {
            await EnsureBucketExistsAsync(ct);
            await using Stream stream = file.OpenReadStream();
            PutObjectArgs putArgs = new PutObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            await minioClient.PutObjectAsync(putArgs, ct);

            return new StoredAvatar(objectKey, file.ContentType, file.Length, version);
        }
        catch (MinioException ex) {
            logger.LogWarning(ex, "MinIO error while uploading avatar for user {userId}: {message}", userId, ex.Message);
            return Error.Failure(description: "Failed to upload avatar.");
        }
    }

    public async Task<StoredAvatar?> FindByUserIdAsync(string userId, CancellationToken ct = default) {
        foreach ((string contentType, string extension) in AvatarFormats) {
            string objectKey  = $"users/{userId}/avatar.{extension}";
            var    statResult = await GetStatAsync(objectKey, ct);
            if (!statResult.IsError)
                return new StoredAvatar(objectKey, contentType, statResult.Value.Size, string.Empty);
        }

        return null;
    }

    public async Task<Error?> DeleteByUserIdAsync(string userId, CancellationToken ct = default) {
        foreach ((_, string extension) in AvatarFormats) {
            string objectKey  = $"users/{userId}/avatar.{extension}";
            var    statResult = await GetStatAsync(objectKey, ct);
            if (statResult.IsError)
                continue;

            Error? deleteError = await DeleteAsync(objectKey, ct);
            if (deleteError is not null)
                return deleteError;
        }

        return null;
    }

    public async Task<Error?> DeleteOtherVariantsAsync(string userId, string keepObjectKey, CancellationToken ct = default) {
        foreach ((_, string extension) in AvatarFormats) {
            string objectKey = $"users/{userId}/avatar.{extension}";
            if (string.Equals(objectKey, keepObjectKey, StringComparison.Ordinal))
                continue;

            var statResult = await GetStatAsync(objectKey, ct);
            if (statResult.IsError)
                continue;

            Error? deleteError = await DeleteAsync(objectKey, ct);
            if (deleteError is not null)
                return deleteError;
        }

        return null;
    }

    public async Task<ErrorOr<ObjectStat>> GetStatAsync(string objectKey, CancellationToken ct = default) {
        try {
            StatObjectArgs args = new StatObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectKey);
            return await minioClient.StatObjectAsync(args, ct);
        }
        catch (MinioException ex) {
            logger.LogWarning(ex, "MinIO error while getting avatar stat for {objectKey}: {message}", objectKey, ex.Message);
            return Error.NotFound(description: "Avatar not found.");
        }
    }

    public async Task<Error?> WriteToStreamAsync(string objectKey, Stream outputStream, CancellationToken ct = default) {
        try {
            GetObjectArgs args = new GetObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectKey)
                .WithCallbackStream(async (stream, token) => await stream.CopyToAsync(outputStream, token));
            await minioClient.GetObjectAsync(args, ct);
            return null;
        }
        catch (MinioException ex) {
            logger.LogWarning(ex, "MinIO error while reading avatar {objectKey}: {message}", objectKey, ex.Message);
            return Error.NotFound(description: "Avatar not found.");
        }
    }

    public async Task<Error?> DeleteAsync(string? objectKey, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(objectKey))
            return null;

        try {
            RemoveObjectArgs args = new RemoveObjectArgs()
                .WithBucket(BucketName)
                .WithObject(objectKey);
            await minioClient.RemoveObjectAsync(args, ct);
            return null;
        }
        catch (MinioException ex) {
            logger.LogWarning(ex, "MinIO error while deleting avatar {objectKey}: {message}", objectKey, ex.Message);
            return Error.Failure(description: "Failed to delete avatar.");
        }
    }

    private async Task EnsureBucketExistsAsync(CancellationToken ct) {
        BucketExistsArgs existsArgs = new BucketExistsArgs().WithBucket(BucketName);
        bool             exists     = await minioClient.BucketExistsAsync(existsArgs, ct);
        if (exists)
            return;

        MakeBucketArgs makeArgs = new MakeBucketArgs().WithBucket(BucketName);
        await minioClient.MakeBucketAsync(makeArgs, ct);
    }

    private static string BuildObjectKey(string userId, string contentType) {
        string extension = contentType switch {
            "image/jpeg" => "jpg",
            "image/png"  => "png",
            "image/webp" => "webp",
            _            => "bin"
        };

        return $"users/{userId}/avatar.{extension}";
    }
}
