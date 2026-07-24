using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Operations;

public enum FlatOpcConversionDirection
{
    ToFlatOpc,
    FromFlatOpc,
}

public static class FlatOpcWordPackageContract
{
    public const string OperationName = "convert_ooxml_flat_opc";
    public const string Contract = "wordtoolkit.convert_ooxml_flat_opc/1.0";

    public static string Name(FlatOpcConversionDirection direction) => direction switch
    {
        FlatOpcConversionDirection.ToFlatOpc => "to_flat_opc",
        FlatOpcConversionDirection.FromFlatOpc => "from_flat_opc",
        _ => throw new ArgumentOutOfRangeException(
            nameof(direction),
            direction,
            null
        ),
    };

    public static bool TryParse(
        string? value,
        out FlatOpcConversionDirection direction
    )
    {
        direction = value switch
        {
            "to_flat_opc" => FlatOpcConversionDirection.ToFlatOpc,
            "from_flat_opc" => FlatOpcConversionDirection.FromFlatOpc,
            _ => default,
        };
        return value is "to_flat_opc" or "from_flat_opc";
    }
}

public sealed record FlatOpcWordPackageRequest(
    string InputPath,
    string OutputPath,
    FlatOpcConversionDirection Direction
);

public sealed record FlatOpcWordPackageResult(
    string OperationContract,
    string Direction,
    string InputFileName,
    string OutputFileName,
    string InputSha256,
    string OutputSha256,
    string SourcePackageFingerprint,
    string ResultPackageFingerprint,
    bool PackageSemanticallyEquivalent,
    int PartCount,
    int XmlPartCount,
    int BinaryPartCount,
    long TotalPartBytes,
    bool StructurallyValid,
    bool DigitalSignaturesPresent,
    bool RawXmlReturned,
    bool WordOpened
);

