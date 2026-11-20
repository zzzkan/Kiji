namespace Kiji;

internal sealed record PageParameterRegistration(
    Type ComponentType,
    Func<IServiceProvider, IEnumerable<object>> Parameters);
