using System.Text.Json;
using WordToolkit.Engine.Operations;

namespace WordToolkit.Native.Protocol;

/// <summary>
/// Direct adapter for open-agreements/docx-platform-tests protocol v1.
/// It deliberately receives only the neutral operation descriptor, never the
/// scenario assertions or expected output.
/// </summary>
internal static class DocxPlatformTestAdapterCli
{
    private const string Usage =
        "usage: wordtoolkit-native docx-platform-adapter --protocol-version 1 --operation <operation.json> --input <input.docx> --output <output.docx>";

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error
    )
    {
        if (arguments.Count == 1 && arguments[0] is "--help" or "-h")
        {
            output.WriteLine(Usage);
            return 0;
        }

        string? version = null;
        string? operationPath = null;
        string? inputPath = null;
        string? outputPath = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = arguments[index];
            if (!TryValue(arguments, ref index, out var value))
            {
                error.WriteLine("invalid adapter arguments");
                return 1;
            }
            switch (name)
            {
                case "--protocol-version":
                    version = value;
                    break;
                case "--operation":
                    operationPath = value;
                    break;
                case "--input":
                    inputPath = value;
                    break;
                case "--output":
                    outputPath = value;
                    break;
                default:
                    error.WriteLine("invalid adapter arguments");
                    return 1;
            }
        }

        if (!string.Equals(version, "1", StringComparison.Ordinal))
        {
            output.WriteLine("unsupported protocol version");
            return 3;
        }
        if (
            string.IsNullOrWhiteSpace(operationPath)
            || string.IsNullOrWhiteSpace(inputPath)
            || string.IsNullOrWhiteSpace(outputPath)
        )
        {
            error.WriteLine("missing required adapter argument");
            return 1;
        }

        try
        {
            using var descriptor = JsonDocument.Parse(
                File.ReadAllText(operationPath)
            );
            if (descriptor.RootElement.ValueKind != JsonValueKind.Object)
            {
                error.WriteLine("operation descriptor must be an object");
                return 1;
            }
            var operationName = RequiredString(descriptor.RootElement, "operationName");
            var request = operationName switch
            {
                "replaceFirstTextOccurrence" => new TransformWordPackageRequest(
                    inputPath,
                    outputPath,
                    WordPackageTransformKind.ReplaceFirstTextOccurrence,
                    RequiredString(descriptor.RootElement, "findText"),
                    RequiredString(descriptor.RootElement, "replaceText")
                ),
                "acceptAllTrackedChanges" => new TransformWordPackageRequest(
                    inputPath,
                    outputPath,
                    WordPackageTransformKind.AcceptAllTrackedChanges
                ),
                "rejectAllTrackedChanges" => new TransformWordPackageRequest(
                    inputPath,
                    outputPath,
                    WordPackageTransformKind.RejectAllTrackedChanges
                ),
                _ => throw new UnsupportedOperationException(operationName),
            };
            _ = new TransformWordPackageOperation().Execute(request);
            return 0;
        }
        catch (UnsupportedOperationException exception)
        {
            output.WriteLine($"unsupported operation: {Bound(exception.OperationName, 80)}");
            return 2;
        }
        catch (WordToolkitOperationException exception) when (
            exception.Code is "UNSUPPORTED_DOCUMENT" or "SIGNED_PACKAGE"
        )
        {
            output.WriteLine(
                "unsupported input: " + Bound(exception.Reason ?? exception.Message, 240)
            );
            return 2;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or WordToolkitOperationException
        )
        {
            error.WriteLine("adapter error: " + Bound(exception.Message, 240));
            return 1;
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (
            !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
        )
        {
            throw new JsonException($"{name} must be a string");
        }
        return value.GetString()!;
    }

    private static bool TryValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value
    )
    {
        if (index + 1 >= arguments.Count)
        {
            value = string.Empty;
            return false;
        }
        value = arguments[++index];
        return true;
    }

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    private sealed class UnsupportedOperationException(string operationName)
        : Exception
    {
        public string OperationName { get; } = operationName;
    }
}
