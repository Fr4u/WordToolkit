using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordToolkit.Native.Ocr;

internal static class OcrProviderTrustPairCoordinator
{
    internal sealed record Journal(
        [property: JsonPropertyName("primary_path")] string PrimaryPath,
        [property: JsonPropertyName("secondary_path")] string SecondaryPath,
        [property: JsonPropertyName("secondary_sha256")] string SecondarySha256,
        [property: JsonPropertyName("transaction_id")] string TransactionId);
    internal static IDisposable Acquire(string manifestPath, string storePath)
    {
        var manifest = Path.GetFullPath(manifestPath);
        var store = Path.GetFullPath(storePath);
        var identity = (OperatingSystem.IsWindows() ? manifest.ToUpperInvariant() : manifest)
            + "\n" + (OperatingSystem.IsWindows() ? store.ToUpperInvariant() : store);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var dir = Path.GetDirectoryName(manifest)!;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".ocr-provider-trust-" + hash + ".lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try { return new Lease(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(25); }
        }
    }
    internal static string JournalPath(string primaryPath, string secondaryPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(primaryPath))!, ".ocr-provider-trust-" + PairHash(primaryPath, secondaryPath) + ".journal.json");
    internal static void Recover(string primaryPath, string secondaryPath)
    {
        var journalPath = JournalPath(primaryPath, secondaryPath);
        if (!File.Exists(journalPath)) return;
        Journal? journal;
        try { journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath, Encoding.UTF8)); }
        catch { throw new IOException("OCR provider trust recovery journal is invalid."); }
        if (journal is null || !Same(journal.PrimaryPath, primaryPath) || !Same(journal.SecondaryPath, secondaryPath))
            throw new IOException("OCR provider trust recovery journal does not match the pair.");
        var primary = File.Exists(primaryPath); var secondary = File.Exists(secondaryPath);
        if (!primary && !secondary) { File.Delete(journalPath); return; }
        if (!primary && secondary)
        {
            if (!string.Equals(HashFile(secondaryPath), journal.SecondarySha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("OCR provider trust recovery secondary does not match the journal.");
            File.Delete(secondaryPath); File.Delete(journalPath); return;
        }
        if (primary && secondary) { File.Delete(journalPath); return; }
        throw new IOException("OCR provider trust recovery is incomplete: primary exists without secondary.");
    }
    internal static void WriteJournal(string primaryPath, string secondaryPath, byte[] secondaryBytes, string transactionId)
    {
        var path = JournalPath(primaryPath, secondaryPath);
        var journal = new Journal(Path.GetFullPath(primaryPath), Path.GetFullPath(secondaryPath), Convert.ToHexString(SHA256.HashData(secondaryBytes)).ToLowerInvariant(), transactionId);
        var json = JsonSerializer.Serialize(journal) + "\n";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
    }
    internal static void DeleteJournal(string primaryPath, string secondaryPath) => TryDelete(JournalPath(primaryPath, secondaryPath));
    private static string PairHash(string primaryPath, string secondaryPath)
    {
        var identity = (OperatingSystem.IsWindows() ? Path.GetFullPath(primaryPath).ToUpperInvariant() : Path.GetFullPath(primaryPath)) + "\n" + (OperatingSystem.IsWindows() ? Path.GetFullPath(secondaryPath).ToUpperInvariant() : Path.GetFullPath(secondaryPath));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private sealed class Lease(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }
}
