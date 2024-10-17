using System.Collections.Generic;

namespace ZoopMod.Zoop {
	public class ZoopSegment {
		public List<ZoopDirection> Directions { get; set; } = new List<ZoopDirection>();
		public bool IncreasingX { get; set; }
		public bool IncreasingY { get; set; }
		public bool IncreasingZ { get; set; }
		public int CountX { get; set; }
		public int CountY { get; set; }
		public int CountZ { get; set; }

	}
}
