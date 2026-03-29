using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using ZoopMod.Zoop.EntryPoints.Configuration;
using ZoopMod.Zoop.EntryPoints.Integrations;
using ZoopMod.Zoop.EntryPoints.Input;

namespace ZoopMod;

[BepInPlugin("ZoopMod", "ZoopMod", "2026.28.03")]
public class ZoopMod : BaseUnityPlugin
{
  public enum Logs
  {
    debug = 0,
    info = 1,
    error = 2
  }

  private static readonly Logs CurrentLogLevel = Logs.debug;

  // ReSharper disable once UnusedMember.Local
  private void Awake()
  {
    try
    {
      ZoopConfig.Initialize(Config);

      var harmony = new Harmony("ZoopMod");
      harmony.PatchAll();
      Log("Patch succeeded", Logs.info);
      Log($"Build commit: {GetBuildCommitHash()}", Logs.info);
      ZoopIntegrations.Initialize();
      ZoopKeyBindings.Initialize();
    }
    catch (Exception e)
    {
      Log("Patch Failed", Logs.error);
      Log(e.ToString(), Logs.error);
    }
  }

  public static void Log(string line, Logs level)
  {
#if !DEBUG
      if (level == Logs.debug)
      {
        return;
      }
#endif

    if (level >= CurrentLogLevel)
    {
      Debug.Log($"[{level} : Zoop Mod] {line}");
    }
  }

  private static string GetBuildCommitHash()
  {
    return ThisAssembly.Git.Commit.Substring(0, 7);
  }
}
