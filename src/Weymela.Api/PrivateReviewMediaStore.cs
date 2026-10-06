using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Operations;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Api;

public sealed record SavedReviewMedia(string StorageKey, string ContentType, long Length,
    string Sha256, string OriginalFileName);

/// <summary>API-only storage for immutable private review copies.</summary>
public sealed class PrivateReviewMediaStore(RuntimeOptions options)
{
    public const string Mp4 = "video/mp4";

    public async Task<SavedReviewMedia> SaveAsync(IFormFile media, bool videoRequired, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.ReviewMediaDirectory))
            throw new ApplicationFailure(FailureKind.Validation, "Private review uploads are unavailable.");
        if (media.Length is < 1 or > RuntimeOptions.ReviewMediaBytes)
            throw new ApplicationFailure(FailureKind.Validation, "Review media must be 100 MB or smaller.");
        if (media.ContentType is not (Mp4 or PrivateReceiptStore.Jpeg or PrivateReceiptStore.Png)
            || videoRequired && media.ContentType != Mp4)
            throw new ApplicationFailure(FailureKind.Validation,
                videoRequired ? "Choose an MP4 review video." : "Choose an MP4, JPEG, or PNG review file.");
        if (media.ContentType != Mp4 && media.Length > RuntimeOptions.ReviewImageBytes)
            throw new ApplicationFailure(FailureKind.Validation, "Review images must be 10 MB or smaller.");

        var key = "m_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var path = Path.Combine(options.ReviewMediaDirectory, key);
        var temporary = Path.Combine(options.ReviewMediaDirectory, "tmp_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (var source = media.OpenReadStream())
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    written += read;
                    if (written > RuntimeOptions.ReviewMediaBytes)
                        throw new ApplicationFailure(FailureKind.Validation, "Review media must be 100 MB or smaller.");
                    digest.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                target.Flush(flushToDisk: true);
            }
            if (written != media.Length || !await ValidMediaAsync(temporary, media.ContentType, ct))
                throw new ApplicationFailure(FailureKind.Validation, "The review file is not a valid supported media file.");
            File.Move(temporary, path);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserRead);
            return new(key, media.ContentType, written,
                Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), SafeFileName(media.FileName, media.ContentType));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public FileStream Open(PrivateReviewMediaAsset asset)
    {
        if (!PrivateReviewMediaAsset.IsStorageKey(asset.StorageKey)
            || string.IsNullOrEmpty(options.ReviewMediaDirectory))
            throw new ApplicationFailure(FailureKind.NotFound, "Review media not found.");
        var path = Path.Combine(options.ReviewMediaDirectory, asset.StorageKey);
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || info.Length != asset.Length
            || info.Length is < 1 or > RuntimeOptions.ReviewMediaBytes)
            throw new ApplicationFailure(FailureKind.NotFound, "Review media not found.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Delete(string? key)
    {
        if (PrivateReviewMediaAsset.IsStorageKey(key) && !string.IsNullOrEmpty(options.ReviewMediaDirectory))
            File.Delete(Path.Combine(options.ReviewMediaDirectory, key!));
    }

    public async Task PruneOrphansAsync(WeymelaDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.ReviewMediaDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(options.ReviewMediaDirectory, "m_*"))
        {
            var file = new FileInfo(path);
            if (!PrivateReviewMediaAsset.IsStorageKey(file.Name) || file.LinkTarget is not null
                || file.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-1)) continue;
            if (!await db.PrivateReviewMediaAssets.AsNoTracking().AnyAsync(x => x.StorageKey == file.Name, ct))
                File.Delete(path);
        }
        foreach (var path in Directory.EnumerateFiles(options.ReviewMediaDirectory, "tmp_*"))
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is null && file.LastWriteTimeUtc <= DateTime.UtcNow.AddDays(-1)) File.Delete(path);
        }
    }

    private static async Task<bool> ValidMediaAsync(string path, string contentType, CancellationToken ct)
    {
        if (contentType == Mp4)
        {
            var header = new byte[32];
            await using var stream = File.OpenRead(path);
            var read = await stream.ReadAsync(header, ct);
            return read >= 12 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8)
                && header.AsSpan(8, 4).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 "u8) < 0;
        }
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return PrivateReceiptStore.ValidImage(bytes, contentType);
    }

    private static string SafeFileName(string value, string contentType)
    {
        var fallback = contentType == Mp4 ? "review.mp4" : contentType == PrivateReceiptStore.Png ? "review.png" : "review.jpg";
        var name = Path.GetFileName(value ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return fallback;
        name = new string(name.Select(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '-' or '_' ? x : '_').ToArray());
        return string.IsNullOrWhiteSpace(name) ? fallback : name[..Math.Min(name.Length, 120)];
    }
}
