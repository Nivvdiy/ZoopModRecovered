using BepInEx.Configuration;

namespace ZoopMod.Zoop.EntryPoints.Configuration;

/// <summary>
/// Owns zoop-related configuration binding and access.
/// </summary>
internal static class ZoopConfig
{
  public static ConfigEntry<int> MaxZoopWaitTimeMultiplier { get; private set; } = null!;

  public static void Initialize(ConfigFile config)
  {
    MaxZoopWaitTimeMultiplier =
      config.Bind(
        new ConfigDefinition("Zoop", nameof(MaxZoopWaitTimeMultiplier)),
        5,
        new ConfigDescription("Maximum multiplier applied to placement wait time when building multiple structures in one zoop."));
  }
}
