using System.IO.Compression;

namespace WordToolkit.Engine.Packaging;

public enum OpcSerializationMode
{
    Preserve,
    Deterministic,
}

public sealed class OpcPackageSerializer
{
    internal static DateTimeOffset DeterministicTimestamp { get; } = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero
    );

    public void Write(
        Stream destination,
        OpcPackageMutationBuilder mutation,
        OpcSerializationMode mode = OpcSerializationMode.Preserve
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(mutation);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "Destination stream must be writable.",
                nameof(destination)
            );
        }

        if (destination.CanSeek && (destination.Position != 0 || destination.Length != 0))
        {
            throw new ArgumentException(
                "Destination stream must be empty and positioned at zero.",
                nameof(destination)
            );
        }

        using var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true
        );
        foreach (var sourceEntry in mutation.Materialize(mode))
        {
            var targetEntry = archive.CreateEntry(sourceEntry.Name, CompressionLevel.Optimal);
            targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
            targetEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
            using var targetStream = targetEntry.Open();
            targetStream.Write(sourceEntry.Content);
        }
    }
}
