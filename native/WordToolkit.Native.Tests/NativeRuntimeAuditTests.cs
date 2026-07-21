namespace WordToolkit.Native.Tests;

public sealed class NativeRuntimeAuditTests
{
    [Fact]
    public void NativeProjectHasNoInterpreterOrShellLaunch()
    {
        var projectDirectory = FindProjectDirectory();
        var project = File.ReadAllText(
            Path.Combine(projectDirectory, "WordToolkit.Native.csproj")
        );
        Assert.DoesNotContain("<Exec", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pywin32", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pythonnet", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process.Start", ReadNativeSources(projectDirectory));
    }

    [Fact]
    public void PluginLaunchesOnlyTheNativeExecutable()
    {
        var projectDirectory = FindProjectDirectory();
        var repository = Directory.GetParent(
            Directory.GetParent(projectDirectory)!.FullName
        )!.FullName;
        var plugin = Path.Combine(repository, "plugin", "wordtoolkit");
        var configuration = File.ReadAllText(Path.Combine(plugin, ".mcp.json"));
        Assert.Contains(
            "./runtime/win-x64/wordtoolkit-native.exe",
            configuration,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\"command\": \"uv\"", configuration);
        Assert.DoesNotContain("python", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(plugin, "*", SearchOption.AllDirectories),
            path =>
                Path.GetExtension(path)
                    is ".py"
                        or ".pyc"
                        or ".pyo"
        );
    }

    private static string FindProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "native",
                "WordToolkit.Native",
                "WordToolkit.Native.csproj"
            );
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Native project root was not found");
    }

    private static string ReadNativeSources(string projectDirectory)
    {
        return string.Join(
            "\n",
            Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                ))
                .Select(File.ReadAllText)
        );
    }
}
