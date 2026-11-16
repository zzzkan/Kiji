namespace Kiji.Docs.Models;

/// <summary>A heading listed in a page's "On this page" navigation.</summary>
/// <param name="Id">The heading's fragment identifier.</param>
/// <param name="Title">The heading's plain text.</param>
/// <param name="Level">The heading level: 2 or 3.</param>
public sealed record OutlineItem(string Id, string Title, int Level);
