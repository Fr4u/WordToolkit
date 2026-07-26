using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordNumberingRebuildMultiLevelKind
{
    SingleLevel,
    Multilevel,
    HybridMultilevel,
}

public enum WordNumberingRebuildFormat
{
    Decimal,
    DecimalZero,
    UpperRoman,
    LowerRoman,
    UpperLetter,
    LowerLetter,
    Bullet,
    None,
}

public enum WordNumberingRebuildSuffix
{
    Tab,
    Space,
    Nothing,
}

public enum WordNumberingRebuildJustification
{
    Left,
    Center,
    Right,
}

public enum WordNumberingRebuildRestartMode
{
    DefaultPreviousLevel,
    Never,
    AfterLevel,
}

public sealed record WordNumberingRebuildLevel(
    int LevelIndex,
    int StartValue,
    WordNumberingRebuildFormat NumberFormat,
    string LevelText,
    WordNumberingRebuildRestartMode RestartMode =
        WordNumberingRebuildRestartMode.DefaultPreviousLevel,
    int? RestartTriggerLevel = null,
    bool IsLegal = false,
    WordNumberingRebuildSuffix Suffix = WordNumberingRebuildSuffix.Tab,
    WordNumberingRebuildJustification Justification =
        WordNumberingRebuildJustification.Left,
    int? LeftIndentTwips = null,
    int? HangingIndentTwips = null,
    int? TabStopTwips = null
);

public sealed record WordNumberingRebuildTarget(
    SemanticNodeId ParagraphNodeId,
    string ExpectedCandidateFingerprint,
    int LevelIndex
);

public sealed record WordNumberingRebuildCommand(
    string CommandId,
    WordNumberingRebuildMultiLevelKind MultiLevelKind,
    bool RestartAfterSectionBreak,
    IReadOnlyList<WordNumberingRebuildLevel> Levels,
    IReadOnlyList<WordNumberingRebuildTarget> Targets
);

public sealed record WordNumberingRebuildOptions
{
    public static WordNumberingRebuildOptions Default { get; } = new();

    public int MaxCommands { get; init; } = 32;

    public int MaxTargets { get; init; } = 10_000;

    public int MaxChangedEntries { get; init; } = 64;

    public int MaxXmlPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxCandidateInspectionItems { get; init; } = 100;

    public int MaxIndentTwips { get; init; } = 31_680;

    internal void Validate()
    {
        if (MaxCommands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }
        if (MaxTargets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTargets));
        }
        if (MaxChangedEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedEntries));
        }
        if (MaxXmlPartBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxXmlPartBytes));
        }
        if (MaxCandidateInspectionItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidateInspectionItems));
        }
        if (MaxIndentTwips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIndentTwips));
        }
    }
}

