using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Object = System.Object; //SOMETHING NEW



//using creativefreedom;


//TODO make it work in authoring mode
// let it ignore collisions if CreativeFreedom mod is enabled.

namespace ZoopMod {

	public class ZoopUtility {

		public static List<Structure> structures = new List<Structure>();
		public static List<Structure> structuresCacheStraight = new List<Structure>();
		public static List<Structure> structuresCacheCorner = new List<Structure>();

		public static bool isZoopKeyPressed;
		public static bool isZooping;
		public static int spacing = 1;
		public static Vector3? previousCurrentPos;
		public static Color lineColor = Color.green;
		public static Color errorColor = Color.red;
		private static CancellationTokenSource _cancellationToken;

		private static List<Vector3?> waypoints = new List<Vector3?>();
		private static Color waypointColor = Color.blue;
		private static Color startColor = Color.magenta;

		//preferred zoop order is built up by every first detection of a direction
		private static List<ZoopDirection> preferredZoopOrder = new List<ZoopDirection>();
		public static bool HasError;
		public static Coroutine ActionCoroutine { get; set; }


		//PROBLEM in new beta: After start of zooping, construct cursor structure disappear, only empty green cube of smallgrid remain

		public static void StartZoop(InventoryManager inventoryManager) {
			if(IsAllowed(InventoryManager.ConstructionCursor)) {
				isZooping = true;
				if(_cancellationToken == null) {
					preferredZoopOrder.Clear();
					waypoints.Clear();
					if(InventoryManager.ConstructionCursor != null) {
						InventoryManager.UpdatePlacement(inventoryManager.ConstructionPanel.Parent.Constructables[0]);
						var startPos = getCurrentMouseGridPosition();
						if(startPos.HasValue) {
							waypoints.Add(startPos); // Add start position as the first waypoint
						}
					}

					if(waypoints.Count > 0) {
						_cancellationToken = new CancellationTokenSource();
						UniTask.Run(async () => await ZoopAsync(_cancellationToken.Token, inventoryManager));
					}
				} else {
					CancelZoop();
				}
			}
		}

		public static void AddWaypoint() {
			var currentPos = getCurrentMouseGridPosition();
			if(currentPos.HasValue && waypoints.Last() != currentPos) {
				waypoints.Add(currentPos);
			} else if(waypoints.Last() == currentPos) {

			}
		}
		public static void RemoveLastWaypoint() {
			if(waypoints.Count > 1) {
				waypoints.RemoveAt(waypoints.Count - 1);
			}
		}

		private static bool IsAllowed(Structure constructionCursor) {
			return constructionCursor is Pipe || constructionCursor is Cable || constructionCursor is Chute;
		}

