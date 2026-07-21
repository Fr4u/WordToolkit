using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Native.Word;

internal sealed record WordComParameter(
    string Name,
    string Type,
    int Flags,
    string[] FlagNames,
    bool Optional
);

internal sealed record WordComMember(
    string Name,
    string Kind,
    int MemberId,
    int DeclarationIndex,
    int FunctionKind,
    int InvokeKind,
    int CallConvention,
    int VtableOffset,
    WordComParameter[] Parameters,
    int ParameterCount,
    int OptionalParameterCount,
    bool Variadic,
    string ReturnType,
    int Flags,
    string[] FlagNames,
    object? ConstantValue = null,
    string ConstantType = ""
);

internal sealed record WordComImplementedType(
    string Name,
    string Kind,
    string Guid,
    int Flags,
    string[] FlagNames
);

internal sealed record WordComType(
    string Name,
    string Kind,
    int TypeIndex,
    string Guid,
    int Flags,
    int DeclaredFunctionCount,
    int DeclaredVariableCount,
    WordComImplementedType[] ImplementedTypes,
    WordComMember[] Members
);

internal sealed record WordComLibrary(
    string Guid,
    int Lcid,
    string SysKind,
    int MajorVersion,
    int MinorVersion,
    int Flags,
    int DeclaredTypeCount,
    int ApplicationTypeIndex
);

internal sealed record WordMemberPolicy(
    string Effect,
    string Execution,
    string Reason,
    bool Mutating
);

internal sealed record WordMemberCapability(
    string CapabilityId,
    string AccessorGroupId,
    string VirtualToolName,
    WordComType Type,
    WordComMember Member,
    string[] AllowedRoots,
    WordMemberPolicy Policy
);

internal sealed class WordObjectModelCatalog
{
    private const int MaxTypes = 2_000;
    private const int MaxMembersPerType = 2_000;
    private const int MaxTotalMembers = 50_000;
    private static readonly IReadOnlyDictionary<TYPEKIND, string> TypeKinds =
        new Dictionary<TYPEKIND, string>
        {
            [TYPEKIND.TKIND_ENUM] = "enum",
            [TYPEKIND.TKIND_RECORD] = "record",
            [TYPEKIND.TKIND_MODULE] = "module",
            [TYPEKIND.TKIND_INTERFACE] = "interface",
            [TYPEKIND.TKIND_DISPATCH] = "dispatch",
            [TYPEKIND.TKIND_COCLASS] = "coclass",
            [TYPEKIND.TKIND_ALIAS] = "alias",
            [TYPEKIND.TKIND_UNION] = "union",
        };
    private static readonly HashSet<string> BaseDispatchMembers = new(
        [
            "QueryInterface",
            "AddRef",
            "Release",
            "GetTypeInfoCount",
            "GetTypeInfo",
            "GetIDsOfNames",
            "Invoke",
        ],
        StringComparer.Ordinal
    );
    private static readonly string[] ParameterFlagNames =
        ["in", "out", "lcid", "retval", "optional", "has_default", "has_custom_data"];
    private static readonly int[] ParameterFlagValues = [1, 2, 4, 8, 16, 32, 64];
    private static readonly string[] FunctionFlagNames =
    [
        "restricted",
        "source",
        "bindable",
        "request_edit",
        "display_bind",
        "default_bind",
        "hidden",
        "uses_get_last_error",
        "default_collection_element",
        "ui_default",
        "non_browsable",
        "replaceable",
        "immediate_bind",
    ];
    private static readonly int[] FunctionFlagValues =
        [1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096];
    private static readonly string[] ImplementedTypeFlagNames =
        ["default", "source", "restricted", "default_vtable"];
    private static readonly int[] ImplementedTypeFlagValues = [1, 2, 4, 8];

