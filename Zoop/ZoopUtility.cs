using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Objects.Structures;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Assets.Scripts.Objects.Structures;
using CreativeFreedom;
using UnityEngine;
using Object = UnityEngine.Object; //SOMETHING NEW

//using creativefreedom;

//TODO make it work in authoring mode
// let it ignore collisions if CreativeFreedom mod is enabled.

namespace ZoopMod.Zoop {

	public class ZoopUtility {

		#region Fields
		
		public static List<Structure> structures = new List<Structure>();
		private static List<Structure> structuresCacheStraight = new List<Structure>();
		private static List<Structure> structuresCacheCorner = new List<Structure>();
		
		private static readonly List<Vector3?> Waypoints = new List<Vector3?>();

		//preferred zoop order is built up by every first detection of a direction
		private static readonly List<ZoopDirection> PreferredZoopOrder = new List<ZoopDirection>();
		
		public static bool HasError { get; private set; }
		public static Coroutine ActionCoroutine { get; set; }
		private static CancellationTokenSource _cancellationToken;

		public static bool isZoopKeyPressed;
		public static bool isZooping { get; private set; }
		private static int spacing = 1;
		
		private static Vector3? previousCurrentPos;
		public static Color lineColor = Color.green;
		private static Color errorColor = Color.red;
		private static readonly Color WaypointColor = Color.blue;
		private static readonly Color StartColor = Color.magenta;

		#endregion

		#region Common Methods

		public static void StartZoop(InventoryManager inventoryManager) {
			if(IsAllowed(InventoryManager.ConstructionCursor)) {
				isZooping = true;
				if(_cancellationToken == null) {
					PreferredZoopOrder.Clear();
					Waypoints.Clear();
					if(InventoryManager.ConstructionCursor != null) {
						//TODO check select index and set it to 0 or 2 if it's chute window selected
						InventoryManager.UpdatePlacement(inventoryManager.ConstructionPanel.Parent.Constructables[0]);
						Vector3? startPos = GetCurrentMouseGridPosition();
						if(startPos.HasValue) {
							Waypoints.Add(startPos); // Add start position as the first waypoint
						}
					}

					if(Waypoints.Count > 0) {
						_cancellationToken = new CancellationTokenSource();
						UniTask.Run(async () => await ZoopAsync(_cancellationToken.Token, inventoryManager));
					}
				} else {
					CancelZoop();
				}
			}

		}

		public static void CancelZoop() {
			// NotAuthoringMode.Completion = false;
			isZooping = false;
			if(_cancellationToken != null) {
				_cancellationToken.Cancel();
				_cancellationToken = null;
				ClearStructureCache();
				previousCurrentPos = null;
				structures.Clear(); //try to reset a list of structures for single piece placing
				Waypoints.Clear();
			}

			if(InventoryManager.ConstructionCursor != null)
				InventoryManager.ConstructionCursor.gameObject.SetActive(true);
		}

