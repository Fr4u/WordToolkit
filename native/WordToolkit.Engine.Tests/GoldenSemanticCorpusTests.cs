using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Tests;

public sealed class GoldenSemanticCorpusTests
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [Fact]
    public void MatchesVersionedMultiProducerSemanticOracle()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(
            root,
            "native",
            "WordToolkit.Engine.Tests",
            "Corpus",
            "golden-semantic-v1.json"
        );
        var manifestText = File.ReadAllText(manifestPath, Encoding.UTF8);
        Assert.DoesNotContain("<w:", manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_xml", manifestText, StringComparison.OrdinalIgnoreCase);
        var manifest = JsonSerializer.Deserialize<GoldenManifest>(
            manifestText,
            ManifestJsonOptions
        );
        Assert.NotNull(manifest);
        ValidateManifest(manifest);

        foreach (var expected in manifest.Documents)
        {
            var actual = Project(root, expected);
            Assert.Equivalent(expected, actual, strict: true);
        }
    }

    private static GoldenDocument Project(string root, GoldenDocument expected)
    {
        var relativePath = expected.Path.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var package = new OpcPackageReader().Read(path);
        var semantic = new WordSemanticProjector().Project(package);
        var styles = new WordStyleGraphBuilder().Build(package, semantic);
        var numbering = new WordNumberingGraphBuilder().Build(package, semantic, styles);
        var references = new WordReferenceGraphBuilder().Build(package, semantic);
        var review = new WordReviewGraphBuilder().Build(package, semantic);
        var sections = new WordSectionGraphBuilder().Build(package, semantic);
        var dependency = new WordDependencyGraphBuilder().Build(package, semantic);

        return new GoldenDocument(
            expected.Path,
            expected.ProducerFamily,
            HashFile(path),
            package.Fingerprint,
            expected.RequiredPartUris.Select(partUri =>
            {
                Assert.True(
                    package.Parts.ContainsKey(partUri),
                    $"{expected.Path} is missing required OPC part {partUri}."
                );
                return partUri;
            }).ToArray(),
            new SemanticSnapshot(
                semantic.NodeCount,
                semantic.ProjectedPartCount,
                semantic.Warnings.Count,
                Counts(semantic.Nodes.Select(node => Snake(node.Kind)))
            ),
            new StyleSnapshot(
                styles.Styles.Count,
                styles.Issues.Count,
                styles.DefaultStyleIds.ToDictionary(
                    pair => Snake(pair.Key),
                    pair => pair.Value,
                    StringComparer.Ordinal
                ),
                expected.Styles.Facts.Select(fact =>
                {
                    Assert.True(
                        styles.TryGetStyle(fact.StyleId, out var style),
                        $"{expected.Path} is missing expected style {fact.StyleId}."
                    );
                    Assert.NotNull(style);
                    return new StyleFact(
                        style.StyleId,
                        Snake(style.Type),
                        style.Name,
                        style.BasedOnStyleId,
                        style.NextStyleId,
                        style.LinkedStyleId,
                        style.IsDefault,
                        style.IsCustom,
                        style.InheritanceResolvable
                    );
                }).ToArray()
            ),
            new NumberingSnapshot(
                numbering.NumberingPartUri is not null,
                numbering.AbstractDefinitions.Count,
                numbering.Instances.Count,
                numbering.PictureBullets.Count,
                numbering.Issues.Count,
                expected.Numbering.InstanceFacts.Select(fact =>
                {
                    var instance = numbering.Instances.Single(item =>
                        item.NumberId == fact.NumberId
                    );
                    return new NumberingInstanceFact(
                        instance.NumberId,
                        instance.AbstractNumberId
                    );
                }).ToArray(),
                expected.Numbering.LevelFacts.Select(fact =>
                {
                    var definition = numbering.AbstractDefinitions.Single(item =>
                        item.AbstractNumberId == fact.AbstractNumberId
                    );
                    var level = definition.Levels.Single(item =>
                        item.LevelIndex == fact.LevelIndex
                    );
                    return new NumberingLevelFact(
                        definition.AbstractNumberId,
                        level.LevelIndex,
                        definition.MultiLevelType,
                        level.Start,
                        level.NumberFormat,
                        level.LevelText
                    );
                }).ToArray()
            ),
            ReferenceSnapshot.From(references),
            ReviewSnapshot.From(review),
            new SectionSnapshot(
                sections.Sections.Count,
                sections.EvenAndOddHeaders,
                sections.ReferencedStoryPartUris.Count,
                sections.UnboundStoryPartUris.Count,
                sections.Sections.Sum(section => section.Bindings.Count)
            ),
            new DependencySnapshot(
                dependency.Nodes.Count,
                dependency.Edges.Count,
                dependency.Edges.Count(edge => edge.IsResolved),
                dependency.Edges.Count(edge => !edge.IsResolved),
                dependency.Edges.Count(edge => edge.IsExternal),
                dependency.Nodes.Count(node =>
                    node.Kind == WordDependencyNodeKind.Part
                    && node.IsResolved
                    && !node.IsPackageReachable
                ),
                dependency.Issues.Count,
                Counts(dependency.Edges.Select(edge => Snake(edge.Kind)))
            ),
            ProjectFormattingFacts(
                package,
                semantic,
                styles,
                numbering,
                expected.FormattingFacts
            )
        );
    }

    private static IReadOnlyList<FormattingFact> ProjectFormattingFacts(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        IReadOnlyList<FormattingFact> expectedFacts
    )
    {
        if (expectedFacts.Count == 0)
        {
            return Array.Empty<FormattingFact>();
        }

        var theme = new WordThemeGraphBuilder().Build(package, semantic);
        var settings = new WordSettingsGraphBuilder().Build(package, semantic);
        var fonts = new WordFontTableGraphBuilder().Build(package, semantic);
        var resolver = new WordEffectiveFormattingResolver();
        return expectedFacts.Select(expected =>
        {
            var node = semantic.Nodes.Single(item =>
                item.SourcePartUri == expected.SourcePartUri
                && item.SourceElementOrdinal == expected.SourceElementOrdinal
                && Snake(item.Kind) == expected.NodeKind
            );
            var resolved = resolver.Resolve(
                package,
                semantic,
                styles,
                numbering,
                theme,
                settings,
                fonts,
                node.Id
            );
            return new FormattingFact(
                node.SourcePartUri,
                node.SourceElementOrdinal,
                Snake(node.Kind),
                resolved.ParagraphStyleId,
                resolved.IsFullyResolved,
                expected.ParagraphProperties.Keys.ToDictionary(
                    key => key,
                    key => resolved.ParagraphProperties[key].Value,
                    StringComparer.Ordinal
                ),
                expected.RunProperties.Keys.ToDictionary(
                    key => key,
                    key => resolved.RunProperties[key].Value,
                    StringComparer.Ordinal
                ),
                resolved.UnmodeledElements.ToArray(),
                resolved.CoverageOmissions.ToArray(),
                resolved.CompatibilityWarnings.Count
            );
        }).ToArray();
    }

    private static void ValidateManifest(GoldenManifest manifest)
    {
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(
            "hand_reviewed_semantic_facts_cross_checked_against_source_opc_parts",
            manifest.OraclePolicy
        );
        Assert.Equal(9, manifest.Documents.Count);
        Assert.True(
            manifest.Documents.Select(document => document.ProducerFamily)
                .Distinct(StringComparer.Ordinal)
                .Count() >= 5
        );
        Assert.Equal(
            manifest.Documents.Count,
            manifest.Documents.Select(document => document.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        );
        Assert.Contains(
            manifest.Documents,
            document => document.FormattingFacts.Count > 0
        );
        Assert.Contains(
            manifest.Documents,
            document => document.Numbering.LevelFacts.Count > 0
        );
        Assert.Contains(
            manifest.Documents,
            document => document.Review.RevisionCount > 0
        );
        Assert.Contains(
            manifest.Documents,
            document => document.Sections.ReferencedStoryPartCount >= 6
        );
        var requiredParts = manifest.Documents.SelectMany(document =>
            document.RequiredPartUris
        );
        Assert.Contains("/word/charts/chart1.xml", requiredParts);
        Assert.Contains("/word/comments.xml", requiredParts);
        Assert.Contains("/word/footnotes.xml", requiredParts);
        Assert.Contains("/word/numbering.xml", requiredParts);
        var semanticKinds = manifest.Documents.SelectMany(document =>
            document.Semantic.KindCounts.Keys
        );
        foreach (
            var requiredKind in new[]
            {
                "comment",
                "drawing",
                "field",
                "footnote",
                "header",
                "revision",
                "text_box",
            }
        )
        {
            Assert.Contains(requiredKind, semanticKinds);
        }
        var fieldTypes = manifest.Documents
            .SelectMany(document => document.References.FieldFacts)
            .Select(fact => fact.FieldType);
        Assert.Contains("AUTHOR", fieldTypes);
        Assert.Contains("CREATEDATE", fieldTypes);
        Assert.Contains("REF", fieldTypes);
        var formatting = Assert.Single(
            manifest.Documents.SelectMany(document => document.FormattingFacts)
        );
        Assert.Equal(7, formatting.ParagraphProperties.Count);
        Assert.Equal(11, formatting.RunProperties.Count);
        Assert.Equal(3, formatting.CoverageOmissions.Count);
        Assert.Contains("font_ascii_resolved", formatting.RunProperties.Keys);
        Assert.Contains("spacing_before_twips", formatting.ParagraphProperties.Keys);

        foreach (var document in manifest.Documents)
        {
            Assert.DoesNotContain("..", document.Path, StringComparison.Ordinal);
            Assert.False(Path.IsPathRooted(document.Path));
            Assert.EndsWith(".docx", document.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{64}$", document.FileSha256);
            Assert.Matches("^[0-9a-f]{64}$", document.PackageFingerprint);
            Assert.NotEmpty(document.RequiredPartUris);
            Assert.All(document.RequiredPartUris, partUri =>
                Assert.StartsWith("/", partUri, StringComparison.Ordinal)
            );
            Assert.NotEmpty(document.Styles.Facts);
            if (document.Numbering.HasPart)
            {
                Assert.True(document.Numbering.AbstractCount > 0);
                Assert.True(document.Numbering.InstanceCount > 0);
            }
        }
    }

    private static IReadOnlyDictionary<string, int> Counts(IEnumerable<string> values) =>
        values.GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Snake<T>(T value)
        where T : struct, Enum
    {
        var source = value.ToString();
        var builder = new StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (
                index > 0
                && char.IsUpper(character)
                && (char.IsLower(source[index - 1]) || char.IsDigit(source[index - 1]))
            )
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the WordToolkit repository root.");
    }

    public sealed record GoldenManifest(
        int SchemaVersion,
        string OraclePolicy,
        IReadOnlyList<GoldenDocument> Documents
    );

    public sealed record GoldenDocument(
        string Path,
        string ProducerFamily,
        string FileSha256,
        string PackageFingerprint,
        IReadOnlyList<string> RequiredPartUris,
        SemanticSnapshot Semantic,
        StyleSnapshot Styles,
        NumberingSnapshot Numbering,
        ReferenceSnapshot References,
        ReviewSnapshot Review,
        SectionSnapshot Sections,
        DependencySnapshot Dependency,
        IReadOnlyList<FormattingFact> FormattingFacts
    );

    public sealed record SemanticSnapshot(
        int NodeCount,
        int ProjectedPartCount,
        int WarningCount,
        IReadOnlyDictionary<string, int> KindCounts
    );

    public sealed record StyleSnapshot(
        int Count,
        int IssueCount,
        IReadOnlyDictionary<string, string> Defaults,
        IReadOnlyList<StyleFact> Facts
    );

    public sealed record StyleFact(
        string StyleId,
        string Type,
        string? Name,
        string? BasedOnStyleId,
        string? NextStyleId,
        string? LinkedStyleId,
        bool DefaultStyle,
        bool CustomStyle,
        bool InheritanceResolvable
    );

    public sealed record NumberingSnapshot(
        bool HasPart,
        int AbstractCount,
        int InstanceCount,
        int PictureBulletCount,
        int IssueCount,
        IReadOnlyList<NumberingInstanceFact> InstanceFacts,
        IReadOnlyList<NumberingLevelFact> LevelFacts
    );

    public sealed record NumberingInstanceFact(int NumberId, int AbstractNumberId);

    public sealed record NumberingLevelFact(
        int AbstractNumberId,
        int LevelIndex,
        string? MultiLevelType,
        int? Start,
        string? NumberFormat,
        string? LevelText
    );

    public sealed record FieldFact(
        string FieldType,
        string Classification,
        int Count,
        int IncompleteCount,
        int ExternalCount,
        int ApplicationInvokingCount
    );

    public sealed record ReferenceSnapshot(
        int StoryCount,
        int BookmarkCount,
        int CompleteBookmarkCount,
        int DuplicateBookmarkNameCount,
        int FieldCount,
        int ComplexFieldCount,
        int SimpleFieldCount,
        int IncompleteFieldCount,
        int ExternalFieldCount,
        int ApplicationInvokingFieldCount,
        int DependencyCount,
        int UnresolvedDependencyCount,
        int ExternalDependencyCount,
        int IssueCount,
        IReadOnlyList<FieldFact> FieldFacts
    )
    {
        public static ReferenceSnapshot From(WordReferenceGraph graph)
        {
            var duplicateNames = graph.Bookmarks
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Name))
                .GroupBy(bookmark => bookmark.Name!, StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Count() > 1);
            var facts = graph.Fields
                .GroupBy(
                    field => (field.FieldType ?? "unknown", Snake(field.Classification)),
                    FieldFactKeyComparer.Instance
                )
                .OrderBy(group => group.Key.Item1, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Item2, StringComparer.Ordinal)
                .Select(group => new FieldFact(
                    group.Key.Item1,
                    group.Key.Item2,
                    group.Count(),
                    group.Count(field =>
                        field.Status != WordFieldStatus.Complete
                        || !field.InstructionParseComplete
                    ),
                    group.Count(field => field.RequiresExternalAccess),
                    group.Count(field => field.MayInvokeApplication)
                ))
                .ToArray();
            return new ReferenceSnapshot(
                graph.Stories.Count,
                graph.Bookmarks.Count,
                graph.Bookmarks.Count(bookmark => bookmark.IsComplete),
                duplicateNames,
                graph.Fields.Count,
                graph.Fields.Count(field => field.Kind == WordFieldKind.Complex),
                graph.Fields.Count(field => field.Kind == WordFieldKind.Simple),
                graph.Fields.Count(field =>
                    field.Status != WordFieldStatus.Complete
                    || !field.InstructionParseComplete
                ),
                graph.Fields.Count(field => field.RequiresExternalAccess),
                graph.Fields.Count(field => field.MayInvokeApplication),
                graph.Edges.Count,
                graph.Edges.Count(edge => !edge.IsResolved),
                graph.Edges.Count(edge => edge.IsExternal),
                graph.Issues.Count,
                facts
            );
        }
    }

    public sealed record ReviewSnapshot(
        int CommentCount,
        int AnchoredCommentCount,
        int ReplyCount,
        int ThreadCount,
        int ResolvedCommentCount,
        int ReactionCommentCount,
        int CommentAnchorCount,
        int IncompleteCommentAnchorCount,
        int PersonCount,
        int RevisionCount,
        int InsertionCount,
        int DeletionCount,
        int PropertyRevisionCount,
        int TrackedTextCharacterCount,
        int MoveRangeCount,
        int MoveCount,
        int IncompleteMoveCount,
        int PermissionRangeCount,
        int IncompletePermissionRangeCount,
        bool? TrackRevisionsEnabled,
        int IssueCount
    )
    {
        public static ReviewSnapshot From(WordReviewGraph graph) => new(
            graph.Comments.Count,
            graph.Comments.Count(comment => comment.AnchorIds.Count > 0),
            graph.ReplyCount,
            graph.ThreadCount,
            graph.ResolvedCommentCount,
            graph.Comments.Count(comment => comment.HasReactions),
            graph.Anchors.Count,
            graph.Anchors.Count(anchor =>
                anchor.Status is not WordCommentAnchorStatus.Complete
                    and not WordCommentAnchorStatus.PointReference
            ),
            graph.People.Count,
            graph.Revisions.Count,
            graph.Revisions.Count(revision => revision.Kind == WordRevisionKind.Insertion),
            graph.Revisions.Count(revision => revision.Kind == WordRevisionKind.Deletion),
            graph.Revisions.Count(revision =>
                revision.Kind.ToString().EndsWith("Change", StringComparison.Ordinal)
            ),
            graph.TrackedTextCharacterCount,
            graph.MoveRanges.Count,
            graph.Moves.Count,
            graph.Moves.Count(move => move.Status != WordMovePairStatus.Complete),
            graph.Permissions.Count,
            graph.Permissions.Count(permission =>
                permission.Status != WordReviewRangeStatus.Complete
            ),
            graph.Settings?.TrackRevisions,
            graph.Issues.Count
        );
    }

    public sealed record SectionSnapshot(
        int Count,
        bool EvenAndOddHeaders,
        int ReferencedStoryPartCount,
        int UnboundStoryPartCount,
        int BindingCount
    );

    public sealed record DependencySnapshot(
        int NodeCount,
        int EdgeCount,
        int ResolvedEdgeCount,
        int UnresolvedEdgeCount,
        int ExternalEdgeCount,
        int PackageUnreachablePartCount,
        int IssueCount,
        IReadOnlyDictionary<string, int> EdgeKindCounts
    );

    public sealed record FormattingFact(
        string SourcePartUri,
        int SourceElementOrdinal,
        string NodeKind,
        string? ParagraphStyleId,
        bool FullyResolved,
        IReadOnlyDictionary<string, string> ParagraphProperties,
        IReadOnlyDictionary<string, string> RunProperties,
        IReadOnlyList<string> UnmodeledElements,
        IReadOnlyList<string> CoverageOmissions,
        int CompatibilityWarningCount
    );

    private sealed class FieldFactKeyComparer
        : IEqualityComparer<(string FieldType, string Classification)>
    {
        public static FieldFactKeyComparer Instance { get; } = new();

        public bool Equals(
            (string FieldType, string Classification) x,
            (string FieldType, string Classification) y
        ) => string.Equals(x.FieldType, y.FieldType, StringComparison.Ordinal)
            && string.Equals(x.Classification, y.Classification, StringComparison.Ordinal);

        public int GetHashCode((string FieldType, string Classification) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.FieldType),
                StringComparer.Ordinal.GetHashCode(obj.Classification)
            );
    }
}
