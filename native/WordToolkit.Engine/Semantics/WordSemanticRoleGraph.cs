using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public enum WordSemanticRoleKind
{
    Theorem,
    Lemma,
    Proposition,
    Corollary,
    Definition,
    Proof,
    Example,
    Remark,
    Axiom,
    Assumption,
}

public enum WordSemanticRoleEvidenceKind
{
    ContentControlTag,
    ContentControlAlias,
    DirectStyleId,
    StyleName,
    StyleAlias,
    InheritedStyleId,
    LexicalLabel,
}

public enum WordSemanticRoleClassification
{
    Declared,
    StyleConvention,
    LexicalCandidate,
    Conflicting,
}

public enum WordSemanticRoleIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordSemanticRoleEvidence(
    string Id,
    WordSemanticRoleEvidenceKind Kind,
    WordSemanticRoleKind Role,
    bool AuthorDeclared,
    string? ContentControlId,
    string? StyleId,
    string ValueFingerprint
);

public sealed record WordSemanticRoleCandidate(
    string Id,
    string Fingerprint,
    SemanticNodeId ParagraphNodeId,
    WordSemanticRoleKind? Role,
    WordSemanticRoleClassification Classification,
    WordStoryKind StoryKind,
    int SourceOrder,
    int ParagraphCharacterCount,
    int? LabelCharacterCount,
    string ParagraphTextFingerprint,
    bool ViewAmbiguous,
    bool UsableAsSemanticRole,
    IReadOnlyList<WordSemanticRoleEvidence> Evidence
);

public sealed record WordSemanticRoleIssue(
    string Code,
    WordSemanticRoleIssueSeverity Severity,
    string Message,
    SemanticNodeId? ParagraphNodeId,
    WordStoryKind? StoryKind,
    int? SourceOrder,
    string? CandidateId
);

public sealed class WordSemanticRoleGraph
{
    internal WordSemanticRoleGraph(
        string packageFingerprint,
        string profile,
        int examinedParagraphCount,
        int eligibleParagraphCount,
        int ambiguousParagraphCount,
        IReadOnlyList<WordSemanticRoleCandidate> candidates,
        IReadOnlyList<WordSemanticRoleIssue> issues,
        bool analysisExecutionComplete,
        bool modeledEvidenceCoverageComplete
    )
    {
        PackageFingerprint = packageFingerprint;
        Profile = profile;
        ExaminedParagraphCount = examinedParagraphCount;
        EligibleParagraphCount = eligibleParagraphCount;
        AmbiguousParagraphCount = ambiguousParagraphCount;
        Candidates = new ReadOnlyCollection<WordSemanticRoleCandidate>(candidates.ToArray());
        Issues = new ReadOnlyCollection<WordSemanticRoleIssue>(issues.ToArray());
        AnalysisExecutionComplete = analysisExecutionComplete;
        ModeledEvidenceCoverageComplete = modeledEvidenceCoverageComplete;
    }

    public string PackageFingerprint { get; }

    public string Profile { get; }

    public int ExaminedParagraphCount { get; }

    public int EligibleParagraphCount { get; }

    public int AmbiguousParagraphCount { get; }

    public IReadOnlyList<WordSemanticRoleCandidate> Candidates { get; }

    public IReadOnlyList<WordSemanticRoleIssue> Issues { get; }

    public bool AnalysisExecutionComplete { get; }

    public bool ModeledEvidenceCoverageComplete { get; }
}

public sealed record WordSemanticRoleGraphOptions
{
    public static WordSemanticRoleGraphOptions Default { get; } = new();

    public int MaxParagraphs { get; init; } = 100_000;

    public int MaxParagraphTextCharacters { get; init; } = 65_536;

    public int MaxEvidencePerParagraph { get; init; } = 32;

    public int MaxIssues { get; init; } = 1_000;

    public void Validate()
    {
        if (MaxParagraphs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParagraphs));
        }
        if (MaxParagraphTextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParagraphTextCharacters));
        }
        if (MaxEvidencePerParagraph <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEvidencePerParagraph));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
    }
}

public sealed class WordSemanticRoleGraphBuilder
{
    public const string ConservativePolishEnglishProfile = "conservative_pl_en_v1";

