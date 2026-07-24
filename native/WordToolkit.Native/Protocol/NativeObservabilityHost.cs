using WordToolkit.Engine.Observability;

namespace WordToolkit.Native.Protocol;

internal sealed class NativeObservabilityHost : IDisposable
{
    private readonly IDisposable? _ownedSink;

    public WordOperationObservability Observability { get; }

    private NativeObservabilityHost(
        WordOperationObservability observability,
        IDisposable? ownedSink
    )
    {
        Observability = observability;
        _ownedSink = ownedSink;
    }

    public static NativeObservabilityHost CreateFromEnvironment() => Create(
        Environment.GetEnvironmentVariable
    );

    internal static NativeObservabilityHost Create(Func<string, string?> readSetting)
    {
        ArgumentNullException.ThrowIfNull(readSetting);
        var telemetryEnabled = ReadBoolean(
            readSetting,
            "WORDTOOLKIT_TELEMETRY_ENABLED",
            defaultValue: false
        );
        var auditMode = ReadText(readSetting, "WORDTOOLKIT_AUDIT_MODE") ?? "off";
        if (auditMode is not ("off" or "memory" or "jsonl"))
        {
            throw InvalidConfiguration("WORDTOOLKIT_AUDIT_MODE");
        }
        var auditEnabled = auditMode != "off";
        var memoryCapacity = ReadInteger(
            readSetting,
            "WORDTOOLKIT_AUDIT_MEMORY_EVENTS",
            defaultValue: 256,
            WordOperationObservabilityContract.MinimumMemoryCapacity,
            WordOperationObservabilityContract.MaximumMemoryCapacity
        );
        var retentionDays = ReadInteger(
            readSetting,
            "WORDTOOLKIT_AUDIT_RETENTION_DAYS",
            defaultValue: 7,
            minimum: 1,
            maximum: 365
        );
        IWordAuditSink? sink = null;
        IDisposable? ownedSink = null;
        var directory = ReadText(readSetting, "WORDTOOLKIT_AUDIT_DIRECTORY");
        if (auditMode == "jsonl")
        {
            if (directory is null)
            {
                throw InvalidConfiguration("WORDTOOLKIT_AUDIT_DIRECTORY");
            }
            var maximumFileBytes = ReadLong(
                readSetting,
                "WORDTOOLKIT_AUDIT_MAX_FILE_BYTES",
                defaultValue: 4 * 1024 * 1024,
                WordAuditJsonLinesContract.MinimumFileBytes,
                WordAuditJsonLinesContract.MaximumFileBytes
            );
            var jsonLinesSink = new WordAuditJsonLinesSink(
                directory,
                retentionDays,
                maximumFileBytes
            );
            sink = jsonLinesSink;
            ownedSink = jsonLinesSink;
        }
        else if (directory is not null)
        {
            throw InvalidConfiguration("WORDTOOLKIT_AUDIT_DIRECTORY");
        }

        var observability = new WordOperationObservability(
            new WordOperationObservabilityOptions(
                TelemetryEnabled: telemetryEnabled,
                AuditEnabled: auditEnabled,
                MemoryCapacity: memoryCapacity,
                Retention: TimeSpan.FromDays(retentionDays),
                Sink: sink
            )
        );
        return new NativeObservabilityHost(observability, ownedSink);
    }

    public void Dispose()
    {
        Observability.Dispose();
        _ownedSink?.Dispose();
    }

    private static string? ReadText(Func<string, string?> readSetting, string name)
    {
        var value = readSetting(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ReadBoolean(
        Func<string, string?> readSetting,
        string name,
        bool defaultValue
    )
    {
        var value = ReadText(readSetting, name);
        if (value is null)
        {
            return defaultValue;
        }
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw InvalidConfiguration(name),
        };
    }

    private static int ReadInteger(
        Func<string, string?> readSetting,
        string name,
        int defaultValue,
        int minimum,
        int maximum
    )
    {
        var value = ReadText(readSetting, name);
        if (value is null)
        {
            return defaultValue;
        }
        if (
            int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed >= minimum
            && parsed <= maximum
        )
        {
            return parsed;
        }
        throw InvalidConfiguration(name);
    }

    private static long ReadLong(
        Func<string, string?> readSetting,
        string name,
        long defaultValue,
        long minimum,
        long maximum
    )
    {
        var value = ReadText(readSetting, name);
        if (value is null)
        {
            return defaultValue;
        }
        if (
            long.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed >= minimum
            && parsed <= maximum
        )
        {
            return parsed;
        }
        throw InvalidConfiguration(name);
    }

    private static InvalidOperationException InvalidConfiguration(string name) => new(
        $"WordToolkit observability configuration '{name}' is invalid."
    );
}
