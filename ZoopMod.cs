using System;
using BepInEx;
using HarmonyLib;
using ZoopMod.Zoop.EntryPoints.Configuration;
using ZoopMod.Zoop.EntryPoints.Input;
using ZoopMod.Zoop.EntryPoints.Integrations;
using ZoopMod.Zoop.Logging;

namespace ZoopMod;

[BepInPlugin("ZoopMod", "ZoopMod", "2026.28.03")]
// ReSharper disable once UnusedMember.Global
public class ZoopMod : BaseUnityPlugin
{
  // ReSharper disable once UnusedMember.Local
  private void Awake()
  {
    ZoopLog.Initialize(Logger);

    try
    {
      ZoopConfig.Initialize(Config);

      var harmony = new Harmony("ZoopMod");
      harmony.PatchAll();
      ZoopLog.Info("Harmony patch registration succeeded.");
      ZoopLog.Info($"Build commit: {GetBuildCommitHash()}");
      ZoopIntegrations.Initialize();
      ZoopKeyBindings.Initialize();
    }
    catch (Exception e)
    {
      ZoopLog.Error(e, "Plugin startup failed.");
    }
  }

  private static string GetBuildCommitHash()
  {
    return ThisAssembly.Git.Commit.Substring(0, 7);
  }
}