public sealed class WordNumberingRebuildCandidate
{
    internal WordNumberingRebuildCandidate(
        SemanticNodeId paragraphNodeId,
        string fingerprint,
        WordStoryKind storyKind,
        string sourcePartUri,
        string sourcePath,
        int sourceOrder,
        int? currentNumberId,
        int? currentLevelIndex,
        IReadOnlyList<string> blockedReasons
    )
    {
        ParagraphNodeId = paragraphNodeId;
        Fingerprint = fingerprint;
        StoryKind = storyKind;
        SourcePartUri = sourcePartUri;
        SourcePath = sourcePath;
        SourceOrder = sourceOrder;
        CurrentNumberId = currentNumberId;
        CurrentLevelIndex = currentLevelIndex;
        BlockedReasons = new ReadOnlyCollection<string>(
            blockedReasons.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public SemanticNodeId ParagraphNodeId { get; }

    public string Fingerprint { get; }

    public WordStoryKind StoryKind { get; }

    public string SourcePartUri { get; }

    public string SourcePath { get; }

    public int SourceOrder { get; }

    public int? CurrentNumberId { get; }

    public int? CurrentLevelIndex { get; }

    public IReadOnlyList<string> BlockedReasons { get; }

    public bool CanRebuild => BlockedReasons.Count == 0;
}

public sealed class WordNumberingRebuildCandidateInspector
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private readonly WordNumberingRebuildOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordNumberingRebuildCandidateInspector(
        WordNumberingRebuildOptions? options = null
    )
    {
        _options = options ?? WordNumberingRebuildOptions.Default;
        _options.Validate();
        _xmlOptions = new LosslessXmlOptions
        {
            MaxSourceBytes = _options.MaxXmlPartBytes,
            MaxXmlCharacters = _options.MaxXmlPartBytes,
            MaxTextCharacters = _options.MaxXmlPartBytes,
        };
    }

    public IReadOnlyList<WordNumberingRebuildCandidate> Inspect(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlyList<SemanticNodeId> paragraphNodeIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(paragraphNodeIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticPreconditionException(
                "Candidate inspection requires package and semantic snapshots from the same document version."
            );
        }
        if (paragraphNodeIds.Count is < 1)
        {
            throw new ArgumentException("At least one paragraph node ID is required.");
        }
        if (paragraphNodeIds.Count > _options.MaxCandidateInspectionItems)
        {
            throw new WordSemanticTransactionLimitException(
                $"Candidate inspection requested {paragraphNodeIds.Count} paragraphs; the limit is {_options.MaxCandidateInspectionItems}."
            );
        }
        if (paragraphNodeIds.Distinct().Count() != paragraphNodeIds.Count)
        {
            throw new ArgumentException("Paragraph node IDs must be unique.");
        }

        var nodes = paragraphNodeIds.Select(id =>
        {
            if (!SemanticNodeId.HasValidSyntax(id.Value))
            {
                throw new ArgumentException($"Paragraph node ID '{id}' has invalid syntax.");
            }
            if (!semanticDocument.TryGetNode(id, out var node)
                || node is null
                || node.Kind != WordSemanticNodeKind.Paragraph)
            {
                throw new WordSemanticPreconditionException(
                    $"Semantic node '{id}' is not a current paragraph."
                );
            }
            return node;
        }).OrderBy(node => node.SourceOrder).ToArray();

        var parentById = semanticDocument.Nodes.ToDictionary(node => node.Id);
        var sources = nodes.Select(node => node.SourcePartUri)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                uri => uri,
                uri => ParsePart(package, uri, cancellationToken),
                StringComparer.Ordinal
            );
        return new ReadOnlyCollection<WordNumberingRebuildCandidate>(
            nodes.Select(node => InspectOne(
                package,
                node,
                parentById,
                sources[node.SourcePartUri]
            )).ToArray()
        );
    }

    internal static string CreateFingerprint(
        OpcPackageSnapshot package,
        WordSemanticNode paragraph,
        WordStoryKind storyKind,
        string numberingSignature
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "word-numbering-rebuild-candidate-v1");
        AppendHash(hash, package.Fingerprint);
        AppendHash(hash, paragraph.Id.Value);
        AppendHash(hash, paragraph.SourcePartUri);
        AppendHash(hash, paragraph.SourcePath);
        AppendHash(hash, paragraph.SourceOrder.ToString(CultureInfo.InvariantCulture));
        AppendHash(hash, paragraph.IdentityFingerprint);
        AppendHash(hash, paragraph.SubtreeFingerprint);
        AppendHash(hash, paragraph.StructuralFingerprint);
        AppendHash(hash, storyKind.ToString());
        AppendHash(hash, numberingSignature);
        return "wnrb_" + Convert.ToBase64String(hash.GetHashAndReset().AsSpan(0, 18))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static WordStoryKind ResolveStoryKind(
        WordSemanticNode paragraph,
        IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> nodes,
        ICollection<string>? blockedReasons = null
    )
    {
        var current = paragraph;
        var visited = new HashSet<SemanticNodeId>();
        while (true)
        {
            if (!visited.Add(current.Id))
            {
                blockedReasons?.Add("semantic_parent_cycle");
                return WordStoryKind.Other;
            }
            if (current.Kind is WordSemanticNodeKind.Revision
                or WordSemanticNodeKind.AlternateContent
                or WordSemanticNodeKind.ExtensionIsland)
            {
                blockedReasons?.Add("revision_or_markup_compatibility_ancestry");
            }
            var kind = current.Kind switch
            {
                WordSemanticNodeKind.Header => WordStoryKind.Header,
                WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                WordSemanticNodeKind.Document => WordStoryKind.Main,
                _ => (WordStoryKind?)null,
            };
            if (kind is not null)
            {
                return kind.Value;
            }
            if (current.ParentId is not { } parentId
                || !nodes.TryGetValue(parentId, out current!))
            {
                blockedReasons?.Add("semantic_parent_missing");
                return WordStoryKind.Other;
            }
        }
    }

