#nullable enable
using System.Collections.Concurrent;
using System.IO.Hashing;

namespace AssetExtractor.Pipeline;

/// <summary>
/// Provides deduplication utilities using XXHash64 for fast, collision-resistant hashing.
/// Thread-safe for use in parallel extraction scenarios.
/// </summary>
public class DeduplicationService
{
    /// <summary>
    /// Atomic path reservation and conflict detection during parallel extraction.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _savedFiles = new();

    /// <summary>
    /// Compute XXHash64 hash of byte data.
    /// </summary>
    /// <param name="data">Raw byte data to hash.</param>
    /// <returns>16-character lowercase hex hash string.</returns>
    public static string ComputeHash(byte[] data)
    {
        if (data == null || data.Length == 0)
            return string.Empty;

        var xxHash = new XxHash64();
        xxHash.Append(data);
        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute XXHash64 hash of a portion of byte data without copying.
    /// </summary>
    /// <param name="data">Raw byte data to hash.</param>
    /// <param name="offset">Starting offset in the array.</param>
    /// <param name="length">Number of bytes to hash.</param>
    /// <returns>16-character lowercase hex hash string.</returns>
    public static string ComputeHash(byte[] data, int offset, int length)
    {
        if (data == null || length == 0)
            return string.Empty;

        var xxHash = new XxHash64();
        xxHash.Append(data.AsSpan(offset, length));
        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute XXHash64 hash of a file.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <returns>16-character lowercase hex hash string.</returns>
    public static string ComputeFileHash(string filePath)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        var xxHash = new XxHash64();
        using var stream = File.OpenRead(filePath);

        // Read in chunks for large files
        byte[] buffer = new byte[81920]; // 80KB chunks
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            xxHash.Append(buffer.AsSpan(0, bytesRead));
        }

        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Atomic path reservation for parallel extraction.
    /// </summary>
    /// <param name="targetPath">Desired output path.</param>
    /// <param name="hash">Hash of the data to be written.</param>
    /// <param name="actualPath">The actual path to use (may differ if conflict resolution needed).</param>
    /// <returns>
    /// PathReservationResult: success, duplicate (same hash already exists),
    /// or conflict (different hash at same path - need suffix).
    /// </returns>
    public PathReservationResult TryReservePath(string targetPath, string hash, out string actualPath)
    {
        targetPath = Path.GetFullPath(targetPath);

        if (_savedFiles.TryAdd(targetPath, hash))
        {
            actualPath = targetPath;
            return PathReservationResult.Success;
        }

        if (_savedFiles.TryGetValue(targetPath, out var existingHash) && existingHash == hash)
        {
            actualPath = targetPath;
            return PathReservationResult.Duplicate;
        }

        actualPath = FindConflictPath(targetPath, hash);
        return PathReservationResult.ConflictResolved;
    }

    /// <summary>
    /// Find a unique path for a conflicting file using AssetRipper-style suffixes (_0, _1, etc.).
    /// </summary>
    private string FindConflictPath(string basePath, string hash)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        int suffix = 0;
        const int maxAttempts = 1000;

        while (suffix < maxAttempts)
        {
            var newPath = Path.Combine(dir, $"{nameWithoutExt}_{suffix}{ext}");

            if (_savedFiles.TryAdd(newPath, hash))
            {
                return newPath;
            }

            // Path already taken - check if it's the same hash
            if (_savedFiles.TryGetValue(newPath, out var existingHash) && existingHash == hash)
            {
                return newPath;
            }

            suffix++;
        }

        throw new InvalidOperationException($"Too many conflicts for path: {basePath}");
    }

    public bool IsPathReserved(string path)
    {
        return _savedFiles.ContainsKey(Path.GetFullPath(path));
    }

    public string? GetPathHash(string path)
    {
        return _savedFiles.TryGetValue(Path.GetFullPath(path), out var hash) ? hash : null;
    }

    public void RegisterExistingFile(string path, string hash)
    {
        _savedFiles.TryAdd(Path.GetFullPath(path), hash);
    }

    public void ClearReservations()
    {
        _savedFiles.Clear();
    }

    public int ReservedPathCount => _savedFiles.Count;

    public IReadOnlyDictionary<string, string> GetAllReservations()
    {
        return _savedFiles;
    }
}

public enum PathReservationResult
{
    Success,
    Duplicate,
    ConflictResolved
}
