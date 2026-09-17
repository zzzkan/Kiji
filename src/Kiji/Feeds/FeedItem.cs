namespace Kiji.Feeds;

/// <summary>An RSS feed entry.</summary>
public sealed record FeedItem
{
    /// <summary>Creates an RSS feed entry.</summary>
    /// <param name="Title">The entry title.</param>
    /// <param name="Description">The entry description.</param>
    /// <param name="PublishedAt">The publication timestamp.</param>
    /// <param name="RelativePath">The path relative to <see cref="SiteInfo.BaseUrl"/>.</param>
    public FeedItem(string Title, string Description, DateTimeOffset PublishedAt, string RelativePath)
    {
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Description);
        global::Kiji.RelativePath.Validate(RelativePath, nameof(RelativePath));

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
}
