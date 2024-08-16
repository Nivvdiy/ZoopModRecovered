using Assets.Scripts.Util;
using HarmonyLib;
using StationeersMods.Interface;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;



namespace ZoopMod {
	class ZoopMod : ModBehaviour {

		public static KeyCode ZoopHold;// = KeyCode.LeftShift;
		public static KeyCode ZoopSwitch;// = KeyCode.Z;
		public static KeyCode ZoopAddWaypoint;// = KeyCode.Mouse2
		public static KeyCode ZoopRemoveWaypoint;// = KeyCode.V

		public static ZoopMod Instance;

		public static bool CFree = false;

		private static string loglevel = "INFO";

		public enum Logs {
			DEBUG = 1,
			ERROR = 2,
			INFO = 0,
		}

		public static void Log(string line, Logs level) {
			if((int)Enum.Parse(typeof(Logs), loglevel) - (int)level >= 0) {
				Debug.Log("[" + level + " : Zoop Mod] " + line);
			}
		}

		public override void OnLoaded(ContentHandler contentHandler) {

			try {
				Instance = this;
				var harmony = new Harmony("ZoopMod");
				harmony.PatchAll();
				Log("Patch succeeded", Logs.INFO);
				KeyManager.OnControlsChanged += new KeyManager.Event(ControlsChangedEvent);


				Type type = Type.GetType("CreativeFreedom.CreativeFreedom, CreativeFreedom");
				if(type != null) {
					CFree = true;
				} else {
					CFree = false;
				}

			} catch(Exception e) {
				Log("Patch Failed", Logs.ERROR);
				Debug.Log(e.ToString());
			}
		}


		/* Track current player keybinding selection, event trigger after any
		 * keybinding change.
		 */
		private void ControlsChangedEvent() {
			ZoopHold = KeyManager.GetKey("Zoop Hold");
			ZoopSwitch = KeyManager.GetKey("Zoop Switch");
			ZoopAddWaypoint = KeyManager.GetKey("Zoop Add Waypoint");
			ZoopRemoveWaypoint = KeyManager.GetKey("Zoop Remove Last Waypoint");
		}
	}
}