    private WordObjectModelCatalog(
        DateTimeOffset generatedAt,
        WordComLibrary library,
        WordComType[] types,
        WordMemberCapability[] capabilities,
        int scanErrors,
        bool truncated,
        double scanDurationMs
    )
    {
        GeneratedAt = generatedAt;
        Library = library;
        Types = types;
        Capabilities = capabilities;
        ScanErrors = scanErrors;
        Truncated = truncated;
        ScanDurationMs = scanDurationMs;
        CapabilitiesById = capabilities.ToDictionary(
            item => item.CapabilityId,
            StringComparer.Ordinal
        );
        CapabilityStats = new
        {
            catalog_member_count = types.Sum(item => item.Members.Length),
            profile_count = capabilities.Length,
            unique_capability_id_count = CapabilitiesById.Count,
            virtual_tool_count = capabilities.Length,
            unique_virtual_tool_name_count = capabilities
                .Select(item => item.VirtualToolName)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            complete = types.Sum(item => item.Members.Length) == capabilities.Length
                && CapabilitiesById.Count == capabilities.Length,
            execution_counts = capabilities
                .GroupBy(item => item.Policy.Execution, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count()),
            effect_counts = capabilities
                .GroupBy(item => item.Policy.Effect, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count()),
            member_kind_counts = capabilities
                .GroupBy(item => item.Member.Kind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count()),
        };
    }

    public DateTimeOffset GeneratedAt { get; }
    public WordComLibrary Library { get; }
    public WordComType[] Types { get; }
    public WordMemberCapability[] Capabilities { get; }
    public IReadOnlyDictionary<string, WordMemberCapability> CapabilitiesById { get; }
    public int ScanErrors { get; }
    public bool Truncated { get; }
    public double ScanDurationMs { get; }
    public object CapabilityStats { get; }

