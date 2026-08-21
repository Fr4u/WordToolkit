using System.Text.Json;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private async Task<object> InspectLibreOfficeBackendAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var request = LibreOfficeBackendProbeOperationJson.ParseRequest(arguments);
            var result = await new InspectLibreOfficeBackendOperation(
                    _libreOfficeBackendProbeProvider
                )
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return WordToolkitOperationJson.SerializeToNode(result)!;
        }
        catch (WordToolkitOperationException exception)
        {
            throw new NativeToolException(
                exception.Code,
                exception.Message,
                exception.Details,
                exception.Retryable
            );
        }
    }
}
