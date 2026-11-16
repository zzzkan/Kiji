using System.Xml;

namespace Kiji.Feeds;

/// <summary>An RSS feed entry.</summary>
public sealed record FeedItem
{
    /// <summary>Creates an RSS feed entry.</summary>
    /// <param name="Title">The entry title.</param>
    /// <param name="Description">The entry description.</param>
    /// <param name="PublishedAt">The publication timestamp.</param>
    /// <param name="RelativePath">The path relative to <see cref="SiteInfo.BaseUrl"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="RelativePath"/> has a leading slash, a query or a fragment, or is a path
    /// <see cref="SiteInfo.ResolveUrl"/> rejects; or <paramref name="Title"/> or
    /// <paramref name="Description"/> contains a character XML cannot represent.
    /// </exception>
    public FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)
    {
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Description);
        global::Kiji.RelativePath.Validate(RelativePath, nameof(RelativePath));
        // Reject here what the feed writer could not resolve or serialize, while the caller
        // still knows which entry is at fault.
        SiteInfo.ParseRelativePath(RelativePath, nameof(RelativePath));
        ValidateXmlText(Title, nameof(Title), RelativePath);
        ValidateXmlText(Description, nameof(Description), RelativePath);

        this.Title = Title;
        this.Description = Description;
        this.PublishedAt = PublishedAt;
        this.RelativePath = RelativePath;
    }

    /// <summary>The entry title.</summary>
    public string Title { get; }

    /// <summary>The entry description.</summary>
    public string Description { get; }

    /// <summary>The publication timestamp.</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>The path relative to <see cref="SiteInfo.BaseUrl"/>.</summary>
    public string RelativePath { get; }

    private static void ValidateXmlText(string value, string parameterName, string relativePath)
    {
        foreach (var character in value)
        {
            // Surrogates are left to the encoder, as the XML writer leaves them.
            if (!XmlConvert.IsXmlChar(character) && !char.IsSurrogate(character))
            {
                throw new ArgumentException(
                    $"The {parameterName} of feed item '{relativePath}' contains the character U+{(int)character:X4}, which XML cannot represent.",
                    parameterName);
            }
        }
    }
}
