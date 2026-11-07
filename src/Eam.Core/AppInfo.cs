using System.Reflection;

namespace Eam.Core;

/// <summary>General information about the application.</summary>
public static class AppInfo
{
    public const string Name = "Empyrion Admin Manager";
    public const string ShortName = "EAM";
    public const string ServiceName = "EmpyrionAdminManager";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
