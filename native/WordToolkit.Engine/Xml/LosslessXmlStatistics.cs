namespace WordToolkit.Engine.Xml;

public sealed record LosslessXmlStatistics(
    int SourceBytes,
    long XmlCharacters,
    int ElementCount,
    int MaximumDepth,
    long TextCharacters
);
