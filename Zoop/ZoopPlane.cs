namespace ZoopMod.Zoop {

	public class ZoopPlane {
		
		public (ZoopDirection direction1, ZoopDirection direction2) Directions { get; set; } = (ZoopDirection.none, ZoopDirection.none);
		public (bool direction1, bool direction2) Increasing { get; set; }
		public (int direction1, int direction2) Count { get; set; }
	}

}