using System.Collections.Generic;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Util;
using UnityEngine;
using ZoopMod.Zoop.Core;

namespace ZoopMod.Zoop.Preview;

internal static class ZoopPreviewColorizer
{
  private static readonly Color ErrorColor = Color.red;
  private static readonly Color WaypointColor = Color.blue;
  private static readonly Color StartColor = Color.magenta;
  private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
  private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
  private static readonly MaterialPropertyBlock SharedPropertyBlock = new();

  public static void ApplyColor(InventoryManager inventoryManager, Structure structure,
    IReadOnlyList<Vector3> waypoints,
    bool hasError, Color lineColor)
  {
    var canConstruct = !hasError;
    var waypointIndex = GetWaypointIndex(waypoints, structure.Position);
    var isWaypoint = waypointIndex >= 0;
    var isStart = waypointIndex == 0;

    var color = ResolveMainColor(canConstruct, isStart, isWaypoint, lineColor);

    if (structure is SmallGrid smallGrid)
    {
      color = ApplySmallGridColors(inventoryManager, smallGrid, canConstruct, isStart, isWaypoint, color);
    }

    color.a = inventoryManager.CursorAlphaConstructionMesh;
    if (structure.Wireframe?.BlueprintRenderer?.material != null)
    {
      structure.Wireframe.BlueprintRenderer.material.color = color;
    }
  }

  private static Color ResolveMainColor(bool canConstruct, bool isStart, bool isWaypoint, Color lineColor)
  {
    if (!canConstruct) return ErrorColor;
    if (isStart) return StartColor;
    if (isWaypoint) return WaypointColor;
    return lineColor;
  }

  private static Color ResolveHelperColor(InventoryManager inventoryManager, bool canConstruct, int joiningCount)
  {
    if (!canConstruct) return Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
    if (joiningCount > 0) return Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
    return Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
  }

  private static Color ApplySmallGridColors(InventoryManager inventoryManager, SmallGrid smallGrid,
    bool canConstruct, bool isStart, bool isWaypoint, Color color)
  {
    var joiningOpenEnds = smallGrid.WillJoinNetwork() ?? [];
    var hasBlueprintMaterial = smallGrid.Wireframe?.BlueprintRenderer?.material != null;
    var helperColor = ResolveHelperColor(inventoryManager, canConstruct, joiningOpenEnds.Count);

    // Some modded small-grid previews do not expose a blueprint renderer, so their mesh tint
    // needs to carry the start/waypoint/error color that vanilla previews show separately.
    var rendererColor = !hasBlueprintMaterial && (!canConstruct || isStart || isWaypoint)
      ? color.SetAlpha(inventoryManager.CursorAlphaConstructionHelper)
      : helperColor;

    ApplyRendererColors(smallGrid, rendererColor, hasBlueprintMaterial);
    ApplyOpenEndColors(smallGrid, helperColor);

    return canConstruct && joiningOpenEnds.Count > 0 ? Color.yellow : color;
  }

  // Intentional guard-clause loop — avoids .Where() enumerator allocation in this per-frame hot path.
#pragma warning disable S3267
  private static void ApplyRendererColors(SmallGrid smallGrid, Color rendererColor, bool hasBlueprintMaterial)
  {
    if (smallGrid.Renderers == null)
    {
      return;
    }

    foreach (var renderer in smallGrid.Renderers)
    {
      if (renderer != null && renderer.HasRenderer())
      {
        SetThingRendererColor(renderer, rendererColor, !hasBlueprintMaterial);
      }
    }
  }

  private static void ApplyOpenEndColors(SmallGrid smallGrid, Color helperColor)
  {
    if (smallGrid.OpenEnds == null)
    {
      return;
    }

    foreach (var end in smallGrid.OpenEnds)
    {
      if (end?.HelperRenderer?.material != null)
      {
        end.HelperRenderer.material.color = helperColor;
      }
    }
  }
#pragma warning restore S3267

  private static int GetWaypointIndex(IReadOnlyList<Vector3> waypoints, Vector3 position)
  {
    for (var index = 0; index < waypoints.Count; index++)
    {
      if (ZoopPositionUtility.IsSameZoopPosition(waypoints[index], position))
      {
        return index;
      }
    }

    return -1;
  }

  private static void SetThingRendererColor(ThingRenderer thingRenderer, Color color, bool usePropertyBlock)
  {
    if (!usePropertyBlock)
    {
      thingRenderer.SetColor(color);
      return;
    }

    var unityRenderer = thingRenderer.GetRenderer();
    if (unityRenderer == null)
    {
      return;
    }

    unityRenderer.GetPropertyBlock(SharedPropertyBlock);
    SharedPropertyBlock.SetColor(ColorPropertyId, color);
    SharedPropertyBlock.SetColor(BaseColorPropertyId, color);
    unityRenderer.SetPropertyBlock(SharedPropertyBlock);
  }
}
