namespace ZoopMod {
	public enum ZoopDifficulty {
		Creative,//Only work for creative mode
		Easy,
		Normal,
		Hard
	}

	public static class ZoopDifficultyTime {

		public static ZoopDifficulty difficulty = ZoopDifficulty.Normal;

		public static float GetTimeScale() {
			switch(difficulty) {
				case ZoopDifficulty.Normal:
					return 1.5f;
				case ZoopDifficulty.Easy:
					return 1f;
				case ZoopDifficulty.Hard:
					return 2f;
				case ZoopDifficulty.Creative:
					//Check gamemode if creative set to instant build, else set to Normal time
					return 1f;
				default:
					return 1f;
			}
		}

	}
}
