using Assets.Scripts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZoopMod {
	public static class ZoopText {
		public static Assets.Scripts.Localization2.GameString msgNoDoubleWaypoints = Assets.Scripts.Localization2.GameString.Create(
			new string[]{
				"zoopNoDoubleWaypoints",
				"You can't add a waypoint at last waypoint location"
			});

	}
}
