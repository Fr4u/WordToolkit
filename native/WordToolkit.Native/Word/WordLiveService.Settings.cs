using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private static Task<object> InspectPackageSettingsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (
            view is not "summary"
                and not "compatibility"
                and not "variables"
                and not "mail_merge"
                and not "inventory"
        )
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, compatibility, variables, mail_merge, or inventory"
            );
        }

        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }
        if (maximum is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 100"
            );
        }

        var includeSensitive = arguments.Boolean("include_sensitive", false);
        var includeIssues = arguments.Boolean("include_issues", true);
        var includeSource = arguments.Boolean("include_source", false);
        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordSettingsGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            var matching = SettingsItems(
                graph,
                view,
                includeSensitive,
                includeSource
            );
            var page = matching.Skip((int)offset).Take((int)maximum).ToArray();
            var consumed = (long)offset + page.Length;
            var returnedIssues = includeIssues
                ? graph.Issues.Take(40).Select(issue => new
                {
                    code = BoundForResponse(issue.Code, 128),
                    severity = ToSnakeCase(issue.Severity.ToString()),
                    element_name = BoundForResponse(issue.ElementName, 128),
                    message = BoundForResponse(issue.Message, 512),
                }).ToArray()
                : null;
            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                main_part_uri = graph.MainPartUri,
                settings_part_uri = includeSource
                    ? BoundForResponse(graph.SettingsPartUri, 512)
                    : null,
                has_settings_part = graph.HasSettingsPart,
                boolean_setting_count = graph.BooleanSettings.Count,
                track_revisions = graph.TrackRevisions,
                even_and_odd_headers = graph.EvenAndOddHeaders,
                font_embedding = new
                {
                    embed_true_type_fonts = graph.EmbedTrueTypeFonts,
                    embed_system_fonts = graph.EmbedSystemFonts,
                    save_subset_fonts = graph.SaveSubsetFonts,
                },
                theme_font_languages = graph.ThemeFontLanguages is { } languages
                    ? new
                    {
                        latin = BoundForResponse(languages.Latin, 256),
                        east_asian = BoundForResponse(languages.EastAsian, 256),
                        complex_script = BoundForResponse(
                            languages.ComplexScript,
                            256
                        ),
                    }
                    : null,
                compatibility_mode = graph.Compatibility?.CompatibilityMode,
                document_protection = ProtectionSummary(graph.DocumentProtection),
                write_protection = WriteProtectionSummary(graph.WriteProtection),
                document_variable_count = graph.DocumentVariables.Count,
                has_mail_merge = graph.MailMerge is not null,
                has_attached_template = graph.AttachedTemplate is not null,
                view_settings = new
                {
                    view = BoundForResponse(graph.View.View, 128),
                    zoom_kind = BoundForResponse(graph.View.ZoomKind, 128),
                    zoom_percent = graph.View.ZoomPercent,
                    default_tab_stop_twips = graph.View.DefaultTabStopTwips,
                    default_image_dpi = graph.View.DefaultImageDpi,
                },
                view,
                sensitive_values_included = includeSensitive,
                matched_item_count = matching.Count,
                offset,
                returned_item_count = page.Length,
                next_offset = consumed < matching.Count ? (int)consumed : (int?)null,
                items = page,
                issue_count = graph.Issues.Count,
                issues = returnedIssues,
                issues_truncated = returnedIssues is not null
                    && graph.Issues.Count > returnedIssues.Length
                        ? true
                        : (bool?)null,
                unmodeled_root_element_count = graph.UnmodeledRootElements.Count,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordSettingsLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Settings graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSettingsProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a Word settings graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The Word package could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static IReadOnlyList<object> SettingsItems(
        WordSettingsGraph graph,
        string view,
        bool includeSensitive,
        bool includeSource
    ) => view switch
    {
        "summary" => graph.BooleanSettings.Values
            .OrderBy(setting => setting.Name, StringComparer.Ordinal)
            .Select(setting => (object)new
            {
                name = BoundForResponse(setting.Name, 128),
                value = setting.Value,
                source_element_ordinal = includeSource
                    ? setting.SourceElementOrdinal
                    : (int?)null,
            })
            .ToArray(),
        "compatibility" => CompatibilityItems(graph.Compatibility, includeSource),
        "variables" => graph.DocumentVariables.Select(variable => (object)new
        {
            name = includeSensitive
                ? BoundForResponse(variable.Name, 2_048)
                : null,
            value = includeSensitive
                ? BoundForResponse(variable.Value, 8_192)
                : null,
            name_redacted = includeSensitive ? (bool?)null : true,
            value_redacted = includeSensitive ? (bool?)null : true,
            name_character_count = variable.Name.Length,
            value_character_count = variable.Value.Length,
            source_element_ordinal = includeSource
                ? variable.SourceElementOrdinal
                : (int?)null,
        }).ToArray(),
        "mail_merge" => graph.MailMerge is { } mailMerge
            ?
            [
                new
                {
                    main_document_type = BoundForResponse(
                        mailMerge.MainDocumentType,
                        256
                    ),
                    data_type = BoundForResponse(mailMerge.DataType, 256),
                    link_to_query = mailMerge.LinkToQuery,
                    has_query = mailMerge.Query is not null,
                    query = includeSensitive
                        ? BoundForResponse(mailMerge.Query, 8_192)
                        : null,
                    has_connection_string = mailMerge.ConnectionString is not null,
                    connection_string = includeSensitive
                        ? BoundForResponse(mailMerge.ConnectionString, 8_192)
                        : null,
                    has_office_data_source_object =
                        mailMerge.HasOfficeDataSourceObject,
                    data_source = SettingsRelationshipItem(
                        mailMerge.DataSource,
                        includeSensitive
                    ),
                    header_source = SettingsRelationshipItem(
                        mailMerge.HeaderSource,
                        includeSensitive
                    ),
                    unmodeled_elements = mailMerge.UnmodeledElements.Count == 0
                        ? null
                        : mailMerge.UnmodeledElements.Take(40)
                            .Select(value => BoundForResponse(value, 256))
                            .ToArray(),
                    source_element_ordinal = includeSource
                        ? mailMerge.SourceElementOrdinal
                        : (int?)null,
                },
            ]
            : Array.Empty<object>(),
        _ => graph.Inventory.Select(item => (object)new
        {
            qualified_name = BoundForResponse(item.QualifiedName, 512),
            count = item.Count,
        }).ToArray(),
    };

    private static IReadOnlyList<object> CompatibilityItems(
        WordCompatibilityProfile? profile,
        bool includeSource
    )
    {
        if (profile is null)
        {
            return Array.Empty<object>();
        }
        return profile.Settings.Select(setting => (object)new
        {
            kind = "setting",
            name = BoundForResponse(setting.Name, 256),
            uri = BoundForResponse(setting.Uri, 2_048),
            value = BoundForResponse(setting.Value, 2_048),
            source_element_ordinal = includeSource
                    ? setting.SourceElementOrdinal
                    : (int?)null,
        })
            .Concat(profile.LegacyOptions.Select(option => (object)new
            {
                kind = "legacy_option",
                name = BoundForResponse(option.Name, 256),
                uri = (string?)null,
                value = option.Value ? "true" : "false",
                source_element_ordinal = includeSource
                    ? option.SourceElementOrdinal
                    : (int?)null,
            }))
            .ToArray();
    }

    private static object? ProtectionSummary(WordProtectionDescriptor? value) =>
        value is null
            ? null
            : new
            {
                enforced = value.IsEnforced,
                formatting_restricted = value.FormattingRestricted,
                edit_mode = BoundForResponse(value.EditMode, 128),
                algorithm_name = BoundForResponse(value.AlgorithmName, 256),
                spin_count = value.SpinCount,
                has_hash = value.HasHash,
                has_salt = value.HasSalt,
                security_boundary = false,
            };

    private static object? WriteProtectionSummary(
        WordWriteProtectionDescriptor? value
    ) => value is null
        ? null
        : new
        {
            recommended = value.IsRecommended,
            algorithm_name = BoundForResponse(value.AlgorithmName, 256),
            spin_count = value.SpinCount,
            has_hash = value.HasHash,
            has_salt = value.HasSalt,
        };

    private static object? SettingsRelationshipItem(
        WordSettingsRelationshipReference? value,
        bool includeSensitive
    ) => value is null
        ? null
        : new
        {
            relationship_id = BoundForResponse(value.RelationshipId, 1_024),
            relationship_type = BoundForResponse(value.RelationshipType, 1_024),
            target_mode = value.TargetMode is { } mode
                ? ToSnakeCase(mode.ToString())
                : null,
            target = includeSensitive
                ? BoundForResponse(value.Target, 2_048)
                : null,
            resolved_target_part_uri = includeSensitive
                ? BoundForResponse(value.ResolvedTargetPartUri, 2_048)
                : null,
            target_redacted = includeSensitive ? (bool?)null : true,
            resolved = value.IsResolved,
        };
}
