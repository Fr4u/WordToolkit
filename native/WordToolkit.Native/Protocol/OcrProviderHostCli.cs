using System.ComponentModel;
using System.Text;
using WordToolkit.Engine.Extensions;
using WordToolkit.Native.Ocr;

namespace WordToolkit.Native.Protocol;

internal static class OcrProviderHostCli
{
    internal static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IWordOcrProvider? provider = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length != 0)
        {
            await error.WriteLineAsync("The internal OCR provider host accepts no arguments.");
            return 64;
        }

        var requestId = new string('0', 32);
        try
        {
            var json = await ReadBoundedAsync(
                input,
                OcrProviderHostProtocol.MaximumRequestCharacters,
                cancellationToken
            );
            var request = OcrProviderHostProtocol.ParseRequest(json);
            requestId = request.RequestId;
            var currentIdentity = OcrProviderHostIdentityResolver.Current(cancellationToken);
            if (
                !string.Equals(
                    request.HostExecutableSha256,
                    currentIdentity.ExecutableSha256,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    request.HostAssemblySha256,
                    currentIdentity.AssemblySha256,
                    StringComparison.Ordinal
                )
            )
            {
                throw new WordToolkitExtensionException(
                    "EXTENSION_IDENTITY_MISMATCH",
                    "The OCR process host does not match the bound executable identity."
                );
            }

            var result = (provider ?? new TesseractCliOcrProvider(
                request.TrustBinding,
                resourcesVerifiedByParent: true
            )).Recognize(
                request.ToProviderRequest(),
                cancellationToken
            );
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeSuccess(requestId, result)
            );
            return 0;
        }
        catch (WordToolkitExtensionException exception)
        {
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeError(
                    requestId,
                    exception.Code,
                    exception.Retryable
                )
            );
            return exception.Code == "EXTENSION_PROTOCOL_VIOLATION" ? 64 : 70;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeError(
                    requestId,
                    "EXTENSION_TIMEOUT",
                    retryable: true
                )
            );
            return 70;
        }
        catch (UnauthorizedAccessException)
        {
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeError(
                    requestId,
                    "OCR_PROVIDER_ACCESS_DENIED",
                    retryable: false
                )
            );
            return 70;
        }
        catch (Win32Exception)
        {
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeError(
                    requestId,
                    "OCR_PROVIDER_START_FAILED",
                    retryable: false
                )
            );
            return 70;
        }
        catch
        {
            await output.WriteLineAsync(
                OcrProviderHostProtocol.SerializeError(
                    requestId,
                    "EXTENSION_EXECUTION_FAILED",
                    retryable: false
                )
            );
            return 70;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        TextReader input,
        int maximumCharacters,
        CancellationToken cancellationToken
    )
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 64 * 1024));
        var buffer = new char[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (result.Length > maximumCharacters - read)
            {
                throw new WordToolkitExtensionException(
                    "EXTENSION_LIMIT_EXCEEDED",
                    "The OCR process-host request exceeded its IPC limit."
                );
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
}