    internal static ParagraphNumberingState ReadParagraphNumberingState(
        LosslessXmlDocument source,
        WordSemanticNode paragraph,
        ICollection<string> blockedReasons
    )
    {
        var element = source.GetParsedElement(paragraph.SourceElementOrdinal);
        if (!IsWordNamespace(element.Name.NamespaceName)
            || element.Name.LocalName != "p")
        {
            blockedReasons.Add("semantic_source_is_not_paragraph");
            return new ParagraphNumberingState(null, null, "invalid_source");
        }
        var w = element.Name.Namespace;
        var properties = element.Elements(w + "pPr").ToArray();
        if (properties.Length > 1)
        {
            blockedReasons.Add("duplicate_paragraph_properties");
            return new ParagraphNumberingState(null, null, "duplicate_pPr");
        }
        if (properties.Length == 0)
        {
            return new ParagraphNumberingState(null, null, "none");
        }
        if (properties[0].Elements(w + "pPrChange").Any())
        {
            blockedReasons.Add("tracked_paragraph_properties");
        }
        var numbering = properties[0].Elements(w + "numPr").ToArray();
        if (numbering.Length > 1)
        {
            blockedReasons.Add("duplicate_numbering_properties");
            return new ParagraphNumberingState(null, null, "duplicate_numPr");
        }
        if (numbering.Length == 0)
        {
            return new ParagraphNumberingState(null, null, "none");
        }
        var unknown = numbering[0].Elements().Where(child =>
            child.Name != w + "ilvl" && child.Name != w + "numId"
        ).ToArray();
        if (unknown.Length != 0)
        {
            blockedReasons.Add("tracked_or_unmodeled_numbering_properties");
        }
        var levels = numbering[0].Elements(w + "ilvl").ToArray();
        var numbers = numbering[0].Elements(w + "numId").ToArray();
        if (levels.Length > 1 || numbers.Length > 1)
        {
            blockedReasons.Add("duplicate_numbering_reference_children");
        }
        var level = levels.Length == 1 ? OptionalNonNegativeInt(levels[0], w + "val") : null;
        var number = numbers.Length == 1 ? OptionalNonNegativeInt(numbers[0], w + "val") : null;
        if ((levels.Length == 1 && level is null) || (numbers.Length == 1 && number is null))
        {
            blockedReasons.Add("invalid_numbering_reference");
        }
        var signature = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                numbering[0].ToString(SaveOptions.DisableFormatting)
            ))
        ).ToLowerInvariant();
        return new ParagraphNumberingState(number, level, signature);
    }

    private WordNumberingRebuildCandidate InspectOne(
        OpcPackageSnapshot package,
        WordSemanticNode node,
        IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> parentById,
        LosslessXmlDocument source
    )
    {
        var blocked = new List<string>();
        var storyKind = ResolveStoryKind(node, parentById, blocked);
        var numbering = ReadParagraphNumberingState(source, node, blocked);
        return new WordNumberingRebuildCandidate(
            node.Id,
            CreateFingerprint(package, node, storyKind, numbering.Signature),
            storyKind,
            node.SourcePartUri,
            node.SourcePath,
            node.SourceOrder,
            numbering.NumberId,
            numbering.LevelIndex,
            blocked
        );
    }

    private LosslessXmlDocument ParsePart(
        OpcPackageSnapshot package,
        string partUri,
        CancellationToken cancellationToken
    )
    {
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Paragraph source part '{partUri}' does not exist."
            );
        }
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"Paragraph source part '{partUri}' cannot be inspected losslessly.",
                exception
            );
        }
    }

    private static int? OptionalNonNegativeInt(XElement element, XName attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed >= 0
            ? parsed
            : null;
    }

    private static void AppendHash(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is WordTransitionalNamespace or WordStrictNamespace;

    internal sealed record ParagraphNumberingState(
        int? NumberId,
        int? LevelIndex,
        string Signature
    );
}

internal static class WordNumberingRebuildRules
{
    private static readonly Regex LevelPlaceholder = new(
        "%([1-9])",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );

