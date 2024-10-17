using HarmonyLib;
using StationeersMods.Interface;
using System;
using UnityEngine;



namespace ZoopMod {
	public class ZoopMod : ModBehaviour {

		public static KeyCode ZoopHold;// = KeyCode.LeftShift;
		public static KeyCode ZoopSwitch;// = KeyCode.Z;
		public static KeyCode ZoopAddWaypoint;// = KeyCode.Mouse2
		public static KeyCode ZoopRemoveWaypoint;// = KeyCode.V

		public static ZoopMod Instance;

		public static bool CFree;

		private static string loglevel = "info";

		public enum Logs {
			debug = 1,
			error = 2,
			info = 0,
		}

		public static void Log(string line, Logs level) {
			if((int)Enum.Parse(typeof(Logs), loglevel) - (int)level >= 0) {
				Debug.Log("[" + level + " : Zoop Mod] " + line);
			}
		}

		public override void OnLoaded(ContentHandler contentHandler) {

			try {
				Instance = this;
				Harmony harmony = new Harmony("ZoopMod");
				harmony.PatchAll();
				Log("Patch succeeded", Logs.info);
				KeyManager.OnControlsChanged += ControlsChangedEvent;


				Type type = Type.GetType("CreativeFreedom.CreativeFreedom, CreativeFreedom");
				CFree = type != null;

			} catch(Exception e) {
				Log("Patch Failed", Logs.error);
				Debug.Log(e.ToString());
			}
		}


		/* Track current player keybinding selection, event trigger after any
		 * keybinding change.
		 */
		private static void ControlsChangedEvent() {
			ZoopHold = KeyManager.GetKey("Zoop Hold");
			ZoopSwitch = KeyManager.GetKey("Zoop Switch");
			ZoopAddWaypoint = KeyManager.GetKey("Zoop Add Waypoint");
			ZoopRemoveWaypoint = KeyManager.GetKey("Zoop Remove Last Waypoint");
		}
	}
}