    private const string DeclarationPrefix = "wordtoolkit:role=";

    private static readonly IReadOnlyDictionary<WordSemanticRoleKind, string[]> Terms =
        new Dictionary<WordSemanticRoleKind, string[]>
        {
            [WordSemanticRoleKind.Theorem] = ["theorem", "twierdzenie"],
            [WordSemanticRoleKind.Lemma] = ["lemma", "lemat"],
            [WordSemanticRoleKind.Proposition] = ["proposition", "stwierdzenie"],
            [WordSemanticRoleKind.Corollary] = ["corollary", "wniosek"],
            [WordSemanticRoleKind.Definition] = ["definition", "definicja"],
            [WordSemanticRoleKind.Proof] = ["proof", "dowód"],
            [WordSemanticRoleKind.Example] = ["example", "przykład"],
            [WordSemanticRoleKind.Remark] = ["remark", "uwaga"],
            [WordSemanticRoleKind.Axiom] = ["axiom", "aksjomat"],
            [WordSemanticRoleKind.Assumption] = ["assumption", "założenie"],
        };

    private static readonly IReadOnlyDictionary<string, WordSemanticRoleKind> CanonicalRoles =
        Enum.GetValues<WordSemanticRoleKind>().ToDictionary(
            role => SnakeCase(role),
            role => role,
            StringComparer.Ordinal
        );

    private readonly WordSemanticRoleGraphOptions _options;

    public WordSemanticRoleGraphBuilder(WordSemanticRoleGraphOptions? options = null)
    {
        _options = options ?? WordSemanticRoleGraphOptions.Default;
        _options.Validate();
    }

