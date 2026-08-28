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
        var isolatedWorkerPath = Path.Combine(
            projectDirectory,
            "Word",
            "EquationPreflightProcessRunner.cs"
        );
        var isolatedWorker = File.ReadAllText(isolatedWorkerPath);
        Assert.DoesNotContain(
            "Process.Start",
            ReadNativeSources(projectDirectory, isolatedWorkerPath)
        );
        Assert.Equal(
            1,
            CountOccurrences(isolatedWorker, "Process.Start")
        );
        Assert.Contains("UseShellExecute = false", isolatedWorker, StringComparison.Ordinal);
        Assert.Contains("CreateNoWindow = true", isolatedWorker, StringComparison.Ordinal);
        Assert.Contains("ResolveExecutablePath()", isolatedWorker, StringComparison.Ordinal);
        Assert.Contains(
            "wordtoolkit-native.exe",
            isolatedWorker,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "--internal-equation-preflight-worker",
            isolatedWorker,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("cmd.exe", isolatedWorker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", isolatedWorker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("python.exe", isolatedWorker, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("python3", isolatedWorker, StringComparison.OrdinalIgnoreCase);
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

    private static string ReadNativeSources(
        string projectDirectory,
        string? excludedPath = null
    )
    {
        return string.Join(
            "\n",
            Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                ))
                .Where(path => excludedPath is null || !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(excludedPath),
                    StringComparison.OrdinalIgnoreCase
                ))
                .Select(File.ReadAllText)
        );
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
    }
}
