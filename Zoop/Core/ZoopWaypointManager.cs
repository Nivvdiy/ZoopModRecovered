using Assets.Scripts.Inventory;
using UnityEngine;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.Core;

/// <summary>
/// Owns waypoint mutation for an active zoop draft: adding, removing, and snapping
/// waypoints from the current cursor position.
/// </summary>
internal sealed class ZoopWaypointManager
{
  private enum WaypointCaptureResult
  {
    Added,
    Duplicate,
    MissingCursor
  }

  private readonly ZoopPreviewCoordinator previewCoordinator;
  private readonly IZoopController host;

  internal ZoopWaypointManager(ZoopPreviewCoordinator previewCoordinator, IZoopController host)
  {
    this.previewCoordinator = previewCoordinator;
    this.host = host;
  }

  public void AddWaypoint()
  {
    var draft = host.ActiveDraft;
    var supportsWaypoints = previewCoordinator.FindStrategy(InventoryManager.ConstructionCursor)?.SupportsWaypoints ?? false;
    if (!host.IsPreviewing || draft == null || !supportsWaypoints)
    {
      ZoopLog.Debug(
        $"[Waypoint] Add ignored. IsPreviewing={host.IsPreviewing}, DraftPresent={draft != null}, SupportsWaypoints={supportsWaypoints}.");
      return;
    }

    var currentPos = previewCoordinator.GetCurrentMouseGridPosition();
    var lastWaypoint = draft.Waypoints[draft.Waypoints.Count - 1];
    ZoopLog.Debug(
      $"[Waypoint] Add requested. CurrentPos={(currentPos.HasValue ? currentPos.Value.ToString() : "<none>")}, LastWaypoint={lastWaypoint}, PreviewCount={draft.PreviewCount}, WaypointCount={draft.Waypoints.Count}.");
    switch (TryCaptureCurrentWaypoint(draft, invalidatePreview: true, out var capturedPos))
    {
      case WaypointCaptureResult.Added:
        ZoopLog.Debug(
          $"[Waypoint] Added waypoint at {capturedPos} directly from the snapped cursor position. NewWaypointCount={draft.Waypoints.Count}.");
        break;
      case WaypointCaptureResult.Duplicate:
        ZoopLog.Debug("[Waypoint] Add ignored because the current cursor position already matches the last waypoint.");
        // TODO show message to user that waypoint is already added
        break;
      case WaypointCaptureResult.MissingCursor:
        ZoopLog.Debug("[Waypoint] Add ignored because no current grid position was available.");
        break;
    }
  }

  public void RemoveLastWaypoint()
  {
    var draft = host.ActiveDraft;
    if (!host.IsPreviewing || draft == null ||
        !(previewCoordinator.FindStrategy(InventoryManager.ConstructionCursor)?.SupportsWaypoints ?? false))
    {
      return;
    }

    if (draft.Waypoints.Count > 1)
    {
      draft.Waypoints.RemoveAt(draft.Waypoints.Count - 1);
      previewCoordinator.Invalidate();
    }
  }

  private WaypointCaptureResult TryCaptureCurrentWaypoint(ZoopDraft draft, bool invalidatePreview,
    out Vector3 capturedPos)
  {
    capturedPos = default;

    var currentPos = previewCoordinator.GetCurrentMouseGridPosition();
    if (!currentPos.HasValue)
    {
      return WaypointCaptureResult.MissingCursor;
    }

    capturedPos = currentPos.Value;
    if (ZoopPositionUtility.IsSameZoopPosition(draft.Waypoints[draft.Waypoints.Count - 1], capturedPos))
    {
      return WaypointCaptureResult.Duplicate;
    }

    draft.Waypoints.Add(capturedPos);
    if (invalidatePreview)
    {
      previewCoordinator.Invalidate();
    }

    return WaypointCaptureResult.Added;
  }
}