    public WordSemanticRoleGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        WordStyleGraph styles,
        WordContentControlBindingGraph contentControls,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semantic);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(contentControls);
        if (!string.Equals(package.Fingerprint, semantic.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, styles.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(package.Fingerprint, contentControls.PackageFingerprint, StringComparison.Ordinal))
        {
            throw new WordSemanticRoleProjectionException(
                "Semantic-role inputs do not describe the same Word package."
            );
        }

        var nodes = semantic.Nodes.ToDictionary(node => node.Id);
        var paragraphs = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Paragraph)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (paragraphs.Length > _options.MaxParagraphs)
        {
            throw new WordSemanticRoleLimitException(
                $"Semantic-role paragraph count exceeds {_options.MaxParagraphs}."
            );
        }

        var controlsBySemanticNode = contentControls.Controls.ToDictionary(
            control => control.SemanticNodeId
        );
        var candidates = new List<WordSemanticRoleCandidate>();
        var issues = new List<WordSemanticRoleIssue>();
        var eligible = 0;
        var ambiguous = 0;
        var complete = true;
        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storyBlocks = new List<string>();
            var storyKind = WordNumberingRebuildCandidateInspector.ResolveStoryKind(
                paragraph,
                nodes,
                storyBlocks
            );
            var descendantAmbiguity = paragraph.DescendantsAndSelf().Any(node =>
                node.Kind is WordSemanticNodeKind.Revision
                    or WordSemanticNodeKind.AlternateContent
                    or WordSemanticNodeKind.ExtensionIsland
            );
            var viewAmbiguous = descendantAmbiguity
                || storyBlocks.Contains(
                    "revision_or_markup_compatibility_ancestry",
                    StringComparer.Ordinal
                );
            if (viewAmbiguous)
            {
                ambiguous++;
                complete = false;
            }
            if (storyBlocks.Contains("semantic_parent_cycle", StringComparer.Ordinal)
                || storyBlocks.Contains("semantic_parent_missing", StringComparer.Ordinal))
            {
                complete = false;
                AddIssue(
                    issues,
                    new WordSemanticRoleIssue(
                        "SEMANTIC_ROLE_PARENT_UNRESOLVED",
                        WordSemanticRoleIssueSeverity.Error,
                        "A paragraph story or ancestor chain could not be resolved.",
                        paragraph.Id,
                        storyKind,
                        paragraph.SourceOrder,
                        null
                    )
                );
                continue;
            }

            var text = ParagraphText(paragraph, _options.MaxParagraphTextCharacters);
            if (text is null)
            {
                complete = false;
                AddIssue(
                    issues,
                    new WordSemanticRoleIssue(
                        "SEMANTIC_ROLE_TEXT_LIMIT",
                        WordSemanticRoleIssueSeverity.Warning,
                        "A paragraph exceeded the bounded text limit and was not classified.",
                        paragraph.Id,
                        storyKind,
                        paragraph.SourceOrder,
                        null
                    )
                );
                continue;
            }
            eligible++;

            var evidence = new List<WordSemanticRoleEvidence>();
            AddContentControlEvidence(
                paragraph,
                nodes,
                controlsBySemanticNode,
                evidence
            );
            if (!AddStyleEvidence(paragraph, styles, evidence, issues, storyKind))
            {
                complete = false;
            }
            var lexical = MatchLeadingRole(text);
            if (lexical is not null)
            {
                evidence.Add(Evidence(
                    paragraph,
                    WordSemanticRoleEvidenceKind.LexicalLabel,
                    lexical.Value.Role,
                    authorDeclared: false,
                    contentControlId: null,
                    styleId: null,
                    lexical.Value.Term
                ));
            }
            evidence = evidence
                .GroupBy(item => new
                {
                    item.Kind,
                    item.Role,
                    item.ContentControlId,
                    item.StyleId,
                    item.ValueFingerprint,
                })
                .Select(group => group.First())
                .Take(_options.MaxEvidencePerParagraph + 1)
                .ToList();
            if (evidence.Count > _options.MaxEvidencePerParagraph)
            {
                throw new WordSemanticRoleLimitException(
                    $"Semantic-role evidence for one paragraph exceeds {_options.MaxEvidencePerParagraph}."
                );
            }
            if (evidence.Count == 0)
            {
                continue;
            }

            var roles = evidence.Select(item => item.Role).Distinct().ToArray();
            var classification = roles.Length > 1
                ? WordSemanticRoleClassification.Conflicting
                : evidence.Any(item => item.AuthorDeclared)
                    ? WordSemanticRoleClassification.Declared
                    : evidence.Any(item => item.Kind is not WordSemanticRoleEvidenceKind.LexicalLabel)
                        ? WordSemanticRoleClassification.StyleConvention
                        : WordSemanticRoleClassification.LexicalCandidate;
            var role = roles.Length == 1 ? roles[0] : (WordSemanticRoleKind?)null;
            var id = StableId(
                "wdsr_",
                package.Fingerprint,
                paragraph.Id.Value,
                paragraph.SourceOrder.ToString(CultureInfo.InvariantCulture)
            );
            var fingerprint = StableId(
                "wdsrf_",
                package.Fingerprint,
                paragraph.IdentityFingerprint,
                paragraph.SubtreeFingerprint,
                paragraph.StructuralFingerprint,
                string.Join(
                    "|",
                    evidence.Select(item => item.Id).Order(StringComparer.Ordinal)
                )
            );
            var candidate = new WordSemanticRoleCandidate(
                id,
                fingerprint,
                paragraph.Id,
                role,
                classification,
                storyKind,
                paragraph.SourceOrder,
                text.Length,
                lexical?.Term.Length,
                Sha256(text)[..16],
                viewAmbiguous,
                role is not null
                    && classification != WordSemanticRoleClassification.Conflicting
                    && !viewAmbiguous,
                evidence.OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ToArray()
            );
            candidates.Add(candidate);
            if (classification == WordSemanticRoleClassification.Conflicting)
            {
                complete = false;
                AddIssue(
                    issues,
                    new WordSemanticRoleIssue(
                        "SEMANTIC_ROLE_CONFLICT",
                        WordSemanticRoleIssueSeverity.Warning,
                        "Independent evidence channels assign different roles to one paragraph.",
                        paragraph.Id,
                        storyKind,
                        paragraph.SourceOrder,
                        id
                    )
                );
            }
            if (viewAmbiguous)
            {
                AddIssue(
                    issues,
                    new WordSemanticRoleIssue(
                        "SEMANTIC_ROLE_VIEW_AMBIGUOUS",
                        WordSemanticRoleIssueSeverity.Warning,
                        "Revision or Markup Compatibility content makes the candidate view ambiguous.",
                        paragraph.Id,
                        storyKind,
                        paragraph.SourceOrder,
                        id
                    )
                );
            }
        }

        return new WordSemanticRoleGraph(
            package.Fingerprint,
            ConservativePolishEnglishProfile,
            paragraphs.Length,
            eligible,
            ambiguous,
            candidates,
            issues,
            analysisExecutionComplete: true,
            modeledEvidenceCoverageComplete: complete
        );
    }

    public static bool TryParseRole(string value, out WordSemanticRoleKind role) =>
        CanonicalRoles.TryGetValue(value, out role);

    public static string RoleToken(WordSemanticRoleKind role) => SnakeCase(role);

    private void AddIssue(
        ICollection<WordSemanticRoleIssue> issues,
        WordSemanticRoleIssue issue
    )
    {
        if (issues.Count >= _options.MaxIssues)
        {
            throw new WordSemanticRoleLimitException(
                $"Semantic-role issue count exceeds {_options.MaxIssues}."
            );
        }
        issues.Add(issue);
    }

    private static void AddContentControlEvidence(
        WordSemanticNode paragraph,
        IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> nodes,
        IReadOnlyDictionary<SemanticNodeId, WordContentControlDefinition> controls,
        ICollection<WordSemanticRoleEvidence> evidence
    )
    {
        var related = new Dictionary<SemanticNodeId, WordSemanticNode>();
        // A run-level SDT nested inside the paragraph can describe one inline fragment,
        // not the semantic role of the enclosing paragraph. Only the paragraph itself
        // and its ancestors may declare the paragraph role.
        var current = paragraph;
        var visited = new HashSet<SemanticNodeId>();
        while (visited.Add(current.Id))
        {
            if (current.Kind == WordSemanticNodeKind.ContentControl)
            {
                related[current.Id] = current;
            }
            if (current.ParentId is not { } parentId
                || !nodes.TryGetValue(parentId, out current!))
            {
                break;
            }
        }

        foreach (var node in related.Values.OrderBy(node => node.SourceOrder))
        {
            if (!controls.TryGetValue(node.Id, out var control))
            {
                continue;
            }
            AddDeclaration(
                paragraph,
                control,
                WordSemanticRoleEvidenceKind.ContentControlTag,
                control.Tag,
                evidence
            );
            AddDeclaration(
                paragraph,
                control,
                WordSemanticRoleEvidenceKind.ContentControlAlias,
                control.Alias,
                evidence
            );
        }
    }

    private static void AddDeclaration(
        WordSemanticNode paragraph,
        WordContentControlDefinition control,
        WordSemanticRoleEvidenceKind kind,
        string? value,
        ICollection<WordSemanticRoleEvidence> evidence
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (!normalized.StartsWith(DeclarationPrefix, StringComparison.Ordinal))
        {
            return;
        }
        var token = normalized[DeclarationPrefix.Length..];
        if (!TryParseRole(token, out var role))
        {
            return;
        }
        evidence.Add(Evidence(
            paragraph,
            kind,
            role,
            authorDeclared: true,
            control.Id,
            styleId: null,
            normalized
        ));
    }

    private bool AddStyleEvidence(
        WordSemanticNode paragraph,
        WordStyleGraph styles,
        ICollection<WordSemanticRoleEvidence> evidence,
        ICollection<WordSemanticRoleIssue> issues,
        WordStoryKind storyKind
    )
    {
        if (!paragraph.Properties.TryGetValue("style_id", out var directStyleId)
            || string.IsNullOrWhiteSpace(directStyleId))
        {
            return true;
        }
        if (!styles.TryGetStyle(directStyleId, out var directStyle) || directStyle is null)
        {
            AddIssue(issues, new WordSemanticRoleIssue(
                "SEMANTIC_ROLE_STYLE_UNRESOLVED",
                WordSemanticRoleIssueSeverity.Warning,
                "A paragraph style could not be resolved for semantic-role evidence.",
                paragraph.Id,
                storyKind,
                paragraph.SourceOrder,
                null
            ));
            return false;
        }
        var complete = true;
        if (!directStyle.InheritanceResolvable)
        {
            complete = false;
            AddIssue(issues, new WordSemanticRoleIssue(
                "SEMANTIC_ROLE_STYLE_INHERITANCE_UNRESOLVED",
                WordSemanticRoleIssueSeverity.Warning,
                "A paragraph style inheritance chain is unresolved.",
                paragraph.Id,
                storyKind,
                paragraph.SourceOrder,
                null
            ));
        }

        AddStyleValue(
            paragraph,
            WordSemanticRoleEvidenceKind.DirectStyleId,
            directStyle.StyleId,
            directStyle.StyleId,
            evidence
        );
        AddStyleValue(
            paragraph,
            WordSemanticRoleEvidenceKind.StyleName,
            directStyle.Name,
            directStyle.StyleId,
            evidence
        );
        foreach (var alias in directStyle.Aliases)
        {
            AddStyleValue(
                paragraph,
                WordSemanticRoleEvidenceKind.StyleAlias,
                alias,
                directStyle.StyleId,
                evidence
            );
        }
        foreach (var inheritedId in directStyle.InheritanceChainStyleIds.SkipLast(1))
        {
            AddStyleValue(
                paragraph,
                WordSemanticRoleEvidenceKind.InheritedStyleId,
                inheritedId,
                inheritedId,
                evidence
            );
        }
        return complete;
    }

    private static void AddStyleValue(
        WordSemanticNode paragraph,
        WordSemanticRoleEvidenceKind kind,
        string? value,
        string styleId,
        ICollection<WordSemanticRoleEvidence> evidence
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        foreach (var pair in Terms)
        {
            if (!pair.Value.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            evidence.Add(Evidence(
                paragraph,
                kind,
                pair.Key,
                authorDeclared: false,
                contentControlId: null,
                styleId,
                normalized
            ));
        }
    }

    private static (WordSemanticRoleKind Role, string Term)? MatchLeadingRole(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).TrimStart();
        foreach (var pair in Terms)
        {
            foreach (var term in pair.Value.OrderByDescending(value => value.Length))
            {
                if (!normalized.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (normalized.Length == term.Length
                    || IsLabelBoundary(normalized[term.Length]))
                {
                    return (pair.Key, term);
                }
            }
        }
        return null;
    }

    private static bool IsLabelBoundary(char value) =>
        char.IsWhiteSpace(value)
        || char.IsDigit(value)
        || value is ':' or '.' or ';' or ',' or '(' or '[' or '{' or '-' or '–' or '—';

    private static WordSemanticRoleEvidence Evidence(
        WordSemanticNode paragraph,
        WordSemanticRoleEvidenceKind kind,
        WordSemanticRoleKind role,
        bool authorDeclared,
        string? contentControlId,
        string? styleId,
        string value
    ) => new(
        StableId(
            "wdsre_",
            paragraph.Id.Value,
            kind.ToString(),
            role.ToString(),
            contentControlId ?? string.Empty,
            styleId ?? string.Empty,
            Sha256(value)
        ),
        kind,
        role,
        authorDeclared,
        contentControlId,
        styleId,
        Sha256(value)[..16]
    );

    private static string? ParagraphText(WordSemanticNode paragraph, int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 256));
        foreach (var node in paragraph.DescendantsAndSelf())
        {
            var value = node.Kind switch
            {
                WordSemanticNodeKind.Text => node.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }
            if (builder.Length + value.Length > maximumCharacters)
            {
                return null;
            }
            builder.Append(value);
        }
        return builder.ToString();
    }

    private static string StableId(string prefix, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return prefix + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))
    ).ToLowerInvariant();

    private static string SnakeCase<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        var builder = new StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}

public class WordSemanticRoleException : InvalidOperationException
{
    public WordSemanticRoleException(string message)
        : base(message) { }
}

public sealed class WordSemanticRoleLimitException : WordSemanticRoleException
{
    public WordSemanticRoleLimitException(string message)
        : base(message) { }
}

public sealed class WordSemanticRoleProjectionException : WordSemanticRoleException
{
    public WordSemanticRoleProjectionException(string message)
        : base(message) { }
}
