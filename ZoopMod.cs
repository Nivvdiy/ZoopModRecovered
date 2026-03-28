using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

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

  public static KeyCode ZoopHold;
  public static KeyCode ZoopSwitch;
  public static KeyCode ZoopAddWaypoint;
  public static KeyCode ZoopRemoveWaypoint;

  public static ZoopMod Instance;

  public static bool CFree;
  public static ConfigEntry<int> MaxZoopWaitTimeMultiplier;

  private static readonly Logs CurrentLogLevel = Logs.debug;

  private void Awake()
  {
    try
    {
      Instance = this;
      MaxZoopWaitTimeMultiplier =
        Config.Bind(
          new ConfigDefinition("Zoop", nameof(MaxZoopWaitTimeMultiplier)),
          5,
          new ConfigDescription("Maximum multiplier applied to placement wait time when building multiple structures in one zoop."));

      var harmony = new Harmony("ZoopMod");
      harmony.PatchAll();
      Log("Patch succeeded", Logs.info);
      KeyManager.OnControlsChanged += ControlsChangedEvent;

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

  /* Track current player keybinding selection, event trigger after any
   * keybinding change.
   */
  private static void ControlsChangedEvent()
  {
    ZoopHold = KeyManager.GetKey("Zoop Hold");
    ZoopSwitch = KeyManager.GetKey("Zoop Switch");
    ZoopAddWaypoint = KeyManager.GetKey("Zoop Add Waypoint");
    ZoopRemoveWaypoint = KeyManager.GetKey("Zoop Remove Last Waypoint");
  }
}
