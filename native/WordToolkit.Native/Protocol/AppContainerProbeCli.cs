using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WordToolkit.Native.Protocol;

internal static class AppContainerProbeCli
{
    private const int MaximumRequestCharacters = 32 * 1024;
    private const int TokenIsAppContainer = 29;
    private static readonly IReadOnlySet<string> RequestProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "contract",
            "unbrokered_read_path",
            "unbrokered_write_path",
            "brokered_read_path",
            "brokered_write_path",
            "loopback_port",
        };

    internal static async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        TextWriter error
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (!OperatingSystem.IsWindows())
        {
            await error.WriteLineAsync("APPCONTAINER_UNAVAILABLE");
            return 69;
        }

        try
        {
            var request = ParseRequest(await ReadBoundedAsync(input));
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var value = 0;
            var size = Marshal.SizeOf<int>();
            if (!GetTokenInformation(
                identity.AccessToken.DangerousGetHandle(),
                TokenIsAppContainer,
                ref value,
                size,
                out var returned
            ) || returned != size)
            {
                await error.WriteLineAsync("APPCONTAINER_TOKEN_QUERY_FAILED");
                return 70;
            }

            var result = new
            {
                contract = "wordtoolkit.internal.appcontainer-probe/1.0",
                is_app_container = value != 0,
                unbrokered_read_succeeded = TryRead(request?.UnbrokeredReadPath),
                unbrokered_write_succeeded = TryWrite(request?.UnbrokeredWritePath),
                brokered_read_succeeded = TryRead(request?.BrokeredReadPath),
                brokered_write_succeeded = TryWrite(request?.BrokeredWritePath),
                loopback_connect_succeeded = await TryConnectAsync(request?.LoopbackPort),
            };
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonDefaults.Compact));
            return value != 0 ? 0 : 71;
        }
        catch
        {
            await error.WriteLineAsync("APPCONTAINER_PROBE_FAILED");
            return 70;
        }
    }

    private static ProbeRequest? ParseRequest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!RequestProperties.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new JsonException();
            }
        }
        var root = document.RootElement;
        if (!string.Equals(
            RequiredString(root, "contract"),
            "wordtoolkit.internal.appcontainer-probe-request/1.0",
            StringComparison.Ordinal
        ))
        {
            throw new JsonException();
        }
        var port = root.GetProperty("loopback_port").GetInt32();
        if (port is < 1 or > 65535)
        {
            throw new JsonException();
        }
        return new ProbeRequest(
            RequiredPath(root, "unbrokered_read_path"),
            RequiredPath(root, "unbrokered_write_path"),
            RequiredPath(root, "brokered_read_path"),
            RequiredPath(root, "brokered_write_path"),
            port
        );
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException();
        }
        return value.GetString() ?? throw new JsonException();
    }

    private static string RequiredPath(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        if (value.Length is < 1 or > 32_767 || !Path.IsPathFullyQualified(value))
        {
            throw new JsonException();
        }
        return Path.GetFullPath(value);
    }

    private static bool TryRead(string? path)
    {
        if (path is null)
        {
            return false;
        }
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan
            );
            return stream.ReadByte() >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWrite(string? path)
    {
        if (path is null)
        {
            return false;
        }
        try
        {
            File.WriteAllBytes(path, [0x57, 0x54]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryConnectAsync(int? port)
    {
        if (port is null)
        {
            return false;
        }
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync("127.0.0.1", port.Value, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ReadBoundedAsync(TextReader input)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return result.ToString();
            }
            if (result.Length > MaximumRequestCharacters - read)
            {
                throw new InvalidDataException();
            }
            result.Append(buffer, 0, read);
        }
    }

    private sealed record ProbeRequest(
        string UnbrokeredReadPath,
        string UnbrokeredWritePath,
        string BrokeredReadPath,
        string BrokeredWritePath,
        int LoopbackPort
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int informationClass,
        ref int information,
        int informationLength,
        out int returnLength
    );
}
