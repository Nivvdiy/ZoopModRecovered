using System;

namespace ZoopMod.Zoop.EntryPoints.Integrations;

/// <summary>
/// Detects and exposes optional mod integrations that affect zoop behavior.
/// </summary>
internal static class ZoopIntegrations
{
  public static bool CreativeFreedomAvailable { get; private set; }

  public static void Initialize()
  {
    CreativeFreedomAvailable = Type.GetType("CreativeFreedom.CreativeFreedom, CreativeFreedom") != null;
  }
}
