using System.Collections.Generic;
using Assets.Scripts.UI;
using HarmonyLib;
using UnityEngine;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.EntryPoints.Input;

[HarmonyPatch(typeof(KeyManager), "SetupKeyBindings")]
internal static class KeyManagerSetupKeyBindingsPatch
{
  /* Custom shortcut key binding injection is done after KeyManager.SetupKeyBindings() method is
   * called; this way, we can get our custom new bindings saved/load by the game during the
   * controls initialisation without needing any extra file access.
   */
  public static void Postfix()
  {
    // We need to add a custom control group for the keys to be attached to, and create
    // the lookup reference.
    ZoopLog.Debug("[Input] Adding custom Shortcuts group.");
    var controlsGroup1 = new ControlsGroup("Zoop");
    KeyManager.AddGroupLookup(controlsGroup1);

    // We will add the custom keys with default values to the KeyItem list using our new
    // created control group, however this method -due to accesibility of the class method-
    // will change the current ControlGroup name.
    AddKey("Zoop Hold", KeyCode.LeftShift, controlsGroup1);
    AddKey("Zoop Switch", KeyCode.Z, controlsGroup1);
    AddKey("Zoop Add Waypoint", KeyCode.Mouse2, controlsGroup1);
    AddKey("Zoop Remove Last Waypoint", KeyCode.V, controlsGroup1);
    AddKey("Zoop Bulk Deconstruct", KeyCode.N, controlsGroup1);
    AddKey("Zoop Diagnostic", KeyCode.D, controlsGroup1);

    ControlsAssignment.RefreshState();
  }

  /* Custom method to add keys to a ControlGroup. We 'hijack' the control group lookup function
   * that will also save the name of they key in the list for us
   */
  private static void AddKey
    (string assignmentName, KeyCode keyCode, ControlsGroup controlsGroup, bool hidden = false)
  {
    // This is just because of the accessibility to change the assigned name, we use
    // the control group for that.
    var controlsLookupList =
      Traverse.Create(typeof(KeyManager)).Field("_controlsGroupLookup").GetValue() as
        Dictionary<string, ControlsGroup>; //read static field
    if (controlsLookupList != null)
    {
      controlsLookupList[assignmentName] = controlsGroup;
      Traverse.Create(typeof(KeyManager)).Field("_controlsGroupLookup")
        .SetValue(controlsLookupList); //for static fields
    }

    // Now Create the key, add its looup string name, and save it in the allkeys list, to ensure
    // is being saved/load by the game config initialisation function.
    var keyItem = new KeyItem(assignmentName, keyCode, hidden);
    KeyManager.KeyItemLookup[assignmentName] = keyItem;
    KeyManager.AllKeys.Add(keyItem);
  }
}
