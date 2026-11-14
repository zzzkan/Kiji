using System.Reflection;

namespace Kiji.Hosting;

/// <summary>Proves provider validation ran and records code reached through registrations.</summary>
internal sealed record KijiServiceProviderStamp(IReadOnlyCollection<Assembly> Assemblies);
