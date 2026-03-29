using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using ZoopMod.Zoop.EntryPoints.Configuration;
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

  public static ZoopMod Instance;

  public static bool CFree;

  private static readonly Logs CurrentLogLevel = Logs.debug;

  // ReSharper disable once UnusedMember.Local
  private void Awake()
  {
    try
    {
      Instance = this;
      ZoopConfig.Initialize(Config);

      var harmony = new Harmony("ZoopMod");
      harmony.PatchAll();
      Log("Patch succeeded", Logs.info);
      Log($"Build commit: {GetBuildCommitHash()}", Logs.info);
      ZoopKeyBindings.Initialize();

      var type = Type.GetType("CreativeFreedom.CreativeFreedom, CreativeFreedom");
      CFree = type != null;
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
