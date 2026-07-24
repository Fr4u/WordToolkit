using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

public sealed class RealWordRollbackAcceptanceTests
{
    [Fact]
    public async Task WordUndoThatLeavesOoxmlDriftFailsClosed()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_ROLLBACK_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        await using var host = new WordComHost();
        var service = new WordLiveService(host);
        await host.InvokeAsync(
            application =>
            {
                dynamic document = application.Documents.Add(Visible: false);
                try
                {
                    var record = new LiveDocumentRecord
                    {
                        Id = "real-word-rollback-proof",
                        Name = (string)document.Name,
                        FullName = (string)document.FullName,
                        WindowHwnd = 0,
                        Version = 0,
                    };
                    var baseline = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    dynamic undoRecord = application.UndoRecord;
                    undoRecord.StartCustomRecord("WordToolkit: real rollback proof");
                    var undoStarted = true;
                    document.Range(0, 0).InsertBefore("contamination\r");

                    var error = Assert.Throws<NativeToolException>(
                        () =>
                            service.RollbackPreparedOperationsOrThrow(
                                (object)document,
                                (object)undoRecord,
                                ref undoStarted,
                                mutationAttempted: true,
                                baseline,
                                record,
                                new NativeToolException(
                                    "INVALID_INPUT",
                                    "synthetic failure"
                                )
                            )
                    );

                    Assert.Equal("ROLLBACK_FAILED", error.ErrorCode);
                    var restored = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    var differences = baseline.Differences(restored);
                    Assert.Contains("document_word_open_xml_sha256", differences);
                    Assert.Contains("story_graph_sha256", differences);
                    Assert.Equal(baseline.ContentTextSha256, restored.ContentTextSha256);
                    Assert.Equal(baseline.ParagraphCount, restored.ParagraphCount);
                    Assert.Equal(baseline.EquationCount, restored.EquationCount);
                    Assert.Equal(0, record.Version);
                    return true;
                }
                finally
                {
                    document.Close(0);
                }
            },
            launchIfMissing: true
        );
    }

    [Fact]
    public async Task FlatOpcBaselineRemovesVisibleResidueButDoesNotFakePackageIdentity()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WORDTOOLKIT_REAL_WORD_ROLLBACK_TEST"),
            "1",
            StringComparison.Ordinal
        ))
        {
            return;
        }

        await using var host = new WordComHost();
        await host.InvokeAsync(
            application =>
            {
                dynamic document = application.Documents.Add(Visible: false);
                try
                {
                    document.Content.Text = "Przed\rEquation: x^2 + y^2 = z^2\rPo\r";
                    dynamic equationParagraph = document.Paragraphs.Item(2).Range;
                    dynamic equationRange = document.Range(
                        (int)equationParagraph.Start + 10,
                        (int)equationParagraph.End - 1
                    );
                    dynamic added = document.OMaths.Add(equationRange);
                    added.OMaths.Item(1).BuildUp();
                    var baseline = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );
                    var flatOpc = (string)document.WordOpenXML;

                    document.Content.Text = "skażenie\rresztka\rresztka\r";
                    Assert.Equal(0, (int)document.OMaths.Count);

                    WordLiveService.RestoreLiveMainStoryFromFlatOpc(
                        application,
                        document,
                        flatOpc
                    );
                    var restored = WordLiveService.CaptureLiveRollbackSnapshot(
                        (object)document,
                        0
                    );

                    Assert.Equal(baseline.ContentTextSha256, restored.ContentTextSha256);
                    Assert.Equal(baseline.ParagraphCount, restored.ParagraphCount);
                    Assert.Equal(baseline.EquationCount, restored.EquationCount);
                    Assert.Equal(
                        new[] { "document_semantic_word_open_xml_sha256" },
                        baseline.RecoveryDifferences(restored)
                    );
                    return true;
                }
                finally
                {
                    document.Close(0);
                }
            },
            launchIfMissing: true
        );
    }
}
