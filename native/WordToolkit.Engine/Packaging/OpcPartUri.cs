namespace WordToolkit.Engine.Packaging;

internal static class OpcPartUri
{
    internal const string PackageRoot = "/";
    internal const string ContentTypesEntryName = "[Content_Types].xml";
    internal const string RootRelationshipsEntryName = "_rels/.rels";

    public static bool TryFromEntryName(
        string entryName,
        out string? partUri,
        out string? error
    )
    {
        partUri = null;
        error = null;

        if (string.IsNullOrEmpty(entryName))
        {
            error = "Entry name is empty.";
            return false;
        }

        if (entryName.Contains("\\", StringComparison.Ordinal))
        {
            error = "Entry name contains a backslash.";
            return false;
        }

        if (entryName.StartsWith("/", StringComparison.Ordinal))
        {
            error = "Entry name is rooted.";
            return false;
        }

        if (entryName.Contains('\0', StringComparison.Ordinal))
        {
            error = "Entry name contains a NUL character.";
            return false;
        }

        var withoutDirectoryMarker = entryName.EndsWith("/", StringComparison.Ordinal)
            ? entryName[..^1]
            : entryName;
        if (withoutDirectoryMarker.Length == 0)
        {
            error = "Entry name does not identify a part.";
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(withoutDirectoryMarker);
        }
        catch (UriFormatException exception)
        {
            error = $"Entry name has invalid URI escaping: {exception.Message}";
            return false;
        }

        if (decoded.Contains("\\", StringComparison.Ordinal))
        {
            error = "Decoded entry name contains a backslash.";
            return false;
        }

        var segments = decoded.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            error = "Entry name contains an empty or traversal segment.";
            return false;
        }

        partUri = "/" + string.Join('/', segments);
        return true;
    }

    public static bool TryRelationshipSource(
        string entryName,
        out string? sourcePartUri
    )
    {
        sourcePartUri = null;
        if (string.Equals(
            entryName,
            RootRelationshipsEntryName,
            StringComparison.Ordinal
        ))
        {
            sourcePartUri = PackageRoot;
            return true;
        }

        const string marker = "/_rels/";
        var markerIndex = entryName.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0 || !entryName.EndsWith(".rels", StringComparison.Ordinal))
        {
            return false;
        }

        var directory = entryName[..markerIndex];
        var relationshipFile = entryName[(markerIndex + marker.Length)..];
        var sourceFile = relationshipFile[..^".rels".Length];
        if (sourceFile.Length == 0)
        {
            return false;
        }

        return TryFromEntryName(
            string.IsNullOrEmpty(directory) ? sourceFile : $"{directory}/{sourceFile}",
            out sourcePartUri,
            out _
        );
    }

    public static bool IsRelationshipPartUri(string partUri)
    {
        if (
            string.IsNullOrEmpty(partUri)
            || !partUri.StartsWith("/", StringComparison.Ordinal)
        )
        {
            return false;
        }

        return TryRelationshipSource(partUri[1..], out _);
    }

    public static bool IsPackageInfrastructureUri(string partUri) =>
        string.Equals(partUri, "/" + ContentTypesEntryName, StringComparison.Ordinal)
        || IsRelationshipPartUri(partUri);

    public static bool TryResolveRelationshipTarget(
        string sourcePartUri,
        string target,
        out string? resolvedPartUri,
        out string? targetFragment,
        out string? error
    )
    {
        resolvedPartUri = null;
        targetFragment = null;
        error = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Relationship target is empty.";
            return false;
        }

        if (target.Contains("\\", StringComparison.Ordinal) || target.Contains('\0'))
        {
            error = "Relationship target contains an unsafe character.";
            return false;
        }

        if (!Uri.TryCreate(target, UriKind.RelativeOrAbsolute, out var targetUri))
        {
            error = "Relationship target is not a valid URI reference.";
            return false;
        }

        if (targetUri.IsAbsoluteUri)
        {
            error = "Internal relationship target is an absolute URI.";
            return false;
        }

        var baseUri = new Uri(
            "https://wordtoolkit.invalid"
                + (sourcePartUri == PackageRoot ? "/" : sourcePartUri),
            UriKind.Absolute
        );
        if (!Uri.TryCreate(baseUri, targetUri, out var absoluteTarget))
        {
            error = "Relationship target cannot be resolved.";
            return false;
        }

        if (!string.IsNullOrEmpty(absoluteTarget.Query))
        {
            error = "Internal relationship target contains a query component.";
            return false;
        }

        if (!string.Equals(
            absoluteTarget.Host,
            baseUri.Host,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            error = "Relationship target escapes the package URI space.";
            return false;
        }

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(absoluteTarget.AbsolutePath);
        }
        catch (UriFormatException exception)
        {
            error = $"Relationship target has invalid URI escaping: {exception.Message}";
            return false;
        }

        if (decodedPath.Contains("\\", StringComparison.Ordinal))
        {
            error = "Decoded relationship target contains a backslash.";
            return false;
        }

        var entryName = decodedPath.TrimStart('/');
        if (!TryFromEntryName(entryName, out resolvedPartUri, out error))
        {
            return false;
        }

        targetFragment = string.IsNullOrEmpty(absoluteTarget.Fragment)
            ? null
            : absoluteTarget.Fragment[1..];

        return true;
    }


    public static bool TryValidateRelationshipType(string value, out string? error) =>
        TryValidateUriReference(value, "Relationship type", out error);

    public static bool TryValidateExternalRelationshipTarget(
        string value,
        out string? error
    ) => TryValidateUriReference(value, "External relationship target", out error);

    private static bool TryValidateUriReference(
        string value,
        string description,
        out string? error
    )
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{description} is empty.";
            return false;
        }

        if (
            value.Contains('\\', StringComparison.Ordinal)
            || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
        )
        {
            error = $"{description} contains a character forbidden by RFC 3986.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _))
        {
            error = $"{description} is not a valid URI reference.";
            return false;
        }

        return true;
    }
}
