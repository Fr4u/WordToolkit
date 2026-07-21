using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed class WordSemanticEditor
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string MathTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string MathStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/math";

    private readonly LosslessXmlOptions _xmlOptions;

    public WordSemanticEditor(LosslessXmlOptions? xmlOptions = null)
    {
        _xmlOptions = xmlOptions ?? LosslessXmlOptions.Default;
        _xmlOptions.Validate();
    }

    public OpcPackageMutationBuilder ReplaceText(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        SemanticNodeId nodeId,
        string newValue,
        string? expectedText = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(newValue);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                semanticDocument.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Semantic projection and package snapshot have different fingerprints."
            );
        }

        if (!semanticDocument.TryGetNode(nodeId, out var node) || node is null)
        {
            throw new KeyNotFoundException($"Semantic node '{nodeId}' does not exist.");
        }

        if (node.Kind != WordSemanticNodeKind.Text)
        {
            throw new WordSemanticEditException(
                $"Semantic node '{nodeId}' is {node.Kind}, not editable text."
            );
        }

        var projectedText = node.Text ?? string.Empty;
        if (
            expectedText is not null
            && !string.Equals(projectedText, expectedText, StringComparison.Ordinal)
        )
        {
            throw new WordSemanticPreconditionException(
                $"Semantic node '{nodeId}' no longer contains the expected text."
            );
        }

        if (!package.Parts.TryGetValue(node.SourcePartUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Source part '{node.SourcePartUri}' no longer exists."
            );
        }

        LosslessXmlDocument source;
        try
        {
            source = LosslessXmlDocument.Parse(
                part.Entry.Content,
                _xmlOptions,
                cancellationToken
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"Source part '{node.SourcePartUri}' cannot be edited losslessly.",
                exception
            );
        }

        var element = source.GetElement(node.SourceElementOrdinal);
        if (!IsProjectedTextElement(element))
        {
            throw new WordSemanticPreconditionException(
                $"Source element {node.SourceElementOrdinal} is no longer a Word text element."
            );
        }

        if (!string.Equals(element.Value, projectedText, StringComparison.Ordinal))
        {
            throw new WordSemanticPreconditionException(
                $"Source text for semantic node '{nodeId}' changed after projection."
            );
        }

        byte[] changed;
        try
        {
            changed = source.ReplaceElementText(
                element.Ordinal,
                newValue,
                projectedText,
                part.Entry.Sha256,
                preserveBoundaryWhitespace: true,
                cancellationToken
            );
        }
        catch (LosslessXmlPreconditionException exception)
        {
            throw new WordSemanticPreconditionException(exception.Message, exception);
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"Text node '{nodeId}' cannot be changed without destroying source markup.",
                exception
            );
        }

        var mutation = new OpcPackageMutationBuilder(package);
        if (!changed.AsSpan().SequenceEqual(part.Entry.Content.Span))
        {
            mutation.ReplacePart(part.Uri, changed, part.Entry.Sha256);
        }

        return mutation;
    }

    private static bool IsProjectedTextElement(XmlSourceElement element) =>
        (
            element.NamespaceUri is WordTransitionalNamespace or WordStrictNamespace
            && element.LocalName is "t" or "delText"
        )
        || (
            element.NamespaceUri is MathTransitionalNamespace or MathStrictNamespace
            && element.LocalName == "t"
        );
}

public class WordSemanticEditException : InvalidOperationException
{
    public WordSemanticEditException(string message)
        : base(message)
    {
    }

    public WordSemanticEditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WordSemanticPreconditionException : WordSemanticEditException
{
    public WordSemanticPreconditionException(string message)
        : base(message)
    {
    }

    public WordSemanticPreconditionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
