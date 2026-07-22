using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> PlanPackageTextEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var context = BuildPackageTextPlan(path, arguments, cancellationToken);
        var includeDetails = arguments.Boolean("include_details", false);
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.Plan.PlanId,
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            operation_count = context.Plan.OperationCount,
            changed_operation_count = context.Plan.ChangedOperationCount,
            changed_part_count = context.Plan.ChangedPartCount,
            total_xml_byte_delta = context.Plan.TotalXmlByteDelta,
            has_changes = context.Plan.HasChanges,
            apply_blocked = context.HasDigitalSignatures,
            apply_blocked_reason = context.HasDigitalSignatures
                ? "digital_signature_present"
                : null,
            operations = includeDetails
                ? context.Plan.Operations.Select(operation => new
                {
                    index = operation.Index,
                    kind = operation.Kind,
                    node_id = operation.NodeId.Value,
                    source_part_uri = BoundForResponse(operation.SourcePartUri, 512),
                    source_element_ordinal = operation.SourceElementOrdinal,
                    before_characters = operation.BeforeCharacters,
                    after_characters = operation.AfterCharacters,
                    xml_byte_delta = operation.XmlByteDelta,
                    has_change = operation.HasChange,
                }).ToArray()
                : null,
            changed_parts = includeDetails
                ? context.Plan.ChangedParts.Select(part => new
                {
                    part_uri = BoundForResponse(part.PartUri, 512),
                    before_bytes = part.BeforeBytes,
                    after_bytes = part.AfterBytes,
                    byte_delta = (long)part.AfterBytes - part.BeforeBytes,
                }).ToArray()
                : null,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static Task<object> ApplyPackageTextEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var expectedPlanId = RequiredPlanId(arguments);
        var context = BuildPackageTextPlan(path, arguments, cancellationToken);
        if (
            !string.Equals(
                context.Plan.PlanId,
                expectedPlanId,
                StringComparison.Ordinal
            )
        )
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "Commands do not reproduce the reviewed semantic plan ID"
            );
        }

        if (context.HasDigitalSignatures)
        {
            throw new NativeToolException(
                "SIGNED_PACKAGE",
                "Direct OOXML editing is blocked because the package contains digital signatures"
            );
        }

        if (!context.Plan.HasChanges)
        {
            return new
            {
                file_name = Path.GetFileName(path),
                plan_id = context.Plan.PlanId,
                applied = false,
                no_op = true,
                package_fingerprint = context.Package.Fingerprint,
                backup_path = (string?)null,
                changed_entry_names = Array.Empty<string>(),
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }

        var mutation = context.Plan.CreateMutation(context.Package);
        var result = new OpcAtomicPackageWriter().Write(
            path,
            mutation,
            new OpcAtomicWriteOptions
            {
                ExpectedDestinationFingerprint = context.Package.Fingerprint,
                ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                KeepBackup = arguments.Boolean("keep_backup", true),
            }
        );
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.Plan.PlanId,
            applied = true,
            no_op = false,
            previous_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            backup_path = result.BackupPath,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static PackageTextPlanContext BuildPackageTextPlan(
        string path,
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commands = ParseTextReplacementCommands(arguments);
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        var package = new OpcPackageReader().Read(path, cancellationToken);
        if (
            !string.Equals(
                package.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Saved package changed before the semantic text plan was built."
            );
        }

        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions { MaxCommands = 200 }
        ).PlanTextReplacements(package, semantic, commands, cancellationToken);
        return new PackageTextPlanContext(
            package,
            plan,
            HasDigitalSignatures(package)
        );
    }

    private static IReadOnlyList<WordTextReplacementCommand>
        ParseTextReplacementCommands(JsonElement arguments)
    {
        var array = arguments.RequiredArray("commands");
        if (array.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "commands must contain between 1 and 200 text replacements"
            );
        }

        var result = new List<WordTextReplacementCommand>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every text replacement command must be an object"
                );
            }

            _ = item.Required("node_id");
            _ = item.Required("new_text");
            var nodeId = item.String("node_id");
            var newText = item.String("new_text");
            var expectedText = OptionalString(item, "expected_text");
            if (
                nodeId.Length is < 5 or > 128
                || !nodeId.StartsWith("wdn_", StringComparison.Ordinal)
                || nodeId[4..].Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '_' and not '-'
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "node_id is not a valid semantic node ID"
                );
            }

            if (newText.Length > 1_000_000 || expectedText?.Length > 1_000_000)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "new_text and expected_text cannot exceed 1,000,000 characters per command"
                );
            }

            result.Add(
                new WordTextReplacementCommand(
                    new SemanticNodeId(nodeId),
                    newText,
                    expectedText
                )
            );
        }

        return result;
    }

    private static string RequiredSha256(JsonElement arguments, string name)
    {
        _ = arguments.Required(name);
        var value = arguments.String(name);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be a 64-character hexadecimal SHA-256 value"
            );
        }

        return value;
    }

    private static string RequiredPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_plan_id");
        var value = arguments.String("expected_plan_id");
        if (
            value.Length is < 10 or > 128
            || !value.StartsWith("wplan_", StringComparison.Ordinal)
            || value[6..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_plan_id is not a valid semantic plan ID"
            );
        }

        return value;
    }

    private static bool HasDigitalSignatures(OpcPackageSnapshot package) =>
        package.Entries.Any(entry =>
            entry.Name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase)
        )
        || package.Parts.Values.Any(part =>
            part.ContentType?.Contains(
                "digital-signature",
                StringComparison.OrdinalIgnoreCase
            ) == true
        )
        || package.Relationships.Any(relationship =>
            relationship.Type.Contains(
                "digital-signature",
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static Task<object> ExecutePackageTextAction(Func<object> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (NativeToolException)
        {
            throw;
        }
        catch (WordSemanticTransactionLimitException exception)
        {
            throw new NativeToolException(
                "TRANSACTION_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Transaction limit exceeded"
            );
        }
        catch (WordReviewTransactionLimitException exception)
        {
            throw new NativeToolException(
                "TRANSACTION_LIMIT",
                BoundForResponse(exception.Message, 512) ?? "Review transaction limit exceeded"
            );
        }
        catch (WordSemanticPreconditionException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                BoundForResponse(exception.Message, 512) ?? "Semantic precondition failed"
            );
        }
        catch (WordSemanticEditException exception)
        {
            throw new NativeToolException(
                "UNSAFE_EDIT",
                BoundForResponse(exception.Message, 512) ?? "Semantic edit is unsafe"
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageConcurrencyException exception)
        {
            throw new NativeToolException(
                "VERSION_CONFLICT",
                "Destination package changed during the atomic write",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageResultMismatchException exception)
        {
            throw new NativeToolException(
                "RESULT_MISMATCH",
                "Candidate package does not match the reviewed semantic plan",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageValidationException exception)
        {
            throw new NativeToolException(
                "VALIDATION_FAILED",
                "Candidate package failed structural validation",
                new
                {
                    diagnostics = exception.Diagnostics.Take(20).Select(diagnostic => new
                    {
                        code = diagnostic.Code,
                        severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                        message = BoundForResponse(diagnostic.Message, 512),
                        part_uri = diagnostic.PartUri,
                    }).ToArray(),
                }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Invalid semantic text edit"
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
                "The Word package cannot be written with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read or written",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private sealed record PackageTextPlanContext(
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        bool HasDigitalSignatures
    );
}