    public static WordObjectModelCatalog Scan(dynamic application)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        ITypeInfo? applicationTypeInfo = null;
        ITypeLib? typeLibrary = null;
        IntPtr libraryAttributesPointer = IntPtr.Zero;
        var errors = 0;
        var truncated = false;
        try
        {
            var dispatch = (IDispatch)application;
            dispatch.GetTypeInfo(0, 0, out applicationTypeInfo);
            applicationTypeInfo.GetContainingTypeLib(out typeLibrary, out var applicationTypeIndex);
            typeLibrary.GetLibAttr(out libraryAttributesPointer);
            var libraryAttributes = Marshal.PtrToStructure<TYPELIBATTR>(
                libraryAttributesPointer
            );
            var declaredTypeCount = Math.Max(0, typeLibrary.GetTypeInfoCount());
            var scanTypeCount = Math.Min(declaredTypeCount, MaxTypes);
            truncated = declaredTypeCount > scanTypeCount;
            var types = new List<WordComType>(scanTypeCount);
            var totalMembers = 0;
            for (var typeIndex = 0; typeIndex < scanTypeCount; typeIndex++)
            {
                try
                {
                    typeLibrary.GetTypeInfo(typeIndex, out var typeInfo);
                    var type = ScanType(
                        typeInfo,
                        typeIndex,
                        Math.Min(
                            MaxMembersPerType,
                            Math.Max(0, MaxTotalMembers - totalMembers)
                        ),
                        ref errors,
                        ref truncated
                    );
                    totalMembers += type.Members.Length;
                    types.Add(type);
                    if (totalMembers >= MaxTotalMembers && typeIndex + 1 < scanTypeCount)
                    {
                        truncated = true;
                        break;
                    }
                }
                catch
                {
                    errors++;
                }
            }
            var orderedTypes = types
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TypeIndex)
                .ToArray();
            var library = new WordComLibrary(
                libraryAttributes.guid.ToString("D"),
                libraryAttributes.lcid,
                libraryAttributes.syskind.ToString(),
                libraryAttributes.wMajorVerNum,
                libraryAttributes.wMinorVerNum,
                (int)libraryAttributes.wLibFlags,
                declaredTypeCount,
                applicationTypeIndex
            );
            var capabilities = BuildCapabilities(library, orderedTypes);
            return new WordObjectModelCatalog(
                DateTimeOffset.UtcNow,
                library,
                orderedTypes,
                capabilities,
                errors,
                truncated,
                Math.Round(
                    System.Diagnostics.Stopwatch
                        .GetElapsedTime(started)
                        .TotalMilliseconds,
                    3
                )
            );
        }
        finally
        {
            if (libraryAttributesPointer != IntPtr.Zero && typeLibrary is not null)
            {
                typeLibrary.ReleaseTLibAttr(libraryAttributesPointer);
            }
            ReleaseComObject(typeLibrary);
            ReleaseComObject(applicationTypeInfo);
        }
    }

    private static WordComType ScanType(
        ITypeInfo typeInfo,
        int typeIndex,
        int memberLimit,
        ref int errors,
        ref bool truncated
    )
    {
        IntPtr attributesPointer = IntPtr.Zero;
        try
        {
            typeInfo.GetTypeAttr(out attributesPointer);
            var attributes = Marshal.PtrToStructure<TYPEATTR>(attributesPointer);
            var typeName = DocumentationName(typeInfo, -1, $"type_{typeIndex}");
            var typeKind = TypeKinds.GetValueOrDefault(
                attributes.typekind,
                $"typekind_{(int)attributes.typekind}"
            );
            var implementedTypes = new List<WordComImplementedType>();
            for (
                var implementedIndex = 0;
                implementedIndex < Math.Max(0, (int)attributes.cImplTypes)
                    && implementedIndex < 64;
                implementedIndex++
            )
            {
                try
                {
                    typeInfo.GetRefTypeOfImplType(implementedIndex, out var reference);
                    typeInfo.GetRefTypeInfo(reference, out var implementedInfo);
                    IntPtr implementedAttributesPointer = IntPtr.Zero;
                    try
                    {
                        implementedInfo.GetTypeAttr(out implementedAttributesPointer);
                        var implementedAttributes =
                            Marshal.PtrToStructure<TYPEATTR>(
                                implementedAttributesPointer
                            );
                        typeInfo.GetImplTypeFlags(
                            implementedIndex,
                            out var implementedFlags
                        );
                        implementedTypes.Add(
                            new WordComImplementedType(
                                DocumentationName(
                                    implementedInfo,
                                    -1,
                                    $"implemented_type_{implementedIndex}"
                                ),
                                TypeKinds.GetValueOrDefault(
                                    implementedAttributes.typekind,
                                    $"typekind_{(int)implementedAttributes.typekind}"
                                ),
                                implementedAttributes.guid.ToString("D"),
                                (int)implementedFlags,
                                FlagNames(
                                    (int)implementedFlags,
                                    ImplementedTypeFlagValues,
                                    ImplementedTypeFlagNames
                                )
                            )
                        );
                    }
                    finally
                    {
                        if (implementedAttributesPointer != IntPtr.Zero)
                        {
                            implementedInfo.ReleaseTypeAttr(
                                implementedAttributesPointer
                            );
                        }
                        ReleaseComObject(implementedInfo);
                    }
                }
                catch
                {
                    errors++;
                }
            }

            var members = new List<WordComMember>();
            var functionCount = Math.Max(0, (int)attributes.cFuncs);
            var variableCount = Math.Max(0, (int)attributes.cVars);
            for (
                var functionIndex = 0;
                functionIndex < functionCount && members.Count < memberLimit;
                functionIndex++
            )
            {
                try
                {
                    var member = ScanFunction(typeInfo, functionIndex);
                    if (member is not null)
                    {
                        members.Add(member);
                    }
                }
                catch
                {
                    errors++;
                }
            }
            for (
                var variableIndex = 0;
                variableIndex < variableCount && members.Count < memberLimit;
                variableIndex++
            )
            {
                try
                {
                    members.Add(
                        ScanVariable(
                            typeInfo,
                            variableIndex,
                            attributes.typekind == TYPEKIND.TKIND_ENUM
                        )
                    );
                }
                catch
                {
                    errors++;
                }
            }
            if (functionCount + variableCount > memberLimit)
            {
                truncated = true;
            }
            return new WordComType(
                typeName,
                typeKind,
                typeIndex,
                attributes.guid.ToString("D"),
                (int)attributes.wTypeFlags,
                functionCount,
                variableCount,
                implementedTypes.ToArray(),
                members.ToArray()
            );
        }
        finally
        {
            if (attributesPointer != IntPtr.Zero)
            {
                typeInfo.ReleaseTypeAttr(attributesPointer);
            }
            ReleaseComObject(typeInfo);
        }
    }

    private static WordComMember? ScanFunction(ITypeInfo typeInfo, int index)
    {
        IntPtr descriptorPointer = IntPtr.Zero;
        try
        {
            typeInfo.GetFuncDesc(index, out descriptorPointer);
            var descriptor = Marshal.PtrToStructure<FUNCDESC>(descriptorPointer);
            var names = GetNames(
                typeInfo,
                descriptor.memid,
                Math.Min(256, Math.Max(1, descriptor.cParams + 1))
            );
            var name = BoundedName(
                names.Length > 0 ? names[0] : "",
                $"member_{descriptor.memid}"
            );
            if (BaseDispatchMembers.Contains(name))
            {
                return null;
            }
            var parameterCount = Math.Max(0, (int)descriptor.cParams);
            var optionalCount = descriptor.cParamsOpt == -1
                ? -1
                : Math.Clamp(descriptor.cParamsOpt, 0, parameterCount);
            var parameters = new List<WordComParameter>(parameterCount);
            var elementSize = Marshal.SizeOf<ELEMDESC>();
            for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
            {
                var parameterPointer = IntPtr.Add(
                    descriptor.lprgelemdescParam,
                    parameterIndex * elementSize
                );
                var element = Marshal.PtrToStructure<ELEMDESC>(parameterPointer);
                var flags = (int)element.desc.paramdesc.wParamFlags;
                var optionalByPosition =
                    optionalCount > 0
                    && parameterIndex >= parameterCount - optionalCount;
                parameters.Add(
                    new WordComParameter(
                        BoundedName(
                            names.Length > parameterIndex + 1
                                ? names[parameterIndex + 1]
                                : "",
                            $"arg{parameterIndex + 1}"
                        ),
                        TypeDescription(typeInfo, element.tdesc),
                        flags,
                        FlagNames(
                            flags,
                            ParameterFlagValues,
                            ParameterFlagNames
                        ),
                        (flags & 16) != 0 || optionalByPosition
                    )
                );
            }
            var invokeKind = (int)descriptor.invkind;
            var memberKind = invokeKind switch
            {
                1 => "method",
                2 => "property_get",
                4 => "property_put",
                8 => "property_put_ref",
                _ => $"invoke_{invokeKind}",
            };
            var functionFlags = (int)descriptor.wFuncFlags;
            return new WordComMember(
                name,
                memberKind,
                descriptor.memid,
                index,
                (int)descriptor.funckind,
                invokeKind,
                (int)descriptor.callconv,
                descriptor.oVft,
                parameters.ToArray(),
                parameterCount,
                optionalCount,
                descriptor.cParamsOpt == -1,
                TypeDescription(typeInfo, descriptor.elemdescFunc.tdesc),
                functionFlags,
                FlagNames(functionFlags, FunctionFlagValues, FunctionFlagNames)
            );
        }
        finally
        {
            if (descriptorPointer != IntPtr.Zero)
            {
                typeInfo.ReleaseFuncDesc(descriptorPointer);
            }
        }
    }

    private static WordComMember ScanVariable(
        ITypeInfo typeInfo,
        int index,
        bool enumType
    )
    {
        IntPtr descriptorPointer = IntPtr.Zero;
        try
        {
            typeInfo.GetVarDesc(index, out descriptorPointer);
            var descriptor = Marshal.PtrToStructure<VARDESC>(descriptorPointer);
            var names = GetNames(typeInfo, descriptor.memid, 1);
            object? constant = null;
            if (
                enumType
                && descriptor.varkind == VARKIND.VAR_CONST
                && descriptor.desc.lpvarValue != IntPtr.Zero
            )
            {
                try
                {
                    constant = Marshal.GetObjectForNativeVariant(
                        descriptor.desc.lpvarValue
                    );
                    if (
                        constant is not null
                        && constant is not (bool or byte or sbyte or short or ushort
                            or int or uint or long or ulong or float or double
                            or decimal or string)
                    )
                    {
                        constant = null;
                    }
                }
                catch
                {
                    constant = null;
                }
            }
            var type = TypeDescription(typeInfo, descriptor.elemdescVar.tdesc);
            return new WordComMember(
                BoundedName(
                    names.Length > 0 ? names[0] : "",
                    $"member_{descriptor.memid}"
                ),
                enumType ? "enum_value" : "variable",
                descriptor.memid,
                index,
                0,
                0,
                0,
                0,
                [],
                0,
                0,
                false,
                type,
                (int)descriptor.wVarFlags,
                [],
                constant,
                type
            );
        }
        finally
        {
            if (descriptorPointer != IntPtr.Zero)
            {
                typeInfo.ReleaseVarDesc(descriptorPointer);
            }
        }
    }

    private static WordMemberCapability[] BuildCapabilities(
        WordComLibrary library,
        WordComType[] types
    )
    {
        var capabilities = new List<WordMemberCapability>();
        foreach (var type in types)
        {
            foreach (var member in type.Members)
            {
                var capabilityId = StableId(
                    "wmc1",
                    library.Guid,
                    library.MajorVersion.ToString(CultureInfo.InvariantCulture),
                    library.MinorVersion.ToString(CultureInfo.InvariantCulture),
                    type.Guid,
                    type.TypeIndex.ToString(CultureInfo.InvariantCulture),
                    type.Name,
                    member.MemberId.ToString(CultureInfo.InvariantCulture),
                    member.DeclarationIndex.ToString(CultureInfo.InvariantCulture),
                    member.Kind,
                    member.Name
                );
                var accessorId = StableId(
                    "wma1",
                    library.Guid,
                    type.Guid,
                    type.TypeIndex.ToString(CultureInfo.InvariantCulture),
                    member.MemberId.ToString(CultureInfo.InvariantCulture),
                    member.Name
                );
                var effect = ClassifyEffect(type.Name, member.Name, member.Kind);
                var policy = ClassifyPolicy(type.Name, member, effect);
                var virtualToolName = string.Join(
                    "_",
                    "wm",
                    SafeSegment(type.Name, 24),
                    SafeSegment(member.Name, 32),
                    SafeSegment(member.Kind, 16),
                    capabilityId[^12..]
                );
                capabilities.Add(
                    new WordMemberCapability(
                        capabilityId,
                        accessorId,
                        virtualToolName,
                        type,
                        member,
                        AllowedRoots(type.Name),
                        policy
                    )
                );
            }
        }
        return capabilities
            .OrderBy(item => item.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Member.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Member.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Member.DeclarationIndex)
            .ToArray();
    }

    private static string TypeDescription(ITypeInfo typeInfo, TYPEDESC descriptor)
    {
        return TypeDescription(typeInfo, descriptor, 0);
    }

    private static string TypeDescription(
        ITypeInfo typeInfo,
        TYPEDESC descriptor,
        int depth
    )
    {
        if (depth >= 8)
        {
            return "UNKNOWN";
        }
        var raw = (int)descriptor.vt;
        var byReference = (raw & 0x4000) != 0;
        var isArray = (raw & 0x2000) != 0;
        var baseType = (VarEnum)(raw & 0x0FFF);
        string label;
        if (baseType == VarEnum.VT_USERDEFINED)
        {
            try
            {
                typeInfo.GetRefTypeInfo(descriptor.lpValue.ToInt32(), out var referenced);
                try
                {
                    label = DocumentationName(
                        referenced,
                        -1,
                        $"USERDEFINED({descriptor.lpValue.ToInt64()})"
                    );
                }
                finally
                {
                    ReleaseComObject(referenced);
                }
            }
            catch
            {
                label = $"USERDEFINED({descriptor.lpValue.ToInt64()})";
            }
        }
        else if (
            baseType is VarEnum.VT_PTR or VarEnum.VT_SAFEARRAY or VarEnum.VT_CARRAY
            && descriptor.lpValue != IntPtr.Zero
        )
        {
            var nested = Marshal.PtrToStructure<TYPEDESC>(descriptor.lpValue);
            var nestedName = TypeDescription(typeInfo, nested, depth + 1);
            label = baseType switch
            {
                VarEnum.VT_PTR => $"{nestedName}*",
                VarEnum.VT_SAFEARRAY => $"SAFEARRAY({nestedName})",
                _ => $"CARRAY({nestedName})",
            };
        }
        else
        {
            label = baseType switch
            {
                VarEnum.VT_EMPTY => "EMPTY",
                VarEnum.VT_NULL => "NULL",
                VarEnum.VT_I2 => "I2",
                VarEnum.VT_I4 => "I4",
                VarEnum.VT_R4 => "R4",
                VarEnum.VT_R8 => "R8",
                VarEnum.VT_CY => "CY",
                VarEnum.VT_DATE => "DATE",
                VarEnum.VT_BSTR => "BSTR",
                VarEnum.VT_DISPATCH => "DISPATCH",
                VarEnum.VT_ERROR => "ERROR",
                VarEnum.VT_BOOL => "BOOL",
                VarEnum.VT_VARIANT => "VARIANT",
                VarEnum.VT_UNKNOWN => "UNKNOWN",
                VarEnum.VT_DECIMAL => "DECIMAL",
                VarEnum.VT_I1 => "I1",
                VarEnum.VT_UI1 => "UI1",
                VarEnum.VT_UI2 => "UI2",
                VarEnum.VT_UI4 => "UI4",
                VarEnum.VT_I8 => "I8",
                VarEnum.VT_UI8 => "UI8",
                VarEnum.VT_INT => "INT",
                VarEnum.VT_UINT => "UINT",
                VarEnum.VT_VOID => "VOID",
                VarEnum.VT_HRESULT => "HRESULT",
                VarEnum.VT_LPSTR => "LPSTR",
                VarEnum.VT_LPWSTR => "LPWSTR",
                VarEnum.VT_RECORD => "RECORD",
                _ => $"VT_{(int)baseType}",
            };
        }
        if (isArray && !label.StartsWith("SAFEARRAY(", StringComparison.Ordinal))
        {
            label = $"SAFEARRAY({label})";
        }
        if (byReference)
        {
            label += "&";
        }
        return label[..Math.Min(label.Length, 256)];
    }

    private static string[] GetNames(ITypeInfo typeInfo, int memberId, int count)
    {
        var names = new string[Math.Clamp(count, 1, 256)];
        typeInfo.GetNames(memberId, names, names.Length, out var returned);
        return names
            .Take(Math.Clamp(returned, 0, names.Length))
            .Select((item, index) => BoundedName(item, $"arg{index}"))
            .ToArray();
    }

    private static string DocumentationName(
        ITypeInfo typeInfo,
        int memberId,
        string fallback
    )
    {
        typeInfo.GetDocumentation(
            memberId,
            out var name,
            out _,
            out _,
            out _
        );
        return BoundedName(name, fallback);
    }

    private static string BoundedName(string? value, string fallback)
    {
        var text = string.Join(
            " ",
            (value ?? "")
                .Select(character => char.IsControl(character) ? ' ' : character)
                .ToArray()
                .AsSpan()
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
        text = text[..Math.Min(text.Length, 256)];
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    private static string[] FlagNames(
        int value,
        int[] flagValues,
        string[] flagNames
    )
    {
        return flagValues
            .Select((flag, index) => (flag, index))
            .Where(item => (value & item.flag) != 0)
            .Select(item => flagNames[item.index])
            .ToArray();
    }

    private static string StableId(string prefix, params string[] parts)
    {
        var payload = string.Join("\0", parts);
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))
        ).ToLowerInvariant();
        return $"{prefix}_{digest[..32]}";
    }

    private static string SafeSegment(string value, int limit)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
            if (builder.Length >= limit)
            {
                break;
            }
        }
        var result = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "member" : result;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }
        try
        {
            _ = Marshal.ReleaseComObject(value);
        }
        catch
        {
            // Catalog scanning must not hide the original type-library result.
        }
    }

    internal static string NormalizeComType(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal);
        while (normalized.EndsWith('*') || normalized.EndsWith('&'))
        {
            normalized = normalized[..^1];
        }
        return normalized.TrimStart('_');
    }

    internal static bool CompatibleComType(string actual, string expected)
    {
        var normalizedActual = NormalizeComType(actual);
        var normalizedExpected = NormalizeComType(expected);
        if (normalizedActual is "dispatch" or "unknown" or "variant")
        {
            return false;
        }
        return normalizedExpected is "dispatch" or "unknown" or "variant"
            || normalizedActual == normalizedExpected;
    }

    private static string[] AllowedRoots(string typeName)
    {
        return NormalizeComType(typeName) switch
        {
            "document" => ["document", "result"],
            "selection" => ["selection", "result"],
            "range" => ["document_content", "selection_range", "result"],
            _ => ["result"],
        };
    }

    private static string ClassifyEffect(
        string typeName,
        string memberName,
        string memberKind
    )
    {
        var type = typeName.ToLowerInvariant();
        var name = memberName.ToLowerInvariant();
        if (memberKind == "enum_value")
        {
            return "constant";
        }
        if (type.Contains("events", StringComparison.Ordinal))
        {
            return "event";
        }
        if (LifecycleNames.Contains(name))
        {
            return "lifecycle";
        }
        if (ContainsAny(name, ExternalMarkers))
        {
            return "external";
        }
        if (ContainsAny(name, FormatMarkers))
        {
            return "format";
        }
        if (ContainsAny(name, CalculationMarkers))
        {
            return "calculation";
        }
        if (ContainsAny(name, ContentMarkers))
        {
            return "content";
        }
        if (ContainsAny(name, StructureMarkers))
        {
            return "structure";
        }
        if (ContainsAny(name, ViewMarkers))
        {
            return "view";
        }
        return memberKind is "property_get" or "variable" ? "read" : "unknown";
    }

    private static WordMemberPolicy ClassifyPolicy(
        string typeName,
        WordComMember member,
        string effect
    )
    {
        var type = NormalizeComType(typeName);
        var name = member.Name.ToLowerInvariant();
        if (member.Kind == "enum_value")
        {
            return new("constant", "metadata_only", "enum_constant_has_no_runtime_target", false);
        }
        if (member.Kind == "variable")
        {
            return new(effect, "metadata_only", "type_library_variable_is_metadata_only", false);
        }
        if (effect == "event" || member.FlagNames.Contains("source", StringComparer.Ordinal))
        {
            return new(effect, "blocked", "event_callback_is_not_an_invocable_edit", false);
        }
        if (member.FlagNames.Contains("restricted", StringComparer.Ordinal))
        {
            return new(effect, "blocked", "type_library_marks_member_restricted", false);
        }
        if (ExternalTypes.Contains(type))
        {
            return new(effect, "blocked", "external_type_mutation_is_out_of_scope", false);
        }
        if (effect == "lifecycle")
        {
            return new(effect, "blocked", "document_or_application_lifecycle_is_out_of_scope", false);
        }
        if (effect == "external")
        {
            return new(effect, "blocked", "external_side_effect_is_out_of_scope", false);
        }
        if (member.Kind == "property_get")
        {
            return SensitiveNames.Contains(name)
                ? new(effect, "blocked", "sensitive_or_external_metadata_is_not_returned", false)
                : new(effect, "read_allowed", "bounded_property_read", false);
        }
        if (member.Kind is "property_put" or "property_put_ref")
        {
            if (ApplicationTypes.Contains(type))
            {
                return new(effect, "blocked", "application_global_mutation_is_out_of_scope", false);
            }
            if (SensitiveNames.Contains(name))
            {
                return new(effect, "blocked", "sensitive_or_external_setting_is_out_of_scope", false);
            }
            if (member.ParameterCount != 1)
            {
                return new(effect, "blocked", "indexed_property_setter_is_not_verified_undoable", false);
            }
            if (!PrimitiveSetterType(member.Parameters[0].Type))
            {
                return new(effect, "blocked", "object_property_setter_is_not_verified_undoable", false);
            }
            return new(effect, "write_allowed", "document_scoped_property_write", true);
        }
        if (member.Kind == "method")
        {
            if (ApplicationTypes.Contains(type))
            {
                return new(effect, "blocked", "application_global_mutation_is_out_of_scope", false);
            }
            if (type == "document" && name == "range")
            {
                return new(
                    "read",
                    "read_allowed",
                    "bounded_document_range_factory",
                    false
                );
            }
            if (type == "range" && name == "select" && member.ParameterCount == 0)
            {
                return new(
                    "view",
                    "write_allowed",
                    "bounded_document_range_selection",
                    false
                );
            }
            if (ReadPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return new(effect, "read_allowed", "bounded_method_read", false);
            }
            if (effect == "view")
            {
                return new(effect, "blocked", "view_state_action_requires_a_dedicated_tool", false);
            }
            if (effect == "unknown")
            {
                return new(effect, "blocked", "method_effect_has_not_been_proven_document_scoped", false);
            }
            return new(effect, "write_allowed", "document_scoped_method_call", true);
        }
        return new(effect, "blocked", "unsupported_invocation_kind", false);
    }

    private static bool PrimitiveSetterType(string type)
    {
        var normalized = type.ToUpperInvariant().TrimEnd('*', '&');
        return normalized is
            "BSTR" or "BOOL" or "I1" or "I2" or "I4" or "I8" or "INT"
            or "UI1" or "UI2" or "UI4" or "UI8" or "UINT" or "R4" or "R8"
            or "CY" or "DECIMAL" or "DATE" or "VARIANT"
            || normalized.StartsWith("WD", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string value, string[] markers)
    {
        return markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
    }

    private static readonly HashSet<string> SensitiveNames = new(
        [
            "address",
            "code",
            "connection",
            "fullname",
            "hyperlink",
            "name",
            "path",
            "password",
            "sourcefullname",
            "subaddress",
            "vbproject",
        ],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> LifecycleNames = new(
        [
            "addblogdocument",
            "addold",
            "changefileopendirectory",
            "close",
            "newwindow",
            "open",
            "openandrepair",
            "quit",
            "save",
            "saveas",
            "saveas2",
            "savecopyas",
        ],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ApplicationTypes = new(
        [
            "application",
            "global",
            "options",
            "autocorrect",
            "addins",
            "addins2",
            "dictionaries",
            "fileconverters",
            "keybinding",
            "keybindings",
            "languages",
            "recentfiles",
            "system",
            "task",
            "tasks",
            "templates",
        ],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ExternalTypes = new(
        ["source", "xmlnamespace", "xsltransform"],
        StringComparer.Ordinal
    );
    private static readonly string[] ExternalMarkers =
    [
        "addins", "broadcast", "checkin", "checkout", "dde", "download",
        "email", "export", "fax", "fileconverter", "filedialog",
        "followhyperlink", "import", "mail", "macro", "ole", "organizer",
        "print", "route", "run", "send", "upload", "web",
    ];
    private static readonly string[] FormatMarkers =
    [
        "align", "autofit", "bold", "border", "color", "font", "format",
        "height", "indent", "italic", "layout", "margin", "orientation",
        "position", "shading", "size", "spacing", "style", "underline", "width",
    ];
    private static readonly string[] ContentMarkers =
        ["append", "caption", "copy", "cut", "insert", "paste", "replace", "text", "type"];
    private static readonly string[] StructureMarkers =
    [
        "accept", "add", "apply", "bookmark", "build", "collapse", "delete",
        "field", "list", "merge", "move", "paragraph", "reject", "row",
        "section", "setrange", "sort", "split", "table",
    ];
    private static readonly string[] CalculationMarkers =
        ["builddown", "buildup", "calculate", "compute", "formula", "statistic", "update"];
    private static readonly string[] ViewMarkers =
        ["activate", "arrange", "display", "scroll", "select", "show", "view", "window", "zoom"];
    private static readonly string[] ReadPrefixes =
        ["_default", "_newenum", "can", "compare", "compute", "count", "get", "has", "information", "is", "item"];
}

[ComImport]
[Guid("00020400-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDispatch
{
    [PreserveSig]
    int GetTypeInfoCount(out uint count);

    void GetTypeInfo(uint typeInfoIndex, int localeId, out ITypeInfo typeInfo);

    void GetIDsOfNames(
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)]
            string[] names,
        uint nameCount,
        int localeId,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] int[] dispatchIds
    );

    void Invoke(
        int dispatchId,
        ref Guid interfaceId,
        int localeId,
        short flags,
        IntPtr parameters,
        IntPtr result,
        IntPtr exceptionInfo,
        IntPtr argumentError
    );
}
