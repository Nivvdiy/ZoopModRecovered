using System;
using HarmonyLib;
using StationeersMods.Interface;
using UnityEngine;

namespace ZoopMod;

public class ZoopMod : ModBehaviour
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

  private static readonly Logs CurrentLogLevel = Logs.debug;

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

  public override void OnLoaded(ContentHandler contentHandler)
  {
    try
    {
      Instance = this;
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