		public static async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager) {

			await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

			List<ZoopSegment> zoops = new List<ZoopSegment>();
			if(InventoryManager.ConstructionCursor != null)
				InventoryManager.ConstructionCursor.gameObject.SetActive(false);

			while(cancellationToken != null && !cancellationToken.IsCancellationRequested) {
				try {
					if(waypoints.Count > 0) {
						var currentPos = getCurrentMouseGridPosition();
						if(currentPos.HasValue) {
							zoops.Clear();
							HasError = false;
							bool singleItem = true;

							// Iterate over each pair of waypoints
							for(int wpIndex = 0; wpIndex < waypoints.Count; wpIndex++) {
								var startPos = waypoints[wpIndex].Value;
								var endPos = (wpIndex < waypoints.Count - 1) ? waypoints[wpIndex + 1].Value : currentPos.Value;

								ZoopSegment segment = new ZoopSegment();
								CalculateZoopSegments(startPos, endPos, segment);

								singleItem = startPos == endPos;
								if(singleItem) {
									segment.CountX = 1 + (int)(Math.Abs(startPos.x - endPos.x) * 2);
									segment.IncreasingX = startPos.x < endPos.x;
									segment.Directions.Add(ZoopDirection.x); // unused for single item
								}

								zoops.Add(segment);
							}

							await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

							BuildStructureList(inventoryManager, zoops);

							int structureCounter = 0;

							if(structures.Count > 0) {
								ZoopDirection lastDirection = ZoopDirection.n;
								for(int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++) {
									ZoopSegment segment = zoops[segmentIndex];
									float xOffset = 0;
									float yOffset = 0;
									float zOffset = 0;
									Vector3 startPos = waypoints[segmentIndex].Value;
									for(int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++) {
										if(structureCounter == structures.Count) {
											break;
										}

										ZoopDirection zoopDirection = segment.Directions[directionIndex];
										bool increasing = getIncreasingForDirection(zoopDirection, segment);
										var zoopCounter = getCountForDirection(zoopDirection, segment);

										if(segmentIndex < zoops.Count - 1 && directionIndex == segment.Directions.Count - 1) {
											zoopCounter--;
										} else if(directionIndex < segment.Directions.Count - 1) {
											zoopCounter--;
										}

										for(int zi = 0; zi < zoopCounter; zi++) {
											if(structureCounter == structures.Count) {
												break;
											}

											spacing = Mathf.Max(spacing, 1);
											float minValue = InventoryManager.ConstructionCursor is SmallGrid ? 0.5f : 2f;
											float value = increasing ? minValue * spacing : -(minValue * spacing);
											switch(zoopDirection) {
												case ZoopDirection.x:
													xOffset = zi * value;
													break;
												case ZoopDirection.y:
													yOffset = zi * value;
													break;
												case ZoopDirection.z:
													zOffset = zi * value;
													break;
											}

											bool increasingTo = increasing;
											bool increasingFrom = lastDirection != ZoopDirection.n ? getIncreasingForDirection(lastDirection, segment) : false;
											// Correct the logic to avoid overlapping
											if(segmentIndex > 0 && directionIndex == 0 && zi == 0) {
												ZoopDirection lastSegmentDirection = zoops[segmentIndex-1].Directions.Last();
												switch(lastSegmentDirection) {
													case ZoopDirection.x:
														increasingFrom = zoops[segmentIndex - 1].IncreasingX;
														break;
													case ZoopDirection.y:
														increasingFrom = zoops[segmentIndex - 1].IncreasingY;
														break;
													case ZoopDirection.z:
														increasingFrom = zoops[segmentIndex - 1].IncreasingZ;
														break;
												}
											}
											if((directionIndex > 0 || (segmentIndex > 0 && directionIndex == 0)) && zi == 0) {
												if(lastDirection == zoopDirection) {
													SetStraightRotation(structures[structureCounter], zoopDirection);
												} else {
													SetCornerRotation(structures[structureCounter],
														lastDirection,
														increasingFrom,
														zoopDirection,
														increasingTo);
												}
											} else {
												if(!singleItem) {
													SetStraightRotation(structures[structureCounter], zoopDirection);
												}
											}
											lastDirection = zoopDirection;

											var offset = new Vector3(xOffset, yOffset, zOffset);
											structures[structureCounter].GameObject.SetActive(true);
											structures[structureCounter].ThingTransformPosition = (startPos + offset);
											structures[structureCounter].Position = (startPos + offset);
											if(!ZoopMod.CFree) {
												HasError = HasError || !CanConstruct(inventoryManager, structures[structureCounter]);
											}
											structureCounter++;
											if(zi == zoopCounter - 1) {
												switch(zoopDirection) {
													case ZoopDirection.x:
														xOffset = (zi + 1) * value;
														break;
													case ZoopDirection.y:
														yOffset = (zi + 1) * value;
														break;
													case ZoopDirection.z:
														zOffset = (zi + 1) * value;
														break;
												}
											}
										}
									}
								}
							}

							foreach(Structure structure in structures) {
								SetColor(inventoryManager, structure, HasError);
							}
						}
					}

					await UniTask.Delay(100, DelayType.Realtime);
				} catch(Exception e) {
					Debug.Log(e.Message);
					Debug.LogException(e);
				}
			}
		}


