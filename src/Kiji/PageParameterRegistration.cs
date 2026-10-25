namespace Kiji;

/// <summary>Parameter sets for one page component, expanded once per build or reload.</summary>
internal sealed record PageParameterRegistration(
    Type ComponentType,
    Func<IServiceProvider, IEnumerable<object>> Parameters);
