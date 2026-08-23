using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static readonly Regex MemberReferencePattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,63}$",
        RegexOptions.CultureInvariant
    );
    private static readonly HashSet<string> ValidObjectModelTypeKinds = new(
        ["enum", "record", "module", "interface", "dispatch", "coclass", "alias", "union"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ValidMemberKinds = new(
        ["method", "property_get", "property_put", "property_put_ref", "enum_value", "variable"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ValidCapabilityEffects = new(
        ["constant", "read", "format", "content", "structure", "calculation", "view", "event", "external", "lifecycle", "unknown"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ValidCapabilityExecutions = new(
        ["metadata_only", "read_allowed", "write_allowed", "blocked"],
        StringComparer.Ordinal
    );
    private readonly SemaphoreSlim _objectModelGate = new(1, 1);
    private WordObjectModelCatalog? _objectModelCatalog;

    private async Task<object> InspectObjectModelTypesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var query = BoundedObjectModelQuery(arguments.String("query"));
        var kind = arguments.String("kind").Trim().ToLowerInvariant();
        var offset = BoundedPageInteger(arguments, "offset", 0, 0, 1_000_000);
        var limit = BoundedPageInteger(arguments, "limit", 100, 1, 200);
        var refresh = arguments.Boolean("refresh", false);
        if (kind.Length > 0 && !ValidObjectModelTypeKinds.Contains(kind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind is not a supported Word object-model type kind",
                new { kind, allowed = ValidObjectModelTypeKinds.Order().ToArray() }
            );
        }
        var (catalog, source) = await ObjectModelCatalogAsync(
            refresh,
            cancellationToken
        );
        var matches = catalog.Types
            .Where(
                item =>
                    (query.Length == 0
                        || item.Name.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    && (kind.Length == 0 || item.Kind == kind)
            )
            .ToArray();
        var page = matches.Skip(offset).Take(limit).Select(ObjectModelTypePayload).ToArray();
        return new
        {
            schema_version = 2,
            generated_at = catalog.GeneratedAt,
            source = "installed_microsoft_word_com_type_library",
            source_access = source,
            privacy = ObjectModelPrivacy,
            library = ObjectModelLibraryPayload(catalog.Library),
            stats = ObjectModelStatsPayload(catalog),
            query,
            kind,
            offset,
            limit,
            matched_count = matches.Length,
            returned_count = page.Length,
            has_more = offset + page.Length < matches.Length,
            types = page,
            document_content_returned = false,
            performance = Performance(started),
        };
    }

    private async Task<object> InspectObjectModelMembersAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var typeName = arguments.String("type_name").Trim();
        if (typeName.Length is < 1 or > 256)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "type_name must be a non-empty string of at most 256 characters"
            );
        }
        var query = BoundedObjectModelQuery(arguments.String("query"));
        var kind = arguments.String("kind").Trim().ToLowerInvariant();
        var offset = BoundedPageInteger(arguments, "offset", 0, 0, 1_000_000);
        var limit = BoundedPageInteger(arguments, "limit", 100, 1, 200);
        var refresh = arguments.Boolean("refresh", false);
        if (kind.Length > 0 && !ValidMemberKinds.Contains(kind))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "kind is not a supported Word member kind",
                new { kind, allowed = ValidMemberKinds.Order().ToArray() }
            );
        }
        var (catalog, source) = await ObjectModelCatalogAsync(
            refresh,
            cancellationToken
        );
        var selected = catalog.Types.FirstOrDefault(
            item => string.Equals(item.Name, typeName, StringComparison.OrdinalIgnoreCase)
        ) ?? throw new NativeToolException(
            "INVALID_INPUT",
            "The requested Word object-model type was not found",
            new { type_name = typeName }
        );
        var matches = selected.Members
            .Where(
                item =>
                    (query.Length == 0
                        || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    && (kind.Length == 0 || item.Kind == kind)
            )
            .ToArray();
        var page = matches.Skip(offset).Take(limit).Select(ObjectModelMemberPayload).ToArray();
        return new
        {
            schema_version = 2,
            generated_at = catalog.GeneratedAt,
            source = "installed_microsoft_word_com_type_library",
            source_access = source,
            privacy = ObjectModelPrivacy,
            library = ObjectModelLibraryPayload(catalog.Library),
            stats = ObjectModelStatsPayload(catalog),
            type = new
            {
                name = selected.Name,
                kind = selected.Kind,
                type_index = selected.TypeIndex,
                guid = selected.Guid,
                member_count = selected.Members.Length,
                implemented_types = selected.ImplementedTypes.Select(
                    item => new
                    {
                        name = item.Name,
                        kind = item.Kind,
                        guid = item.Guid,
                        flags = item.Flags,
                        flag_names = item.FlagNames,
                    }
                ),
            },
            query,
            kind,
            offset,
            limit,
            matched_count = matches.Length,
            returned_count = page.Length,
            has_more = offset + page.Length < matches.Length,
            members = page,
            document_content_returned = false,
            performance = Performance(started),
        };
    }

    private async Task<object> InspectMemberCapabilitiesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var query = BoundedObjectModelQuery(arguments.String("query"));
        var typeName = arguments.String("type_name").Trim();
        var memberKind = arguments.String("member_kind").Trim().ToLowerInvariant();
        var effect = arguments.String("effect").Trim().ToLowerInvariant();
        var execution = arguments.String("execution").Trim().ToLowerInvariant();
        var offset = BoundedPageInteger(arguments, "offset", 0, 0, 1_000_000);
        var limit = BoundedPageInteger(arguments, "limit", 100, 1, 200);
        var refresh = arguments.Boolean("refresh", false);
        if (typeName.Length > 256)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "type_name must contain at most 256 characters"
            );
        }
        ValidateCatalogFilter("member_kind", memberKind, ValidMemberKinds);
        ValidateCatalogFilter("effect", effect, ValidCapabilityEffects);
        ValidateCatalogFilter("execution", execution, ValidCapabilityExecutions);
        var (catalog, source) = await ObjectModelCatalogAsync(
            refresh,
            cancellationToken
        );
        var matches = catalog.Capabilities
            .Where(
                item =>
                {
                    var searchable =
                        $"{item.Type.Name} {item.Member.Name} {item.CapabilityId} {item.VirtualToolName}";
                    return (query.Length == 0
                            || searchable.Contains(query, StringComparison.OrdinalIgnoreCase))
                        && (typeName.Length == 0
                            || string.Equals(
                                item.Type.Name,
                                typeName,
                                StringComparison.OrdinalIgnoreCase
                            ))
                        && (memberKind.Length == 0 || item.Member.Kind == memberKind)
                        && (effect.Length == 0 || item.Policy.Effect == effect)
                        && (execution.Length == 0 || item.Policy.Execution == execution);
                }
            )
            .ToArray();
        var page = matches.Skip(offset).Take(limit).Select(CapabilityPayload).ToArray();
        return new
        {
            schema_version = 2,
            generated_at = catalog.GeneratedAt,
            source = "installed_microsoft_word_com_type_library",
            source_access = source,
            privacy = ObjectModelPrivacy,
            library = ObjectModelLibraryPayload(catalog.Library),
            stats = ObjectModelStatsPayload(catalog),
            registry = new
            {
                schema_version = 2,
                stats = catalog.CapabilityStats,
            },
            query,
            type_name = typeName,
            member_kind = memberKind,
            effect,
            execution,
            offset,
            limit,
            matched_count = matches.Length,
            returned_count = page.Length,
            has_more = offset + page.Length < matches.Length,
            capabilities = page,
            document_content_returned = false,
            performance = Performance(started),
        };
    }

    private async Task<(WordObjectModelCatalog Catalog, string Source)>
        ObjectModelCatalogAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && _objectModelCatalog is not null)
        {
            return (_objectModelCatalog, "memory_cache");
        }
        await _objectModelGate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _objectModelCatalog is not null)
            {
                return (_objectModelCatalog, "memory_cache");
            }
            var catalog = await _host.InvokeAsync(
                application => WordObjectModelCatalog.Scan(application),
                WordComReplaySafety.ReplaySafe,
                cancellationToken,
                launchIfMissing: false
            );
            _objectModelCatalog = catalog;
            return (catalog, "live_installed_type_library_scan");
        }
        finally
        {
            _objectModelGate.Release();
        }
    }

    private const string ObjectModelPrivacy =
        "Only installed Word API metadata is kept in memory. No document content, "
        + "document counts, paths, handles, owners, help text or help-file paths are retained.";

    private static object ObjectModelLibraryPayload(WordComLibrary library)
    {
        return new
        {
            guid = library.Guid,
            lcid = library.Lcid,
            syskind = library.SysKind,
            major_version = library.MajorVersion,
            minor_version = library.MinorVersion,
            flags = library.Flags,
            declared_type_count = library.DeclaredTypeCount,
            application_type_index = library.ApplicationTypeIndex,
        };
    }

    private static object ObjectModelStatsPayload(WordObjectModelCatalog catalog)
    {
        return new
        {
            type_count = catalog.Types.Length,
            member_count = catalog.Types.Sum(item => item.Members.Length),
            scan_errors = catalog.ScanErrors,
            truncated = catalog.Truncated,
            scan_duration_ms = catalog.ScanDurationMs,
        };
    }

    private static object ObjectModelTypePayload(WordComType item)
    {
        return new
        {
            name = item.Name,
            kind = item.Kind,
            type_index = item.TypeIndex,
            guid = item.Guid,
            flags = item.Flags,
            declared_function_count = item.DeclaredFunctionCount,
            declared_variable_count = item.DeclaredVariableCount,
            implemented_type_count = item.ImplementedTypes.Length,
            member_count = item.Members.Length,
        };
    }

    private static object ObjectModelMemberPayload(WordComMember member)
    {
        return new
        {
            name = member.Name,
            kind = member.Kind,
            member_id = member.MemberId,
            declaration_index = member.DeclarationIndex,
            flags = member.Flags,
            flag_names = member.FlagNames,
            parameters = member.Parameters.Select(ParameterPayload).ToArray(),
            parameter_count = member.ParameterCount,
            optional_parameter_count = member.OptionalParameterCount,
            function_kind = member.FunctionKind,
            invoke_kind = member.InvokeKind,
            call_convention = member.CallConvention,
            vtable_offset = member.VtableOffset,
            variadic = member.Variadic,
            return_type = member.ReturnType,
            type = member.ConstantType,
            value = member.ConstantValue,
        };
    }

    private static object ParameterPayload(WordComParameter parameter)
    {
        return new
        {
            name = parameter.Name,
            type = parameter.Type,
            flags = parameter.Flags,
            flag_names = parameter.FlagNames,
            optional = parameter.Optional,
        };
    }

    private static object CapabilityPayload(WordMemberCapability capability)
    {
        var execution = capability.Policy.Execution;
        var endpoint = execution is "read_allowed" or "write_allowed"
            ? "execute_live_word_member_operations"
            : "inspect_live_word_member_capabilities";
        return new
        {
            capability_id = capability.CapabilityId,
            accessor_group_id = capability.AccessorGroupId,
            type = new
            {
                name = capability.Type.Name,
                kind = capability.Type.Kind,
                type_index = capability.Type.TypeIndex,
                guid = capability.Type.Guid,
            },
            member = new
            {
                name = capability.Member.Name,
                kind = capability.Member.Kind,
                member_id = capability.Member.MemberId,
                declaration_index = capability.Member.DeclarationIndex,
                flags = capability.Member.Flags,
                invoke_kind = capability.Member.InvokeKind,
                function_kind = capability.Member.FunctionKind,
                call_convention = capability.Member.CallConvention,
                flag_names = capability.Member.FlagNames,
            },
            signature = new
            {
                parameters = capability.Member.Parameters.Select(ParameterPayload),
                parameter_count = capability.Member.ParameterCount,
                optional_parameter_count = capability.Member.OptionalParameterCount,
                variadic = capability.Member.Variadic,
                return_type = capability.Member.ReturnType,
            },
            target = new
            {
                required_type = capability.Type.Name,
                allowed_roots = capability.AllowedRoots,
                result_chaining_allowed = true,
            },
            policy = new
            {
                effect = capability.Policy.Effect,
                execution = capability.Policy.Execution,
                reason = capability.Policy.Reason,
                mutating = capability.Policy.Mutating,
                undo_required = capability.Policy.Mutating,
            },
            constant = capability.Member.Kind == "enum_value"
                ? new
                {
                    type = capability.Type.Name,
                    storage_type = capability.Member.ConstantType,
                    value = capability.Member.ConstantValue,
                }
                : null,
            virtual_tool = new
            {
                tool_id = capability.CapabilityId,
                name = capability.VirtualToolName,
                title =
                    $"{capability.Type.Name}.{capability.Member.Name} [{capability.Member.Kind}]",
                kind = VirtualToolKind(capability),
                availability = capability.Policy.Execution,
                endpoint,
                input_schema = VirtualInputSchema(capability),
                output_schema = VirtualOutputSchema(capability),
            },
        };
    }

    private static string VirtualToolKind(WordMemberCapability capability)
    {
        if (capability.Policy.Execution == "blocked")
        {
            return "unavailable";
        }
        return capability.Member.Kind switch
        {
            "enum_value" => "constant",
            "property_get" => "read",
            "property_put" or "property_put_ref" => "edit",
            "method" => "call",
            "variable" => "metadata",
            _ => "unavailable",
        };
    }

    private static object VirtualInputSchema(WordMemberCapability capability)
    {
        if (
            capability.Policy.Execution is not ("read_allowed" or "write_allowed")
        )
        {
            return new { type = "object", executable = false };
        }
        return new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "capability_id", "target" },
            capability_id = capability.CapabilityId,
            target_roots = capability.AllowedRoots,
            parameters = capability.Member.Parameters.Select(ParameterPayload),
        };
    }

    private static object VirtualOutputSchema(WordMemberCapability capability)
    {
        return new
        {
            type = "object",
            capability_id = capability.CapabilityId,
            execution = capability.Policy.Execution,
            return_type = capability.Member.ReturnType,
        };
    }

    private static string BoundedObjectModelQuery(string value)
    {
        var query = value.Trim();
        if (query.Length > 128)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "query must contain at most 128 characters"
            );
        }
        return query;
    }

    private static int BoundedPageInteger(
        JsonElement arguments,
        string name,
        int defaultValue,
        int minimum,
        int maximum
    )
    {
        var value = arguments.NullableInt64(name) ?? defaultValue;
        if (value < minimum || value > maximum)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} must be between {minimum} and {maximum}"
            );
        }
        return checked((int)value);
    }

    private static void ValidateCatalogFilter(
        string name,
        string value,
        HashSet<string> allowed
    )
    {
        if (value.Length > 0 && !allowed.Contains(value))
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} is not supported by the Word capability registry",
                new { value, allowed = allowed.Order().ToArray() }
            );
        }
    }

    private async Task<object> PreflightMemberOperationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var operationsNode = arguments.RequiredArray("operations");
        var (catalog, source) = await ObjectModelCatalogAsync(
            refresh: false,
            cancellationToken
        );
        var prepared = PrepareMemberOperations(catalog, operationsNode);
        return new
        {
            valid = true,
            registry_complete = CatalogComplete(catalog),
            registry_profile_count = catalog.Capabilities.Length,
            operation_count = prepared.Length,
            mutating_count = prepared.Count(item => item.Capability.Policy.Mutating),
            read_count = prepared.Count(item => !item.Capability.Policy.Mutating),
            requires_expected_version = prepared.Any(
                item => item.Capability.Policy.Mutating
            ),
            single_com_attachment_on_execute = true,
            single_undo_record_on_execute = prepared.Any(
                item => item.Capability.Policy.Mutating
            ),
            operations = prepared.Select(MemberOperationPayload).ToArray(),
            source_access = source,
            document_content_returned = false,
            performance = Performance(started),
        };
    }

    private static PreparedCatalogOperation[] PrepareMemberOperations(
        WordObjectModelCatalog catalog,
        JsonElement operationsNode
    )
    {
        var count = operationsNode.GetArrayLength();
        if (count is < 1 or > 50)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "operations must contain from 1 to 50 member operations"
            );
        }
        if (Encoding.UTF8.GetByteCount(operationsNode.GetRawText()) > 512_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "member operations exceed the 512,000-byte preflight limit"
            );
        }
        var enumTypes = catalog.Types
            .Where(item => item.Kind == "enum")
            .Select(item => WordObjectModelCatalog.NormalizeComType(item.Name))
            .ToHashSet(StringComparer.Ordinal);
        var constants = catalog.Capabilities
            .Where(item => item.Member.Kind == "enum_value")
            .ToDictionary(item => item.CapabilityId, StringComparer.Ordinal);
        var prepared = new List<PreparedCatalogOperation>(count);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var availableResults = new Dictionary<string, string>(
            StringComparer.Ordinal
        );
        var position = 0;
        foreach (var operation in operationsNode.EnumerateArray())
        {
            position++;
            if (operation.ValueKind != JsonValueKind.Object)
            {
                throw InvalidMemberOperation(
                    "Each member operation must be an object",
                    position
                );
            }
            var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
            {
                "operation_id",
                "capability_id",
                "target",
                "arguments",
                "result_id",
            };
            var unknownKeys = operation
                .EnumerateObject()
                .Select(item => item.Name)
                .Where(name => !allowedKeys.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unknownKeys.Length > 0)
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "A member operation contains unsupported fields",
                    new { position, fields = unknownKeys }
                );
            }
            var operationId = operation.TryGetProperty(
                "operation_id",
                out var operationIdNode
            )
                ? RequireReferenceString(operationIdNode, "operation_id", position)
                : $"op_{position}";
            if (!operationIds.Add(operationId))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "operation_id values must be unique",
                    new { operation_id = operationId }
                );
            }
            if (
                !operation.TryGetProperty("capability_id", out var capabilityNode)
                || capabilityNode.ValueKind != JsonValueKind.String
            )
            {
                throw InvalidMemberOperation(
                    "capability_id must be a string",
                    position
                );
            }
            var capabilityId = capabilityNode.GetString() ?? "";
            if (
                capabilityId.Length > 64
                || !catalog.CapabilitiesById.TryGetValue(
                    capabilityId,
                    out var capability
                )
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The Word member capability_id was not found in this catalog",
                    new { position, capability_id = capabilityId }
                );
            }
            if (
                capability.Policy.Execution
                    is not ("read_allowed" or "write_allowed")
            )
            {
                throw new NativeToolException(
                    "AUTH_FORBIDDEN",
                    "The Word member capability is not executable",
                    new
                    {
                        position,
                        capability_id = capabilityId,
                        execution = capability.Policy.Execution,
                        reason = capability.Policy.Reason,
                    }
                );
            }
            if (
                !operation.TryGetProperty("target", out var targetNode)
                || targetNode.ValueKind != JsonValueKind.Object
            )
            {
                throw InvalidMemberOperation(
                    "target must be an object",
                    position
                );
            }
            var targetProperties = targetNode
                .EnumerateObject()
                .Select(item => item.Name)
                .ToArray();
            if (
                targetProperties.Any(
                    name => name is not ("kind" or "result_id")
                )
                || !targetNode.TryGetProperty("kind", out var targetKindNode)
                || targetKindNode.ValueKind != JsonValueKind.String
            )
            {
                throw InvalidMemberOperation(
                    "target must contain a supported kind and optional result_id",
                    position
                );
            }
            var targetKind = targetKindNode.GetString() ?? "";
            if (
                targetKind
                    is not (
                        "document"
                        or "selection"
                        or "selection_range"
                        or "document_content"
                        or "result"
                    )
            )
            {
                throw InvalidMemberOperation(
                    "target.kind is not supported",
                    position
                );
            }
            if (!capability.AllowedRoots.Contains(targetKind, StringComparer.Ordinal))
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The target root does not match the capability type",
                    new
                    {
                        position,
                        target_kind = targetKind,
                        required_type = capability.Type.Name,
                        allowed_roots = capability.AllowedRoots,
                    }
                );
            }
            var targetResultId = "";
            if (targetKind == "result")
            {
                if (
                    !targetNode.TryGetProperty(
                        "result_id",
                        out var targetResultNode
                    )
                )
                {
                    throw InvalidMemberOperation(
                        "A result target must refer to an earlier operation",
                        position
                    );
                }
                targetResultId = RequireReferenceString(
                    targetResultNode,
                    "target.result_id",
                    position
                );
                if (
                    !availableResults.TryGetValue(
                        targetResultId,
                        out var actualTargetType
                    )
                )
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "A result target must refer to an earlier operation",
                        new { position, result_id = targetResultId }
                    );
                }
                if (
                    !WordObjectModelCatalog.CompatibleComType(
                        actualTargetType,
                        capability.Type.Name
                    )
                )
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "The target result type does not match the capability type",
                        new
                        {
                            position,
                            actual_type = actualTargetType,
                            required_type = capability.Type.Name,
                        }
                    );
                }
            }
            else if (targetNode.TryGetProperty("result_id", out _))
            {
                throw InvalidMemberOperation(
                    "target.result_id is valid only when target.kind is result",
                    position
                );
            }

            var argumentsNode = operation.TryGetProperty(
                "arguments",
                out var suppliedArguments
            )
                ? suppliedArguments
                : default;
            if (
                argumentsNode.ValueKind is not (
                    JsonValueKind.Undefined or JsonValueKind.Array
                )
            )
            {
                throw InvalidMemberOperation(
                    "arguments must be an array",
                    position
                );
            }
            var inputParameters = capability.Member.Parameters
                .Where(parameter => !IsOutOnly(parameter))
                .ToArray();
            var argumentCount = argumentsNode.ValueKind == JsonValueKind.Array
                ? argumentsNode.GetArrayLength()
                : 0;
            if (argumentCount > 64)
            {
                throw InvalidMemberOperation(
                    "arguments must contain at most 64 values",
                    position
                );
            }
            var requiredCount = inputParameters
                .Select((parameter, index) => (parameter, index))
                .Where(item => !item.parameter.Optional)
                .Select(item => item.index + 1)
                .DefaultIfEmpty(0)
                .Max();
            if (
                argumentCount < requiredCount
                || (!capability.Member.Variadic
                    && argumentCount > inputParameters.Length)
            )
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "The argument count does not match the Word member signature",
                    new
                    {
                        position,
                        provided = argumentCount,
                        required = requiredCount,
                        maximum = capability.Member.Variadic
                            ? (int?)null
                            : inputParameters.Length,
                    }
                );
            }
            var preparedArguments = new List<PreparedMemberArgument>(
                argumentCount
            );
            if (argumentsNode.ValueKind == JsonValueKind.Array)
            {
                var argumentIndex = 0;
                foreach (var argument in argumentsNode.EnumerateArray())
                {
                    var expectedType = argumentIndex < inputParameters.Length
                        ? inputParameters[argumentIndex].Type
                        : "VARIANT";
                    var optional = argumentIndex < inputParameters.Length
                        && inputParameters[argumentIndex].Optional;
                    preparedArguments.Add(
                        PrepareMemberArgument(
                            argument,
                            expectedType,
                            optional,
                            availableResults,
                            constants,
                            enumTypes,
                            position,
                            argumentIndex
                        )
                    );
                    argumentIndex++;
                }
            }
            var resultId = "";
            if (operation.TryGetProperty("result_id", out var resultNode))
            {
                if (
                    resultNode.ValueKind != JsonValueKind.String
                    || (resultNode.GetString() ?? "").Length > 0
                        && !MemberReferencePattern.IsMatch(
                            resultNode.GetString() ?? ""
                        )
                )
                {
                    throw InvalidMemberOperation(
                        "result_id is invalid",
                        position
                    );
                }
                resultId = resultNode.GetString() ?? "";
            }
            if (resultId.Length > 0)
            {
                if (availableResults.ContainsKey(resultId))
                {
                    throw new NativeToolException(
                        "INVALID_INPUT",
                        "result_id values must be unique",
                        new { position, result_id = resultId }
                    );
                }
                if (IsVoidComType(capability.Member.ReturnType))
                {
                    throw InvalidMemberOperation(
                        "A void Word member cannot publish a result_id",
                        position
                    );
                }
                availableResults[resultId] = capability.Member.ReturnType;
            }
            prepared.Add(
                new PreparedCatalogOperation(
                    operationId,
                    capability,
                    targetKind,
                    targetResultId,
                    preparedArguments.ToArray(),
                    resultId
                )
            );
        }
        return prepared.ToArray();
    }

    private static PreparedMemberArgument PrepareMemberArgument(
        JsonElement value,
        string expectedType,
        bool optional,
        IReadOnlyDictionary<string, string> availableResults,
        IReadOnlyDictionary<string, WordMemberCapability> constants,
        HashSet<string> enumTypes,
        int position,
        int argumentIndex
    )
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (
                properties.Length == 1
                && properties[0].Name == "missing"
                && properties[0].Value.ValueKind == JsonValueKind.True
            )
            {
                if (!optional)
                {
                    throw InvalidMemberArgument(
                        "Only an optional Word parameter may use {'missing': true}",
                        position,
                        argumentIndex,
                        expectedType
                    );
                }
                return new("missing", null, "");
            }
            if (
                properties.Length == 1
                && properties[0].Name == "result_id"
            )
            {
                var resultId = RequireReferenceString(
                    properties[0].Value,
                    "argument.result_id",
                    position
                );
                if (!availableResults.TryGetValue(resultId, out var actualType))
                {
                    throw InvalidMemberArgument(
                        "Argument result_id must refer to an earlier operation",
                        position,
                        argumentIndex,
                        expectedType,
                        new { result_id = resultId }
                    );
                }
                if (
                    !WordObjectModelCatalog.CompatibleComType(
                        actualType,
                        expectedType
                    )
                )
                {
                    throw InvalidMemberArgument(
                        "An argument result type does not match the Word parameter type",
                        position,
                        argumentIndex,
                        expectedType,
                        new { result_id = resultId, actual_type = actualType }
                    );
                }
                return new("result", null, resultId);
            }
            if (
                properties.Length == 1
                && properties[0].Name == "constant_id"
            )
            {
                var constantId = RequireReferenceString(
                    properties[0].Value,
                    "argument.constant_id",
                    position
                );
                if (!constants.TryGetValue(constantId, out var constant))
                {
                    throw InvalidMemberArgument(
                        "Argument constant_id must refer to an enum virtual tool",
                        position,
                        argumentIndex,
                        expectedType,
                        new { constant_id = constantId }
                    );
                }
                var expected = WordObjectModelCatalog.NormalizeComType(
                    expectedType
                );
                var constantType = WordObjectModelCatalog.NormalizeComType(
                    constant.Type.Name
                );
                if (
                    expected != constantType
                    && !IntegerOrVariantComType(expectedType)
                )
                {
                    throw InvalidMemberArgument(
                        "The enum constant type does not match the Word parameter type",
                        position,
                        argumentIndex,
                        expectedType,
                        new
                        {
                            constant_id = constantId,
                            constant_type = constant.Type.Name,
                        }
                    );
                }
                return new("value", constant.Member.ConstantValue, "");
            }
            throw InvalidMemberArgument(
                "Argument objects may contain one result_id, constant_id, or missing marker",
                position,
                argumentIndex,
                expectedType
            );
        }
        var scalar = JsonScalar(value, position, argumentIndex);
        ValidatePrimitiveMemberArgument(
            scalar,
            expectedType,
            enumTypes,
            position,
            argumentIndex
        );
        return new("value", scalar, "");
    }

    private async Task<object> ExecuteMemberOperationsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var record = Record(arguments.String("live_document_id"));
        var activate = arguments.Boolean("activate", true);
        var optimizeScreenUpdates = arguments.Boolean(
            "optimize_screen_updates",
            true
        );
        var expectedVersion = arguments.NullableInt64("expected_version");
        var operationsNode = arguments.RequiredArray("operations");
        var (catalog, source) = await ObjectModelCatalogAsync(
            refresh: false,
            cancellationToken
        );
        var prepared = PrepareMemberOperations(catalog, operationsNode);
        var mutating = prepared.Any(item => item.Capability.Policy.Mutating);
        if (mutating && expectedVersion is null)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "expected_version is required for mutating Word member operations"
            );
        }
        if (expectedVersion is not null)
        {
            CheckVersion(record, expectedVersion);
        }
        return await _host.InvokeAsync(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                if (expectedVersion is not null)
                {
                    CheckVersion(record, expectedVersion);
                }
                if (mutating)
                {
                    RequireEditable(document);
                }
                if (activate)
                {
                    document.Activate();
                }
                var rawResults = new Dictionary<string, object?>(
                    StringComparer.Ordinal
                );
                var returnedResults = new List<object>();
                var rollbackSnapshot = mutating
                    ? CaptureLiveRollbackSnapshot(document, record.Version)
                    : null;
                dynamic? undoRecord = null;
                var undoStarted = false;
                bool? originalScreenUpdating = null;
                try
                {
                    if (mutating && optimizeScreenUpdates)
                    {
                        originalScreenUpdating = (bool)application.ScreenUpdating;
                        application.ScreenUpdating = false;
                    }
                    if (mutating)
                    {
                        undoRecord = application.UndoRecord;
                        undoRecord.StartCustomRecord(
                            "WordToolkit: catalog member operations"
                        );
                        undoStarted = true;
                    }
                    foreach (var item in prepared)
                    {
                        object target = ResolveMemberTarget(
                            application,
                            document,
                            item,
                            rawResults
                        );
                        var invocationArguments = item.Arguments
                            .Select(argument => ResolveMemberArgument(argument, rawResults))
                            .ToArray();
                        object? value;
                        try
                        {
                            value = InvokeCatalogMember(
                                target,
                                item.Capability.Member,
                                invocationArguments
                            );
                        }
                        catch (Exception exception)
                            when (exception is not NativeToolException)
                        {
                            throw new NativeToolException(
                                "EXTERNAL_TOOL_FAILED",
                                "Microsoft Word rejected a catalog-backed member operation",
                                new
                                {
                                    operation_id = item.OperationId,
                                    capability_id = item.Capability.CapabilityId,
                                    member =
                                        $"{item.Capability.Type.Name}.{item.Capability.Member.Name}",
                                    exception = (
                                        exception is TargetInvocationException
                                            && exception.InnerException is not null
                                            ? exception.InnerException.GetType().Name
                                            : exception.GetType().Name
                                    ),
                                },
                                retryable: true
                            );
                        }
                        if (item.ResultId.Length > 0)
                        {
                            rawResults[item.ResultId] = value;
                            returnedResults.Add(
                                new
                                {
                                    operation_id = item.OperationId,
                                    result_id = item.ResultId,
                                    result = MemberResultPayload(
                                        value,
                                        item.Capability.Member.ReturnType
                                    ),
                                }
                            );
                        }
                    }
                    if (mutating)
                    {
                        undoRecord!.EndCustomRecord();
                        undoStarted = false;
                        record.Version++;
                        InvalidateSelectionGrants(record.Id);
                        InvalidateRangeGrants(record.Id);
                        InvalidateUndoGrants(record.Id);
                    }
                    return new
                    {
                        live_document_id = record.Id,
                        live_version = record.Version,
                        preflight = new
                        {
                            valid = true,
                            registry_complete = CatalogComplete(catalog),
                            operation_count = prepared.Length,
                            mutating_count = prepared.Count(
                                item => item.Capability.Policy.Mutating
                            ),
                            read_count = prepared.Count(
                                item => !item.Capability.Policy.Mutating
                            ),
                            requires_expected_version = mutating,
                            operations = prepared
                                .Select(MemberOperationPayload)
                                .ToArray(),
                        },
                        executed_count = prepared.Length,
                        mutating,
                        results = returnedResults,
                        document = DocumentInfo(application, document),
                        source_access = source,
                        execution = new
                        {
                            catalog_capability_ids_only = true,
                            raw_member_names_accepted = false,
                            arbitrary_com_paths_allowed = false,
                            single_com_attachment = true,
                            single_undo_record = mutating,
                            rollback_on_error = mutating,
                            screen_updates_suspended = mutating
                                && optimizeScreenUpdates,
                        },
                        performance = Performance(started),
                    };
                }
                catch (Exception exception)
                {
                    if (mutating)
                    {
                        RollbackPreparedOperationsOrThrow(
                            document,
                            undoRecord,
                            ref undoStarted,
                            undoRecord is not null,
                            rollbackSnapshot!,
                            record,
                            exception
                        );
                    }
                    throw;
                }
                finally
                {
                    if (originalScreenUpdating is not null)
                    {
                        try
                        {
                            application.ScreenUpdating =
                                originalScreenUpdating.Value;
                        }
                        catch
                        {
                            // The original execution result remains authoritative.
                        }
                    }
                }
            },
            WordComReplaySafety.NonReplayable,
            cancellationToken
        );
    }

    private static object ResolveMemberTarget(
        dynamic application,
        dynamic document,
        PreparedCatalogOperation operation,
        IReadOnlyDictionary<string, object?> results
    )
    {
        return operation.TargetKind switch
        {
            "document" => (object)document,
            "document_content" => (object)document.Content,
            "selection" => ActiveSelection(application, document, range: false),
            "selection_range" => ActiveSelection(application, document, range: true),
            "result" => results.TryGetValue(
                operation.TargetResultId,
                out var result
            ) && result is not null
                ? result
                : throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "An earlier Word member operation returned no COM target",
                    new
                    {
                        operation_id = operation.OperationId,
                        result_id = operation.TargetResultId,
                    }
                ),
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "The prepared member target is not supported"
            ),
        };
    }

    private static object ActiveSelection(
        dynamic application,
        dynamic document,
        bool range
    )
    {
        RequireActive(application, document);
        return range ? (object)application.Selection.Range : (object)application.Selection;
    }

    private static object? ResolveMemberArgument(
        PreparedMemberArgument argument,
        IReadOnlyDictionary<string, object?> results
    )
    {
        return argument.Kind switch
        {
            "missing" => Missing.Value,
            "result" => results.TryGetValue(argument.ReferenceId, out var result)
                ? result
                : throw new NativeToolException(
                    "EXTERNAL_TOOL_FAILED",
                    "An earlier Word member result is unavailable",
                    new { result_id = argument.ReferenceId }
                ),
            _ => argument.Value,
        };
    }

    private static object? InvokeCatalogMember(
        object target,
        WordComMember member,
        object?[] arguments
    )
    {
        var flags =
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.OptionalParamBinding;
        flags |= member.Kind switch
        {
            "property_get" => BindingFlags.GetProperty,
            "property_put" or "property_put_ref" => BindingFlags.SetProperty,
            "method" => BindingFlags.InvokeMethod,
            _ => throw new NativeToolException(
                "INVALID_INPUT",
                "The catalog-backed member kind is not executable",
                new { member_kind = member.Kind }
            ),
        };
        return target.GetType().InvokeMember(
            member.Name,
            flags,
            binder: null,
            target,
            arguments,
            modifiers: null,
            culture: CultureInfo.InvariantCulture,
            namedParameters: null
        );
    }

    private static object MemberResultPayload(object? value, string declaredType)
    {
        if (
            value is null
            || value is bool
            || value is byte
            || value is sbyte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal
        )
        {
            return new
            {
                kind = "scalar",
                declared_type = declaredType,
                value,
                truncated = false,
            };
        }
        if (value is string text)
        {
            return new
            {
                kind = "text",
                declared_type = declaredType,
                value = text[..Math.Min(text.Length, 10_000)],
                truncated = text.Length > 10_000,
            };
        }
        if (value is Array array)
        {
            var values = new List<object?>();
            var truncated = array.Length > 100;
            foreach (var item in array.Cast<object?>().Take(100))
            {
                if (
                    item is null
                    || item is bool
                    || item is byte
                    || item is sbyte
                    || item is short
                    || item is ushort
                    || item is int
                    || item is uint
                    || item is long
                    || item is ulong
                    || item is float
                    || item is double
                    || item is decimal
                )
                {
                    values.Add(item);
                }
                else if (item is string itemText)
                {
                    values.Add(itemText[..Math.Min(itemText.Length, 1_000)]);
                    truncated |= itemText.Length > 1_000;
                }
                else
                {
                    truncated = true;
                }
            }
            return new
            {
                kind = "array",
                declared_type = declaredType,
                value = values,
                truncated,
            };
        }
        return new
        {
            kind = "com_object",
            declared_type = declaredType,
            runtime_dotnet_type = value.GetType().Name[..Math.Min(
                value.GetType().Name.Length,
                128
            )],
            value_returned = false,
            usable_by_result_id = true,
        };
    }

    private static object MemberOperationPayload(PreparedCatalogOperation item)
    {
        return new
        {
            operation_id = item.OperationId,
            capability_id = item.Capability.CapabilityId,
            member = $"{item.Capability.Type.Name}.{item.Capability.Member.Name}",
            member_kind = item.Capability.Member.Kind,
            target_kind = item.TargetKind,
            target_result_id = item.TargetResultId,
            argument_count = item.Arguments.Length,
            result_id = item.ResultId,
            return_type = item.Capability.Member.ReturnType,
            effect = item.Capability.Policy.Effect,
            execution = item.Capability.Policy.Execution,
            mutating = item.Capability.Policy.Mutating,
        };
    }

    private static bool CatalogComplete(WordObjectModelCatalog catalog)
    {
        return catalog.Capabilities.Length
                == catalog.Types.Sum(item => item.Members.Length)
            && catalog.CapabilitiesById.Count == catalog.Capabilities.Length;
    }

    private static object? JsonScalar(
        JsonElement value,
        int position,
        int argumentIndex
    )
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                {
                    var text = value.GetString() ?? "";
                    if (text.Length > 100_000)
                    {
                        throw new NativeToolException(
                            "LIMIT_EXCEEDED",
                            "A member-operation string argument exceeds 100,000 characters",
                            new { position, argument_index = argumentIndex }
                        );
                    }
                    return text;
                }
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer))
                {
                    return integer;
                }
                if (value.TryGetDouble(out var number) && double.IsFinite(number))
                {
                    return number;
                }
                break;
        }
        throw InvalidMemberArgument(
            "Nested or non-scalar member-operation arguments are not supported",
            position,
            argumentIndex,
            "scalar"
        );
    }

    private static void ValidatePrimitiveMemberArgument(
        object? value,
        string expectedType,
        HashSet<string> enumTypes,
        int position,
        int argumentIndex
    )
    {
        var baseType = expectedType
            .ToUpperInvariant()
            .TrimEnd('&', '*');
        var normalized = WordObjectModelCatalog.NormalizeComType(expectedType);
        if (value is null)
        {
            if (
                baseType
                    is "VARIANT" or "DISPATCH" or "UNKNOWN" or "EMPTY" or "NULL"
            )
            {
                return;
            }
            throw InvalidMemberArgument(
                "null is valid only for a VARIANT or dispatch-compatible parameter",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (baseType == "BSTR" && value is not string)
        {
            throw InvalidMemberArgument(
                "A Word BSTR parameter requires a string",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (baseType == "BOOL" && value is not bool)
        {
            throw InvalidMemberArgument(
                "A Word BOOL parameter requires true or false",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (
            IsIntegerComType(baseType)
            && value
                is not (
                    byte
                    or sbyte
                    or short
                    or ushort
                    or int
                    or uint
                    or long
                    or ulong
                )
        )
        {
            throw InvalidMemberArgument(
                "An integer Word parameter requires an integer",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (
            IsFloatingComType(baseType)
            && value
                is not (
                    byte
                    or sbyte
                    or short
                    or ushort
                    or int
                    or uint
                    or long
                    or ulong
                    or float
                    or double
                    or decimal
                )
        )
        {
            throw InvalidMemberArgument(
                "A numeric Word parameter requires a number",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (
            enumTypes.Contains(normalized)
            && value
                is not (
                    byte
                    or sbyte
                    or short
                    or ushort
                    or int
                    or uint
                    or long
                    or ulong
                )
        )
        {
            throw InvalidMemberArgument(
                "A Word enum parameter requires an integer or typed constant_id",
                position,
                argumentIndex,
                expectedType
            );
        }
        if (
            baseType is "DATE" or "FILETIME"
            && value
                is not (
                    string
                    or byte
                    or sbyte
                    or short
                    or ushort
                    or int
                    or uint
                    or long
                    or ulong
                    or float
                    or double
                    or decimal
                )
        )
        {
            throw InvalidMemberArgument(
                "A Word date parameter requires a number or string",
                position,
                argumentIndex,
                expectedType
            );
        }
        var knownScalar =
            baseType
                is "VARIANT"
                    or "EMPTY"
                    or "NULL"
                    or "BSTR"
                    or "BOOL"
                    or "DATE"
                    or "FILETIME"
            || IsIntegerComType(baseType)
            || IsFloatingComType(baseType)
            || enumTypes.Contains(normalized);
        if (!knownScalar)
        {
            throw InvalidMemberArgument(
                "A Word object parameter requires a typed earlier result_id",
                position,
                argumentIndex,
                expectedType
            );
        }
    }

    private static string RequireReferenceString(
        JsonElement value,
        string name,
        int position
    )
    {
        if (
            value.ValueKind != JsonValueKind.String
            || !MemberReferencePattern.IsMatch(value.GetString() ?? "")
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                $"{name} is invalid",
                new { position }
            );
        }
        return value.GetString()!;
    }

    private static bool IsOutOnly(WordComParameter parameter)
    {
        return (parameter.Flags & 2) != 0 && (parameter.Flags & 1) == 0;
    }

    private static bool IsVoidComType(string value)
    {
        return WordObjectModelCatalog.NormalizeComType(value)
            is "void" or "empty" or "null";
    }

    private static bool IntegerOrVariantComType(string value)
    {
        var normalized = value.ToUpperInvariant().TrimEnd('&', '*');
        return IsIntegerComType(normalized)
            || normalized is "VARIANT" or "EMPTY" or "NULL";
    }

    private static bool IsIntegerComType(string value)
    {
        return value
            is "I1" or "I2" or "I4" or "I8" or "INT" or "UI1" or "UI2"
                or "UI4" or "UI8" or "UINT";
    }

    private static bool IsFloatingComType(string value)
    {
        return value is "CY" or "DECIMAL" or "R4" or "R8";
    }

    private static NativeToolException InvalidMemberOperation(
        string message,
        int position
    )
    {
        return new NativeToolException(
            "INVALID_INPUT",
            message,
            new { position }
        );
    }

    private static NativeToolException InvalidMemberArgument(
        string message,
        int position,
        int argumentIndex,
        string expectedType,
        object? extra = null
    )
    {
        return new NativeToolException(
            "INVALID_INPUT",
            message,
            new
            {
                position,
                argument_index = argumentIndex,
                expected_type = expectedType,
                context = extra,
            }
        );
    }

    private sealed record PreparedMemberArgument(
        string Kind,
        object? Value,
        string ReferenceId
    );

    private sealed record PreparedCatalogOperation(
        string OperationId,
        WordMemberCapability Capability,
        string TargetKind,
        string TargetResultId,
        PreparedMemberArgument[] Arguments,
        string ResultId
    );
}
