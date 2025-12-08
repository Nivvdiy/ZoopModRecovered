using HarmonyLib;
using StationeersMods.Interface;
using System;
using System.Collections;
using UnityEngine;

namespace ZoopMod
{
  public class ZoopMod : ModBehaviour
  {
    public static KeyCode ZoopHold;// = KeyCode.LeftShift;
    public static KeyCode ZoopSwitch;// = KeyCode.Z;
    public static KeyCode ZoopAddWaypoint;// = KeyCode.Mouse2
    public static KeyCode ZoopRemoveWaypoint;// = KeyCode.V

    public static ZoopMod Instance;

    public static bool CFree;

    private static string loglevel = "info";

    public enum Logs
    {
      debug = 1,
      error = 2,
      info = 0,
    }

    public static void Log(string line, Logs level)
    {
      if ((int)Enum.Parse(typeof(Logs), loglevel) - (int)level >= 0)
        Debug.Log($"[{level} : Zoop Mod] {line}");
    }

    private void Awake()
    {
      // Ensure Instance is immediately ready for coroutine usage
      Instance = this;
    }

    public override void OnLoaded(ContentHandler contentHandler)
    {
      try
      {
        // Ensure Instance is assigned even if Awake executes late
        Instance = this;

        Harmony harmony = new Harmony("ZoopMod");
        harmony.PatchAll();
        Log("Patch succeeded", Logs.info);
        KeyManager.OnControlsChanged += ControlsChangedEvent;

        Type type = Type.GetType("CreativeFreedom.CreativeFreedom, CreativeFreedom");
        CFree = type != null;
      }
      catch (Exception e)
      {
        Log("Patch Failed", Logs.error);
        Debug.LogException(e);
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

    /// <summary>
    /// Safe wrapper for launching coroutines from static code
    /// </summary>
    public static Coroutine RunCoroutine(IEnumerator routine)
    {
      if (Instance != null)
        return Instance.StartCoroutine(routine);

      Debug.LogError("[ZoopMod] Instance is null — coroutine not started!");
      return null;
    }

    /// <summary>
    /// Safe wrapper for stopping coroutines
    /// </summary>
    public static void StopModCoroutine(Coroutine routine)
    {
      if (routine != null && Instance != null)
        Instance.StopCoroutine(routine);
    }
  }
}
