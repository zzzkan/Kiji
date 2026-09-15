namespace Kiji;

/// <summary>Registers validation rules for content items.</summary>
public class ContentSourceOptions<T>
    where T : class
{
    private readonly List<Action<T>> _validators = [];

    internal IReadOnlyList<Action<T>> Validators => _validators;

    /// <summary>Registers a predicate that each loaded item must satisfy.</summary>
    /// <param name="message">The error reported for a rejected item; validation failures are collected together.</param>
    public void AddValidation(Func<T, bool> predicate, string message)
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

    /// <summary>Registers a check that throws to reject a loaded item, with failures collected together.</summary>
    public void AddValidation(Action<T> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);

        _validators.Add(validate);
    }
}
