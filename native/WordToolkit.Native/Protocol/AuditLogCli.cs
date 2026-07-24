using System.Globalization;
using System.Text.Json;
using WordToolkit.Engine.Observability;

namespace WordToolkit.Native.Protocol;

internal static class AuditLogCli
{
    private const string Usage =
        "usage: wordtoolkit-native audit-log verify <path> [--max-bytes <1..268435456>] [--max-events <1..100000>] [--format json]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 2 || args[0] != "verify")
        {
            error.WriteLine(Usage);
            return 64;
        }
        var path = args[1];
        var maximumBytes = WordAuditJsonLinesContract.MaximumVerificationBytes;
        var maximumEvents = WordAuditJsonLinesContract.MaximumVerificationEvents;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--format" when index + 1 < args.Length && args[index + 1] == "json":
                    index++;
                    break;
                case "--max-bytes" when index + 1 < args.Length:
                    if (
                        !long.TryParse(
                            args[++index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out maximumBytes
                        )
                    )
                    {
                        error.WriteLine(Usage);
                        return 64;
                    }
                    break;
                case "--max-events" when index + 1 < args.Length:
                    if (
                        !int.TryParse(
                            args[++index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out maximumEvents
                        )
                    )
                    {
                        error.WriteLine(Usage);
                        return 64;
                    }
                    break;
                default:
                    error.WriteLine(Usage);
                    return 64;
            }
        }

        try
        {
            var result = WordAuditJsonLinesVerifier.Verify(
                path,
                maximumBytes,
                maximumEvents
            );
            output.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            return result.Valid ? 0 : 2;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FileNotFoundException
                or UnauthorizedAccessException
                or IOException
        )
        {
            error.WriteLine("The audit log could not be verified.");
            return 2;
        }
    }
}
