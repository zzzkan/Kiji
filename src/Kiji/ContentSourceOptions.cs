namespace Kiji;

/// <summary>
/// Declares what makes a content source's items valid. Everything a dictionary needs is
/// stated here, at the point of registration — they are immutable once built, so
/// there is no configuring them afterwards.
/// </summary>
/// <remarks>
/// There is deliberately no ordering here. A dictionary is keyed, not ordered: pages sort
/// as they see fit at render time, which is where the choice belongs.
/// </remarks>
public class ContentSourceOptions<T>
    where T : class
{
    private readonly List<Action<T>> _validators = [];

    internal IReadOnlyList<Action<T>> Validators => _validators;

    /// <summary>
    /// Requires <paramref name="predicate"/> to hold for every item. Failures are
    /// reported together, each naming the item's source file.
    /// </summary>
    /// <param name="predicate">Returns <see langword="true"/> for a valid item.</param>
    /// <param name="message">Describes what a failing item is missing.</param>
    public void Validate(Func<T, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _validators.Add(item =>
        {
            if (!predicate(item))
            {
                throw new InvalidOperationException(message);
            }
        });
    }

    /// <summary>
    /// Runs an arbitrary check over every item; throw to reject one. Failures are
    /// reported together, each naming the item's source file.
    /// </summary>
    public void Validate(Action<T> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);

        _validators.Add(validate);
    }
}
