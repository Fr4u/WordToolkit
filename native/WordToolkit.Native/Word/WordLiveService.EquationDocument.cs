using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly HashSet<string> EquationDocumentArguments = new(
        [
            "output_path",
            "equations",
            "idempotency_key",
            "visible",
            "activate",
            "keep_open",
            "render_output_directory",
            "artifact_stem",
            "render_output",
            "dpi",
            "optimize_for",
            "include_document_properties",
            "bookmarks",
            "pdf_a",
            "pdfinfo_path",
            "rasterizer_path",
            "rasterizer_kind",
            "per_equation_timeout_seconds",
            "total_timeout_seconds",
        ],
        StringComparer.Ordinal
    );

    private async Task<object> CreateEquationDocumentAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "equation-only document arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (!EquationDocumentArguments.Contains(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unsupported equation-only document argument",
                    new { argument = property.Name }
                );
            }
        }
        var outputPath = ValidateNewDocxOutputPath(arguments.String("output_path"));
        var equations = arguments.RequiredArray("equations");
        if (equations.GetArrayLength() is < 1 or > 100)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "equations must contain between 1 and 100 items"
            );
        }
        var visible = arguments.Boolean("visible", true);
        var activate = arguments.Boolean("activate", true);
        var keepOpen = arguments.Boolean("keep_open", true);
        var renderDirectory = arguments.String("render_output_directory");
        renderDirectory = renderDirectory.Length == 0
            ? Path.GetDirectoryName(outputPath)!
            : Path.GetFullPath(renderDirectory);
        if (!Directory.Exists(renderDirectory))
        {
            throw new NativeToolException(
                "NOT_FOUND",
                "render_output_directory must already exist"
            );
        }
        var artifactStem = arguments.String("artifact_stem");
        artifactStem = artifactStem.Length == 0
            ? SafeEquationArtifactStem(Path.GetFileNameWithoutExtension(outputPath))
            : artifactStem;
        var renderOutput = arguments.String("render_output", "pdf");
        if (renderOutput is not ("pdf" or "png_pages" or "pdf_and_png_pages"))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "render_output must be pdf, png_pages, or pdf_and_png_pages"
            );
        }
        var idempotencyKey = arguments.String("idempotency_key");

        using var preflightDocument = JsonDocument.Parse(
            BuildEquationDocumentPreflightArguments(arguments, equations)
                .ToJsonString(JsonDefaults.Compact)
        );
        var preflight = await PreflightEquationsAsync(
            preflightDocument.RootElement,
            cancellationToken
        ).ConfigureAwait(false);
        var preflightNode = JsonSerializer.SerializeToNode(
            preflight,
            JsonDefaults.Compact
        )?.AsObject() ?? new JsonObject();
        if (preflightNode["valid"]?.GetValue<bool>() != true)
        {
            return new
            {
                operation_contract = "wordtoolkit.create_live_word_equation_document/1.0",
                workflow_complete = false,
                stage = "preflight",
                created = false,
                published = false,
                saved = false,
                rendered = false,
                package_inspected = false,
                output_path = outputPath,
                equation_count = equations.GetArrayLength(),
                preflight = CompactEquationDocumentPreflight(preflightNode),
                raw_document_content_returned = false,
            };
        }

        await StartWordAsync(
            JsonSerializer.SerializeToElement(new { visible }),
            cancellationToken
        ).ConfigureAwait(false);

        string? liveDocumentId = null;
        long liveVersion = 0;
        var published = false;
        var saved = false;
        var stage = "create";
        try
        {
            var create = await CreateDocumentAsync(
                JsonSerializer.SerializeToElement(
                    new
                    {
                        output_path = outputPath,
                        activate,
                        lifecycle = "persistent",
                    }
                ),
                CancellationToken.None
            ).ConfigureAwait(false);
            var createNode = JsonSerializer.SerializeToNode(
                create,
                JsonDefaults.Compact
            )!.AsObject();
            liveDocumentId = createNode["live_document_id"]!.GetValue<string>();
            liveVersion = createNode["live_version"]!.GetValue<long>();

            stage = "publish";
            var applyArguments = BuildEquationDocumentApplyArguments(
                liveDocumentId,
                liveVersion,
                equations,
                idempotencyKey
            );
            using var applyDocument = JsonDocument.Parse(
                applyArguments.ToJsonString(JsonDefaults.Compact)
            );
            var apply = await ApplyOperationsAsync(
                applyDocument.RootElement,
                CancellationToken.None
            ).ConfigureAwait(false);
            var applyNode = JsonSerializer.SerializeToNode(
                apply,
                JsonDefaults.Compact
            )!.AsObject();
            liveVersion = applyNode["live_version"]!.GetValue<long>();
            published = true;

            stage = "save";
            var save = await SaveAsync(
                JsonSerializer.SerializeToElement(
                    new
                    {
                        live_document_id = liveDocumentId,
                        expected_version = liveVersion,
                    }
                ),
                CancellationToken.None
            ).ConfigureAwait(false);
            var saveNode = JsonSerializer.SerializeToNode(
                save,
                JsonDefaults.Compact
            )!.AsObject();
            saved = saveNode["saved"]?.GetValue<bool>() == true;
            if (!saved)
            {
                throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "Microsoft Word did not confirm the equation-only document save"
                );
            }

            stage = "validate";
            var validation = await ValidateLiveDocumentAsync(
                JsonSerializer.SerializeToElement(
                    new { live_document_id = liveDocumentId }
                ),
                CancellationToken.None
            ).ConfigureAwait(false);
            var validationNode = JsonSerializer.SerializeToNode(
                validation,
                JsonDefaults.Compact
            )!.AsObject();
            if (
                validationNode["validation"]?["valid"]?.GetValue<bool>()
                != true
            )
            {
                throw new NativeToolException(
                    "OOXML_INVALID",
                    "The saved equation-only document failed Microsoft Open XML validation",
                    new
                    {
                        live_document_id = liveDocumentId,
                        live_version = liveVersion,
                        output_created = true,
                    }
                );
            }

            stage = "render";
            var renderArguments = BuildEquationDocumentRenderArguments(
                arguments,
                liveDocumentId,
                liveVersion,
                renderDirectory,
                artifactStem,
                renderOutput
            );
            using var renderDocument = JsonDocument.Parse(
                renderArguments.ToJsonString(JsonDefaults.Compact)
            );
            var render = await ExportLiveWordArtifactsAsync(
                renderDocument.RootElement,
                CancellationToken.None
            ).ConfigureAwait(false);
            var renderNode = JsonSerializer.SerializeToNode(
                render,
                JsonDefaults.Compact
            )!.AsObject();
            if (
                renderNode["equation_render_qa"]?["risk_codes"]
                    is JsonArray renderRisks
                && renderRisks.Count > 0
            )
            {
                throw new NativeToolException(
                    "EQUATION_RENDER_RISK",
                    "The equation-only document render triggered an automatic risk gate",
                    new
                    {
                        risk_codes = renderRisks.DeepClone(),
                        page_numbers = renderNode["equation_render_qa"]?["page_numbers"]
                            ?.DeepClone(),
                        subjective_visual_review_still_required = true,
                        raw_document_content_returned = false,
                    }
                );
            }

            stage = "inspect_package";
            var package = await InspectPackageAsync(
                JsonSerializer.SerializeToElement(
                    new { local_path = outputPath, include_details = false }
                ),
                CancellationToken.None
            ).ConfigureAwait(false);
            var packageNode = JsonSerializer.SerializeToNode(
                package,
                JsonDefaults.Compact
            )!.AsObject();

            object? close = null;
            if (!keepOpen)
            {
                stage = "close";
                close = await CloseDocumentAsync(
                    JsonSerializer.SerializeToElement(
                        new
                        {
                            live_document_id = liveDocumentId,
                            expected_version = liveVersion,
                            save_changes = "save",
                        }
                    ),
                    CancellationToken.None
                ).ConfigureAwait(false);
            }

            return new
            {
                operation_contract = "wordtoolkit.create_live_word_equation_document/1.0",
                workflow_complete = true,
                stage = "complete",
                created = true,
                published = true,
                saved = true,
                rendered = true,
                package_inspected = true,
                output_path = outputPath,
                live_document_id = liveDocumentId,
                live_version = liveVersion,
                live_document_open = keepOpen,
                equation_count = equations.GetArrayLength(),
                preflight = CompactEquationDocumentPreflight(preflightNode),
                operation_id = applyNode["operation_id"]?.GetValue<string>(),
                receipt_replayed = applyNode["receipt_replayed"]?.GetValue<bool>()
                    ?? false,
                validation = validationNode["validation"]?.DeepClone(),
                render = renderNode,
                package = CompactEquationDocumentPackage(packageNode),
                close,
                raw_document_content_returned = false,
            };
        }
        catch (Exception exception)
        {
            if (!published && liveDocumentId is not null)
            {
                Exception? cleanupFailure = null;
                try
                {
                    await CleanupFailedEquationDocumentAsync(
                        liveDocumentId,
                        outputPath
                    ).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailure = cleanupException;
                }
                if (cleanupFailure is not null)
                {
                    throw new NativeToolException(
                        "ROLLBACK_FAILED",
                        "Equation-only document creation failed and cleanup could not be proven",
                        new
                        {
                            stage,
                            original_error_code = exception is NativeToolException native
                                ? native.ErrorCode
                                : "INTERNAL_ERROR",
                            cleanup_exception_type = cleanupFailure.GetType().Name,
                            output_created = File.Exists(outputPath),
                            raw_document_content_returned = false,
                        }
                    );
                }
            }
            if (published)
            {
                throw new NativeToolException(
                    "EQUATION_DOCUMENT_PARTIAL",
                    "The native equation document was published but a later workflow stage failed",
                    new
                    {
                        stage,
                        original_error_code = exception is NativeToolException native
                            ? native.ErrorCode
                            : "INTERNAL_ERROR",
                        live_document_id = liveDocumentId,
                        live_version = liveVersion,
                        output_created = File.Exists(outputPath),
                        saved,
                        raw_document_content_returned = false,
                    }
                );
            }
            throw;
        }
    }

    private static JsonObject BuildEquationDocumentPreflightArguments(
        JsonElement arguments,
        JsonElement equations
    )
    {
        var result = new JsonObject
        {
            ["validation_mode"] = "native",
            ["equations"] = JsonNode.Parse(equations.GetRawText()),
        };
        CopyOptionalInteger(
            arguments,
            result,
            "per_equation_timeout_seconds"
        );
        CopyOptionalInteger(arguments, result, "total_timeout_seconds");
        return result;
    }

    private static JsonObject BuildEquationDocumentApplyArguments(
        string liveDocumentId,
        long liveVersion,
        JsonElement equations,
        string idempotencyKey
    )
    {
        var operations = new JsonArray();
        foreach (var equation in equations.EnumerateArray())
        {
            var operation = new JsonObject
            {
                ["type"] = "equation",
                ["value"] = equation.GetProperty("value").GetString(),
                ["input_format"] = equation.TryGetProperty(
                    "input_format",
                    out var format
                ) ? format.GetString() : "latex",
                ["display"] = !equation.TryGetProperty("display", out var display)
                    || display.GetBoolean(),
                ["verify_readback"] = true,
            };
            operations.Add(operation);
        }
        var result = new JsonObject
        {
            ["live_document_id"] = liveDocumentId,
            ["expected_version"] = liveVersion,
            ["operations"] = operations,
            ["activate"] = true,
            ["optimize_screen_updates"] = true,
        };
        if (idempotencyKey.Length > 0)
        {
            result["idempotency_key"] = idempotencyKey;
        }
        return result;
    }

    private static JsonObject BuildEquationDocumentRenderArguments(
        JsonElement source,
        string liveDocumentId,
        long liveVersion,
        string outputDirectory,
        string artifactStem,
        string renderOutput
    )
    {
        var result = new JsonObject
        {
            ["live_document_id"] = liveDocumentId,
            ["expected_version"] = liveVersion,
            ["output_directory"] = outputDirectory,
            ["artifact_stem"] = artifactStem,
            ["output"] = renderOutput,
        };
        foreach (
            var name in new[]
            {
                "dpi",
                "optimize_for",
                "include_document_properties",
                "bookmarks",
                "pdf_a",
                "pdfinfo_path",
                "rasterizer_path",
                "rasterizer_kind",
            }
        )
        {
            if (source.TryGetProperty(name, out var value))
            {
                result[name] = JsonNode.Parse(value.GetRawText());
            }
        }
        return result;
    }

    private async Task CleanupFailedEquationDocumentAsync(
        string liveDocumentId,
        string outputPath
    )
    {
        if (_records.TryGetValue(liveDocumentId, out var record))
        {
            await _host.InvokeAsync(
                application =>
                {
                    dynamic document = ResolveDocument(application, record);
                    document.Close(WordDoNotSaveChanges);
                    return true;
                },
                CancellationToken.None
            ).ConfigureAwait(false);
            _records.TryRemove(liveDocumentId, out _);
            InvalidateSelectionGrants(liveDocumentId);
            InvalidateRangeGrants(liveDocumentId);
            InvalidateUndoGrants(liveDocumentId);
        }
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }
    }

    private static object CompactEquationDocumentPreflight(JsonObject source) => new
    {
        valid = source["valid"]?.DeepClone(),
        valid_count = source["valid_count"]?.DeepClone(),
        invalid_count = source["invalid_count"]?.DeepClone(),
        equation_count = source["equation_count"]?.DeepClone(),
        equations = source["valid"]?.GetValue<bool>() == false
            ? source["equations"]?.DeepClone()
            : null,
    };

    private static object CompactEquationDocumentPackage(JsonObject source) => new
    {
        package_fingerprint = source["package_fingerprint"]?.DeepClone(),
        package_type = source["package_type"]?.DeepClone(),
        valid = source["valid"]?.DeepClone(),
        issues = source["issues"]?.DeepClone(),
        external_relationship_count = source["external_relationship_count"]?.DeepClone(),
        orphan_part_count = source["orphan_part_count"]?.DeepClone(),
    };

    private static string SafeEquationArtifactStem(string value)
    {
        var normalized = new string(
            value.Select(character =>
                char.IsAsciiLetterOrDigit(character) ? character : '-'
            ).ToArray()
        ).Trim('-');
        if (normalized.Length == 0)
        {
            normalized = "equations";
        }
        return normalized[..Math.Min(normalized.Length, 64)];
    }

    private static void CopyOptionalInteger(
        JsonElement source,
        JsonObject target,
        string name
    )
    {
        if (source.TryGetProperty(name, out var value))
        {
            target[name] = JsonNode.Parse(value.GetRawText());
        }
    }
}
