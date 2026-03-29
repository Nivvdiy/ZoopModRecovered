using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;

namespace ZoopMod.Zoop;

public static class ZoopUtility
{
  #region Fields

  private static readonly ZoopSession Session = new();
  private static readonly ZoopPreviewFactory PreviewFactory = new(Session);
  private static readonly ZoopPreviewValidator PreviewValidator =
    new(ZoopConstructableResolver.ResolveBuildIndex, ZoopConstructableResolver.GetConstructableForBuildIndex,
      allowPlacementUpdate => Session.AllowPlacementUpdate = allowPlacementUpdate);
  private static readonly ZoopPreviewColorizer PreviewColorizer = new(Session, () => LineColor);
  private static readonly ZoopSmallGridCoordinator SmallGridCoordinator =
    new(Session, PreviewFactory, CanConstructSmallCell, hasError => Session.HasError = Session.HasError || hasError);
  private static readonly ZoopBigGridCoordinator BigGridCoordinator =
    new(Session, PreviewFactory, CanConstructBigCell, hasError => Session.HasError = Session.HasError || hasError);
  private static readonly ZoopController Controller =
    new(Session, PreviewFactory, PreviewColorizer, SmallGridCoordinator, BigGridCoordinator);

  public static int PreviewCount => Session.PreviewCount;
  public static bool HasError => Session.HasError;
  public static Coroutine ActionCoroutine => Session.ActionCoroutine;
  public static bool AllowPlacementUpdate => Session.AllowPlacementUpdate;

  public static bool IsZoopKeyPressed { get; set; }

  public static bool IsZooping => Controller.IsZooping;

  public static Color LineColor { get; set; } = Color.green;
  #endregion

  #region Common Methods

  public static void StartZoop(InventoryManager inventoryManager)
  {
    Controller.StartZoop(inventoryManager);
  }

  public static void CancelZoop()
  {
    Controller.CancelZoop();
  }

  public static void SetPendingBuild(InventoryManager inventoryManager, Coroutine coroutine)
  {
    Controller.SetPendingBuild(inventoryManager, coroutine);
  }

  public static void BuildZoop(InventoryManager inventoryManager)
  {
    Controller.BuildZoop(inventoryManager);
  }

  public static void AddWaypoint()
  {
    Controller.AddWaypoint();
  }

  public static void RemoveLastWaypoint()
  {
    Controller.RemoveLastWaypoint();
  }

  #endregion

  #region Conditional Methods

  /// <summary>
  /// Checks whether a small-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructSmallCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return PreviewValidator.CanConstructSmallCell(Session, inventoryManager, structure, structureIndex);
  }

  /// <summary>
  /// Checks whether a large-grid preview structure can be built in its current cell.
  /// </summary>
  private static bool CanConstructBigCell(InventoryManager inventoryManager, Structure structure, int structureIndex)
  {
    return PreviewValidator.CanConstructBigCell(Session, inventoryManager, structure, structureIndex);
  }

  #endregion
}
