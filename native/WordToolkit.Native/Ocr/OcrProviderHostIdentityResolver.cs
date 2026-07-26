using System.Reflection;
using System.Security.Cryptography;
using WordToolkit.Engine.Extensions;

namespace WordToolkit.Native.Ocr;

internal static class OcrProviderHostIdentityResolver
{
    private const long MaximumHostBinaryBytes = 512L * 1024 * 1024;

    internal static OcrProviderHostIdentity Current(
        CancellationToken cancellationToken = default
    )
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw Error("The OCR process host cannot resolve its executable identity.");
        }
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            assemblyPath = executablePath;
        }
        return ForPaths(executablePath, assemblyPath, cancellationToken);
    }

    internal static OcrProviderHostIdentity ForPaths(
        string executablePath,
        string assemblyPath,
        CancellationToken cancellationToken = default
    ) => new(
        HashFile(executablePath, cancellationToken),
        HashFile(assemblyPath, cancellationToken)
    );

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 || file.Length > MaximumHostBinaryBytes)
        {
            throw Error("The OCR process-host identity file is missing or exceeds its limit.");
        }
        using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static WordToolkitExtensionException Error(string message) => new(
        "EXTENSION_IDENTITY_UNAVAILABLE",
        message
    );
}
