using System.Collections.Generic;
using System.Linq;
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

  public static void ApplyColor(InventoryManager inventoryManager, Structure structure, IReadOnlyList<Vector3> waypoints,
    bool hasError, Color lineColor)
  {
    var canConstruct = !hasError;
    var waypointIndex = GetWaypointIndex(waypoints, structure.Position);
    var isWaypoint = waypointIndex >= 0;
    var isStart = waypointIndex == 0;
    Color color;
    if (!canConstruct)
    {
      color = ErrorColor;
    }
    else if (isStart)
    {
      color = StartColor;
    }
    else if (isWaypoint)
    {
      color = WaypointColor;
    }
    else
    {
      color = lineColor;
    }

    if (structure is SmallGrid smallGrid)
    {
      var joiningOpenEnds = smallGrid.WillJoinNetwork() ?? [];
      var hasBlueprintMaterial = structure.Wireframe?.BlueprintRenderer?.material != null;
      Color helperColor;
      if (!canConstruct)
      {
        helperColor = Color.red.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
      }
      else if (joiningOpenEnds.Count > 0)
      {
        helperColor = Color.yellow.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
      }
      else
      {
        helperColor = Color.green.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
      }

      var rendererColor = helperColor;
      // Some modded small-grid previews do not expose a blueprint renderer, so their mesh tint
      // needs to carry the start/waypoint/error color that vanilla previews show separately.
      if (!hasBlueprintMaterial && (!canConstruct || isStart || isWaypoint))
      {
        rendererColor = color.SetAlpha(inventoryManager.CursorAlphaConstructionHelper);
      }

      if (smallGrid.Renderers != null)
      {
        foreach (var renderer in smallGrid.Renderers.Where(renderer => renderer != null && renderer.HasRenderer()))
        {
          SetThingRendererColor(renderer, rendererColor, !hasBlueprintMaterial);
        }
      }

      if (smallGrid.OpenEnds != null)
      {
        foreach (var end in smallGrid.OpenEnds.Where(end => end?.HelperRenderer?.material != null))
        {
          end.HelperRenderer.material.color = helperColor;
        }
      }

      color = canConstruct && joiningOpenEnds.Count > 0 ? Color.yellow : color;
    }

    color.a = inventoryManager.CursorAlphaConstructionMesh;
    if (structure.Wireframe?.BlueprintRenderer?.material != null)
    {
      structure.Wireframe.BlueprintRenderer.material.color = color;
    }
  }

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

    var propertyBlock = new MaterialPropertyBlock();
    unityRenderer.GetPropertyBlock(propertyBlock);
    propertyBlock.SetColor(ColorPropertyId, color);
    propertyBlock.SetColor(BaseColorPropertyId, color);
    unityRenderer.SetPropertyBlock(propertyBlock);
  }
}