/// <summary>
/// Publishes a bounded, create-new conversion between a saved Word OPC package
/// and Flat OPC. The result is written to an isolated sibling file, reopened,
/// structurally and semantically verified, and moved into place only after the
/// source and destination package graphs prove equivalent.
/// </summary>
public sealed class FlatOpcWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly FlatOpcPackageCodec _codec;

    public FlatOpcWordPackageOperation(OpcPackageLimits? limits = null)
    {
        _reader = new OpcPackageReader(limits);
        _codec = new FlatOpcPackageCodec(limits);
    }

    public FlatOpcWordPackageResult Execute(
        FlatOpcWordPackageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw InvalidInput("Flat OPC conversion request is required");
        }
        var (inputPath, outputPath) = ValidateAndResolve(request);
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw InvalidInput("output_path has no parent directory");
        Directory.CreateDirectory(outputDirectory);
        var transactionId = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".wordtoolkit-flatopc-{transactionId}.tmp"
        );
        var lockPath = outputPath + ".wordtoolkit.lock";

        try
        {
            using var outputLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None
            );
            AssertDestinationAbsent(outputPath);
            var result = request.Direction == FlatOpcConversionDirection.ToFlatOpc
                ? Export(
                    inputPath,
                    outputPath,
                    temporaryPath,
                    request.Direction,
                    cancellationToken
                )
                : Import(
                    inputPath,
                    outputPath,
                    temporaryPath,
                    request.Direction,
                    cancellationToken
                );
            AssertDestinationAbsent(outputPath);
            try
            {
                File.Move(temporaryPath, outputPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(outputPath))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The output path was created while the conversion was being published"
                );
            }
            return result with { OutputSha256 = HashFile(outputPath) };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception);
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(lockPath);
        }
    }

    private FlatOpcWordPackageResult Export(
        string inputPath,
        string outputPath,
        string temporaryPath,
        FlatOpcConversionDirection direction,
        CancellationToken cancellationToken
    )
    {
        var source = ReadValidWordPackage(inputPath, cancellationToken);
        BlockDigitalSignatures(source);
        FlatOpcPackageStatistics statistics;
        using (
            var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.WriteThrough
            )
        )
        {
            statistics = _codec.Write(destination, source, cancellationToken);
            destination.Flush(flushToDisk: true);
        }

        OpcPackageSnapshot roundTrip;
        using (var flatOpc = File.OpenRead(temporaryPath))
        {
            roundTrip = _codec.Read(flatOpc, cancellationToken);
        }
        ValidateWordPackage(roundTrip, cancellationToken);
        AssertEquivalent(source, roundTrip, cancellationToken);
        return Result(
            direction,
            inputPath,
            outputPath,
            HashFile(inputPath),
            HashFile(temporaryPath),
            source,
            roundTrip,
            statistics
        );
    }

    private FlatOpcWordPackageResult Import(
        string inputPath,
        string outputPath,
        string temporaryPath,
        FlatOpcConversionDirection direction,
        CancellationToken cancellationToken
    )
    {
        FlatOpcPackageStatistics statistics;
        using (var source = File.OpenRead(inputPath))
        using (
            var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.WriteThrough
            )
        )
        {
            statistics = _codec.ConvertToPackage(
                source,
                destination,
                cancellationToken
            );
            destination.Flush(flushToDisk: true);
        }

        using var sourceAgain = File.OpenRead(inputPath);
        var sourcePackage = _codec.Read(sourceAgain, cancellationToken);
        ValidateWordPackage(sourcePackage, cancellationToken);
        BlockDigitalSignatures(sourcePackage);
        var candidate = _reader.Read(temporaryPath, cancellationToken);
        ValidateWordPackage(candidate, cancellationToken);
        AssertOutputExtensionMatches(candidate, outputPath);
        AssertEquivalent(sourcePackage, candidate, cancellationToken);
        return Result(
            direction,
            inputPath,
            outputPath,
            HashFile(inputPath),
            HashFile(temporaryPath),
            sourcePackage,
            candidate,
            statistics
        );
    }

    private static FlatOpcWordPackageResult Result(
        FlatOpcConversionDirection direction,
        string inputPath,
        string outputPath,
        string inputSha256,
        string outputSha256,
        OpcPackageSnapshot source,
        OpcPackageSnapshot result,
        FlatOpcPackageStatistics statistics
    ) => new(
        FlatOpcWordPackageContract.Contract,
        FlatOpcWordPackageContract.Name(direction),
        Path.GetFileName(inputPath),
        Path.GetFileName(outputPath),
        inputSha256,
        outputSha256,
        source.Fingerprint,
        result.Fingerprint,
        PackageSemanticallyEquivalent: true,
        statistics.PartCount,
        statistics.XmlPartCount,
        statistics.BinaryPartCount,
        statistics.TotalPartBytes,
        result.IsStructurallyValid,
        DigitalSignaturesPresent: false,
        RawXmlReturned: false,
        WordOpened: false
    );

    private OpcPackageSnapshot ReadValidWordPackage(
        string path,
        CancellationToken cancellationToken
    )
    {
        var package = _reader.Read(path, cancellationToken);
        ValidateWordPackage(package, cancellationToken);
        return package;
    }

    private static void ValidateWordPackage(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The converted package has structural OPC errors"
            );
        }
        try
        {
            _ = new WordSemanticProjector().Project(package, cancellationToken);
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The converted package is not a valid Word semantic document",
                Bound(exception.Message),
                innerException: exception
            );
        }
    }

    private static void BlockDigitalSignatures(OpcPackageSnapshot package)
    {
        if (WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package))
        {
            throw new WordToolkitOperationException(
                "SIGNED_PACKAGE",
                "Flat OPC conversion is blocked because XML reserialization would invalidate package signatures"
            );
        }
    }

    private static void AssertEquivalent(
        OpcPackageSnapshot expected,
        OpcPackageSnapshot actual,
        CancellationToken cancellationToken
    )
    {
        var expectedEntries = ComparableEntries(expected);
        var actualEntries = ComparableEntries(actual);
        if (!expectedEntries.Keys.SequenceEqual(actualEntries.Keys, StringComparer.Ordinal))
        {
            throw ResultMismatch("Flat OPC conversion changed the OPC part-name set");
        }
        foreach (var entryName in expectedEntries.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = expectedEntries[entryName];
            var after = actualEntries[entryName];
            var beforeType = ContentType(expected, before);
            var afterType = ContentType(actual, after);
            if (!string.Equals(beforeType, afterType, StringComparison.Ordinal))
            {
                throw ResultMismatch(
                    $"Flat OPC conversion changed content type for '{entryName}'"
                );
            }
            if (before.Content.Span.SequenceEqual(after.Content.Span))
            {
                continue;
            }
            if (
                beforeType.EndsWith("xml", StringComparison.Ordinal)
                && TryCanonicalXml(before.Content, cancellationToken, out var beforeXml)
                && TryCanonicalXml(after.Content, cancellationToken, out var afterXml)
                && HasEquivalentXmlInformationSet(beforeXml!, afterXml!)
            )
            {
                continue;
            }
            throw ResultMismatch(
                $"Flat OPC conversion changed payload semantics for '{entryName}'"
            );
        }

        var expectedRelationships = RelationshipSet(expected);
        var actualRelationships = RelationshipSet(actual);
        if (!expectedRelationships.SequenceEqual(actualRelationships, StringComparer.Ordinal))
        {
            throw ResultMismatch("Flat OPC conversion changed the relationship graph");
        }
    }

    private static SortedDictionary<string, OpcPackageEntry> ComparableEntries(
        OpcPackageSnapshot package
    ) => new(
        package.Entries.Where(entry =>
                !entry.IsDirectory
                && !string.Equals(
                    entry.Name,
                    OpcPartUri.ContentTypesEntryName,
                    StringComparison.Ordinal
                )
            )
            .ToDictionary(entry => entry.Name, StringComparer.Ordinal),
        StringComparer.Ordinal
    );

    private static string ContentType(
        OpcPackageSnapshot package,
        OpcPackageEntry entry
    )
    {
        if (entry.IsInfrastructure)
        {
            return "application/vnd.openxmlformats-package.relationships+xml";
        }
        return entry.PartUri is null
            ? string.Empty
            : package.ContentTypes.Resolve(entry.PartUri) ?? string.Empty;
    }

    private static bool TryCanonicalXml(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken,
        out XDocument? document
    )
    {
        document = null;
        try
        {
            document = LosslessXmlDocument.Parse(
                content,
                cancellationToken: cancellationToken
            ).ParsedDocument;
            return document.Root is not null;
        }
        catch (LosslessXmlException)
        {
            return false;
        }
    }

    private static bool HasEquivalentXmlInformationSet(
        XDocument expected,
        XDocument actual
    )
    {
        // The declaration belongs to the transport encoding, not to the XML
        // information set stored in the OPC part. Comparing the roots still
        // retains every element, attribute, comment, processing instruction
        // and text node inside the part, including significant whitespace.
        return XNode.DeepEquals(expected.Root, actual.Root);
    }

    private static string[] RelationshipSet(OpcPackageSnapshot package) =>
        package.Relationships.Select(relationship =>
                string.Join(
                    "\u001f",
                    relationship.SourcePartUri,
                    relationship.RelationshipPartUri,
                    relationship.Id,
                    relationship.Type,
                    relationship.Target,
                    relationship.TargetMode,
                    relationship.ResolvedTargetPartUri,
                    relationship.TargetFragment
                )
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertOutputExtensionMatches(
        OpcPackageSnapshot package,
        string outputPath
    )
    {
        var mainRelationships = package.RelationshipsFrom(OpcPartUri.PackageRoot)
            .Where(relationship =>
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.Type.EndsWith(
                    "/officeDocument",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (
            mainRelationships.Length != 1
            || mainRelationships[0].ResolvedTargetPartUri is not { } main
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "Flat OPC must resolve exactly one internal Word main part"
            );
        }
        var contentType = package.ContentTypes.Resolve(main) ?? string.Empty;
        var expectedExtension = contentType switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" => ".docx",
            "application/vnd.ms-word.document.macroEnabled.main+xml" => ".docm",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml" => ".dotx",
            "application/vnd.ms-word.template.macroEnabledTemplate.main+xml" => ".dotm",
            _ => throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "Flat OPC main part has an unsupported Word content type"
            ),
        };
        if (!string.Equals(
            expectedExtension,
            Path.GetExtension(outputPath),
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw InvalidInput(
                $"output_path must use '{expectedExtension}' for the Flat OPC main-part content type"
            );
        }
    }

    private static (string InputPath, string OutputPath) ValidateAndResolve(
        FlatOpcWordPackageRequest request
    )
    {
        if (!Enum.IsDefined(request.Direction))
        {
            throw InvalidInput("direction is not supported");
        }
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw InvalidInput("input_path must be a non-empty string");
        }
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw InvalidInput("output_path must be a non-empty string");
        }
        string inputPath;
        string outputPath;
        try
        {
            inputPath = Path.GetFullPath(request.InputPath);
            outputPath = Path.GetFullPath(request.OutputPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw InvalidInput("input_path or output_path is not a valid filesystem path");
        }
        if (!File.Exists(inputPath))
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The Flat OPC conversion input does not exist"
            );
        }
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidInput("output_path must differ from input_path");
        }
        if (request.Direction == FlatOpcConversionDirection.ToFlatOpc)
        {
            if (!InspectWordPackageContract.IsSupportedFileName(inputPath))
            {
                throw InvalidInput("to_flat_opc input must be DOCX, DOCM, DOTX, or DOTM");
            }
            if (!string.Equals(
                Path.GetExtension(outputPath),
                ".xml",
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw InvalidInput("to_flat_opc output must use the .xml extension");
            }
        }
        else
        {
            if (!string.Equals(
                Path.GetExtension(inputPath),
                ".xml",
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw InvalidInput("from_flat_opc input must use the .xml extension");
            }
            if (!InspectWordPackageContract.IsSupportedFileName(outputPath))
            {
                throw InvalidInput("from_flat_opc output must be DOCX, DOCM, DOTX, or DOTM");
            }
        }
        AssertDestinationAbsent(outputPath);
        return (inputPath, outputPath);
    }

    private static void AssertDestinationAbsent(string outputPath)
    {
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The output path already exists; Flat OPC conversion never overwrites"
            );
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static WordToolkitOperationException MapFailure(Exception exception) =>
        exception switch
        {
            OpcPackageLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Flat OPC conversion exceeds a bounded package limit",
                Bound(limit.Message),
                innerException: limit
            ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "Flat OPC conversion input or output is invalid",
                Bound(invalid.Message),
                innerException: invalid
            ),
            UnauthorizedAccessException denied => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "Flat OPC conversion cannot access the requested path",
                Bound(denied.Message),
                innerException: denied
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "Flat OPC conversion failed during filesystem I/O",
                Bound(io.Message),
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                "Flat OPC conversion received an invalid argument",
                Bound(invalid.Message),
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "Flat OPC conversion failed",
                innerException: exception
            ),
        };

    private static WordToolkitOperationException ResultMismatch(string message) =>
        new("RESULT_MISMATCH", message);

    private static WordToolkitOperationException InvalidInput(
        string message,
        string? reason = null,
        Exception? innerException = null
    ) => new(
        "INVALID_INPUT",
        message,
        reason,
        innerException: innerException
    );

    private static string Bound(string value) =>
        value.Length <= 512 ? value : value[..512] + "…";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
