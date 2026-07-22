using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int MaxSemanticSelectorCommands = 16;

    private static Task<object> PlanPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var context = BuildPackageSemanticEditPlan(path, arguments, cancellationToken);
        return SemanticEditPlanResponse(
            context,
            path,
            arguments.Boolean("include_details", false),
            started
        );
    });

    private static Task<object> ApplyPackageSemanticEditsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    ) => ExecutePackageTextAction(() =>
    {
        var started = Stopwatch.GetTimestamp();
        var path = ResolveInspectablePackagePath(arguments);
        var expectedPlanId = RequiredSemanticEditPlanId(arguments);
        var context = BuildPackageSemanticEditPlan(path, arguments, cancellationToken);
        if (!string.Equals(context.PlanId, expectedPlanId, StringComparison.Ordinal))
        {
            throw new NativeToolException(
                "PLAN_MISMATCH",
                "Commands do not reproduce the reviewed semantic edit plan ID"
            );
        }
        if (context.HasDigitalSignatures)
        {
            throw new NativeToolException(
                "SIGNED_PACKAGE",
                "Direct OOXML editing is blocked because the package contains digital signatures"
            );
        }
        if (!context.Validation.NoNewErrors)
        {
            throw new NativeToolException(
                "OOXML_SCHEMA_INVALID",
                "The exact candidate package introduces Microsoft Open XML schema errors",
                new
                {
                    error_count = context.Validation.ErrorCount,
                    baseline_error_count = context.Validation.BaselineErrorCount,
                    candidate_error_count = context.Validation.CandidateErrorCount,
                    errors_truncated = context.Validation.ErrorsTruncated,
                    issues = context.Validation.Issues.Take(20).Select(ValidationIssueItem).ToArray(),
                }
            );
        }
        if (!context.Plan.HasChanges)
        {
            return new
            {
                file_name = Path.GetFileName(path),
                plan_id = context.PlanId,
                applied = false,
                no_op = true,
                package_fingerprint = context.Package.Fingerprint,
                backup_path = (string?)null,
                changed_entry_names = Array.Empty<string>(),
                microsoft_schema_valid = context.Validation.CandidateValid,
                microsoft_schema_no_new_errors = context.Validation.NoNewErrors,
                raw_xml_returned = false,
                mutation_performed = false,
                word_opened = false,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }

        var result = new OpcAtomicPackageWriter().Write(
            path,
            context.Plan.CreateMutation(context.Package),
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
            plan_id = context.PlanId,
            applied = true,
            no_op = false,
            operation_count = context.Plan.OperationCount,
            previous_package_fingerprint = context.Package.Fingerprint,
            package_fingerprint = result.Fingerprint,
            predicted_package_fingerprint = context.Plan.ResultPackageFingerprint,
            backup_path = result.BackupPath,
            changed_entry_names = result.ChangedEntryNames,
            diagnostic_count = result.Diagnostics.Count,
            microsoft_schema_valid = context.Validation.CandidateValid,
            microsoft_schema_no_new_errors = context.Validation.NoNewErrors,
            raw_xml_returned = false,
            mutation_performed = true,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    });

    private static object SemanticEditPlanResponse(
        PackageSemanticEditPlanContext context,
        string path,
        bool includeDetails,
        long started
    )
    {
        var blockedReasons = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blockedReasons.Add("digital_signature_present");
        }
        if (!context.Validation.NoNewErrors)
        {
            blockedReasons.Add("microsoft_schema_validation_failed");
        }
        return new
        {
            file_name = Path.GetFileName(path),
            plan_id = context.PlanId,
            base_package_fingerprint = context.Plan.BasePackageFingerprint,
            result_package_fingerprint = context.Plan.ResultPackageFingerprint,
            submitted_command_count = context.SubmittedCommandCount,
            selector_command_count = context.SelectorResolutions.Count,
            selector_match_count = context.SelectorResolutions.Sum(item => item.MatchedNodeCount),
            operation_count = context.Plan.OperationCount,
            changed_operation_count = context.Plan.ChangedOperationCount,
            changed_part_count = context.Plan.ChangedPartCount,
            total_xml_byte_delta = context.Plan.TotalXmlByteDelta,
            has_changes = context.Plan.HasChanges,
            can_apply = blockedReasons.Count == 0,
            apply_blocked = blockedReasons.Count != 0,
            apply_blocked_reasons = blockedReasons,
            candidate_validation = new
            {
                performed = context.Validation.Performed,
                valid = context.Validation.CandidateValid,
                no_new_errors = context.Validation.NoNewErrors,
                error_count = context.Validation.ErrorCount,
                baseline_error_count = context.Validation.BaselineErrorCount,
                candidate_error_count = context.Validation.CandidateErrorCount,
                errors_truncated = context.Validation.ErrorsTruncated,
                not_performed_reason = context.Validation.NotPerformedReason,
                issues = includeDetails
                    ? context.Validation.Issues.Select(ValidationIssueItem).ToArray()
                    : null,
            },
            operations = includeDetails
                ? context.Plan.Operations.Select(operation => new
                {
                    index = operation.Index,
                    kind = operation.Kind,
                    node_id = operation.NodeId.Value,
                    property_name = operation.PropertyName,
                    before_value = BoundForResponse(operation.BeforeValue, 253),
                    after_value = BoundForResponse(operation.AfterValue, 253),
                    source_part_uri = BoundForResponse(operation.SourcePartUri, 512),
                    source_element_ordinal = operation.SourceElementOrdinal,
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
            selector_resolutions = includeDetails
                ? context.SelectorResolutions.Select(item => new
                {
                    command_index = item.CommandIndex,
                    matched_node_count = item.MatchedNodeCount,
                    scanned_node_count = item.ScannedNodeCount,
                    candidate_seed = item.CandidateSeed,
                }).ToArray()
                : null,
            raw_xml_returned = false,
            mutation_performed = false,
            word_opened = false,
            runtime = "dotnet-native",
            python_used = false,
            performance = new
            {
                total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private static PackageSemanticEditPlanContext BuildPackageSemanticEditPlan(
        string path,
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedFingerprint = RequiredSha256(
            arguments,
            "expected_package_fingerprint"
        );
        var package = new OpcPackageReader().Read(path, cancellationToken);
        if (!string.Equals(
            package.Fingerprint,
            expectedFingerprint,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new WordSemanticPreconditionException(
                "Saved package changed before the semantic edit plan was built."
            );
        }
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        var parsed = ParseSemanticEditCommands(
            arguments,
            semantic,
            cancellationToken
        );
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions { MaxCommands = 200 }
        ).PlanStyleAssignments(
            package,
            semantic,
            parsed.Commands,
            cancellationToken
        );
        var validation = ValidatePackageCandidate(
            package,
            plan.CreateMutation(package),
            cancellationToken
        );
        return new PackageSemanticEditPlanContext(
            package,
            plan,
            CreateSemanticEditPlanId(plan.PlanId, parsed.IntentFields),
            parsed.SubmittedCommandCount,
            parsed.SelectorResolutions,
            HasDigitalSignatures(package),
            validation
        );
    }

    private static ParsedSemanticEditBatch ParseSemanticEditCommands(
        JsonElement arguments,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken
    )
    {
        var array = arguments.RequiredArray("commands");
        if (array.GetArrayLength() is < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "commands must contain between 1 and 200 semantic edits"
            );
        }
        var result = new List<WordStyleAssignmentCommand>(array.GetArrayLength());
        var intentFields = new List<string>(checked(array.GetArrayLength() * 16));
        var selectorResolutions = new List<SemanticSelectorResolution>();
        var commandIndex = 0;
        foreach (var item in array.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Every semantic edit command must be an object"
                );
            }
            _ = item.Required("type");
            var type = item.String("type");
            switch (type)
            {
                case "set_style":
                    ParseExactStyleCommand(item, commandIndex, result, intentFields);
                    break;
                case "set_style_where":
                    if (selectorResolutions.Count >= MaxSemanticSelectorCommands)
                    {
                        throw new NativeToolException(
                            "TRANSACTION_LIMIT",
                            $"At most {MaxSemanticSelectorCommands} selector commands are allowed"
                        );
                    }
                    ParseSelectorStyleCommand(
                        item,
                        commandIndex,
                        semanticDocument,
                        result,
                        intentFields,
                        selectorResolutions,
                        cancellationToken
                    );
                    break;
                default:
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "Semantic edit command type must be set_style or set_style_where"
                    );
            }

            if (result.Count > 200)
            {
                throw new NativeToolException(
                    "TRANSACTION_LIMIT",
                    "Resolved semantic edits exceed the 200-operation transaction limit"
                );
            }
            commandIndex++;
        }
        return new ParsedSemanticEditBatch(
            result,
            intentFields,
            array.GetArrayLength(),
            selectorResolutions
        );
    }

    private static void ParseExactStyleCommand(
        JsonElement item,
        int commandIndex,
        ICollection<WordStyleAssignmentCommand> result,
        ICollection<string> intentFields
    )
    {
        ValidateCommandProperties(
            item,
            [
                "type",
                "node_id",
                "style_id",
                "expected_style_id",
                "require_no_explicit_style",
            ]
        );
        _ = item.Required("node_id");
        var style = ParseStyleAssignment(item);
        var nodeId = item.String("node_id");
        ValidateSemanticNodeId(nodeId);
        result.Add(
            new WordStyleAssignmentCommand(
                new SemanticNodeId(nodeId),
                style.StyleId,
                style.ExpectedStyleId,
                style.RequireNoExplicitStyle
            )
        );
        AddCommandIntent(
            intentFields,
            commandIndex,
            "set_style",
            nodeId,
            style
        );
    }

    private static void ParseSelectorStyleCommand(
        JsonElement item,
        int commandIndex,
        WordSemanticDocument semanticDocument,
        ICollection<WordStyleAssignmentCommand> result,
        ICollection<string> intentFields,
        ICollection<SemanticSelectorResolution> selectorResolutions,
        CancellationToken cancellationToken
    )
    {
        ValidateCommandProperties(
            item,
            [
                "type",
                "selector",
                "style_id",
                "expected_style_id",
                "require_no_explicit_style",
                "max_matches",
            ]
        );
        _ = item.Required("selector");
        _ = item.Required("max_matches");
        if (
            !item.TryGetProperty("selector", out var selector)
            || selector.ValueKind != JsonValueKind.Object
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "selector must be an object"
            );
        }
        var maxMatches = item.NullableInt64("max_matches");
        if (maxMatches is null or < 1 or > 200)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_matches must be between 1 and 200"
            );
        }
        var maxMatchLimit = maxMatches.Value;
        var style = ParseStyleAssignment(item);
        var query = ParseSemanticStyleSelector(selector);
        WordSemanticQueryResult queryResult;
        try
        {
            queryResult = new WordSemanticQueryEngine().Query(
                semanticDocument,
                query,
                cancellationToken
            );
        }
        catch (KeyNotFoundException exception)
        {
            throw new NativeToolException(
                "UNSAFE_EDIT",
                BoundForResponse(exception.Message, 512) ?? "Selector scope does not exist"
            );
        }
        catch (ArgumentException exception)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                BoundForResponse(exception.Message, 512) ?? "Selector is invalid"
            );
        }
        if (queryResult.MatchedNodeCount == 0)
        {
            throw new NativeToolException(
                "EMPTY_SELECTION",
                "set_style_where selector matched no semantic nodes"
            );
        }
        if (queryResult.MatchedNodeCount > maxMatchLimit)
        {
            throw new NativeToolException(
                "SELECTION_LIMIT",
                "set_style_where selector exceeded max_matches",
                new
                {
                    command_index = commandIndex,
                    matched_node_count = queryResult.MatchedNodeCount,
                    max_matches = maxMatchLimit,
                }
            );
        }
        foreach (var match in queryResult.Matches)
        {
            result.Add(
                new WordStyleAssignmentCommand(
                    match.NodeId,
                    style.StyleId,
                    style.ExpectedStyleId,
                    style.RequireNoExplicitStyle
                )
            );
        }
        selectorResolutions.Add(
            new SemanticSelectorResolution(
                commandIndex,
                queryResult.MatchedNodeCount,
                queryResult.ScannedNodeCount,
                queryResult.CandidateSeed
            )
        );
        AddCommandIntent(
            intentFields,
            commandIndex,
            "set_style_where",
            null,
            style
        );
        AddQueryIntent(intentFields, query, maxMatchLimit);
    }

    private static StyleAssignmentInput ParseStyleAssignment(JsonElement item)
    {
        _ = item.Required("style_id");
        var styleId = item.String("style_id");
        var expectedStyleId = OptionalString(item, "expected_style_id");
        var requireNoExplicitStyle = item.Boolean(
            "require_no_explicit_style",
            false
        );
        if (
            string.IsNullOrWhiteSpace(styleId)
            || styleId.Length > 253
            || expectedStyleId is not null
                && (string.IsNullOrWhiteSpace(expectedStyleId) || expectedStyleId.Length > 253)
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "style_id and expected_style_id must contain between 1 and 253 characters"
            );
        }
        if (requireNoExplicitStyle && expectedStyleId is not null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "Use expected_style_id or require_no_explicit_style, never both"
            );
        }
        return new StyleAssignmentInput(
            styleId,
            expectedStyleId,
            requireNoExplicitStyle
        );
    }

    private static WordSemanticQuery ParseSemanticStyleSelector(JsonElement selector)
    {
        ValidateCommandProperties(
            selector,
            [
                "kind",
                "text",
                "text_match",
                "text_scope",
                "case_sensitive",
                "property_equals",
                "ancestor",
                "descendant",
                "within_node_id",
                "source_part_uri",
            ]
        );
        _ = selector.Required("kind");
        var kind = ParseAssignableStyleKind(selector.String("kind"), "kind");
        var properties = ParsePropertyEquals(selector);
        if (properties is { Count: 0 })
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "selector.property_equals cannot be empty"
            );
        }
        var query = new WordSemanticQuery
        {
            Kinds = [kind],
            Text = OptionalString(selector, "text"),
            TextMatch = selector.String("text_match", "contains") switch
            {
                "contains" => WordSemanticTextMatchMode.Contains,
                "equals" => WordSemanticTextMatchMode.Equals,
                "starts_with" => WordSemanticTextMatchMode.StartsWith,
                "ends_with" => WordSemanticTextMatchMode.EndsWith,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "selector.text_match must be contains, equals, starts_with, or ends_with"
                ),
            },
            TextScope = selector.String("text_scope", "node") switch
            {
                "node" => WordSemanticTextScope.Node,
                "subtree" => WordSemanticTextScope.Subtree,
                _ => throw new NativeToolException(
                    "INVALID_INPUT",
                    "selector.text_scope must be node or subtree"
                ),
            },
            CaseSensitive = selector.Boolean("case_sensitive", false),
            PropertyEquals = properties,
            Ancestor = ParseStyleRelatedPredicate(selector, "ancestor"),
            Descendant = ParseStyleRelatedPredicate(selector, "descendant"),
            WithinNodeId = OptionalString(selector, "within_node_id") is { } nodeId
                ? ParseSelectorNodeId(nodeId)
                : null,
            SourcePartUri = OptionalString(selector, "source_part_uri"),
            Offset = 0,
            Limit = 200,
            TextPreviewCharacters = 0,
            IncludeProperties = false,
            IncludeSource = false,
        };
        return query;
    }

    private static WordSemanticRelatedNodePredicate? ParseStyleRelatedPredicate(
        JsonElement selector,
        string name
    )
    {
        if (!selector.TryGetProperty(name, out var value))
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new NativeToolException("INVALID_INPUT", $"selector.{name} must be an object");
        }
        ValidateCommandProperties(value, ["kind", "property_equals"]);
        var kind = OptionalString(value, "kind");
        var properties = ParsePropertyEquals(value);
        if (properties is { Count: 0 })
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"selector.{name}.property_equals cannot be empty"
            );
        }
        if (kind is null && properties is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"selector.{name} must contain kind or property_equals"
            );
        }
        return new WordSemanticRelatedNodePredicate
        {
            Kinds = kind is null ? null : [ParseSemanticKind(kind, $"selector.{name}.kind")],
            PropertyEquals = properties,
        };
    }

    private static WordSemanticNodeKind ParseAssignableStyleKind(string raw, string name)
    {
        var kind = ParseSemanticKind(raw, name);
        if (kind is not WordSemanticNodeKind.Paragraph
            and not WordSemanticNodeKind.Run
            and not WordSemanticNodeKind.Table)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be paragraph, run, or table"
            );
        }
        return kind;
    }

    private static WordSemanticNodeKind ParseSemanticKind(string raw, string name)
    {
        if (!SemanticNodeKinds.TryGetValue(raw, out var kind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} is not a known semantic node kind"
            );
        }
        return kind;
    }

    private static SemanticNodeId ParseSelectorNodeId(string value)
    {
        ValidateSemanticNodeId(value);
        return new SemanticNodeId(value);
    }

    private static void ValidateCommandProperties(
        JsonElement value,
        params string[] allowed
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "A semantic edit command or selector contains a duplicate property"
                );
            }
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "A semantic edit command or selector contains an unknown property"
                );
            }
        }
    }

    private static void AddCommandIntent(
        ICollection<string> fields,
        int commandIndex,
        string type,
        string? nodeId,
        StyleAssignmentInput style
    )
    {
        fields.Add(commandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.Add(type);
        AddNullableIntent(fields, nodeId);
        fields.Add(style.StyleId);
        AddNullableIntent(fields, style.ExpectedStyleId);
        fields.Add(style.RequireNoExplicitStyle ? "1" : "0");
    }

    private static void AddQueryIntent(
        ICollection<string> fields,
        WordSemanticQuery query,
        long maxMatches
    )
    {
        fields.Add(query.Kinds!.Single().ToString());
        AddNullableIntent(fields, query.Text);
        fields.Add(query.TextMatch.ToString());
        fields.Add(query.TextScope.ToString());
        fields.Add(query.CaseSensitive ? "1" : "0");
        AddPropertyIntent(fields, query.PropertyEquals);
        AddRelatedIntent(fields, query.Ancestor);
        AddRelatedIntent(fields, query.Descendant);
        AddNullableIntent(fields, query.WithinNodeId?.Value);
        AddNullableIntent(fields, query.SourcePartUri);
        fields.Add(maxMatches.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddRelatedIntent(
        ICollection<string> fields,
        WordSemanticRelatedNodePredicate? predicate
    )
    {
        if (predicate is null)
        {
            fields.Add("related:null");
            return;
        }
        fields.Add("related:value");
        AddNullableIntent(fields, predicate.Kinds?.Single().ToString());
        AddPropertyIntent(fields, predicate.PropertyEquals);
    }

    private static void AddPropertyIntent(
        ICollection<string> fields,
        IReadOnlyDictionary<string, string>? properties
    )
    {
        if (properties is null)
        {
            fields.Add("properties:null");
            return;
        }
        fields.Add("properties:value");
        fields.Add(properties.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (name, value) in properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            fields.Add(name);
            fields.Add(value);
        }
    }

    private static void AddNullableIntent(
        ICollection<string> fields,
        string? value
    )
    {
        fields.Add(value is null ? "nullable:null" : "nullable:value");
        if (value is not null)
        {
            fields.Add(value);
        }
    }

    private static string CreateSemanticEditPlanId(
        string enginePlanId,
        IReadOnlyList<string> intentFields
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIntentHashField(hash, "wordtoolkit-semantic-edit-intent-v1");
        AppendIntentHashField(hash, enginePlanId);
        foreach (var field in intentFields)
        {
            AppendIntentHashField(hash, field);
        }
        var digest = hash.GetHashAndReset();
        return "wseplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendIntentHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateSemanticNodeId(string nodeId)
    {
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
    }

    private static string RequiredSemanticEditPlanId(JsonElement arguments)
    {
        _ = arguments.Required("expected_plan_id");
        var value = arguments.String("expected_plan_id");
        if (
            value.Length is < 12 or > 128
            || !value.StartsWith("wseplan_", StringComparison.Ordinal)
            || value[8..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'
            )
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_plan_id is not a valid semantic edit plan ID"
            );
        }
        return value;
    }

    private sealed record PackageSemanticEditPlanContext(
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        string PlanId,
        int SubmittedCommandCount,
        IReadOnlyList<SemanticSelectorResolution> SelectorResolutions,
        bool HasDigitalSignatures,
        CandidateSchemaValidation Validation
    );

    private sealed record ParsedSemanticEditBatch(
        IReadOnlyList<WordStyleAssignmentCommand> Commands,
        IReadOnlyList<string> IntentFields,
        int SubmittedCommandCount,
        IReadOnlyList<SemanticSelectorResolution> SelectorResolutions
    );

    private sealed record SemanticSelectorResolution(
        int CommandIndex,
        int MatchedNodeCount,
        int ScannedNodeCount,
        string CandidateSeed
    );

    private sealed record StyleAssignmentInput(
        string StyleId,
        string? ExpectedStyleId,
        bool RequireNoExplicitStyle
    );
}
