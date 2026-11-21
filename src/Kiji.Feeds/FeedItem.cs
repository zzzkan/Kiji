using System.Xml;

namespace Kiji.Feeds;

/// <summary>An RSS feed entry.</summary>
public sealed record FeedItem
{
    /// <summary>Creates an RSS feed entry.</summary>
    /// <param name="Title">The entry title.</param>
    /// <param name="Description">The entry description.</param>
    /// <param name="PublishedAt">The publication time.</param>
    /// <param name="RelativePath">The page path under <see cref="SiteInfo.BaseUrl"/>, without a leading slash.</param>
    /// <exception cref="ArgumentException">The path is not a plain page path, or a text contains a character XML cannot hold.</exception>
    public FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)
    {
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Description);
        Paths.RelativePath.Validate(RelativePath, nameof(RelativePath));
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

    /// <summary>The publication time.</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>The page path under <see cref="SiteInfo.BaseUrl"/>.</summary>
    public string RelativePath { get; }

    private static void ValidateXmlText(string value, string parameterName, string relativePath)
    {
        foreach (var character in value)
        {
            if (!XmlConvert.IsXmlChar(character) && !char.IsSurrogate(character))
            {
                throw new ArgumentException(
                    $"The {parameterName} of feed item '{relativePath}' contains the character U+{(int)character:X4}, which XML cannot represent.",
                    parameterName);
            }
        }
    }
}
