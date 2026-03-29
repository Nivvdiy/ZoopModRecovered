using BepInEx.Configuration;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.EntryPoints.Configuration;

/// <summary>
/// Owns zoop-related configuration binding and access.
/// </summary>
internal static class ZoopConfig
{
  public static bool IsInitialized { get; private set; }
  public static ConfigEntry<bool> EnableDiagnosticLogs { get; private set; } = null!;
  public static ConfigEntry<int> MaxZoopWaitTimeMultiplier { get; private set; } = null!;

  public static void Initialize(ConfigFile config)
  {
    EnableDiagnosticLogs =
      config.Bind(
        new ConfigDefinition("Diagnostics", nameof(EnableDiagnosticLogs)),
        false,
        new ConfigDescription("Enables verbose diagnostic logs, including debug output."));

    MaxZoopWaitTimeMultiplier =
      config.Bind(
        new ConfigDefinition("Zoop", nameof(MaxZoopWaitTimeMultiplier)),
        5,
        new ConfigDescription("Maximum multiplier applied to placement wait time when building multiple structures in one zoop."));

    IsInitialized = true;
  }
}
