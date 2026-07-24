using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public static class WordPackageConformance
{
    public const string TransitionalOfficeDocumentRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string StrictOfficeDocumentRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    public const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private const string DocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string MacroDocumentContentType =
        "application/vnd.ms-word.document.macroEnabled.main+xml";
    private const string TemplateContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    private const string MacroTemplateContentType =
        "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";

    private static readonly HashSet<string> MainContentTypes = new(
        [
            DocumentContentType,
            TemplateContentType,
            MacroDocumentContentType,
            MacroTemplateContentType,
        ],
        StringComparer.OrdinalIgnoreCase
    );

    public static bool IsOfficeDocumentRelationshipType(string? relationshipType)
    {
        return relationshipType
            is TransitionalOfficeDocumentRelationship or StrictOfficeDocumentRelationship;
    }

    public static bool IsWordMainContentType(string? contentType)
    {
        return contentType is not null && MainContentTypes.Contains(contentType);
    }

    public static bool IsMacroEnabledWordMainContentType(string? contentType)
    {
        return contentType is not null
            && (
                contentType.Equals(
                    MacroDocumentContentType,
                    StringComparison.OrdinalIgnoreCase
                )
                || contentType.Equals(
                    MacroTemplateContentType,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    public static bool HasWordDocumentRoot(LosslessXmlDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Root.LocalName == "document"
            && source.Root.NamespaceUri
                is TransitionalWordNamespace or StrictWordNamespace
            && source.Root.Children.Count(child =>
                child.LocalName == "body" && child.NamespaceUri == source.Root.NamespaceUri
            ) == 1;
    }

    public static bool IsMainContentTypeCompatibleWithFileName(
        string fileName,
        string? contentType
    )
    {
        if (string.IsNullOrWhiteSpace(fileName) || contentType is null)
        {
            return false;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".docx" => contentType.Equals(
                DocumentContentType,
                StringComparison.OrdinalIgnoreCase
            ),
            ".docm" => contentType.Equals(
                MacroDocumentContentType,
                StringComparison.OrdinalIgnoreCase
            ),
            ".dotx" => contentType.Equals(
                TemplateContentType,
                StringComparison.OrdinalIgnoreCase
            ),
            ".dotm" => contentType.Equals(
                MacroTemplateContentType,
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };
    }
}