		private static void CalculateZoopSegments(Vector3 startPos, Vector3 endPos, ZoopSegment segment) {
			segment.Directions.Clear();

			var startX = startPos.x;
			var startY = startPos.y;
			var startZ = startPos.z;
			var endX = endPos.x;
			var endY = endPos.y;
			var endZ = endPos.z;

			if(Math.Abs(endX - startX) > float.Epsilon) {
				segment.CountX = 1 + (int)(Math.Abs(startX - endX) * 2);
				segment.IncreasingX = startX < endX;
				updateZoopOrder(ZoopDirection.x);
				segment.Directions.Add(ZoopDirection.x);
			}

			if(Math.Abs(endY - startY) > float.Epsilon) {
				segment.CountY = 1 + (int)(Math.Abs(startY - endY) * 2);
				segment.IncreasingY = startY < endY;
				updateZoopOrder(ZoopDirection.y);
				segment.Directions.Add(ZoopDirection.y);
			}

			if(Math.Abs(endZ - startZ) > float.Epsilon) {
				segment.CountZ = 1 + (int)(Math.Abs(startZ - endZ) * 2);
				segment.IncreasingZ = startZ < endZ;
				updateZoopOrder(ZoopDirection.z);
				segment.Directions.Add(ZoopDirection.z);
			}
		}


