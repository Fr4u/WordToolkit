using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> InspectVersionProfileAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        RequireObject(arguments, "live Word version profile arguments");
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name != "live_document_id")
            {
                throw new NativeToolException(
                    "INVALID_INPUT",
                    "Unknown live Word version profile argument",
                    new { argument = property.Name }
                );
            }
        }

        var record = Record(arguments.String("live_document_id"));
        var started = Stopwatch.GetTimestamp();
        return await _host.InvokeAsync<object>(
            application =>
            {
                dynamic document = ResolveDocument(application, record);
                var issues = new List<string>();
                var applicationVersion = ReadVersionProfileString(
                    () => application.Version,
                    "APPLICATION_VERSION_PROBE_FAILED",
                    issues
                );
                var applicationBuild = ReadVersionProfileString(
                    () => application.Build,
                    "APPLICATION_BUILD_PROBE_FAILED",
                    issues
                );
                var majorVersion = ParseWordMajorVersion(applicationVersion);
                var compatibilityMode = ReadVersionProfileInteger(
                    () => document.CompatibilityMode,
                    "DOCUMENT_COMPATIBILITY_MODE_PROBE_FAILED",
                    issues
                );
                var saveFormat = ReadVersionProfileInteger(
                    () => document.SaveFormat,
                    "DOCUMENT_SAVE_FORMAT_PROBE_FAILED",
                    issues
                );

                var probes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["content_controls"] = ProbeVersionProfileMember(
                        () => document.ContentControls,
                        "CONTENT_CONTROLS_PROBE_FAILED",
                        issues
                    ),
                    ["native_omath"] = ProbeVersionProfileMember(
                        () => application.OMathAutoCorrect,
                        "NATIVE_OMATH_PROBE_FAILED",
                        issues
                    ),
                    ["smartart"] = ProbeVersionProfileMember(
                        () => application.SmartArtLayouts,
                        "SMARTART_PROBE_FAILED",
                        issues
                    ),
                    ["undo_record"] = ProbeVersionProfileMember(
                        () => application.UndoRecord,
                        "UNDO_RECORD_PROBE_FAILED",
                        issues
                    ),
                };

                var applicationProfile = new JsonObject
                {
                    ["version"] = applicationVersion,
                    ["build"] = applicationBuild,
                    ["major_version"] = majorVersion,
                    ["version_family"] = WordVersionFamily(majorVersion),
                    ["product_edition_inferred"] = false,
                };
                var documentProfile = new JsonObject
                {
                    ["compatibility_mode"] = compatibilityMode,
                    ["compatibility_profile"] = CompatibilityProfile(compatibilityMode),
                    ["legacy_feature_restrictions_documented"] =
                        LegacyRestrictionsDocumented(compatibilityMode),
                    ["save_format"] = saveFormat,
                };

                return new
                {
                    operation_contract = "wordtoolkit.inspect_live_word_version_profile/1.0",
                    live_document_id = record.Id,
                    live_version = record.Version,
                    backend = "microsoft_word_com",
                    application = applicationProfile,
                    document = documentProfile,
                    probes,
                    issues = issues.ToArray(),
                    interpretation = new
                    {
                        version_identity_is_feature_guarantee = false,
                        runtime_probe_scope = "property_access_only",
                        runtime_probe_result_is_feature_behavior_guarantee = false,
                    },
                    security = new
                    {
                        reads_document_content = false,
                        returns_document_content = false,
                        returns_paths = false,
                        returns_user_or_license_identity = false,
                        opens_word = false,
                        uses_network = false,
                    },
                    runtime = "dotnet-native",
                    python_used = false,
                    performance = Performance(started),
                };
            },
            WordComReplaySafety.ReplaySafe,
            cancellationToken
        );
    }

    private static string? ReadVersionProfileString(
        Func<object?> read,
        string failureCode,
        ICollection<string> issues
    )
    {
        try
        {
            var value = read();
            if (value is null)
            {
                return null;
            }
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (text is null || text.Length <= 64)
            {
                return text;
            }
            issues.Add(failureCode);
            return null;
        }
        catch
        {
            issues.Add(failureCode);
            return null;
        }
    }

    private static int? ReadVersionProfileInteger(
        Func<object?> read,
        string failureCode,
        ICollection<string> issues
    )
    {
        try
        {
            var value = read();
            return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            issues.Add(failureCode);
            return null;
        }
    }

    private static object ProbeVersionProfileMember(
        Func<object?> read,
        string failureCode,
        ICollection<string> issues
    )
    {
        try
        {
            var value = read();
            return new
            {
                status = value is null ? "unavailable" : "available",
                property_access_succeeded = true,
            };
        }
        catch
        {
            issues.Add(failureCode);
            return new { status = "probe_failed", property_access_succeeded = false };
        }
    }

    private static int? ParseWordMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }
        var separator = version.IndexOf('.');
        var major = separator < 0 ? version : version[..separator];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                && value >= 0
            ? value
            : null;
    }

    private static string WordVersionFamily(int? majorVersion) =>
        majorVersion switch
        {
            11 => "word_2003",
            12 => "word_2007",
            14 => "word_2010",
            15 => "word_2013",
            16 => "word_16_generation",
            _ => "unknown",
        };

    private static string CompatibilityProfile(int? compatibilityMode) =>
        compatibilityMode switch
        {
            11 => "word_2003",
            12 => "word_2007",
            14 => "word_2010",
            15 => "word_2013",
            65535 => "current",
            _ => "unknown",
        };

    private static bool? LegacyRestrictionsDocumented(int? compatibilityMode) =>
        compatibilityMode switch
        {
            11 or 12 or 14 => true,
            15 or 65535 => false,
            _ => null,
        };
}
