using System.Text.Json;
using System.Reflection;
using WordToolkit.Native.Documents;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native;

internal static class Program
{
    private const string Usage =
        "usage: wordtoolkit-native [capabilities [--schema | [--query <text>] [--offset <n>] [--limit <n>]] [--format json] | inspect-package <path> [--include-details] [--max-items <1..200>] [--format json] | query-package --request <query.json|-> [--format json] | style-package --mode <plan|apply> --request <request.json|-> [--format json] | comment-body-package --mode <plan|apply> --request <request.json|-> [--format json] | transform-package <input> <output> --operation <name> [--find-text <text> --replace-text <text>] [--format json] | docx-platform-adapter --protocol-version 1 --operation <operation.json> --input <input.docx> --output <output.docx> | --create-test-document <path> | --benchmark-active-word]";

    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 1 && args[0] == "--version")
        {
            Console.Out.WriteLine(
                Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? "unknown"
            );
            return 0;
        }

        if (args.Length == 2 && args[0] == "--create-test-document")
        {
            var result = NativeTestDocument.Create(Path.GetFullPath(args[1]));
            Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            return 0;
        }

        if (args.Length == 1 && args[0] == "--benchmark-active-word")
        {
            await using var benchmarkHost = new WordComHost();
            var benchmarkService = new WordLiveService(benchmarkHost);
            var result = await NativeBenchmark.RunAsync(benchmarkService);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            return result.Passed ? 0 : 2;
        }

        if (args.Length >= 1 && args[0] == "capabilities")
        {
            return CapabilityCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "inspect-package")
        {
            return InspectPackageCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "query-package")
        {
            return QueryPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "style-package")
        {
            return StylePackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "comment-body-package")
        {
            return CommentBodyPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "transform-package")
        {
            return TransformPackageCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "docx-platform-adapter")
        {
            return DocxPlatformTestAdapterCli.Run(
                args[1..],
                Console.Out,
                Console.Error
            );
        }

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(Usage);
            return 0;
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine(Usage);
            return 64;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        var server = new McpServer(
            Console.In,
            Console.Out,
            ToolCatalog.LoadNativeWordTools(),
            service
        );
        await server.RunAsync();
        return 0;
    }
}
