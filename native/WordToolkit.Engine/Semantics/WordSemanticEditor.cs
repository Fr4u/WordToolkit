using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public sealed class WordSemanticEditor
{
    private readonly WordSemanticTransactionPlanner _planner;

    public WordSemanticEditor(LosslessXmlOptions? xmlOptions = null)
    {
        _planner = new WordSemanticTransactionPlanner(xmlOptions: xmlOptions);
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
        var plan = _planner.PlanTextReplacements(
            package,
            semanticDocument,
            [new WordTextReplacementCommand(nodeId, newValue, expectedText)],
            cancellationToken
        );
        return plan.CreateMutation(package);
    }
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
