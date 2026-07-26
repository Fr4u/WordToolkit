using System.Text.Json;
using System.Reflection;
using WordToolkit.Engine.Resources;
using WordToolkit.Native.Documents;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native;

internal static class Program
{
    private const string Usage =
        "usage: wordtoolkit-native [capabilities [--schema | [--query <text>] [--offset <n>] [--limit <n>]] [--format json] | extensions [--query <text>] [--offset <n>] [--limit <1..32>] [--format json] | libreoffice-backend --request <request.json|-> [--format json] | audit-log verify <path> [--max-bytes <n>] [--max-events <n>] [--format json] | inspect-package <path> [--include-details] [--max-items <1..200>] [--format json] | inspect-encryption <path> [--format json] | query-package --request <query.json|-> [--format json] | heading-outline-package --request <request.json|-> [--format json] | semantic-role-package --request <request.json|-> [--format json] | ocr-package --mode <inspect|recognize> --request <request.json|-> [--format json] | render-package --request <request.json|-> [--backend semantic-html|semantic-svg] [--format json] | fixed-render-package --request <request.json|-> [--format json] | style-package --mode <plan|apply> --request <request.json|-> [--format json] | template-style-alignment-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | numbering-repair-package --mode <plan|apply> --request <request.json|-> [--format json] | numbering-rebuild-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | note-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | equation-repair-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | equation-paragraph-rewrite-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | relationship-repair-package --mode <inspect|plan|apply> --request <request.json|-> [--format json] | comment-body-package --mode <plan|apply> --request <request.json|-> [--format json] | patch-rollback-package --mode <plan|apply> --request <request.json|-> [--format json] | transform-package <input> <output> --operation <name> [--find-text <text> --replace-text <text>] [--format json] | flat-opc-package <input> <output> --direction <to_flat_opc|from_flat_opc> [--format json] | docx-platform-adapter --protocol-version 1 --operation <operation.json> --input <input.docx> --output <output.docx> | --create-test-document <path> | --benchmark-active-word]";

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

        if (args.Length >= 1 && args[0] == "extensions")
        {
            return ExtensionCatalogCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "libreoffice-backend")
        {
            return await LibreOfficeBackendProbeCli.RunAsync(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "audit-log")
        {
            return AuditLogCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "inspect-package")
        {
            return InspectPackageCli.Run(args[1..], Console.Out, Console.Error);
        }

        if (args.Length >= 1 && args[0] == "inspect-encryption")
        {
            return InspectEncryptionCli.Run(args[1..], Console.Out, Console.Error);
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

        if (args.Length >= 1 && args[0] == "heading-outline-package")
        {
            return HeadingOutlinePackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "semantic-role-package")
        {
            return SemanticRolePackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "ocr-package")
        {
            return OcrPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "render-package")
        {
            return RenderPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "fixed-render-package")
        {
            return await FixedRenderPackageCli.RunAsync(
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

        if (args.Length >= 1 && args[0] == "template-style-alignment-package")
        {
            return TemplateStyleAlignmentPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "numbering-repair-package")
        {
            return NumberingRepairPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "numbering-rebuild-package")
        {
            return NumberingRebuildPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "note-package")
        {
            return NotePackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "equation-repair-package")
        {
            return EquationRepairPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "equation-paragraph-rewrite-package")
        {
            return EquationParagraphRewritePackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "relationship-repair-package")
        {
            return RelationshipRepairPackageCli.Run(
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

        if (args.Length >= 1 && args[0] == "patch-rollback-package")
        {
            return PatchRollbackPackageCli.Run(
                args[1..],
                Console.In,
                Console.Out,
                Console.Error
            );
        }

        if (args.Length >= 1 && args[0] == "flat-opc-package")
        {
            return FlatOpcPackageCli.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
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

        using var observabilityHost = NativeObservabilityHost.CreateFromEnvironment();
        await using var host = new WordComHost();
        var service = new WordLiveService(
            host,
            () => new WordOperationResourceLease(),
            observabilityHost.Observability
        );
        var server = new McpServer(
            Console.In,
            Console.Out,
            ToolCatalog.LoadNativeWordTools(),
            service,
            observability: observabilityHost.Observability
        );
        await server.RunAsync();
        return 0;
    }
}