		private static async UniTask ZoopAsync(CancellationToken cancellationToken, InventoryManager inventoryManager) {

			await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

			List<ZoopSegment> zoops = new List<ZoopSegment>();
			if(InventoryManager.ConstructionCursor != null)
				InventoryManager.ConstructionCursor.gameObject.SetActive(false);

			while(cancellationToken is { IsCancellationRequested: false }) {

				try {
					if(Waypoints.Count > 0) {
						Vector3? currentPos = GetCurrentMouseGridPosition();
						if(currentPos.HasValue) {
							zoops.Clear();
							HasError = false;
							bool singleItem = true;

							if(IsZoopingSmallGrid()) {

								// Iterate over each pair of waypoints
								for(int wpIndex = 0; wpIndex < Waypoints.Count; wpIndex++) {
									Vector3 startPos = Waypoints[wpIndex].Value;
									Vector3 endPos = wpIndex < Waypoints.Count - 1 ? Waypoints[wpIndex + 1].Value : currentPos.Value;

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

								BuildSmallStructureList(inventoryManager, zoops);

								int structureCounter = 0;

								if(structures.Count > 0) {
									ZoopDirection lastDirection = ZoopDirection.none;
									for(int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++) {
										ZoopSegment segment = zoops[segmentIndex];
										float xOffset = 0;
										float yOffset = 0;
										float zOffset = 0;
										Vector3 startPos = Waypoints[segmentIndex].Value;
										for(int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++) {
											if(structureCounter == structures.Count) {
												break;
											}

											ZoopDirection zoopDirection = segment.Directions[directionIndex];
											bool increasing = GetIncreasingForDirection(zoopDirection, segment);
											int zoopCounter = GetCountForDirection(zoopDirection, segment);

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
												bool increasingFrom = lastDirection != ZoopDirection.none && GetIncreasingForDirection(lastDirection, segment);
												// Correct the logic to avoid overlapping
												if(segmentIndex > 0 && directionIndex == 0 && zi == 0) {
													ZoopDirection lastSegmentDirection = zoops[segmentIndex - 1].Directions.Last();
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
														case ZoopDirection.none:
														default:
															throw new ArgumentOutOfRangeException();
													}
												}
												if((directionIndex > 0 || segmentIndex > 0 && directionIndex == 0) && zi == 0) {
													if(lastDirection == zoopDirection) {
														SetStraightRotationSmallGrid(structures[structureCounter], zoopDirection);
													} else {
														SetCornerRotation(structures[structureCounter], lastDirection, increasingFrom, zoopDirection, increasingTo);
													}
												} else {
													if(!singleItem) {
														SetStraightRotationSmallGrid(structures[structureCounter], zoopDirection);
													}
												}
												lastDirection = zoopDirection;

												Vector3 offset = new Vector3(xOffset, yOffset, zOffset);
												structures[structureCounter].GameObject.SetActive(true);
												structures[structureCounter].ThingTransformPosition = startPos + offset;
												structures[structureCounter].Position = startPos + offset;
												if(!ZoopMod.CFree) {
													HasError = HasError || !CanConstructSmallCell(inventoryManager, structures[structureCounter]);
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
														case ZoopDirection.none:
														default:
															throw new ArgumentOutOfRangeException();
													}
												}
											}
										}
									}
								}

							} else if(IsZoopingBigGrid()) {
								Vector3 startPos = Waypoints[0].Value;
								Vector3 endPos = currentPos.Value;

								ZoopPlane plane = new ZoopPlane();
								CalculateZoopPlane(startPos, endPos, plane);

								singleItem = startPos == endPos;
								if(singleItem) {
									plane.Count = (direction1: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2), direction2: 1 + (int)(Math.Abs(startPos.x - endPos.x) / 2));
									plane.Increasing = (direction1: startPos.x < endPos.x, direction2: startPos.y < endPos.y);
									plane.Directions = (direction1: ZoopDirection.x, direction2: ZoopDirection.y);
								}

								await UniTask.SwitchToMainThread(); // Switch to main thread for Unity API calls

								BuildBigStructureList(inventoryManager, plane);

								int structureCounter = 0;

								if(structures.Count > 0) {

									float xOffset = 0;
									float yOffset = 0;
									float zOffset = 0;

									spacing = Mathf.Max(spacing, 1);

									for(int indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++) {

										ZoopDirection zoopDirection2 = plane.Directions.direction2;
										bool increasing2 = plane.Increasing.direction2;

										float value2 = increasing2 ? 2f * spacing : -(2f * spacing);
										switch(zoopDirection2) {
											case ZoopDirection.x:
												xOffset = indexDirection2 * value2;
												break;
											case ZoopDirection.y:
												yOffset = indexDirection2 * value2;
												break;
											case ZoopDirection.z:
												zOffset = indexDirection2 * value2;
												break;
										}

										for(int indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++) {
											if(structureCounter == structures.Count) {
												break;
											}

											ZoopDirection zoopDirection1 = plane.Directions.direction1;
											bool increasing1 = plane.Increasing.direction1;

											float value1 = increasing1 ? 2f * spacing : -(2f * spacing);
											switch(zoopDirection1) {
												case ZoopDirection.x:
													xOffset = indexDirection1 * value1;
													break;
												case ZoopDirection.y:
													yOffset = indexDirection1 * value1;
													break;
												case ZoopDirection.z:
													zOffset = indexDirection1 * value1;
													break;
											}

											if(structures[structureCounter] is Wall) {//TODO for future update
												SetStraightRotationBigGrid(structures[structureCounter], zoopDirection1, zoopDirection2);
											}

											Vector3 offset = new Vector3(xOffset, yOffset, zOffset);
											structures[structureCounter].GameObject.SetActive(true);
											structures[structureCounter].ThingTransformPosition = startPos + offset;
											structures[structureCounter].Position = startPos + offset;
											HasError = HasError || !CanConstructBigCell(inventoryManager, structures[structureCounter]);
											structureCounter++;

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

		public static async UniTask BuildZoopAsync(InventoryManager inventoryManager) //public static void BuildZoopAsync(InventoryManager inventoryManager)
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
				UsePrimaryComplete(inventoryManager, item);
			}

			//Debug.Log("zoop canceled at BuildZoopAsync");
			CancelZoop();
		}

		public static void BuildZoop(InventoryManager inventoryManager) {

			foreach(Structure item in structures) {

				UsePrimaryComplete(inventoryManager, item);
				/* if it's working thanks to Katsuk for the help */
				Structure lastItem = Structure.LastCreatedStructure;
				if(InventoryManager.IsAuthoringMode && lastItem.NextBuildState != null) {
					int lastBuildStateIndex = lastItem.BuildStates.Count - 1;
					lastItem.UpdateBuildStateAndVisualizer(lastBuildStateIndex);
				}
			}

			inventoryManager.CancelPlacement();
			CancelZoop();
		}

		public static void AddWaypoint() {
			if(InventoryManager.ConstructionCursor is Frame) {
				return;
			}
			Vector3? currentPos = GetCurrentMouseGridPosition();
			if(currentPos.HasValue && Waypoints.Last() != currentPos) {
				if(structures.Last().GetGridPosition() == currentPos) {
					Waypoints.Add(currentPos);
				}
			} else if(Waypoints.Last() == currentPos) {
				//TODO show message to user that waypoint is already added
			}
		}

		public static void RemoveLastWaypoint() {
			if(InventoryManager.ConstructionCursor is Frame) {
				return;
			}
			if(Waypoints.Count > 1) {
				Waypoints.RemoveAt(Waypoints.Count - 1);
			}
		}

		private static void UsePrimaryComplete(InventoryManager inventoryManager, Structure item) {
			// DynamicThing occupant0 = item.BuildStates[0].Tool.ToolEntry; //try to evade taking authoring tool as occupant

			int buildIndex = inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(structure => structure.PrefabName == item.PrefabName);
			int prefabIndex = InventoryManager.DynamicThingPrefabs.FindIndex(value => inventoryManager.ConstructionPanel.Parent.PrefabName == value);
			//Debug.Log(item.PrefabName + ":" + optionIndex);
			if(GameManager.RunSimulation) {
				if(inventoryManager.ConstructionPanel.IsVisible)
					OnServer.UseMultiConstructor((Assets.Scripts.Objects.Thing)InventoryManager.Parent, inventoryManager.ActiveHand.SlotId, inventoryManager.InactiveHand.SlotId, item.transform.position,
						item.transform.rotation, buildIndex, InventoryManager.IsAuthoringMode, InventoryManager.ParentBrain.ClientId, item);
				else
					OnServer.UseItemPrimary((Assets.Scripts.Objects.Thing)InventoryManager.Parent, inventoryManager.ActiveHand.SlotId, item.transform.position, item.transform.rotation, InventoryManager.ParentBrain.ClientId, item);
			} else {
				CreateStructureMessage structureMessage = new CreateStructureMessage();
				DynamicThing occupant1 = inventoryManager.ActiveHand.Slot.Get(); //InventoryManager.IsAuthoringMode ? occupant0 : inventoryManager.ActiveHand.Slot.Occupant; //inventoryManager.ActiveHand.Slot.Occupant
																				 // ISSUE: explicit non-virtual call
				structureMessage.ConstructorId = occupant1 != null ? occupant1.ReferenceId : 0L;
				DynamicThing occupant2 = inventoryManager.InactiveHand.Slot.Get(); // InventoryManager.IsAuthoringMode ? occupant0 : inventoryManager.InactiveHand.Slot.Occupant;
																				   // ISSUE: explicit non-virtual call
				structureMessage.OffhandOccupantReferenceId = occupant2 != null ? occupant2.ReferenceId : 0L;
				structureMessage.LocalPosition = item.transform.position.ToGridPosition();
				structureMessage.Rotation = item.transform.rotation;
				structureMessage.CreatorSteamId = (ulong)InventoryManager.ParentBrain.ReferenceId;
				structureMessage.OptionIndex = buildIndex;
				DynamicThing occupant3 = inventoryManager.ActiveHand.Slot.Get(); // InventoryManager.IsAuthoringMode ? occupant0 : inventoryManager.ActiveHand.Slot.Occupant;
				structureMessage.PrefabHash = occupant3 != null ? occupant3.PrefabHash : 0;
				structureMessage.AuthoringMode = InventoryManager.IsAuthoringMode;
				NetworkClient.SendToServer<CreateStructureMessage>(structureMessage, NetworkChannel.GeneralTraffic);
			}

			if(InventoryManager.IsAuthoringMode && item.BuildStates.Count > 1) {
				//item.LocalGrid = 
				//item.UpdateBuildStateAndVisualizer(item.BuildStates.Count - 1);
			}

		}

		private static void AddStructure(List<Structure> constructables, bool corner, int index, int secondaryCount, ref bool canBuildNext, InventoryManager im) {

			int selectedIndex = im.ConstructionPanel.Parent.LastSelectedIndex;
			int straightCount = corner ? secondaryCount : index;
			int cornerCount = corner ? index : secondaryCount;

			Structure activeItem = constructables[selectedIndex];
			if(!corner) {
				switch(activeItem) {
					case Pipe or Cable or Frame when selectedIndex != 0:
					case Chute when selectedIndex != 0 && selectedIndex != 2:
						selectedIndex = 0;
						break;
				}
			}

			DynamicThing activeHandItem = InventoryManager.ActiveHandSlot.Get();
			switch(activeHandItem) {
				case Stackable constructor:
					bool canMakeItem = activeItem switch {
						Chute when selectedIndex == 0 => constructor.Quantity > structures.Count,
						Chute when selectedIndex == 2 => constructor.Quantity > ((straightCount) * 2)+ (corner ? 0 : 1) + cornerCount,
						_                             => constructor.Quantity > structures.Count
					};

					if(canMakeItem && canBuildNext) {
						MakeItem(constructables, corner, index, im, !corner ? selectedIndex : 1);
						canBuildNext = true;
					} else {
						canBuildNext = false;
					}
					break;
				case AuthoringTool:
					MakeItem(constructables, corner, index, im, !corner ? selectedIndex : 1);
					canBuildNext = true;
					break;
			}
		}

		private static void ClearStructureCache() {
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

		private static Vector3? GetCurrentMouseGridPosition() {
			if(InventoryManager.ConstructionCursor == null) {
				return null;
			}

			Vector3 cursorHitPoint = InventoryManager.ConstructionCursor.GetLocalGrid().ToVector3();
			return cursorHitPoint;

		}

		private static void MakeItem(List<Structure> constructables, bool corner, int index, InventoryManager inventoryManager, int selectedIndex) {
			if(!corner && structuresCacheStraight.Count > index) {
				structures.Add(structuresCacheStraight[index]);
			} else if(corner && structuresCacheCorner.Count > index) {
				structures.Add(structuresCacheCorner[index]);
			} else {
				Structure structure = constructables[selectedIndex];
				if(structure == null) {
					return;
				}

				Structure structureNew = MonoBehaviour.Instantiate(InventoryManager.GetStructureCursor(structure.PrefabName));
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

		private static void SetColor(InventoryManager inventoryManager, Structure structure, bool hasError) {
			bool canConstruct = !hasError;
			bool isWaypoint = Waypoints.Contains(structure.Position);
			//check if structure is first element of waypoints
			bool isStart = isWaypoint && Waypoints.First<Vector3?>().Equals(structure.Position);
			Color color = canConstruct ? isWaypoint ? isStart ? StartColor : WaypointColor : lineColor : errorColor;
			if(structure is SmallGrid smallGrid) {
				List<Connection> list = smallGrid.WillJoinNetwork();
				foreach(Connection openEnd in smallGrid.OpenEnds) {
					if(canConstruct) {
						Color colorToSet = list.Contains(openEnd) ? Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper) : Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
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
								renderer.SetColor(Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper));
						}

						foreach(Connection end in ((SmallGrid)structure).OpenEnds) {
							end.HelperRenderer.material.color = Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
						}
					}
				}

				color = canConstruct && list.Count > 0 ? Color.yellow : color;
			}

			color.a = inventoryManager.CursorAlphaConstructionMesh;
			structure.Wireframe.BlueprintRenderer.material.color = color;
			//may it affect end structure lineColor at collided pieces and merge same colored cables?
		}

		#endregion

		#region Conditionnal Methods
		private static bool IsAllowed(Structure constructionCursor) {
			return constructionCursor is Pipe or Cable or Chute or Frame;
		}

		private static bool IsZoopingSmallGrid() {
			return InventoryManager.ConstructionCursor is SmallGrid;
		}

		private static bool IsZoopingBigGrid() {
			return InventoryManager.ConstructionCursor is LargeStructure;
		}
		
		private static bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure) {
			SmallCell smallCell = structure.GridController.GetSmallCell(structure.ThingTransformLocalPosition);
			bool invalidStructureExistsOnGrid = smallCell != null &&
												(smallCell.Device != (Object)null &&
													!(structure is Piping pipe && pipe == pipe.IsStraight && smallCell.Device is DevicePipeMounted device && device.contentType == pipe.PipeContentType ||
													  structure is Cable cable && cable == cable.IsStraight && smallCell.Device is DeviceCableMounted) || smallCell.Other != (Object)null);

			bool differentEndsCollision = false;
			Type structureType = null;
			switch(structure) {
				case Piping:
					structureType = typeof(Piping);
					break;
				case Cable:
					structureType = typeof(Cable);
					break;
				case Chute:
					structureType = typeof(Chute);
					break;
			}

			if(structureType != null) {
				MethodInfo method = structureType.GetMethod("_IsCollision", BindingFlags.Instance | BindingFlags.NonPublic);

				if(method != null) {
					differentEndsCollision = smallCell != null && smallCell.Cable != null && (bool)method.Invoke(structure, new object[] { smallCell.Cable });
					differentEndsCollision |= smallCell != null && smallCell.Pipe != null && (bool)method.Invoke(structure, new object[] { smallCell.Pipe });
					differentEndsCollision |= smallCell != null && smallCell.Chute != null && (bool)method.Invoke(structure, new object[] { smallCell.Chute });
				}

			}

			bool canConstruct = !invalidStructureExistsOnGrid && !differentEndsCollision; // || ZoopMod.CFree;

			if(smallCell != null && smallCell.IsValid() && structure is Piping && smallCell.Pipe is Piping piping) {
				int optionIndex = inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(item => structure.PrefabName == item.PrefabName);
				MultiConstructor activeHandOccupant = inventoryManager.ActiveHand.Slot.Get() as MultiConstructor;
				Item inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
				CanConstructInfo canReplace = piping.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
				canConstruct &= canReplace.CanConstruct;
			} else if(smallCell != null && smallCell.IsValid() && structure is Cable && smallCell.Cable is Cable cable2) {
				int optionIndex = inventoryManager.ConstructionPanel.Parent.Constructables.FindIndex(item => structure.PrefabName == item.PrefabName);
				MultiConstructor activeHandOccupant = inventoryManager.ActiveHand.Slot.Get() as MultiConstructor;
				Item inactiveHandOccupant = InventoryManager.Parent.Slots[inventoryManager.InactiveHand.SlotId].Get() as Item;
				CanConstructInfo canReplace = cable2.CanReplace(inventoryManager.ConstructionPanel.Parent, inactiveHandOccupant);
				canConstruct &= canReplace.CanConstruct;
			} else if(smallCell != null && smallCell.IsValid() && structure is Chute && smallCell.Chute is Chute) {
				canConstruct &= false;
			}

			return canConstruct;
		}

		private static bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure) {
			Cell cell = structure.GridController.GetCell(structure.ThingTransformLocalPosition);
			if(cell != null) {
				foreach(Structure cellStructure in cell.AllStructures) {
					if(cellStructure is LargeStructure) {
						return false;
					}
				}
			}
			return true;
		}

		#endregion

		#region SmallGrid Methods

		private static void CalculateZoopSegments(Vector3 startPos, Vector3 endPos, ZoopSegment segment) {
			segment.Directions.Clear();

			float startX = startPos.x;
			float startY = startPos.y;
			float startZ = startPos.z;
			float endX = endPos.x;
			float endY = endPos.y;
			float endZ = endPos.z;

			float absX = Math.Abs(endX - startX);
			float absY = Math.Abs(endY - startY);
			float absZ = Math.Abs(endZ - startZ);

			if(absX > float.Epsilon) {
				segment.CountX = 1 + (int)(Math.Abs(startX - endX) * 2);
				segment.IncreasingX = startX < endX;
				UpdateZoopOrder(ZoopDirection.x);
				segment.Directions.Add(ZoopDirection.x);
			}

			if(absY > float.Epsilon) {
				segment.CountY = 1 + (int)(Math.Abs(startY - endY) * 2);
				segment.IncreasingY = startY < endY;
				UpdateZoopOrder(ZoopDirection.y);
				segment.Directions.Add(ZoopDirection.y);
			}

			if(absZ > float.Epsilon) {
				segment.CountZ = 1 + (int)(Math.Abs(startZ - endZ) * 2);
				segment.IncreasingZ = startZ < endZ;
				UpdateZoopOrder(ZoopDirection.z);
				segment.Directions.Add(ZoopDirection.z);
			}
		}

		private static void BuildSmallStructureList(InventoryManager inventoryManager, List<ZoopSegment> zoops) {
			structures.Clear();
			structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
			structuresCacheCorner.ForEach(structure => structure.GameObject.SetActive(false));

			int straight = 0;
			int corners = 0;
			ZoopDirection lastDirection = ZoopDirection.none;
			bool canBuildNext = true;
			for(int segmentIndex = 0; segmentIndex < zoops.Count; segmentIndex++) {
				ZoopSegment segment = zoops[segmentIndex];
				for(int directionIndex = 0; directionIndex < segment.Directions.Count; directionIndex++) {
					ZoopDirection zoopDirection = segment.Directions[directionIndex];
					int zoopCounter = GetCountForDirection(zoopDirection, segment);

					// If it's not the last segment and it's the last direction in the segment, reduce the counter by 1
					if(segmentIndex < zoops.Count - 1 && directionIndex == segment.Directions.Count - 1) {
						zoopCounter--;
					} else if(directionIndex < segment.Directions.Count - 1) {
						zoopCounter--;
					}

					for(int j = 0; j < zoopCounter; j++) {
						if(structures.Count > 0 && (j == 0 || segmentIndex > 0) && inventoryManager.ConstructionPanel.Parent.Constructables.Count > 1) {
							if(zoopDirection != lastDirection) {
								AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, true, corners, straight, ref canBuildNext, inventoryManager); // start with corner on secondary and tertiary zoop directions
								corners++;
							} else {
								AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners, ref canBuildNext, inventoryManager);
								straight++;
							}
						} else {
							AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, straight, corners, ref canBuildNext, inventoryManager);
							straight++;
						}
						lastDirection = zoopDirection;
					}
				}
			}
		}