    internal static void ValidateCommands(
        IReadOnlyList<WordNumberingRebuildCommand> commands,
        WordNumberingRebuildOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count is < 1 || commands.Count > options.MaxCommands)
        {
            throw new ArgumentException(
                $"Numbering rebuild requires between 1 and {options.MaxCommands} commands."
            );
        }
        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        var targetIds = new HashSet<SemanticNodeId>();
        var targetCount = 0;
        foreach (var command in commands)
        {
            ValidateCommandId(command.CommandId);
            if (!commandIds.Add(command.CommandId))
            {
                throw new ArgumentException(
                    $"Duplicate numbering rebuild command ID '{command.CommandId}'."
                );
            }
            ArgumentNullException.ThrowIfNull(command.Levels);
            ArgumentNullException.ThrowIfNull(command.Targets);
            if (command.Levels.Count is < 1 or > 9)
            {
                throw new ArgumentException(
                    $"Command '{command.CommandId}' must define between one and nine levels."
                );
            }
            if (command.Targets.Count < 1)
            {
                throw new ArgumentException(
                    $"Command '{command.CommandId}' must target at least one paragraph."
                );
            }
            targetCount = checked(targetCount + command.Targets.Count);
            if (targetCount > options.MaxTargets)
            {
                throw new WordSemanticTransactionLimitException(
                    $"Numbering rebuild targets {targetCount} paragraphs; the limit is {options.MaxTargets}."
                );
            }

            var levels = new Dictionary<int, WordNumberingRebuildLevel>();
            foreach (var level in command.Levels)
            {
                ValidateLevel(level, options);
                if (!levels.TryAdd(level.LevelIndex, level))
                {
                    throw new ArgumentException(
                        $"Command '{command.CommandId}' contains duplicate level {level.LevelIndex}."
                    );
                }
            }
            if (command.MultiLevelKind == WordNumberingRebuildMultiLevelKind.SingleLevel
                && (levels.Count != 1 || !levels.ContainsKey(0)))
            {
                throw new ArgumentException(
                    $"Single-level command '{command.CommandId}' must define only level 0."
                );
            }

            foreach (var target in command.Targets)
            {
                if (!SemanticNodeId.HasValidSyntax(target.ParagraphNodeId.Value))
                {
                    throw new ArgumentException(
                        $"Paragraph node ID '{target.ParagraphNodeId}' has invalid syntax."
                    );
                }
                if (!targetIds.Add(target.ParagraphNodeId))
                {
                    throw new ArgumentException(
                        $"Paragraph '{target.ParagraphNodeId}' is targeted more than once."
                    );
                }
                if (string.IsNullOrWhiteSpace(target.ExpectedCandidateFingerprint)
                    || !target.ExpectedCandidateFingerprint.StartsWith(
                        "wnrb_",
                        StringComparison.Ordinal
                    ))
                {
                    throw new ArgumentException(
                        $"Paragraph '{target.ParagraphNodeId}' requires a valid wnrb_ candidate fingerprint."
                    );
                }
                if (!levels.ContainsKey(target.LevelIndex))
                {
                    throw new ArgumentException(
                        $"Paragraph '{target.ParagraphNodeId}' uses undefined level {target.LevelIndex}."
                    );
                }
            }
        }
    }

    internal static int EffectiveLeftIndent(
        WordNumberingRebuildLevel level,
        WordNumberingRebuildOptions options
    ) => level.LeftIndentTwips ?? checked((level.LevelIndex + 1) * 720);

    internal static int EffectiveHangingIndent(
        WordNumberingRebuildLevel level
    ) => level.HangingIndentTwips ?? (level.NumberFormat == WordNumberingRebuildFormat.None
        ? 0
        : 360);

    internal static int EffectiveTabStop(
        WordNumberingRebuildLevel level,
        WordNumberingRebuildOptions options
    ) => level.TabStopTwips ?? EffectiveLeftIndent(level, options);

    internal static int? EffectiveRestartValue(WordNumberingRebuildLevel level) =>
        level.RestartMode switch
        {
            WordNumberingRebuildRestartMode.DefaultPreviousLevel => null,
            WordNumberingRebuildRestartMode.Never => 0,
            WordNumberingRebuildRestartMode.AfterLevel =>
                checked(level.RestartTriggerLevel!.Value + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(level.RestartMode)),
        };

    internal static string FormatToken(WordNumberingRebuildFormat format) => format switch
    {
        WordNumberingRebuildFormat.Decimal => "decimal",
        WordNumberingRebuildFormat.DecimalZero => "decimalZero",
        WordNumberingRebuildFormat.UpperRoman => "upperRoman",
        WordNumberingRebuildFormat.LowerRoman => "lowerRoman",
        WordNumberingRebuildFormat.UpperLetter => "upperLetter",
        WordNumberingRebuildFormat.LowerLetter => "lowerLetter",
        WordNumberingRebuildFormat.Bullet => "bullet",
        WordNumberingRebuildFormat.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static string MultiLevelToken(
        WordNumberingRebuildMultiLevelKind kind
    ) => kind switch
    {
        WordNumberingRebuildMultiLevelKind.SingleLevel => "singleLevel",
        WordNumberingRebuildMultiLevelKind.Multilevel => "multilevel",
        WordNumberingRebuildMultiLevelKind.HybridMultilevel => "hybridMultilevel",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string SuffixToken(WordNumberingRebuildSuffix suffix) => suffix switch
    {
        WordNumberingRebuildSuffix.Tab => "tab",
        WordNumberingRebuildSuffix.Space => "space",
        WordNumberingRebuildSuffix.Nothing => "nothing",
        _ => throw new ArgumentOutOfRangeException(nameof(suffix)),
    };

    internal static string JustificationToken(
        WordNumberingRebuildJustification justification
    ) => justification switch
    {
        WordNumberingRebuildJustification.Left => "left",
        WordNumberingRebuildJustification.Center => "center",
        WordNumberingRebuildJustification.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(justification)),
    };

    private static void ValidateCommandId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'
            ))
        {
            throw new ArgumentException(
                "Numbering rebuild command IDs must contain 1-64 ASCII letters, digits, underscores or hyphens and start with a letter or digit."
            );
        }
    }

    private static void ValidateLevel(
        WordNumberingRebuildLevel level,
        WordNumberingRebuildOptions options
    )
    {
        if (level.LevelIndex is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level.LevelIndex),
                "Numbering levels must be between 0 and 8."
            );
        }
        if (level.StartValue < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level.StartValue),
                "Numbering starts cannot be negative."
            );
        }
        ArgumentNullException.ThrowIfNull(level.LevelText);
        if (level.LevelText.Length > 31
            || level.LevelText.Any(character => character is '\0' or '\r' or '\n'))
        {
            throw new ArgumentException(
                $"Level {level.LevelIndex} text must be a single line of at most 31 characters."
            );
        }
        var placeholders = LevelPlaceholder.Matches(level.LevelText);
        if (placeholders.Any(match =>
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                > level.LevelIndex + 1
        ))
        {
            throw new ArgumentException(
                $"Level {level.LevelIndex} text references a deeper numbering level."
            );
        }
        if (level.NumberFormat == WordNumberingRebuildFormat.Bullet)
        {
            if (CountUnicodeScalars(level.LevelText) != 1 || placeholders.Count != 0)
            {
                throw new ArgumentException(
                    $"Bullet level {level.LevelIndex} must contain exactly one Unicode scalar and no counter placeholder."
                );
            }
        }
        else if (level.NumberFormat == WordNumberingRebuildFormat.None)
        {
            if (level.LevelText.Length != 0)
            {
                throw new ArgumentException(
                    $"Hidden level {level.LevelIndex} must use empty level text."
                );
            }
        }
        else if (!placeholders.Any(match =>
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                == level.LevelIndex + 1
        ))
        {
            throw new ArgumentException(
                $"Numbered level {level.LevelIndex} must contain its own %{level.LevelIndex + 1} placeholder."
            );
        }

        if (level.RestartMode == WordNumberingRebuildRestartMode.AfterLevel)
        {
            if (level.RestartTriggerLevel is null
                || level.RestartTriggerLevel < 0
                || level.RestartTriggerLevel >= level.LevelIndex)
            {
                throw new ArgumentException(
                    $"Level {level.LevelIndex} restart trigger must identify a higher zero-based level."
                );
            }
        }
        else if (level.RestartTriggerLevel is not null)
        {
            throw new ArgumentException(
                $"Level {level.LevelIndex} supplies a restart trigger without after-level restart mode."
            );
        }
        if (level.LevelIndex == 0
            && level.RestartMode == WordNumberingRebuildRestartMode.AfterLevel)
        {
            throw new ArgumentException("Level 0 cannot restart after a higher level.");
        }

        var left = EffectiveLeftIndent(level, options);
        var hanging = EffectiveHangingIndent(level);
        var tab = EffectiveTabStop(level, options);
        if (left is < 0 || left > options.MaxIndentTwips
            || hanging is < 0 || hanging > options.MaxIndentTwips
            || tab is < 0 || tab > options.MaxIndentTwips
            || hanging > left)
        {
            throw new ArgumentException(
                $"Level {level.LevelIndex} indentation must be non-negative, bounded, and hanging cannot exceed the left indent."
            );
        }
    }

    private static int CountUnicodeScalars(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }
        return count;
    }
}
