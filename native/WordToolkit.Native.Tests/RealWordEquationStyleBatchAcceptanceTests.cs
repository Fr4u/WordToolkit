using System.Runtime.ExceptionServices;
using System.Text.Json;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordEquationStyleBatchAcceptanceTests
{
    [Fact]
    public async Task DedicatedWordBatchPreservesFormattedEquationStylesAndOrder()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_EQUATION_STYLE_BATCH_TEST"),
                "1", StringComparison.Ordinal))
        {
            return;
        }

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                    throw new InvalidOperationException("Dedicated Word application was not created.");
                var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word ProgID is unavailable.");
                ownedApplication = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException("Could not create dedicated Word application.");
            }
            return ownedApplication;
        }

        // Create the dedicated instance outside the host; every COM request below must
        // use launchIfMissing:false and therefore can never attach/launch another Word.
        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(CreateApplication, shutdownTimeout: TimeSpan.FromSeconds(15));
        var service = new WordLiveService(host);
        string? documentId = null;
        string? documentName = null;
        ExceptionDispatchInfo? primary = null;
        Exception? cleanupFailure = null;
        try
        {
            documentName = await host.InvokeAsync(
                application => (string)application.Documents.Add(Visible: false).Name,
                launchIfMissing: false);
            using var connected = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync("connect_live_word_document",
                    JsonDocument.Parse(JsonSerializer.Serialize(new { document_name = documentName, activate = false })).RootElement,
                    CancellationToken.None), JsonDefaults.Compact));
            documentId = connected.RootElement.GetProperty("live_document_id").GetString();
            var version = connected.RootElement.GetProperty("live_version").GetInt64();
            using var applied = JsonDocument.Parse(JsonSerializer.Serialize(
                await service.CallAsync("apply_live_word_operations",
                    JsonDocument.Parse(JsonSerializer.Serialize(new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        operations = new object[]
                        {
                            new { type = "equation", value = "x+y", input_format = "latex", display = true, verify_readback = true },
                            new { type = "equation", value = @"\mathbf{x+\boldsymbol{y}}", input_format = "latex", display = true, verify_readback = true },
                            new { type = "equation", value = @"\boldsymbol{\frac{\alpha+\beta}{\gamma}}", input_format = "latex", display = true, verify_readback = true },
                        }
                    })).RootElement, CancellationToken.None), JsonDefaults.Compact));

            var root = applied.RootElement;
            Assert.Equal(3, root.GetProperty("operation_count").GetInt32());
            Assert.Equal(3, root.GetProperty("equation_operation_count").GetInt32());
            Assert.Equal(3, root.GetProperty("document").GetProperty("equation_count").GetInt32());
            var equations = root.GetProperty("operations").EnumerateArray()
                .Select(x => x.GetProperty("equation")).ToArray();
            Assert.Equal(3, equations.Length);
            Assert.All(equations, e => {
                Assert.True(e.GetProperty("native_verified").GetBoolean());
                Assert.True(e.GetProperty("readback_verified").GetBoolean());
            });
            Assert.False(equations[0].GetProperty("native_style_verified").GetBoolean());
            Assert.True(equations[1].GetProperty("native_style_verified").GetBoolean());
            Assert.True(equations[2].GetProperty("native_style_verified").GetBoolean());
            foreach (var equation in equations.Skip(1))
            {
                var formatting = equation.GetProperty("formatting");
                Assert.True(formatting.GetProperty("region_count").GetInt32() > 0);
                Assert.True(formatting.GetProperty("styled_run_count").GetInt32() > 0);
                Assert.Equal(
                    formatting.GetProperty("expected_contract_sha256").GetString(),
                    formatting.GetProperty("actual_contract_sha256").GetString());
            }
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try
            {
                if (documentName is not null)
                    await host.InvokeAsync(application => { foreach (dynamic d in application.Documents) if ((string)d.Name == documentName) { d.Close(0); break; } return true; }, launchIfMissing: false);
            }
            catch (Exception exception) { cleanupFailure = exception; }
            try
            {
                await host.InvokeAsync(application => { application.Quit(0); return true; }, launchIfMissing: false);
            }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (primary is not null) primary.Throw();
            if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }
}
