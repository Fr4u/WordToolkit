using System.Runtime.ExceptionServices;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordToolkit.Engine.Operations;
using WordToolkit.Native.Protocol;
using WordToolkit.Native.Word;

namespace WordToolkit.Native.Tests;

[Collection("RealWordAcceptance")]
public sealed class RealWordCharacterFormattingAcceptanceTests
{
    [Fact]
    public async Task DedicatedWordRoundTripsCompleteScalarFontSurfaceAndExportsPdf()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "WORDTOOLKIT_REAL_WORD_CHARACTER_FORMATTING_TEST"
                ),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        var requestedArtifactDirectory = Environment.GetEnvironmentVariable(
            "WORDTOOLKIT_REAL_WORD_FORMATTING_ARTIFACT_DIR"
        );
        var preserveArtifacts = !string.IsNullOrWhiteSpace(requestedArtifactDirectory);
        var artifactDirectory = preserveArtifacts
            ? Path.GetFullPath(requestedArtifactDirectory!)
            : Path.Combine(
                Path.GetTempPath(),
                $"wordtoolkit-real-character-formatting-{Guid.NewGuid():N}"
            );
        Directory.CreateDirectory(artifactDirectory);
        var savedPath = Path.Combine(artifactDirectory, "character-formatting.docx");
        var pdfPath = Path.Combine(artifactDirectory, "character-formatting.pdf");

        object? ownedApplication = null;
        object CreateApplication(bool launchIfMissing)
        {
            if (ownedApplication is null)
            {
                if (!launchIfMissing)
                {
                    throw new InvalidOperationException("Dedicated Word application was not created.");
                }
                var wordType = Type.GetTypeFromProgID("Word.Application", throwOnError: true)
                    ?? throw new InvalidOperationException("Microsoft Word ProgID is unavailable.");
                ownedApplication = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException(
                        "Could not create dedicated Word application."
                    );
            }
            return ownedApplication;
        }

        ownedApplication = CreateApplication(true);
        await using var host = new WordComHost(
            CreateApplication,
            shutdownTimeout: TimeSpan.FromSeconds(15)
        );
        var service = new WordLiveService(host);
        string? documentId = null;
        string? documentName = null;
        ExceptionDispatchInfo? primary = null;
        Exception? cleanupFailure = null;
        try
        {
            documentName = await host.InvokeAsync(
                application => (string)application.Documents.Add(Visible: false).Name,
                launchIfMissing: false
            );
            using var connectArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { document_name = documentName, activate = false })
            );
            using var connected = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "connect_live_word_document",
                        connectArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            documentId = connected.RootElement.GetProperty("live_document_id").GetString();
            var version = connected.RootElement.GetProperty("live_version").GetInt64();
            using var applyArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        expected_version = version,
                        operations = new object[]
                        {
                            new
                            {
                                type = "text",
                                runs = new object[]
                                {
                                    new
                                    {
                                        text = "subscript",
                                        formatting = new
                                        {
                                            subscript = true,
                                            underline_style = "double",
                                            underline_color = "#C00000",
                                        },
                                    },
                                    new
                                    {
                                        text = " superscript",
                                        formatting = new
                                        {
                                            superscript = true,
                                            underline_style = "wavy_double",
                                            font_color_rgb = "#0070C0",
                                        },
                                    },
                                    new
                                    {
                                        text = " typography",
                                        formatting = new
                                        {
                                            bold = true,
                                            italic = true,
                                            shadow = true,
                                            outline = true,
                                            scaling_percent = 110,
                                            spacing_pt = 0.5,
                                            kerning_pt = 8,
                                        },
                                    },
                                    new
                                    {
                                        text = " OpenType 0123",
                                        formatting = new
                                        {
                                            ligatures = "standard_contextual",
                                            number_form = "lining",
                                            number_spacing = "tabular",
                                            stylistic_sets = new[] { 1 },
                                            contextual_alternates = true,
                                        },
                                    },
                                    new
                                    {
                                        text = " script fonts",
                                        formatting = new
                                        {
                                            font_name_ascii = "Arial",
                                            font_name_bidi = "Arial",
                                            font_name_far_east = "Yu Gothic",
                                            font_name_other = "Courier New",
                                            font_size_bidi_pt = 12,
                                            font_color_bidi_index = 6,
                                            diacritic_color = "#008000",
                                            bold_bidi = true,
                                            italic_bidi = true,
                                            emphasis_mark = "over_solid_circle",
                                            disable_character_space_grid = true,
                                        },
                                    },
                                    new
                                    {
                                        text = " embossed",
                                        formatting = new
                                        {
                                            all_caps = true,
                                            small_caps = false,
                                            hidden = false,
                                            emboss = true,
                                            engrave = false,
                                        },
                                    },
                                    new
                                    {
                                        text = " raised",
                                        formatting = new
                                        {
                                            position_pt = 3,
                                            underline_style = "dash_long_heavy",
                                            underline_color = "automatic",
                                        },
                                    },
                                },
                                as_new_paragraph = true,
                                formatting = new
                                {
                                    font_name = "Aptos",
                                    font_size_pt = 14,
                                    paragraph_alignment = "left",
                                },
                            },
                            new
                            {
                                type = "text",
                                runs = new object[]
                                {
                                    new
                                    {
                                        text = "clear proof",
                                        formatting = new
                                        {
                                            clear_character_formatting = true,
                                            bold = true,
                                        },
                                    },
                                },
                                as_new_paragraph = true,
                                formatting = new
                                {
                                    font_name = "Times New Roman",
                                    font_size_pt = 18,
                                    font_color_rgb = "#C00000",
                                    italic = true,
                                    underline_style = "wavy_double",
                                    underline_color = "#C00000",
                                    shadow = true,
                                    outline = true,
                                    scaling_percent = 120,
                                    spacing_pt = 2,
                                    position_pt = 3,
                                    kerning_pt = 9,
                                    ligatures = "standard_contextual",
                                    number_form = "old_style",
                                    number_spacing = "tabular",
                                    stylistic_sets = new[] { 1, 3 },
                                    contextual_alternates = true,
                                    highlight_color_index = 7,
                                },
                            },
                            new
                            {
                                type = "text",
                                text = "control proof",
                                as_new_paragraph = true,
                            },
                        },
                    }
                )
            );
            using var applied = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "apply_live_word_operations",
                        applyArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            var operation = applied.RootElement.GetProperty("operations")[0];
            Assert.Equal(7, operation.GetProperty("run_count").GetInt32());
            Assert.True(operation.GetProperty("native_formatting_verified").GetBoolean());
            Assert.False(operation.GetProperty("formatting_readback_returned").GetBoolean());
            var operationStart = operation.GetProperty("range").GetProperty("start").GetInt32();
            var clearOperation = applied.RootElement.GetProperty("operations")[1];
            Assert.Equal(1, clearOperation.GetProperty("run_count").GetInt32());
            Assert.True(clearOperation.GetProperty("native_formatting_verified").GetBoolean());
            var clearOperationStart = clearOperation
                .GetProperty("range")
                .GetProperty("start")
                .GetInt32();
            var controlOperation = applied.RootElement.GetProperty("operations")[2];
            var controlOperationStart = controlOperation
                .GetProperty("range")
                .GetProperty("start")
                .GetInt32();
            var appliedVersion = applied.RootElement.GetProperty("live_version").GetInt64();
            await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Item(documentName);
                    document.Activate();
                    application.Selection.SetRange(operationStart, operationStart + 9);
                    return true;
                },
                launchIfMissing: false
            );
            using var selectionArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(new { live_document_id = documentId })
            );
            using var selection = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "get_live_word_selection",
                        selectionArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            var selectionToken = selection.RootElement
                .GetProperty("selection")
                .GetProperty("selection_token")
                .GetString();
            using var formatArguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        live_document_id = documentId,
                        selection_token = selectionToken,
                        expected_version = appliedVersion,
                        formatting = new
                        {
                            subscript = true,
                            underline_style = "double",
                            underline_color = "#C00000",
                        },
                    }
                )
            );
            using var formatted = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    await service.CallAsync(
                        "format_live_word_selection",
                        formatArguments.RootElement,
                        CancellationToken.None
                    ),
                    JsonDefaults.Compact
                )
            );
            Assert.True(
                formatted.RootElement.GetProperty("native_formatting_verified").GetBoolean()
            );
            var publicReadback = formatted.RootElement.GetProperty("formatting_readback");
            Assert.True(publicReadback.GetProperty("subscript").GetBoolean());
            Assert.Equal(
                "double",
                publicReadback.GetProperty("underline_style").GetString()
            );
            Assert.Equal(
                "#C00000",
                publicReadback.GetProperty("underline_color").GetString()
            );

            await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Item(documentName);
                    var runTexts = new[]
                    {
                        "subscript",
                        " superscript",
                        " typography",
                        " OpenType 0123",
                        " script fonts",
                        " embossed",
                        " raised",
                    };
                    var completeText = string.Concat(runTexts);
                    var displayedText = completeText.Replace(
                        " embossed",
                        " EMBOSSED",
                        StringComparison.Ordinal
                    );
                    Assert.Equal(
                        displayedText,
                        (string)document.Range(
                            operationStart,
                            operationStart + completeText.Length
                        ).Text
                    );
                    var offset = operationStart;
                    dynamic subscript = document.Range(offset, offset + runTexts[0].Length);
                    Assert.Equal(-1, (int)subscript.Font.Subscript);
                    Assert.Equal(0, (int)subscript.Font.Superscript);
                    Assert.Equal(3, (int)subscript.Font.Underline);
                    Assert.Equal(0x0000C0, (int)subscript.Font.UnderlineColor);
                    offset += runTexts[0].Length;

                    dynamic superscript = document.Range(offset, offset + runTexts[1].Length);
                    Assert.Equal(-1, (int)superscript.Font.Superscript);
                    Assert.Equal(0, (int)superscript.Font.Subscript);
                    Assert.Equal(43, (int)superscript.Font.Underline);
                    Assert.Equal(0xC07000, (int)superscript.Font.Color);
                    offset += runTexts[1].Length;

                    dynamic typography = document.Range(offset, offset + runTexts[2].Length);
                    Assert.Equal(-1, (int)typography.Font.Bold);
                    Assert.Equal(-1, (int)typography.Font.Italic);
                    Assert.Equal(-1, (int)typography.Font.Shadow);
                    Assert.Equal(-1, (int)typography.Font.Outline);
                    Assert.Equal(110, (int)typography.Font.Scaling);
                    Assert.Equal(0.5f, (float)typography.Font.Spacing);
                    Assert.Equal(8f, (float)typography.Font.Kerning);
                    offset += runTexts[2].Length;

                    dynamic openType = document.Range(offset, offset + runTexts[3].Length);
                    Assert.Equal(3, (int)openType.Font.Ligatures);
                    Assert.Equal(1, (int)openType.Font.NumberForm);
                    Assert.Equal(2, (int)openType.Font.NumberSpacing);
                    Assert.Equal(1, (int)openType.Font.StylisticSet);
                    Assert.Equal(-1, (int)openType.Font.ContextualAlternates);
                    offset += runTexts[3].Length;

                    dynamic scriptFonts = document.Range(offset, offset + runTexts[4].Length);
                    Assert.Equal("Arial", (string)scriptFonts.Font.NameAscii);
                    Assert.Equal("Arial", (string)scriptFonts.Font.NameBi);
                    Assert.Equal("Yu Gothic", (string)scriptFonts.Font.NameFarEast);
                    Assert.Equal("Courier New", (string)scriptFonts.Font.NameOther);
                    Assert.Equal(12f, (float)scriptFonts.Font.SizeBi);
                    Assert.Equal(6, (int)scriptFonts.Font.ColorIndexBi);
                    Assert.Equal(0x00008000, (int)scriptFonts.Font.DiacriticColor);
                    Assert.Equal(-1, (int)scriptFonts.Font.BoldBi);
                    Assert.Equal(-1, (int)scriptFonts.Font.ItalicBi);
                    Assert.Equal(1, (int)scriptFonts.Font.EmphasisMark);
                    Assert.Equal(-1, (int)scriptFonts.Font.DisableCharacterSpaceGrid);
                    offset += runTexts[4].Length;

                    dynamic embossed = document.Range(offset, offset + runTexts[5].Length);
                    Assert.Equal(-1, (int)embossed.Font.AllCaps);
                    Assert.Equal(0, (int)embossed.Font.SmallCaps);
                    Assert.Equal(0, (int)embossed.Font.Hidden);
                    Assert.Equal(-1, (int)embossed.Font.Emboss);
                    Assert.Equal(0, (int)embossed.Font.Engrave);
                    offset += runTexts[5].Length;

                    dynamic raised = document.Range(offset, offset + runTexts[6].Length);
                    Assert.Equal(3, (int)raised.Font.Position);
                    Assert.Equal(55, (int)raised.Font.Underline);
                    Assert.Equal(unchecked((int)0xFF000000), (int)raised.Font.UnderlineColor);

                    dynamic reset = document.Range(
                        clearOperationStart,
                        clearOperationStart + "clear proof".Length
                    );
                    dynamic control = document.Range(
                        controlOperationStart,
                        controlOperationStart + "control proof".Length
                    );
                    Assert.Equal("clear proof", (string)reset.Text);
                    Assert.Equal("control proof", (string)control.Text);
                    Assert.Equal(-1, (int)reset.Font.Bold);
                    Assert.Equal((int)control.Font.Italic, (int)reset.Font.Italic);
                    Assert.Equal((int)control.Font.Underline, (int)reset.Font.Underline);
                    Assert.Equal(
                        (int)control.Font.UnderlineColor,
                        (int)reset.Font.UnderlineColor
                    );
                    Assert.Equal((int)control.Font.Shadow, (int)reset.Font.Shadow);
                    Assert.Equal((int)control.Font.Outline, (int)reset.Font.Outline);
                    Assert.Equal(
                        (int)control.HighlightColorIndex,
                        (int)reset.HighlightColorIndex
                    );
                    Assert.NotEqual("Times New Roman", (string)reset.Font.Name);
                    Assert.NotEqual(18f, (float)reset.Font.Size);
                    Assert.NotEqual(0x0000C0, (int)reset.Font.Color);
                    Assert.Equal((string)control.Font.Name, (string)reset.Font.Name);
                    Assert.Equal((float)control.Font.Size, (float)reset.Font.Size);
                    Assert.Equal((int)control.Font.Color, (int)reset.Font.Color);
                    Assert.Equal((int)control.Font.Scaling, (int)reset.Font.Scaling);
                    Assert.Equal((float)control.Font.Spacing, (float)reset.Font.Spacing);
                    Assert.Equal((int)control.Font.Position, (int)reset.Font.Position);
                    Assert.Equal((float)control.Font.Kerning, (float)reset.Font.Kerning);
                    Assert.Equal((int)control.Font.Ligatures, (int)reset.Font.Ligatures);
                    Assert.Equal((int)control.Font.NumberForm, (int)reset.Font.NumberForm);
                    Assert.Equal(
                        (int)control.Font.NumberSpacing,
                        (int)reset.Font.NumberSpacing
                    );
                    Assert.Equal(
                        (int)control.Font.StylisticSet,
                        (int)reset.Font.StylisticSet
                    );
                    Assert.Equal(
                        (int)control.Font.ContextualAlternates,
                        (int)reset.Font.ContextualAlternates
                    );

                    dynamic mixed = document.Range(
                        operationStart,
                        operationStart + completeText.Length
                    );
                    Assert.Equal(9_999_999, (int)mixed.Font.Underline);
                    Assert.Equal(9_999_999, (int)mixed.Font.Bold);
                    document.SaveAs2(
                        FileName: savedPath,
                        FileFormat: 16,
                        AddToRecentFiles: false
                    );
                    document.ExportAsFixedFormat(
                        OutputFileName: pdfPath,
                        ExportFormat: 17,
                        OpenAfterExport: false,
                        OptimizeFor: 0,
                        Range: 0,
                        Item: 0,
                        IncludeDocProps: true,
                        KeepIRM: true,
                        CreateBookmarks: 1,
                        DocStructureTags: true,
                        BitmapMissingFonts: true,
                        UseISO19005_1: false
                    );
                    documentName = (string)document.Name;
                    return true;
                },
                launchIfMissing: false
            );

            Assert.True(File.Exists(savedPath));
            Assert.True(new FileInfo(savedPath).Length > 0);
            Assert.True(File.Exists(pdfPath));
            Assert.True(new FileInfo(pdfPath).Length > 0);
            var inspection = new InspectWordPackageOperation().Execute(
                new InspectWordPackageRequest(savedPath, IncludeDetails: true, MaxItems: 20)
            );
            Assert.True(inspection.ValidWordPackage);
            Assert.DoesNotContain(
                inspection.Diagnostics.Items,
                diagnostic => diagnostic.Code == "IO_ERROR"
            );
            await host.InvokeAsync(
                application =>
                {
                    dynamic document = application.Documents.Item(documentName);
                    document.Close(0);
                    return true;
                },
                launchIfMissing: false
            );
            documentName = null;
            using var package = WordprocessingDocument.Open(savedPath, false);
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package));
            var mainDocument = package.MainDocumentPart?.Document;
            Assert.NotNull(mainDocument);
            Assert.Contains(
                "embossed",
                mainDocument!.InnerText,
                StringComparison.Ordinal
            );
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
                {
                    await host.InvokeAsync(
                        application =>
                        {
                            foreach (dynamic document in application.Documents)
                            {
                                if ((string)document.Name == documentName)
                                {
                                    document.Close(0);
                                    break;
                                }
                            }
                            return true;
                        },
                        launchIfMissing: false
                    );
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            try
            {
                await host.InvokeAsync(
                    application =>
                    {
                        application.Quit(0);
                        return true;
                    },
                    launchIfMissing: false
                );
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            if (!preserveArtifacts)
            {
                try
                {
                    if (Directory.Exists(artifactDirectory))
                    {
                        Directory.Delete(artifactDirectory, recursive: true);
                    }
                }
                catch (Exception exception)
                {
                    cleanupFailure ??= exception;
                }
            }
            if (primary is not null)
            {
                primary.Throw();
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }
}
