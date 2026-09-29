using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Weymela.Application;
using Weymela.Infrastructure.Operations;

namespace Weymela.Api;

// ProofReference stores only this opaque name. The directory is an API-only durable mount,
// never a web root, and all reads go through an authorized deposit record.
public sealed class PrivateReceiptStore(RuntimeOptions options)
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    public async Task<string> SaveAsync(Guid businessId, string requestKey, IFormFile receipt, CancellationToken ct)
    {
        if (options.DepositMode != "ManualApproval" || string.IsNullOrEmpty(options.ReceiptDirectory))
            throw new ApplicationFailure(FailureKind.Validation, "Receipt deposits are unavailable.");
        if (receipt.Length is < 1 or > RuntimeOptions.ReceiptBytes)
            throw new ApplicationFailure(FailureKind.Validation, "Choose a JPEG or PNG receipt smaller than 4 MB.");
        if (receipt.ContentType is not (Jpeg or Png))
            throw new ApplicationFailure(FailureKind.Validation, "Choose a JPEG or PNG receipt.");

        await using var source = receipt.OpenReadStream();
        using var buffer = new MemoryStream((int)receipt.Length);
        await source.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length != receipt.Length || !ValidImage(bytes, receipt.ContentType))
            throw new ApplicationFailure(FailureKind.Validation, "The receipt image is invalid. Choose a JPEG or PNG image.");

        var digest = SHA256.HashData(bytes);
        var identity = Encoding.UTF8.GetBytes($"{businessId:N}:{requestKey}:{Convert.ToHexString(digest)}");
        var name = "r_" + Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
        var path = Path.Combine(options.ReceiptDirectory, name);
        var temporary = Path.Combine(options.ReceiptDirectory, "tmp_" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await target.WriteAsync(bytes, ct);
                target.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                // An idempotent retry uses the same name only for the same Business, key and bytes.
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)), digest))
                    throw new ApplicationFailure(FailureKind.IdempotencyConflict, "The receipt request changed. Refresh and try again.");
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return name;
    }

    public async Task<(byte[] Bytes, string ContentType)> ReadAsync(string name, CancellationToken ct)
    {
        if (!IsName(name) || string.IsNullOrEmpty(options.ReceiptDirectory))
            throw new ApplicationFailure(FailureKind.NotFound, "Receipt not found.");
        var path = Path.Combine(options.ReceiptDirectory, name);
        if (!File.Exists(path)) throw new ApplicationFailure(FailureKind.NotFound, "Receipt not found.");
        var file = new FileInfo(path);
        if (file.Length is < 1 or > RuntimeOptions.ReceiptBytes || file.LinkTarget is not null)
            throw new ApplicationFailure(FailureKind.NotFound, "Receipt not found.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var contentType = ValidImage(bytes, Jpeg) ? Jpeg : ValidImage(bytes, Png) ? Png : null;
        if (contentType is null) throw new ApplicationFailure(FailureKind.NotFound, "Receipt not found.");
        return (bytes, contentType);
    }

    private static bool IsName(string value) => value.Length == 66 && value.StartsWith("r_", StringComparison.Ordinal)
        && value.AsSpan(2).IndexOfAnyExcept("0123456789abcdef") < 0;

    public static bool ValidImage(ReadOnlySpan<byte> bytes, string contentType)
    {
        if (contentType == Png)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < 45 || !bytes[..8].SequenceEqual(signature)) return false;
            var offset = 8; var ihdr = false; var idat = false; var ended = false;
            while (offset + 12 <= bytes.Length)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
                if (length > bytes.Length - offset - 12) return false;
                var type = bytes.Slice(offset + 4, 4);
                var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + (int)length, 4));
                if (storedCrc != PngCrc(bytes.Slice(offset + 4, 4 + (int)length))) return false;
                if (!ihdr)
                {
                    if (!type.SequenceEqual("IHDR"u8) || length != 13) return false;
                    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8)..]);
                    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 12)..]);
                    if (!ValidDimensions(width, height)) return false;
                    ihdr = true;
                }
                if (type.SequenceEqual("IDAT"u8) && length > 0) idat = true;
                if (type.SequenceEqual("IEND"u8)) { ended = length == 0 && offset + 12 == bytes.Length; break; }
                offset += 12 + (int)length;
            }
            return ihdr && idat && ended;
        }
        if (contentType != Jpeg || bytes.Length < 16 || bytes[0] != 0xff || bytes[1] != 0xd8
            || bytes[^2] != 0xff || bytes[^1] != 0xd9) return false;
        var cursor = 2; var dimensions = false;
        while (cursor + 4 < bytes.Length)
        {
            if (bytes[cursor++] != 0xff) return false;
            while (cursor < bytes.Length && bytes[cursor] == 0xff) cursor++;
            if (cursor >= bytes.Length) return false;
            var marker = bytes[cursor++];
            if (marker == 0xda)
            {
                if (cursor + 2 > bytes.Length) return false;
                var scanLength = BinaryPrimitives.ReadUInt16BigEndian(bytes[cursor..]);
                return dimensions && scanLength >= 2 && cursor + scanLength + 2 < bytes.Length;
            }
            if (marker is 0xd8 or 0xd9 || cursor + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[cursor..]);
            if (length < 2 || cursor + length > bytes.Length) return false;
            if (marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc))
            {
                if (length < 7) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(cursor + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(cursor + 5)..]);
                dimensions = ValidDimensions(width, height);
            }
            cursor += length;
        }
        return false;
    }

    private static bool ValidDimensions(uint width, uint height) => width is >= 1 and <= 12000
        && height is >= 1 and <= 12000 && (ulong)width * height <= 40_000_000;

    private static uint PngCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }
}
