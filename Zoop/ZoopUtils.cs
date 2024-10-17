using System;
using UnityEngine;

namespace ZoopMod.Zoop {
	public static class ZoopUtils {
		public static ZoopTransition GetTransition(ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo) {
			if(zoopDirectionFrom == ZoopDirection.x) {
				if(zoopDirectionTo == ZoopDirection.y) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.xPyP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.xPyN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.xNyP;
					} else {
						return ZoopTransition.xNyN;
					}
				} else if(zoopDirectionTo == ZoopDirection.z) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.xPzP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.xPzN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.xNzP;
					} else {
						return ZoopTransition.xNzN;
					}
				}
			} else if(zoopDirectionFrom == ZoopDirection.y) {
				if(zoopDirectionTo == ZoopDirection.x) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.yPxP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.yPxN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.yNxP;
					} else {
						return ZoopTransition.yNxN;
					}
				} else if(zoopDirectionTo == ZoopDirection.z) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.yPzP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.yPzN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.yNzP;
					} else {
						return ZoopTransition.yNzN;
					}
				}
			} else if(zoopDirectionFrom == ZoopDirection.z) {
				if(zoopDirectionTo == ZoopDirection.x) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.zPxP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.zPxN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.zNxP;
					} else {
						return ZoopTransition.zNxN;
					}
				} else if(zoopDirectionTo == ZoopDirection.y) {
					if(increasingFrom && increasingTo) {
						return ZoopTransition.zPyP;
					} else if(increasingFrom && !increasingTo) {
						return ZoopTransition.zPyN;
					} else if(!increasingFrom && increasingTo) {
						return ZoopTransition.zNyP;
					} else {
						return ZoopTransition.zNyN;
					}
				}
			}

			throw new ArgumentException();
		}

		public static Quaternion GetCornerRotation(ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo, float xOffset, float yOffset, float zOffset) {
			switch (GetTransition(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo)) {
				case ZoopTransition.xPyP:
				case ZoopTransition.yNxN:
					return Quaternion.Euler(xOffset + 90f, yOffset + 0.0f, zOffset + 0.0f);
				case ZoopTransition.xPyN:
				case ZoopTransition.yPxN:
					return Quaternion.Euler(xOffset + -90f, yOffset + 0.0f, zOffset + 0.0f);
				case ZoopTransition.xNyP:
				case ZoopTransition.yNxP:
					return Quaternion.Euler(xOffset + 90f, yOffset + -180.0f, zOffset + 0.0f);
				case ZoopTransition.xNyN:
				case ZoopTransition.yPxP:
					return Quaternion.Euler(xOffset + -90f, yOffset + -180.0f, zOffset + 0.0f);
				case ZoopTransition.xPzP:
				case ZoopTransition.zNxN:
					return Quaternion.Euler(xOffset + 180.0f, yOffset + 0.0f, zOffset + 0.0f);
				case ZoopTransition.xPzN:
				case ZoopTransition.zPxN:
					return Quaternion.Euler(xOffset + 0.0f, yOffset + 0f, zOffset + 0.0f);
				case ZoopTransition.xNzP:
				case ZoopTransition.zNxP:
					return Quaternion.Euler(xOffset + 180.0f, yOffset + 90f, zOffset + 0.0f);
				case ZoopTransition.xNzN:
				case ZoopTransition.zPxP:
					return Quaternion.Euler(xOffset + 0.0f, yOffset + -90f, zOffset + 0.0f);
				case ZoopTransition.yPzP:
				case ZoopTransition.zNyN:
					return Quaternion.Euler(xOffset + -90.0f, yOffset + 90f, zOffset + 0.0f);
				case ZoopTransition.yNzN:
				case ZoopTransition.zPyP:
					return Quaternion.Euler(xOffset + 90.0f, yOffset + -90f, zOffset + 0.0f);
				case ZoopTransition.yPzN:
				case ZoopTransition.zPyN:
					return Quaternion.Euler(xOffset + -90.0f, yOffset + -90f, zOffset + 0.0f);
				case ZoopTransition.yNzP:
				case ZoopTransition.zNyP:
					return Quaternion.Euler(xOffset + 90.0f, yOffset + 90f, zOffset + 0.0f);
			}

			return Quaternion.identity;
		}
	}
}