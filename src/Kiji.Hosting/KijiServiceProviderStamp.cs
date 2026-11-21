using System.Reflection;

namespace Kiji.Hosting;

internal sealed record KijiServiceProviderStamp(IReadOnlyCollection<Assembly> Assemblies);