		private static void BuildStructureList(InventoryManager inventoryManager, List<ZoopSegment> zoops) {
			structures.Clear();
			structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
			structuresCacheCorner.ForEach(structure => structure.GameObject.SetActive(false));

			var straight = 0;
			var corners = 0;
			var lastDirection = ZoopDirection.n;
			for(int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++) {
				var segment = zoops[segmentIndex];
				for(int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++) {
					var zoopDirection = segment.Directions[directionIndex];
					var zoopCounter = getCountForDirection(zoopDirection, segment);

					// If it's not the last segment and it's the last direction in the segment, reduce the counter by 1
					if(segmentIndex < zoops.Count - 1 && directionIndex == segment.Directions.Count - 1) {
						zoopCounter--;
					} else if(directionIndex < segment.Directions.Count - 1) {
						zoopCounter--;
					}

					for(int j = 0; j < zoopCounter; j++) {
						if(structures.Count > 0 && (j == 0 || segmentIndex > 0) && inventoryManager.ConstructionPanel.Parent.Constructables.Count > 1) {
							if(zoopDirection != lastDirection) {
								AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners, inventoryManager); // start with corner on secondary and tertiary zoop directions
								corners++;
							} else {
								AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, inventoryManager);
								straight++;
							}
						} else {
							AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, inventoryManager);
							straight++;
						}
						lastDirection = zoopDirection;
					}
				}
			}
		}


		private static void SetStraightRotation(Structure structure, ZoopDirection zoopDirection) {
			switch(zoopDirection) {
				case ZoopDirection.x:
					if(structure is Chute) {
						structure.ThingTransformRotation =
							SmartRotate.RotX.Rotation;
					} else {
						structure.ThingTransformRotation =
							SmartRotate.RotY.Rotation;
					}

					break;
				case ZoopDirection.y:
					if(structure is Chute) {
						structure.ThingTransformRotation =
							SmartRotate.RotZ.Rotation;
					} else {
						structure.ThingTransformRotation =
							SmartRotate.RotX.Rotation;
					}

					break;
				case ZoopDirection.z:
					if(structure is Chute) {
						structure.ThingTransformRotation =
							SmartRotate.RotY.Rotation;
					} else {
						structure.ThingTransformRotation =
							SmartRotate.RotZ.Rotation;
					}

					break;
			}
		}

		private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom,
			bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo) {
			var xOffset = 0.0f;
			var yOffset = 0.0f;
			var zOffset = 0.0f;
			if(structure.GetPrefabName().Equals("StructureCableCorner")) {
				xOffset = 180.0f;
			}

			if(structure.GetPrefabName().Equals("StructureChuteCorner")) {
				xOffset = -90.0f;
				if(zoopDirectionTo == ZoopDirection.z && zoopDirectionFrom == ZoopDirection.x)
					yOffset = increasingTo ? -90.0f : 90f;
				else if(zoopDirectionTo == ZoopDirection.x && zoopDirectionFrom == ZoopDirection.z) //good
					yOffset = increasingFrom ? 90.0f : -90f;
				else
					yOffset = 180.0f;
			}

			structure.ThingTransformRotation = ZoopUtils.getCornerRotation(zoopDirectionFrom,
				increasingFrom, zoopDirectionTo, increasingTo, xOffset, yOffset, zOffset);
		}


		private static bool getIncreasingForDirection(ZoopDirection direction, ZoopSegment segment) {
			switch(direction) {
				case ZoopDirection.x:
					return segment.IncreasingX;
				case ZoopDirection.y:
					return segment.IncreasingY;
				case ZoopDirection.z:
					return segment.IncreasingZ;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}


		private static int getCountForDirection(ZoopDirection direction, ZoopSegment segment) {
			switch(direction) {
				case ZoopDirection.x:
					return segment.CountX;
				case ZoopDirection.y:
					return segment.CountY;
				case ZoopDirection.z:
					return segment.CountZ;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}



		private static void updateZoopOrder(ZoopDirection direction) {
			// add if this direction is not yet in the list
			if(!preferredZoopOrder.Contains(direction)) {
				preferredZoopOrder.Add(direction);
			}
		}

		private static Vector3? getCurrentMouseGridPosition() {
			if(InventoryManager.ConstructionCursor != null) {
				var cursorHitPoint = InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();//InventoryManager.ConstructionCursor.GetLocalGrid(InventoryManager.ConstructionCursor.ThingTransformPosition).ToVector3();
																									//var cursorHitPoint = new Vector3(cursorGrid.x, cursorGrid.y, cursorGrid.z); //ADDED
				return cursorHitPoint;
			}

			return null;
		}

		public static void CancelZoop() {
			// NotAuthoringMode.Completion = false;
			isZooping = false;
			if(_cancellationToken != null) {
				_cancellationToken.Cancel();
				_cancellationToken = null;
				ClearStructureCache();
				previousCurrentPos = null;
				ZoopUtility.structures.Clear(); //try to reset a list of structures for single piece placing
				waypoints.Clear();
			}

			if(InventoryManager.ConstructionCursor != null)
				InventoryManager.ConstructionCursor.gameObject.SetActive(true);
		}


		public static async UniTask BuildZoopAsync(InventoryManager IM)//public static void BuildZoopAsync(InventoryManager IM)
		{
			await UniTask.SwitchToMainThread();
			foreach(Structure item in structures) {
				if(InventoryManager.ActiveHandSlot.Get() == null) {
					break;
				}
				//if (ZoopMod.CFree)
				//{
				//    Grid3 loc = item.GetLocalGrid();
				//    SmallCell cell = item.GridController.GetSmallCell(loc);
				//    if (cell != null)
				//    {
				//        Cable cab = item as Cable;
				//        if (cab && cell.Cable != null && cell.Cable.CustomColor != cab.CustomColor)
				//        {
				//            //cab.WillMergeWhenPlaced = false;
				//            //CUMBERSOME!
				//        }
				//    }
				//}
				//if (NetworkManager.IsClient) //may it need client role check to evade exceptions?
				// disabled for clients anyway for now, as I cannot make it work in multiplayer client side
				await UniTask.SwitchToMainThread();
				await UniTask.Delay(10, DelayType.Realtime);
				UsePrimaryComplete(IM, item);
			}

			//Debug.Log("zoop canceled at BuildZoopAsync");
			CancelZoop();
		}

		public static void BuildZoop(InventoryManager IM) {

			foreach(Structure item in structures) {

				UsePrimaryComplete(IM, item);
			}

			CancelZoop();
		}

		private static void UsePrimaryComplete(InventoryManager IM, Structure item) {
			// DynamicThing occupant0 = item.BuildStates[0].Tool.ToolEntry; //try to evade taking authoring tool as occupant

			int optionIndex = IM.ConstructionPanel.Parent.Constructables.FindIndex(structure =>
			{
				return structure.PrefabName == item.PrefabName;
			});
			//Debug.Log(item.PrefabName + ":" + optionIndex);
			if(GameManager.RunSimulation) {
				if(IM.ConstructionPanel.IsVisible)
					OnServer.UseMultiConstructor(InventoryManager.Parent, IM.ActiveHand.SlotId, IM.InactiveHand.SlotId,
						item.transform.position, item.transform.rotation, optionIndex,
						InventoryManager.IsAuthoringMode, InventoryManager.ParentBrain.ClientId,
						optionIndex); //InventoryManager.IsAuthoringMode
				else
					OnServer.UseItemPrimary((Assets.Scripts.Objects.Thing)InventoryManager.Parent,
						IM.ActiveHand.SlotId, item.transform.position, item.transform.rotation,
						InventoryManager.ParentBrain.ClientId, optionIndex);
			} else {
				CreateStructureMessage structureMessage = new CreateStructureMessage();
				DynamicThing occupant1 = IM.ActiveHand.Slot.Get();//InventoryManager.IsAuthoringMode ? occupant0 : IM.ActiveHand.Slot.Occupant; //IM.ActiveHand.Slot.Occupant
																  // ISSUE: explicit non-virtual call
				structureMessage.ConstructorId = occupant1 != null ? (occupant1.ReferenceId) : 0L;
				DynamicThing occupant2 = IM.InactiveHand.Slot.Get();// InventoryManager.IsAuthoringMode ? occupant0 : IM.InactiveHand.Slot.Occupant;
																	// ISSUE: explicit non-virtual call
				structureMessage.OffhandOccupantReferenceId = occupant2 != null ? (occupant2.ReferenceId) : 0L;
				structureMessage.LocalPosition = item.transform.position.ToGrid();
				structureMessage.Rotation = item.transform.rotation;
				structureMessage.CreatorSteamId = (ulong)InventoryManager.ParentBrain.ReferenceId;
				structureMessage.OptionIndex = optionIndex;
				DynamicThing occupant3 = IM.ActiveHand.Slot.Get();// InventoryManager.IsAuthoringMode ? occupant0 : IM.ActiveHand.Slot.Occupant;
				structureMessage.PrefabHash = occupant3 != null ? occupant3.PrefabHash : 0;
				structureMessage.AuthoringMode = InventoryManager.IsAuthoringMode;  //false;//InventoryManager.IsAuthoringMode;
				NetworkClient.SendToServer<CreateStructureMessage>(
					(MessageBase<CreateStructureMessage>)structureMessage, NetworkChannel.GeneralTraffic);
			}

		}
		//NEED TO ADD CHECK FOR CREATIVE FREEDOM MOD, so zooping will be without construction checks
		//InventoryManager.IsAuthoringMode ?
		public static bool CanConstruct(InventoryManager IM, Structure structure) {
			var smallCell = structure.GridController.GetSmallCell(structure.ThingTransformLocalPosition);
			bool invalidStructureExistsOnGrid = smallCell != null &&
												((smallCell.Device != (Object) null &&
												!((structure is Piping pipe && pipe == pipe.IsStraight &&
												smallCell.Device is DevicePipeMounted device &&
												device.contentType == pipe.PipeContentType) ||
												(structure is Cable cable && cable == cable.IsStraight &&
												smallCell.Device is DeviceCableMounted))) ||
												smallCell.Other != (Object) null);

			bool differentEndsCollision = false;
			Type structureType = null;
			if(structure is Piping) {
				structureType = typeof(Piping);
			} else if(structure is Cable) {
				structureType = typeof(Cable);
			} else if(structure is Chute) {
				structureType = typeof(Chute);
			}

			if(structureType != null) {
				MethodInfo method =
					structureType.GetMethod("_IsCollision", BindingFlags.Instance | BindingFlags.NonPublic);

				differentEndsCollision = smallCell != null && (smallCell.Cable != null) &&
										 (bool)method.Invoke(structure, new object[] { smallCell.Cable });
				differentEndsCollision |= smallCell != null && smallCell.Pipe != null &&
										  (bool)method.Invoke(structure, new object[] { smallCell.Pipe });
				differentEndsCollision |= smallCell != null && smallCell.Chute != null &&
										  (bool)method.Invoke(structure, new object[] { smallCell.Chute });
			}


			bool canConstruct = (!invalidStructureExistsOnGrid && !differentEndsCollision);// || ZoopMod.CFree;

			if(smallCell != null && smallCell.IsValid() &&
				structure is Piping && smallCell.Pipe is Piping piping) {
				int optionIndex = IM.ConstructionPanel.Parent.Constructables.FindIndex(item =>
				{
					return structure.PrefabName == item.PrefabName;
				});
				MultiConstructor activeHandOccupant = IM.ActiveHand.Slot.Get() as MultiConstructor;
				Item inactiveHandOccupant = InventoryManager.Parent.Slots[IM.InactiveHand.SlotId].Get() as Item;
				var canReplace = piping.CanReplace(IM.ConstructionPanel.Parent, inactiveHandOccupant);
				canConstruct &= canReplace.CanConstruct;
			} else if(smallCell != null && smallCell.IsValid() && structure is Cable && smallCell.Cable is Cable cable2) {
				int optionIndex = IM.ConstructionPanel.Parent.Constructables.FindIndex(item =>
				{
					return structure.PrefabName == item.PrefabName;
				});
				MultiConstructor activeHandOccupant = IM.ActiveHand.Slot.Get() as MultiConstructor;
				Item inactiveHandOccupant = InventoryManager.Parent.Slots[IM.InactiveHand.SlotId].Get() as Item;
				var canReplace = cable2.CanReplace(IM.ConstructionPanel.Parent, inactiveHandOccupant);
				canConstruct &= canReplace.CanConstruct;
			} else if(smallCell != null && smallCell.IsValid() && structure is Chute && smallCell.Chute is Chute) {
				canConstruct &= false;
			}


			return canConstruct;
		}


		private static void SetColor(InventoryManager IM, Structure structure, bool hasError) {
			bool canConstruct = !hasError;
			bool isWaypoint = waypoints.Contains(structure.Position);
			//check if structure is first element of waypoints
			bool isStart = isWaypoint ? waypoints.First<Vector3?>().Equals(structure.Position) : false;
			Color color = canConstruct ? isWaypoint ? isStart ? startColor : waypointColor : lineColor : errorColor;
			if(structure is SmallGrid smallGrid) {
				List<Connection> list = smallGrid.WillJoinNetwork();
				foreach(Connection openEnd in smallGrid.OpenEnds) {
					if(canConstruct) {
						Color colorToSet = (list.Contains(openEnd)
							? Color.yellow.SetAlpha(IM.CursorAlphaConstructionHelper)
							: Color.green.SetAlpha(IM.CursorAlphaConstructionHelper));
						foreach(ThingRenderer renderer in structure.Renderers) {
							if(renderer.HasRenderer()) {
								renderer.SetColor(colorToSet);
							}
						}

						foreach(Connection end in ((SmallGrid)structure).OpenEnds) {
							end.HelperRenderer.material.color = colorToSet;
						}
					} else {
						foreach(ThingRenderer renderer in structure.Renderers) {
							if(renderer.HasRenderer())
								renderer.SetColor(Color.red.SetAlpha(IM.CursorAlphaConstructionHelper));
						}

						foreach(Connection end in ((SmallGrid)structure).OpenEnds) {
							end.HelperRenderer.material.color = Color.red.SetAlpha(IM.CursorAlphaConstructionHelper);
						}
					}
				}

				color = ((canConstruct && list.Count > 0) ? Color.yellow : color);
			}

			color.a = IM.CursorAlphaConstructionMesh;
			structure.Wireframe.BlueprintRenderer.material.color = color;
			//may it affect end structure lineColor at collided pieces and merge same colored cables?
		}

		public static void ClearStructureCache() {
			foreach(Structure structure in structuresCacheStraight) {
				structure.gameObject.SetActive(false);
				MonoBehaviour.Destroy(structure);
			}

			structuresCacheStraight.Clear();

			foreach(Structure structure in structuresCacheCorner) {
				structure.gameObject.SetActive(false);
				MonoBehaviour.Destroy(structure);
			}

			structuresCacheCorner.Clear();
		}

		public static void AddStructure(List<Structure> Constructables, bool corner, int index, InventoryManager IM) {
			if(InventoryManager.ActiveHandSlot.Get() is Stackable Constructor) {
				if(Constructor.Quantity > structures.Count) {
					MakeItem(Constructables, corner, index, IM);
				}
			} else if(InventoryManager.ActiveHandSlot.Get() is AuthoringTool) {
				MakeItem(Constructables, corner, index, IM);
			}
		}

		private static void MakeItem(List<Structure> Constructables, bool corner, int index, InventoryManager IM) {
			if(!corner && structuresCacheStraight.Count > index) {
				structures.Add(structuresCacheStraight[index]);
			} else if(corner && structuresCacheCorner.Count > index) {
				structures.Add(structuresCacheCorner[index]);
			} else {
				var structure = corner ? Constructables[1] : Constructables[0];
				if(structure == null) {
					return;
				}

				Structure structureNew =
					MonoBehaviour.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
				if(structureNew != null) {
					structureNew.gameObject.SetActive(true);
					structures.Add(structureNew);
					if(corner) {
						structuresCacheCorner.Add(structureNew);
					} else {
						structuresCacheStraight.Add(structureNew);
					}
				}
			}
		}
	}

}