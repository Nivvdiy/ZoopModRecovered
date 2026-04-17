using UnityEngine;

namespace ZoopMod.Zoop.EntryPoints.Input;

/// <summary>
/// Stores the current effective key bindings for zoop input actions.
/// </summary>
internal static class ZoopKeyBindings
{
  public static KeyCode Hold { get; private set; }
  public static KeyCode Switch { get; private set; }
  public static KeyCode AddWaypoint { get; private set; }
  public static KeyCode RemoveWaypoint { get; private set; }
  public static KeyCode NetworkDeconstruct { get; private set; }

  public static void Initialize()
  {
    KeyManager.OnControlsChanged += Refresh;
    Refresh();
  }

  private static void Refresh()
  {
    Hold = KeyManager.GetKey("Zoop Hold");
    Switch = KeyManager.GetKey("Zoop Switch");
    AddWaypoint = KeyManager.GetKey("Zoop Add Waypoint");
    RemoveWaypoint = KeyManager.GetKey("Zoop Remove Last Waypoint");
    NetworkDeconstruct = KeyManager.GetKey("Zoop Network Deconstruct");
  }
}
