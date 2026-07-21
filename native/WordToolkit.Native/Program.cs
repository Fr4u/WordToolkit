using System.Text.Json;
using WordToolkit.Native.Documents;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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

        if (args.Length != 0)
        {
            Console.Error.WriteLine(
                "usage: wordtoolkit-native [--create-test-document <path> | --benchmark-active-word]"
            );
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
