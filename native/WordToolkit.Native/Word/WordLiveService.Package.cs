using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> InspectablePackageExtensions = new(
        [".docx", ".docm", ".dotx", ".dotm"],
        StringComparer.OrdinalIgnoreCase
    );

    private static Task<object> InspectPackageAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);

        var includeDetails = arguments.Boolean("include_details", false);
        var requestedMaximum = arguments.NullableInt64("max_items") ?? 40;
        if (requestedMaximum is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 200"
            );
        }

        var maxItems = (int)requestedMaximum;
        try
        {
            var snapshot = new OpcPackageReader().Read(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var errors = snapshot.Diagnostics.Count(diagnostic =>
                diagnostic.Severity is OpcDiagnosticSeverity.Error
                    or OpcDiagnosticSeverity.Fatal
            );
            var warnings = snapshot.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == OpcDiagnosticSeverity.Warning
            );
            var information = snapshot.Diagnostics.Count(diagnostic =>
                diagnostic.Severity == OpcDiagnosticSeverity.Info
            );
            var externalRelationships = snapshot.Relationships.Count(relationship =>
                relationship.TargetMode == OpcRelationshipTargetMode.External
            );
            var officeDocumentPart = snapshot.Relationships
                .FirstOrDefault(relationship =>
                    relationship.SourcePartUri == "/"
                    && relationship.Type.EndsWith(
                        "/officeDocument",
                        StringComparison.Ordinal
                    )
                )
                ?.ResolvedTargetPartUri;
            var diagnosticItems = snapshot.Diagnostics
                .Take(maxItems)
                .Select(diagnostic => new
                {
                    code = diagnostic.Code,
                    severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                    message = BoundForResponse(diagnostic.Message, 512),
                    part_uri = BoundForResponse(diagnostic.PartUri, 512),
                    relationship_id = BoundForResponse(diagnostic.RelationshipId, 128),
                })
                .ToArray();

            object[]? parts = null;
            object[]? relationships = null;
            if (includeDetails)
            {
                parts = snapshot.Parts.Values
                    .OrderBy(part => part.Uri, StringComparer.Ordinal)
                    .Take(maxItems)
                    .Select(part => (object)new
                    {
                        uri = BoundForResponse(part.Uri, 512),
                        content_type = BoundForResponse(part.ContentType, 256),
                        bytes = part.Entry.UncompressedLength,
                        sha256 = part.Entry.Sha256,
                    })
                    .ToArray();
                relationships = snapshot.Relationships
                    .OrderBy(relationship => relationship.SourcePartUri, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
                    .Take(maxItems)
                    .Select(relationship => (object)new
                    {
                        source_part_uri = BoundForResponse(
                            relationship.SourcePartUri,
                            512
                        ),
                        id = BoundForResponse(relationship.Id, 128),
                        type = BoundForResponse(relationship.Type, 512),
                        target_mode = relationship.TargetMode.ToString().ToLowerInvariant(),
                        resolved_target_part_uri = BoundForResponse(
                            relationship.ResolvedTargetPartUri,
                            512
                        ),
                        external_target_redacted =
                            relationship.TargetMode == OpcRelationshipTargetMode.External,
                    })
                    .ToArray();
            }

            var result = new
            {
                file_name = Path.GetFileName(path),
                bytes = new FileInfo(path).Length,
                package_fingerprint = snapshot.Fingerprint,
                structurally_valid = snapshot.IsStructurallyValid,
                word_document_detected = officeDocumentPart is not null,
                valid_word_package =
                    snapshot.IsStructurallyValid && officeDocumentPart is not null,
                office_document_part = officeDocumentPart,
                entry_count = snapshot.Entries.Count,
                part_count = snapshot.Parts.Count,
                relationship_count = snapshot.Relationships.Count,
                external_relationship_count = externalRelationships,
                orphan_part_count = snapshot.Diagnostics.Count(diagnostic =>
                    diagnostic.Code == "OPC040"
                ),
                diagnostics = new
                {
                    errors,
                    warnings,
                    information,
                    items = diagnosticItems,
                    truncated = snapshot.Diagnostics.Count > diagnosticItems.Length,
                },
                details = includeDetails
                    ? new
                    {
                        parts,
                        parts_truncated = snapshot.Parts.Count > parts!.Length,
                        relationships,
                        relationships_truncated =
                            snapshot.Relationships.Count > relationships!.Length,
                    }
                    : null,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
            return Task.FromResult<object>(result);
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static string ResolveInspectablePackagePath(JsonElement arguments)
    {
        var rawPath = arguments.String("local_path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "local_path must be a non-empty string"
            );
        }

        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "local_path is not a valid filesystem path"
            );
        }

        if (!File.Exists(path))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "The requested Word package does not exist"
            );
        }

        if (!InspectablePackageExtensions.Contains(Path.GetExtension(path)))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Package inspection accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }

        return path;
    }

    private static string? BoundForResponse(string? value, int maxCharacters)
    {
        if (value is null || value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..maxCharacters] + "…";
    }
}