		private static int GetCountForDirection(ZoopDirection direction, ZoopSegment segment) {
			switch(direction) {
				case ZoopDirection.x:
					return segment.CountX;
				case ZoopDirection.y:
					return segment.CountY;
				case ZoopDirection.z:
					return segment.CountZ;
				case ZoopDirection.none:
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private static bool GetIncreasingForDirection(ZoopDirection direction, ZoopSegment segment) {
			switch(direction) {
				case ZoopDirection.x:
					return segment.IncreasingX;
				case ZoopDirection.y:
					return segment.IncreasingY;
				case ZoopDirection.z:
					return segment.IncreasingZ;
				case ZoopDirection.none:
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		#endregion

		#region BigGrid Methods

		private static void CalculateZoopPlane(Vector3 startPos, Vector3 endPos, ZoopPlane plane) {

			float startX = startPos.x;
			float startY = startPos.y;
			float startZ = startPos.z;
			float endX = endPos.x;
			float endY = endPos.y;
			float endZ = endPos.z;

			float absX = Math.Abs(endX - startX) / 2;
			float absY = Math.Abs(endY - startY) / 2;
			float absZ = Math.Abs(endZ - startZ) / 2;

			var directions = new List<(float value, ZoopDirection direction, int count, bool increasing)>{
				(absX, ZoopDirection.x, 1 + (int)(Math.Abs(startX - endX)/2), startX < endX),
				(absY, ZoopDirection.y, 1 + (int)(Math.Abs(startY - endY)/2), startY < endY),
				(absZ, ZoopDirection.z, 1 + (int)(Math.Abs(startZ - endZ)/2), startZ < endZ)
			};

			directions.Sort((a, b) => b.value.CompareTo(a.value));

			plane.Directions = plane.Directions with { direction1 = directions[0].direction, direction2 = directions[1].direction };
			plane.Count = plane.Count with { direction1 = directions[0].count, direction2 = directions[1].count };
			plane.Increasing = plane.Increasing with { direction1 = directions[0].increasing, direction2 = directions[1].increasing };
		}

		private static void BuildBigStructureList(InventoryManager inventoryManager, ZoopPlane plane) {
			structures.Clear();
			structuresCacheStraight.ForEach(structure => structure.GameObject.SetActive(false));
			int count = 0;
			bool canBuildNext = true;

			for(int indexDirection2 = 0; indexDirection2 < plane.Count.direction2; indexDirection2++) {
				for(int indexDirection1 = 0; indexDirection1 < plane.Count.direction1; indexDirection1++) {
					AddStructure(inventoryManager.ConstructionPanel.Parent.Constructables, false, count, 0, ref canBuildNext, inventoryManager);
					count++;
				}
			}
		}

		private static void UpdateZoopOrder(ZoopDirection direction) {
			// add if this direction is not yet in the list
			if(!PreferredZoopOrder.Contains(direction)) {
				PreferredZoopOrder.Add(direction);
			}
		}

		#endregion

		#region Calculation Methods

		private static void SetStraightRotationSmallGrid(Structure structure, ZoopDirection zoopDirection) {
			switch(zoopDirection) {
				case ZoopDirection.x:
					if(structure is Chute) {
						structure.ThingTransformRotation = SmartRotate.RotX.Rotation;
					} else {
						structure.ThingTransformRotation = SmartRotate.RotY.Rotation;
					}

					break;
				case ZoopDirection.y:
					if(structure is Chute) {
						structure.ThingTransformRotation = SmartRotate.RotZ.Rotation;
					} else {
						structure.ThingTransformRotation = SmartRotate.RotX.Rotation;
					}

					break;
				case ZoopDirection.z:
					if(structure is Chute) {
						structure.ThingTransformRotation = SmartRotate.RotY.Rotation;
					} else {
						structure.ThingTransformRotation = SmartRotate.RotZ.Rotation;
					}

					break;
				case ZoopDirection.none:
				default:
					throw new ArgumentOutOfRangeException(nameof(zoopDirection), zoopDirection, null);
			}
		}

		private static void SetStraightRotationBigGrid(Structure structure, ZoopDirection zoopDirection1, ZoopDirection zoopDirection2) {
			//TODO change fonctionnement for wall
			switch(zoopDirection1) {
				case ZoopDirection.x:
					structure.ThingTransformRotation = SmartRotate.RotX.Rotation;
					break;
				case ZoopDirection.y:
					structure.ThingTransformRotation = SmartRotate.RotY.Rotation;
					break;
				case ZoopDirection.z:
					structure.ThingTransformRotation = SmartRotate.RotZ.Rotation;
					break;
				case ZoopDirection.none:
				default:
					throw new ArgumentOutOfRangeException(nameof(zoopDirection1), zoopDirection1, null);
			}
		}

		private static void SetCornerRotation(Structure structure, ZoopDirection zoopDirectionFrom, bool increasingFrom, ZoopDirection zoopDirectionTo, bool increasingTo) {
			float xOffset = 0.0f;
			float yOffset = 0.0f;
			float zOffset = 0.0f;
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

			structure.ThingTransformRotation = ZoopUtils.GetCornerRotation(zoopDirectionFrom, increasingFrom, zoopDirectionTo, increasingTo, xOffset, yOffset, zOffset);
		}

		#endregion

	}

}