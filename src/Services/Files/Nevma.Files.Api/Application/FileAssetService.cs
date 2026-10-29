using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Nevma.Contracts.Files;
using Nevma.Files.Api.Domain;

namespace Nevma.Files.Api.Application;

public sealed class FileAssetService(
    IFileAssetRepository repository,
    IFileStorage storage,
    IFileScanner scanner,
    IFileContentProcessor contentProcessor,
    IFilesUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<FileDeliveryOptions> deliveryOptions,
    ILogger<FileAssetService> logger)
{
    public const long MaxFileSize = 25 * 1024 * 1024;

    public async Task<FileUploadResult> UploadAsync(
        Guid ownerId,
        FileUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (upload.Length is <= 0 or > MaxFileSize)
            return new FileUploadResult.Invalid("file", "File size must be between 1 byte and 25 MB.");
        var fileName = Path.GetFileName(upload.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
            return new FileUploadResult.Invalid("fileName", "File name is invalid.");

        await using var buffer = new MemoryStream((int)upload.Length);
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != upload.Length)
            return new FileUploadResult.Invalid("file", "File length changed during upload.");
        var imageType = DetectImageType(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (imageType is not null)
        {
            var (mime, extension) = imageType.Value;
            fileName = $"{Path.GetFileNameWithoutExtension(fileName)}{extension}";
            upload = upload with { ContentType = mime };
        }
        var validation = FileSignatureValidator.Validate(fileName, upload.ContentType, buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (validation is not null)
        {
            logger.LogWarning("File upload validation failed: extension {Extension}, content type {ContentType}, reason {Reason}", Path.GetExtension(fileName), upload.ContentType, validation);
            return new FileUploadResult.Invalid("file", validation);
        }

        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, cancellationToken);
        if (!scan.IsAvailable)
            return new FileUploadResult.ScanUnavailable();
        if (!scan.IsSafe)
        {
            logger.LogWarning(
                "File upload for user {OwnerId} was rejected by the malware scanner as {ThreatName}.",
                ownerId,
                scan.ThreatName ?? "unknown");
            return new FileUploadResult.Rejected("File failed the security scan.");
        }

        var processed = await contentProcessor.ProcessAsync(
            upload.ContentType,
            buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
            cancellationToken);
        if (!processed.IsSuccess)
            return new FileUploadResult.Invalid("file", processed.Error ?? "File processing failed.");
        var content = processed.Content ?? throw new InvalidOperationException("Processed content was not returned.");
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var storageKey = $"{ownerId:N}/{Guid.NewGuid():N}";
        await using var processedStream = new MemoryStream(content, writable: false);
        await storage.SaveAsync(storageKey, processedStream, cancellationToken);
        var asset = FileAsset.Create(
            ownerId,
            fileName,
            upload.ContentType.ToLowerInvariant(),
            content.Length,
            hash,
            storageKey,
            timeProvider.GetUtcNow());
        await repository.AddAsync(asset, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }
        logger.LogInformation(
            "File asset {FileId} was uploaded by {OwnerId} with content type {ContentType} and size {Size}.",
            asset.Id,
            ownerId,
            asset.ContentType,
            asset.Size);
        return new FileUploadResult.Uploaded(ToResponse(asset));
    }

    private static (string Mime, string Extension)? DetectImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) return null;
        if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ("image/jpeg", ".jpg");
        if (bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return ("image/png", ".png");
        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        return null;
    }

    public async Task<IReadOnlyList<FileAssetResponse>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(userId, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<FileAssetResponse?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        return asset is null || !asset.CanRead(userId) ? null : ToResponse(asset);
    }

    public async Task<FileDownloadResult> OpenAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        if (asset is null || !asset.CanRead(userId))
            return new FileDownloadResult.NotFound();
        var stream = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
        logger.LogInformation("File asset {FileId} was downloaded by {UserId}.", asset.Id, userId);
        return new FileDownloadResult.Found(stream, asset.FileName, asset.ContentType);
    }

    public async Task<FileUrlResult> CreateDownloadUrlAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        if (asset is null || !asset.CanRead(userId))
            return new FileUrlResult.NotFound();
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(deliveryOptions.Value.SignedUrlLifetimeMinutes, 1, 15));
        var expiresAt = timeProvider.GetUtcNow().Add(lifetime);
        var url = await storage.CreateReadUrlAsync(
            asset.StorageKey,
            asset.FileName,
            asset.ContentType,
            expiresAt,
            cancellationToken);
        if (url is null) return new FileUrlResult.NotSupported();
        logger.LogInformation("A temporary download URL was issued for file asset {FileId} to {UserId}.", asset.Id, userId);
        return new FileUrlResult.Created(new FileDownloadUrlResponse(url, expiresAt));
    }

    public async Task<bool> GrantAsync(Guid id, Guid ownerId, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        if (asset is null || !asset.Grant(ownerId, userId, timeProvider.GetUtcNow()))
            return false;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("File asset {FileId} access was granted by {OwnerId} to {UserId}.", id, ownerId, userId);
        return true;
    }

    public async Task<bool> RevokeAsync(Guid id, Guid ownerId, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        if (asset is null || !asset.Revoke(ownerId, userId))
            return false;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("File asset {FileId} access was revoked by {OwnerId} from {UserId}.", id, ownerId, userId);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(id, cancellationToken);
        if (asset is null || !asset.Delete(ownerId, timeProvider.GetUtcNow()))
            return false;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(asset.StorageKey, cancellationToken);
        logger.LogInformation("File asset {FileId} was deleted by {OwnerId}.", id, ownerId);
        return true;
    }

    private static FileAssetResponse ToResponse(FileAsset asset) =>
        new(asset.Id, asset.FileName, asset.ContentType, asset.Size, asset.Sha256, asset.CreatedAt);
}

public abstract record FileUploadResult
{
    public sealed record Uploaded(FileAssetResponse File) : FileUploadResult;
    public sealed record Invalid(string Field, string Message) : FileUploadResult;
    public sealed record Rejected(string Message) : FileUploadResult;
    public sealed record ScanUnavailable : FileUploadResult;
}

public abstract record FileUrlResult
{
    public sealed record Created(FileDownloadUrlResponse Download) : FileUrlResult;
    public sealed record NotFound : FileUrlResult;
    public sealed record NotSupported : FileUrlResult;
}

public sealed class FileDeliveryOptions
{
    public int SignedUrlLifetimeMinutes { get; init; } = 5;
}

public abstract record FileDownloadResult
{
    public sealed record Found(Stream Content, string FileName, string ContentType) : FileDownloadResult;
    public sealed record NotFound : FileDownloadResult;
}

internal static class FileSignatureValidator
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".pdf"] = "application/pdf", [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4"
    };

    public static string? Validate(string fileName, string contentType, ReadOnlySpan<byte> bytes)
    {
        var extension = Path.GetExtension(fileName);
        if (!MimeTypes.TryGetValue(extension, out var expectedMime) ||
            !string.Equals(expectedMime, contentType, StringComparison.OrdinalIgnoreCase))
            return "File extension and content type are not allowed.";
        if (bytes.Length < 12)
            return "File is too small to validate.";
        var valid = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".pdf" => bytes[..5].SequenceEqual("%PDF-"u8),
            ".mp3" => bytes[..3].SequenceEqual("ID3"u8) || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0),
            ".m4a" => bytes.Slice(4, 4).SequenceEqual("ftyp"u8),
            _ => false
        };
        return valid ? null : "File signature does not match its declared type.";
    }
}
