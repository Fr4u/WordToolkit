using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed class WordSemanticProjector
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";
    private const string MarkupCompatibilityNamespace =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string RelationshipsTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipsStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";

    private readonly WordSemanticProjectionOptions _options;
    public WordSemanticProjector(WordSemanticProjectionOptions? options = null)
    {
        _options = options ?? WordSemanticProjectionOptions.Default;
        _options.Validate();
    }

    public WordSemanticDocument Project(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        var state = new ProjectionState();

        var officeRelationships = package.Relationships
            .Where(relationship =>
                relationship.SourcePartUri == "/"
                && relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.Type.EndsWith(
                    "/officeDocument",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (officeRelationships.Length == 0)
        {
            throw new WordSemanticProjectionException(
                "Package has no internal officeDocument relationship."
            );
        }

        if (officeRelationships.Length > 1)
        {
            throw new WordSemanticProjectionException(
                "Package has multiple officeDocument relationships."
            );
        }

        var mainPartUri = officeRelationships[0].ResolvedTargetPartUri;
        if (mainPartUri is null || !package.Parts.TryGetValue(mainPartUri, out var mainPart))
        {
            throw new WordSemanticProjectionException(
                "The officeDocument relationship does not resolve to an existing part."
            );
        }

        if (!IsWordMainContentType(mainPart.ContentType))
        {
            throw new WordSemanticProjectionException(
                $"Main part content type '{mainPart.ContentType ?? "(missing)"}' is not a Word main-document type."
            );
        }

        var sourceDocument = ParseSourcePart(mainPart, cancellationToken);

        var xml = sourceDocument.ParsedDocument;
        var documentElement = xml.Root;
        if (
            documentElement is null
            || !IsWordNamespace(documentElement.Name.NamespaceName)
            || documentElement.Name.LocalName != "document"
        )
        {
            throw new WordSemanticProjectionException(
                "The Word main part does not have a w:document root element."
            );
        }

        var roots = new List<MutableSemanticNode>();
        var rootContext = new ProjectionContext(
            parent: null,
            identityContext: mainPartUri,
            occurrences: new Dictionary<string, int>(StringComparer.Ordinal),
            roots
        );
        ProjectElement(
            documentElement,
            mainPartUri,
            sourcePath: $"/{QualifiedName(documentElement.Name)}[1]",
            rootContext,
            state,
            sourceDocument,
            cancellationToken
        );
        if (roots.Count != 1 || roots[0].Kind != WordSemanticNodeKind.Document)
        {
            throw new WordSemanticProjectionException(
                "Semantic projection did not produce exactly one document root."
            );
        }

        ProjectRelatedStories(
            package,
            mainPartUri,
            roots[0],
            roots,
            state,
            cancellationToken
        );
        var frozenRoot = roots[0].Freeze();
        var warnings = package.Diagnostics
            .Where(diagnostic => diagnostic.Severity == OpcDiagnosticSeverity.Warning)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToArray();
        return new WordSemanticDocument(
            package.Fingerprint,
            mainPartUri,
            frozenRoot,
            warnings
        );
    }

    private LosslessXmlDocument ParseSourcePart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxXmlCharacters >= int.MaxValue / 4
                        ? int.MaxValue
                        : checked((int)(_options.MaxXmlCharacters * 4)),
                    MaxXmlCharacters = _options.MaxXmlCharacters,
                    MaxXmlElements = _options.MaxXmlElements,
                    MaxXmlDepth = _options.MaxXmlDepth,
                    MaxTextCharacters = _options.MaxTextCharacters,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordSemanticLimitException(
                $"Word part '{part.Uri}' exceeds a semantic projection limit: "
                    + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticProjectionException(
                $"Word part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private void ProjectRelatedStories(
        OpcPackageSnapshot package,
        string mainPartUri,
        MutableSemanticNode mainRoot,
        List<MutableSemanticNode> roots,
        ProjectionState state,
        CancellationToken cancellationToken
    )
    {
        var storyRelationships = new List<StoryRelationship>();
        foreach (var relationship in package.RelationshipsFrom(mainPartUri))
        {
            if (!TryDescribeStoryRelationship(relationship.Type, out var descriptor))
            {
                continue;
            }

            if (
                relationship.TargetMode != OpcRelationshipTargetMode.Internal
                || relationship.ResolvedTargetPartUri is null
            )
            {
                throw new WordSemanticProjectionException(
                    $"Story relationship '{relationship.Id}' from '{mainPartUri}' "
                        + "does not resolve to an internal package part."
                );
            }

            storyRelationships.Add(new StoryRelationship(relationship, descriptor));
            if (storyRelationships.Count > _options.MaxStoryRelationships)
            {
                throw new WordSemanticLimitException(
                    "Word package contains more than "
                        + $"{_options.MaxStoryRelationships} text-story relationships."
                );
            }
        }

        var groups = storyRelationships
            .GroupBy(
                item => item.Relationship.ResolvedTargetPartUri!,
                StringComparer.Ordinal
            )
            .ToArray();
        if (groups.Length > _options.MaxStoryParts)
        {
            throw new WordSemanticLimitException(
                $"Word package references {groups.Length} text-bearing story parts; "
                    + $"limit is {_options.MaxStoryParts}."
            );
        }

        foreach (
            var group in groups
                .OrderBy(item => item.Min(value => value.Descriptor.Order))
                .ThenBy(item => item.Key, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptors = group.Select(item => item.Descriptor).Distinct().ToArray();
            if (descriptors.Length != 1)
            {
                throw new WordSemanticProjectionException(
                    $"Story part '{group.Key}' is targeted by conflicting relationship types."
                );
            }

            var descriptor = descriptors[0];
            if (!package.Parts.TryGetValue(group.Key, out var part))
            {
                throw new WordSemanticProjectionException(
                    $"Story relationship target '{group.Key}' does not exist."
                );
            }

            if (
                part.ContentType is null
                || !string.Equals(
                    part.ContentType,
                    descriptor.ExpectedContentType,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new WordSemanticProjectionException(
                    $"Story part '{part.Uri}' has content type "
                        + $"'{part.ContentType ?? "(missing)"}', expected a "
                        + $"'{descriptor.Name}' Word part."
                );
            }

            var source = ParseSourcePart(part, cancellationToken);
            var storyElement = source.ParsedDocument.Root;
            if (
                storyElement is null
                || !IsWordNamespace(storyElement.Name.NamespaceName)
                || storyElement.Name.LocalName != descriptor.RootElementName
            )
            {
                throw new WordSemanticProjectionException(
                    $"Story part '{part.Uri}' does not have the expected "
                        + $"w:{descriptor.RootElementName} root element."
                );
            }

            var previousChildCount = mainRoot.Children.Count;
            var storyContext = new ProjectionContext(
                mainRoot,
                identityContext: part.Uri,
                occurrences: new Dictionary<string, int>(StringComparer.Ordinal),
                roots
            );
            ProjectElement(
                storyElement,
                part.Uri,
                sourcePath: $"/{QualifiedName(storyElement.Name)}[1]",
                storyContext,
                state,
                source,
                cancellationToken
            );
            if (
                mainRoot.Children.Count != previousChildCount + 1
                || mainRoot.Children[^1].Kind != descriptor.RootKind
            )
            {
                throw new WordSemanticProjectionException(
                    $"Story part '{part.Uri}' did not produce one "
                        + $"{descriptor.RootKind} semantic root."
                );
            }

            var semanticStory = mainRoot.Children[^1];
            var relationshipIds = group.Select(item => item.Relationship.Id)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            semanticStory.Properties["story_kind"] = descriptor.Name;
            semanticStory.Properties["relationship_count"] = relationshipIds.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            semanticStory.Properties["relationship_ids"] = string.Join(
                ",",
                relationshipIds.Take(32)
            );
            if (relationshipIds.Length > 32)
            {
                semanticStory.Properties["relationship_ids_truncated"] = "true";
            }
            if (relationshipIds.Length == 1)
            {
                semanticStory.Properties["relationship_id"] = relationshipIds[0];
            }
        }
    }

    private void ProjectElement(
        XElement element,
        string sourcePartUri,
        string sourcePath,
        ProjectionContext context,
        ProjectionState state,
        LosslessXmlDocument sourceDocument,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kind = Classify(element);
        var childContext = context;
        if (kind is not null)
        {
            if (++state.SemanticNodeCount > _options.MaxXmlElements)
            {
                throw new WordSemanticLimitException(
                    $"Semantic projection exceeds {_options.MaxXmlElements} nodes."
                );
            }

            var anchor = DurableAnchor(element, kind.Value);
            var fingerprint = Fingerprint(element, state);
            var signature = anchor ?? $"fp:{fingerprint}";
            var occurrenceKey = $"{kind}:{signature}";
            var occurrence = context.occurrences.TryGetValue(occurrenceKey, out var count)
                ? count + 1
                : 1;
            context.occurrences[occurrenceKey] = occurrence;
            var id = CreateNodeId(
                sourcePartUri,
                kind.Value,
                context.identityContext,
                signature,
                occurrence
            );
            var mutable = new MutableSemanticNode(
                id,
                kind.Value,
                context.parent?.Id,
                state.SemanticNodeCount,
                sourceDocument.GetElementOrdinal(element),
                sourcePartUri,
                sourcePath,
                NodeText(element, kind.Value),
                NodeProperties(element, kind.Value)
            );
            if (context.parent is null)
            {
                context.roots.Add(mutable);
            }
            else
            {
                context.parent.Children.Add(mutable);
            }

            var nextIdentityContext = kind.Value is WordSemanticNodeKind.Document
                or WordSemanticNodeKind.Body
                ? $"{context.identityContext}/{kind.Value}"
                : $"{context.identityContext}/{kind.Value}:{id.Value}";
            childContext = new ProjectionContext(
                mutable,
                nextIdentityContext,
                new Dictionary<string, int>(StringComparer.Ordinal),
                context.roots
            );
        }

        var nameOccurrences = new Dictionary<XName, int>();
        foreach (var child in element.Elements())
        {
            var index = nameOccurrences.TryGetValue(child.Name, out var count)
                ? count + 1
                : 1;
            nameOccurrences[child.Name] = index;
            ProjectElement(
                child,
                sourcePartUri,
                $"{sourcePath}/{QualifiedName(child.Name)}[{index}]",
                childContext,
                state,
                sourceDocument,
                cancellationToken
            );
        }
    }

    private static WordSemanticNodeKind? Classify(XElement element)
    {
        var namespaceName = element.Name.NamespaceName;
        var localName = element.Name.LocalName;
        if (IsWordNamespace(namespaceName))
        {
            return localName switch
            {
                "document" => WordSemanticNodeKind.Document,
                "body" => WordSemanticNodeKind.Body,
                "hdr" => WordSemanticNodeKind.Header,
                "ftr" => WordSemanticNodeKind.Footer,
                "footnotes" => WordSemanticNodeKind.Footnotes,
                "footnote" => WordSemanticNodeKind.Footnote,
                "endnotes" => WordSemanticNodeKind.Endnotes,
                "endnote" => WordSemanticNodeKind.Endnote,
                "comments" => WordSemanticNodeKind.Comments,
                "comment" => WordSemanticNodeKind.Comment,
                "glossaryDocument" => WordSemanticNodeKind.GlossaryDocument,
                "docPart" => WordSemanticNodeKind.GlossaryEntry,
                "txbxContent" => WordSemanticNodeKind.TextBox,
                "sectPr" => WordSemanticNodeKind.Section,
                "headerReference" => WordSemanticNodeKind.HeaderReference,
                "footerReference" => WordSemanticNodeKind.FooterReference,
                "footnoteReference" => WordSemanticNodeKind.FootnoteReference,
                "endnoteReference" => WordSemanticNodeKind.EndnoteReference,
                "p" => WordSemanticNodeKind.Paragraph,
                "r" => WordSemanticNodeKind.Run,
                "t" or "delText" => WordSemanticNodeKind.Text,
                "tab" => WordSemanticNodeKind.Tab,
                "br" or "cr" => WordSemanticNodeKind.Break,
                "tbl" => WordSemanticNodeKind.Table,
                "tr" => WordSemanticNodeKind.TableRow,
                "tc" => WordSemanticNodeKind.TableCell,
                "hyperlink" => WordSemanticNodeKind.Hyperlink,
                "fldSimple" or "fldChar" or "instrText" => WordSemanticNodeKind.Field,
                "sdt" => WordSemanticNodeKind.ContentControl,
                "bookmarkStart" => WordSemanticNodeKind.Bookmark,
                "commentRangeStart" or "commentRangeEnd" or "commentReference" =>
                    WordSemanticNodeKind.CommentAnchor,
                "ins" or "del" or "moveFrom" or "moveTo" =>
                    WordSemanticNodeKind.Revision,
                "drawing" or "pict" or "object" => WordSemanticNodeKind.Drawing,
                _ => null,
            };
        }

        if (IsMathNamespace(namespaceName))
        {
            return localName switch
            {
                "oMath" or "oMathPara" => WordSemanticNodeKind.Equation,
                "t" => WordSemanticNodeKind.Text,
                _ => WordSemanticNodeKind.EquationComponent,
            };
        }

        if (
            namespaceName == MarkupCompatibilityNamespace
            && localName == "AlternateContent"
        )
        {
            return WordSemanticNodeKind.AlternateContent;
        }

        if (IsDrawingNamespace(namespaceName))
        {
            return WordSemanticNodeKind.Drawing;
        }

        return string.IsNullOrEmpty(namespaceName)
            ? null
            : WordSemanticNodeKind.ExtensionIsland;
    }

    private static string Fingerprint(XElement element, ProjectionState state)
    {
        if (state.Fingerprints.TryGetValue(element, out var cached))
        {
            return cached;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, element.Name.NamespaceName);
        Append(hash, element.Name.LocalName);
        foreach (
            var attribute in element.Attributes()
                .Where(attribute =>
                    !attribute.IsNamespaceDeclaration
                    && !attribute.Name.LocalName.StartsWith(
                        "rsid",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
        )
        {
            Append(hash, attribute.Name.NamespaceName);
            Append(hash, attribute.Name.LocalName);
            Append(hash, attribute.Value);
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    Append(hash, Fingerprint(child, state));
                    break;
                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    Append(hash, text.Value);
                    break;
            }
        }

        var fingerprint = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
        state.Fingerprints[element] = fingerprint;
        return fingerprint;
    }

    private static string? DurableAnchor(
        XElement element,
        WordSemanticNodeKind kind
    )
    {
        if (
            kind is WordSemanticNodeKind.Header
                or WordSemanticNodeKind.Footer
                or WordSemanticNodeKind.Footnotes
                or WordSemanticNodeKind.Endnotes
                or WordSemanticNodeKind.Comments
                or WordSemanticNodeKind.GlossaryDocument
        )
        {
            return $"story-root:{kind}";
        }

        if (kind == WordSemanticNodeKind.Section)
        {
            return "section-properties";
        }

        var paragraphId = element.Attribute(XName.Get("paraId", Word2010Namespace))?.Value;
        if (!string.IsNullOrWhiteSpace(paragraphId))
        {
            return $"para:{paragraphId}";
        }

        var textId = element.Attribute(XName.Get("textId", Word2010Namespace))?.Value;
        if (!string.IsNullOrWhiteSpace(textId))
        {
            return $"text:{textId}";
        }

        var wordNamespace = element.Name.Namespace;
        var id = element.Attribute(wordNamespace + "id")?.Value;
        if (
            !string.IsNullOrWhiteSpace(id)
            && kind is WordSemanticNodeKind.Bookmark
                or WordSemanticNodeKind.CommentAnchor
                or WordSemanticNodeKind.Revision
                or WordSemanticNodeKind.Footnote
                or WordSemanticNodeKind.Endnote
                or WordSemanticNodeKind.Comment
                or WordSemanticNodeKind.FootnoteReference
                or WordSemanticNodeKind.EndnoteReference
        )
        {
            return $"{kind}:{id}";
        }

        if (kind is WordSemanticNodeKind.HeaderReference or WordSemanticNodeKind.FooterReference)
        {
            var relationshipId = RelationshipId(element);
            if (!string.IsNullOrWhiteSpace(relationshipId))
            {
                return $"{kind}:{relationshipId}";
            }
        }

        if (kind == WordSemanticNodeKind.GlossaryEntry)
        {
            var properties = element.Elements()
                .FirstOrDefault(child =>
                    IsWordNamespace(child.Name.NamespaceName)
                    && child.Name.LocalName == "docPartPr"
                );
            var guid = properties?.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "guid")
                ?.Attribute(wordNamespace + "val")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(guid))
            {
                return $"glossary-guid:{guid}";
            }
        }

        if (kind == WordSemanticNodeKind.ContentControl)
        {
            var properties = element.Elements()
                .FirstOrDefault(child =>
                    IsWordNamespace(child.Name.NamespaceName)
                    && child.Name.LocalName == "sdtPr"
                );
            var tag = properties?.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "tag")
                ?.Attribute(wordNamespace + "val")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return $"sdt-tag:{tag}";
            }

            var controlId = properties?.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "id")
                ?.Attribute(wordNamespace + "val")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(controlId))
            {
                return $"sdt-id:{controlId}";
            }
        }

        return null;
    }

    private static Dictionary<string, string> NodeProperties(
        XElement element,
        WordSemanticNodeKind kind
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordNamespace = element.Name.Namespace;
        AddIfPresent(
            result,
            "paragraph_id",
            element.Attribute(XName.Get("paraId", Word2010Namespace))?.Value
        );
        if (kind == WordSemanticNodeKind.Paragraph)
        {
            var style = element.Elements(wordNamespace + "pPr")
                .Elements(wordNamespace + "pStyle")
                .Attributes(wordNamespace + "val")
                .Select(attribute => attribute.Value)
                .FirstOrDefault();
            AddIfPresent(result, "style_id", style);
        }
        else if (kind == WordSemanticNodeKind.Run)
        {
            var style = element.Elements(wordNamespace + "rPr")
                .Elements(wordNamespace + "rStyle")
                .Attributes(wordNamespace + "val")
                .Select(attribute => attribute.Value)
                .FirstOrDefault();
            AddIfPresent(result, "style_id", style);
        }
        else if (kind == WordSemanticNodeKind.Hyperlink)
        {
            AddIfPresent(
                result,
                "relationship_id",
                RelationshipId(element)
            );
            AddIfPresent(
                result,
                "anchor",
                element.Attribute(wordNamespace + "anchor")?.Value
            );
        }
        else if (kind == WordSemanticNodeKind.Field)
        {
            AddIfPresent(
                result,
                "instruction",
                element.Attribute(wordNamespace + "instr")?.Value
            );
            AddIfPresent(
                result,
                "field_character_type",
                element.Attribute(wordNamespace + "fldCharType")?.Value
            );
        }
        else if (
            kind is WordSemanticNodeKind.Footnote
                or WordSemanticNodeKind.Endnote
                or WordSemanticNodeKind.Comment
                or WordSemanticNodeKind.CommentAnchor
                or WordSemanticNodeKind.FootnoteReference
                or WordSemanticNodeKind.EndnoteReference
        )
        {
            AddIfPresent(result, "id", element.Attribute(wordNamespace + "id")?.Value);
            AddIfPresent(
                result,
                "type",
                element.Attribute(wordNamespace + "type")?.Value
            );
            if (kind == WordSemanticNodeKind.Comment)
            {
                AddIfPresent(
                    result,
                    "author",
                    element.Attribute(wordNamespace + "author")?.Value
                );
                AddIfPresent(
                    result,
                    "initials",
                    element.Attribute(wordNamespace + "initials")?.Value
                );
                AddIfPresent(
                    result,
                    "date",
                    element.Attribute(wordNamespace + "date")?.Value
                );
            }
        }
        else if (
            kind is WordSemanticNodeKind.HeaderReference
                or WordSemanticNodeKind.FooterReference
        )
        {
            AddIfPresent(result, "relationship_id", RelationshipId(element));
            AddIfPresent(
                result,
                "type",
                element.Attribute(wordNamespace + "type")?.Value
            );
        }
        else if (kind == WordSemanticNodeKind.GlossaryEntry)
        {
            var properties = element.Elements(wordNamespace + "docPartPr").FirstOrDefault();
            AddIfPresent(
                result,
                "name",
                properties?.Elements(wordNamespace + "name")
                    .Attributes(wordNamespace + "val")
                    .Select(attribute => attribute.Value)
                    .FirstOrDefault()
            );
            AddIfPresent(
                result,
                "guid",
                properties?.Elements(wordNamespace + "guid")
                    .Attributes(wordNamespace + "val")
                    .Select(attribute => attribute.Value)
                    .FirstOrDefault()
            );
        }
        else if (kind == WordSemanticNodeKind.Section)
        {
            var sectionType = element.Elements(wordNamespace + "type").FirstOrDefault();
            AddIfPresent(
                result,
                "break_type",
                sectionType?.Attribute(wordNamespace + "val")?.Value ?? "nextPage"
            );
            var titlePage = element.Elements(wordNamespace + "titlePg").FirstOrDefault();
            result["title_page"] = NormalizeOnOffValue(titlePage);

            var pageSize = element.Elements(wordNamespace + "pgSz").FirstOrDefault();
            AddWordAttribute(result, pageSize, "w", "page_width_twips");
            AddWordAttribute(result, pageSize, "h", "page_height_twips");
            AddWordAttribute(result, pageSize, "orient", "page_orientation");

            var pageMargins = element.Elements(wordNamespace + "pgMar").FirstOrDefault();
            foreach (
                var (attributeName, propertyName) in new[]
                {
                    ("top", "margin_top_twips"),
                    ("right", "margin_right_twips"),
                    ("bottom", "margin_bottom_twips"),
                    ("left", "margin_left_twips"),
                    ("header", "margin_header_twips"),
                    ("footer", "margin_footer_twips"),
                    ("gutter", "margin_gutter_twips"),
                }
            )
            {
                AddWordAttribute(result, pageMargins, attributeName, propertyName);
            }

            var columns = element.Elements(wordNamespace + "cols").FirstOrDefault();
            AddWordAttribute(result, columns, "num", "column_count");
            AddWordAttribute(result, columns, "space", "column_spacing_twips");
            AddWordAttribute(result, columns, "equalWidth", "columns_equal_width");
            AddWordAttribute(result, columns, "sep", "columns_separator");

            var pageNumbering = element.Elements(wordNamespace + "pgNumType").FirstOrDefault();
            AddWordAttribute(result, pageNumbering, "start", "page_number_start");
            AddWordAttribute(result, pageNumbering, "fmt", "page_number_format");
            AddWordAttribute(result, pageNumbering, "chapStyle", "chapter_style_level");
            AddWordAttribute(result, pageNumbering, "chapSep", "chapter_separator");

            AddIfPresent(
                result,
                "vertical_alignment",
                element.Elements(wordNamespace + "vAlign")
                    .Attributes(wordNamespace + "val")
                    .Select(attribute => attribute.Value)
                    .FirstOrDefault()
            );
            AddIfPresent(
                result,
                "text_direction",
                element.Elements(wordNamespace + "textDirection")
                    .Attributes(wordNamespace + "val")
                    .Select(attribute => attribute.Value)
                    .FirstOrDefault()
            );
            result["header_reference_count"] = element.Elements(
                wordNamespace + "headerReference"
            ).Count().ToString(System.Globalization.CultureInfo.InvariantCulture);
            result["footer_reference_count"] = element.Elements(
                wordNamespace + "footerReference"
            ).Count().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (kind == WordSemanticNodeKind.EquationComponent)
        {
            result["math_element"] = element.Name.LocalName;
        }
        else if (kind == WordSemanticNodeKind.ExtensionIsland)
        {
            result["namespace"] = element.Name.NamespaceName;
            result["element"] = element.Name.LocalName;
        }

        return result;
    }

    private static string? NodeText(XElement element, WordSemanticNodeKind kind) =>
        kind switch
        {
            WordSemanticNodeKind.Text => element.Value,
            WordSemanticNodeKind.Field when element.Name.LocalName == "instrText" =>
                element.Value,
            WordSemanticNodeKind.Tab => "\t",
            WordSemanticNodeKind.Break => "\n",
            _ => null,
        };

    private static SemanticNodeId CreateNodeId(
        string sourcePartUri,
        WordSemanticNodeKind kind,
        string identityContext,
        string signature,
        int occurrence
    )
    {
        var material = string.Join(
            '\u001f',
            sourcePartUri,
            kind.ToString(),
            identityContext,
            signature,
            occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var encoded = Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new SemanticNodeId("wdn_" + encoded);
    }

    private static string QualifiedName(XName name)
    {
        var prefix = name.NamespaceName switch
        {
            WordTransitionalNamespace or WordStrictNamespace => "w",
            MathTransitionalNamespace or MathStrictNamespace => "m",
            MarkupCompatibilityNamespace => "mc",
            _ when IsDrawingNamespace(name.NamespaceName) => "draw",
            "" => "none",
            _ => "ns" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(name.NamespaceName)).AsSpan(0, 3)
            ).ToLowerInvariant(),
        };
        return $"{prefix}:{name.LocalName}";
    }

    private static bool IsWordNamespace(string namespaceName) =>
        namespaceName is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsMathNamespace(string namespaceName) =>
        namespaceName is MathTransitionalNamespace or MathStrictNamespace;

    private static bool IsDrawingNamespace(string namespaceName) =>
        namespaceName.Contains("/drawingml/", StringComparison.Ordinal)
        || namespaceName.EndsWith("/wordprocessingDrawing", StringComparison.Ordinal);

    private static bool IsWordMainContentType(string? contentType) =>
        contentType is not null
        && contentType.EndsWith(".main+xml", StringComparison.OrdinalIgnoreCase)
        && (
            contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("ms-word", StringComparison.OrdinalIgnoreCase)
        );

    private static string? RelationshipId(XElement element) =>
        element.Attribute(XName.Get("id", RelationshipsTransitionalNamespace))?.Value
        ?? element.Attribute(XName.Get("id", RelationshipsStrictNamespace))?.Value;

    private static void AddWordAttribute(
        IDictionary<string, string> properties,
        XElement? element,
        string attributeName,
        string propertyName
    )
    {
        if (element is null)
        {
            return;
        }

        AddIfPresent(
            properties,
            propertyName,
            element.Attribute(element.Name.Namespace + attributeName)?.Value
        );
    }

    private static string NormalizeOnOffValue(XElement? element)
    {
        if (element is null)
        {
            return "false";
        }

        var value = element.Attribute(element.Name.Namespace + "val")?.Value;
        return value?.ToLowerInvariant() switch
        {
            null or "true" or "1" or "on" => "true",
            "false" or "0" or "off" => "false",
            _ => "invalid:" + value,
        };
    }

    private static bool TryDescribeStoryRelationship(
        string relationshipType,
        out StoryPartDescriptor descriptor
    )
    {
        string? name = null;
        foreach (
            var relationshipNamespace in new[]
            {
                RelationshipsTransitionalNamespace,
                RelationshipsStrictNamespace,
            }
        )
        {
            var prefix = relationshipNamespace + "/";
            if (relationshipType.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = relationshipType[prefix.Length..];
                break;
            }
        }

        descriptor = name switch
        {
            "header" => new(
                "header",
                "hdr",
                WordSemanticNodeKind.Header,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
                0
            ),
            "footer" => new(
                "footer",
                "ftr",
                WordSemanticNodeKind.Footer,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
                1
            ),
            "footnotes" => new(
                "footnotes",
                "footnotes",
                WordSemanticNodeKind.Footnotes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
                2
            ),
            "endnotes" => new(
                "endnotes",
                "endnotes",
                WordSemanticNodeKind.Endnotes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml",
                3
            ),
            "comments" => new(
                "comments",
                "comments",
                WordSemanticNodeKind.Comments,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml",
                4
            ),
            "glossaryDocument" => new(
                "glossary",
                "glossaryDocument",
                WordSemanticNodeKind.GlossaryDocument,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml",
                5
            ),
            _ => null!,
        };
        return descriptor is not null;
    }

    private static void AddIfPresent(
        IDictionary<string, string> properties,
        string name,
        string? value
    )
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[name] = value;
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed record StoryPartDescriptor(
        string Name,
        string RootElementName,
        WordSemanticNodeKind RootKind,
        string ExpectedContentType,
        int Order
    );

    private sealed record StoryRelationship(
        OpcRelationship Relationship,
        StoryPartDescriptor Descriptor
    );

    private sealed record ProjectionContext(
        MutableSemanticNode? parent,
        string identityContext,
        Dictionary<string, int> occurrences,
        List<MutableSemanticNode> roots
    );

    private sealed class ProjectionState
    {
        public Dictionary<XElement, string> Fingerprints { get; } = [];

        public int SemanticNodeCount { get; set; }
    }

    private sealed class MutableSemanticNode
    {
        public MutableSemanticNode(
            SemanticNodeId id,
            WordSemanticNodeKind kind,
            SemanticNodeId? parentId,
            int sourceOrder,
            int sourceElementOrdinal,
            string sourcePartUri,
            string sourcePath,
            string? text,
            IDictionary<string, string> properties
        )
        {
            Id = id;
            Kind = kind;
            ParentId = parentId;
            SourceOrder = sourceOrder;
            SourceElementOrdinal = sourceElementOrdinal;
            SourcePartUri = sourcePartUri;
            SourcePath = sourcePath;
            Text = text;
            Properties = properties;
        }

        public SemanticNodeId Id { get; }

        public WordSemanticNodeKind Kind { get; }

        public SemanticNodeId? ParentId { get; }

        public int SourceOrder { get; }

        public int SourceElementOrdinal { get; }

        public string SourcePartUri { get; }

        public string SourcePath { get; }

        public string? Text { get; }

        public IDictionary<string, string> Properties { get; }

        public List<MutableSemanticNode> Children { get; } = [];

        public WordSemanticNode Freeze() => new(
            Id,
            Kind,
            ParentId,
            SourceOrder,
            SourceElementOrdinal,
            SourcePartUri,
            SourcePath,
            Text,
            Properties,
            Children.Select(child => child.Freeze()).ToArray()
        );
    }
}
