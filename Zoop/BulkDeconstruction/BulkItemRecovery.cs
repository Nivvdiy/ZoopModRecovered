using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.GridSystem;
using Assets.Scripts;
using UnityEngine;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Handles item recovery when deconstructing bulk structures.
/// Uses BuildStates[0].Tool to get the correct item and quantity.
/// </summary>
public class BulkItemRecovery
{
  /// <summary>
  /// Collects all items from the bulk structures and spawns them as stacks.
  /// Returns a dictionary of item prefabs and their total quantities.
  /// </summary>
  public Dictionary<Thing, int> CollectItemsFromBulk(List<Structure> bulk)
  {
    Dictionary<Thing, int> itemsByPrefab = new Dictionary<Thing, int>();

    if (bulk == null || bulk.Count == 0)
    {
      ZoopLog.Info("[ItemRecovery] No bulk structures to collect items from");
      return itemsByPrefab;
    }

    ZoopLog.Info($"[ItemRecovery] Collecting items from {bulk.Count} structures");

    foreach (Structure structure in bulk)
    {
      if (structure == null)
        continue;

      CollectItemsFromStructure(structure, itemsByPrefab);
    }

    ZoopLog.Info($"[ItemRecovery] Collected {itemsByPrefab.Count} different item types");
    return itemsByPrefab;
  }

  /// <summary>
  /// Spawns all collected items as stacks at the specified position.
  /// Uses a grid layout to prevent stacks from overlapping.
  /// </summary>
  public void SpawnCollectedItems(Dictionary<Thing, int> itemsByPrefab, Vector3 position)
  {
    if (itemsByPrefab == null || itemsByPrefab.Count == 0)
    {
      ZoopLog.Info("[ItemRecovery] No items to spawn");
      return;
    }

    int totalSpawned = 0;
    StackGridLayout gridLayout = new StackGridLayout(position);

    foreach (KeyValuePair<Thing, int> kvp in itemsByPrefab)
    {
      Thing itemPrefab = kvp.Key;
      int quantity = kvp.Value;

      int spawned = SpawnItemsWithLayout(itemPrefab, quantity, gridLayout);
      totalSpawned += spawned;
    }

    ZoopLog.Info($"[ItemRecovery] Spawned {totalSpawned} items in stacks");
  }

  /// <summary>
  /// Collects items from a single structure and adds them to the dictionary.
  /// </summary>
  private void CollectItemsFromStructure(Structure structure, Dictionary<Thing, int> itemsByPrefab)
  {
    try
    {
      // Get BuildStates - this is a public list on Structure
      if (structure.BuildStates == null || structure.BuildStates.Count == 0)
      {
        return;
      }

      // BuildState is a public class with public field Tool of type ToolUse
      BuildState buildState = structure.BuildStates[0];

      // ToolUse inherits from ToolBasic which has the public fields we need
      ToolBasic tool = buildState.Tool;
      if (tool == null)
      {
        return;
      }

      // Collect primary item
      if (tool.ToolEntry != null && tool.EntryQuantity > 0)
      {
        if (!itemsByPrefab.ContainsKey(tool.ToolEntry))
          itemsByPrefab[tool.ToolEntry] = 0;

        itemsByPrefab[tool.ToolEntry] += tool.EntryQuantity;
      }

      // Collect secondary item
      if (tool.ToolEntry2 != null && tool.EntryQuantity2 > 0)
      {
        if (!itemsByPrefab.ContainsKey(tool.ToolEntry2))
          itemsByPrefab[tool.ToolEntry2] = 0;

        itemsByPrefab[tool.ToolEntry2] += tool.EntryQuantity2;
      }
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error($"[ItemRecovery] Error collecting items from {structure.PrefabName}: {ex.Message}");
    }
  }

  /// <summary>
  /// Spawns multiple items using OnServer.Create with grid layout to prevent overlapping.
  /// GUARANTEES all items are spawned by testing all 4 directions if needed.
  /// </summary>
  private int SpawnItemsWithLayout(Thing itemPrefab, int quantity, StackGridLayout gridLayout)
  {
    try
    {
      // Get max stack size for this item
      int maxStackSize = GetMaxStackSize(itemPrefab);

      // Determine item dimensions based on prefab name
      ItemDimensions dimensions = GetItemDimensions(itemPrefab.PrefabName);

      ZoopLog.Debug($"[ItemRecovery] Spawning {quantity}x {itemPrefab.PrefabName} (max stack: {maxStackSize}, dims: {dimensions.Height}h x {dimensions.Width}w)");

      int totalSpawned = 0;
      int remaining = quantity;

      while (remaining > 0)
      {
        int stackSize = Mathf.Min(remaining, maxStackSize);

        // Try to find a valid position (MUST succeed)
        Vector3 spawnPos = FindValidSpawnPosition(gridLayout, dimensions);

        // Spawn the item (this MUST work)
        Thing spawnedItem = OnServer.Create<Thing>(itemPrefab, spawnPos, Quaternion.identity);

        if (spawnedItem != null)
        {
          Stackable stackable = spawnedItem as Stackable;
          if (stackable != null)
          {
            stackable.SetQuantity(stackSize);
            ZoopLog.Debug($"[ItemRecovery] Spawned stack of {stackSize}x at {spawnPos}");
          }

          totalSpawned += stackSize;
          remaining -= stackSize;
        }
        else
        {
          // This should NEVER happen, but if it does, force spawn at base position + random offset
          ZoopLog.Error($"[ItemRecovery] CRITICAL: Failed to spawn at {spawnPos}, forcing spawn with offset");
          Vector3 emergencyPos = gridLayout.GetBasePosition() + new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(0f, 2f),
            Random.Range(-5f, 5f)
          );

          Thing emergencyItem = OnServer.Create<Thing>(itemPrefab, emergencyPos, Quaternion.identity);
          if (emergencyItem != null)
          {
            Stackable stackable = emergencyItem as Stackable;
            if (stackable != null)
            {
              stackable.SetQuantity(stackSize);
            }
            totalSpawned += stackSize;
            remaining -= stackSize;
          }
        }
      }

      ZoopLog.Info($"[ItemRecovery] Successfully spawned ALL {totalSpawned}x {itemPrefab.PrefabName}");
      return totalSpawned;
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error($"[ItemRecovery] Failed to spawn {quantity}x {itemPrefab.PrefabName}: {ex.Message}");
      return 0;
    }
  }

  /// <summary>
  /// Finds a valid spawn position.
  /// First tries vertical stacking, then finds nearest point from last column base.
  /// </summary>
  private Vector3 FindValidSpawnPosition(StackGridLayout gridLayout, ItemDimensions dimensions)
  {
    // Check if we can stack vertically in current column
    if (!gridLayout.IsCurrentColumnFull())
    {
      Vector3 stackedPos = gridLayout.GetNextPosition(dimensions);

      // Validate the stacked position
      if (!IsPositionObstructed(stackedPos, dimensions))
      {
        ZoopLog.Debug($"[ItemRecovery] Stacked vertically at {stackedPos} (column stack {gridLayout.IsCurrentColumnFull()})");
        return stackedPos;
      }

      // Stacked position is obstructed, need new column
      ZoopLog.Debug($"[ItemRecovery] Vertical stack obstructed at {stackedPos}, searching for new column base");
    }

    // Current column is full or obstructed, find a new column base
    // IMPORTANT: Search from the CURRENT column base, not original base
    Vector3 searchFrom = gridLayout.GetCurrentColumnBase();
    Vector3 newColumnBase = FindNearestSpawnablePoint(searchFrom, dimensions, gridLayout);

    // Start new column at this position
    gridLayout.StartNewColumn(newColumnBase, dimensions);

    ZoopLog.Debug($"[ItemRecovery] Started new column at {newColumnBase} (searched from {searchFrom}, distance: {Vector3.Distance(searchFrom, newColumnBase):F2}m)");

    return newColumnBase;
  }

  /// <summary>
  /// Finds the nearest spawnable point from the base position.
  /// Tests positions in increasing distance order.
  /// </summary>
  private Vector3 FindNearestSpawnablePoint(Vector3 basePos, ItemDimensions dimensions, StackGridLayout gridLayout)
  {
    // List to store all candidate positions with their distances
    List<SpawnCandidate> candidates = new List<SpawnCandidate>();

    // Test immediate neighbors (4 cardinal directions at 0.6m)
    Vector3[] cardinals = new Vector3[]
    {
      basePos + new Vector3(0.6f, 0f, 0f),   // +X
      basePos + new Vector3(-0.6f, 0f, 0f),  // -X
      basePos + new Vector3(0f, 0f, 0.6f),   // +Z
      basePos + new Vector3(0f, 0f, -0.6f),  // -Z
    };

    foreach (Vector3 pos in cardinals)
    {
      if (!IsPositionObstructed(pos, dimensions) && !gridLayout.IsPositionUsed(pos))
      {
        float distance = Vector3.Distance(basePos, pos);
        candidates.Add(new SpawnCandidate { Position = pos, Distance = distance });
      }
    }

    // Test diagonals (4 diagonal directions at ~0.85m)
    Vector3[] diagonals = new Vector3[]
    {
      basePos + new Vector3(0.6f, 0f, 0.6f),   // +X +Z
      basePos + new Vector3(0.6f, 0f, -0.6f),  // +X -Z
      basePos + new Vector3(-0.6f, 0f, 0.6f),  // -X +Z
      basePos + new Vector3(-0.6f, 0f, -0.6f), // -X -Z
    };

    foreach (Vector3 pos in diagonals)
    {
      if (!IsPositionObstructed(pos, dimensions) && !gridLayout.IsPositionUsed(pos))
      {
        float distance = Vector3.Distance(basePos, pos);
        candidates.Add(new SpawnCandidate { Position = pos, Distance = distance });
      }
    }

    // Test expanded radius in a circular pattern
    for (float radius = 1.2f; radius <= 5.0f; radius += 0.6f)
    {
      // Test 8 directions around the circle
      for (int angle = 0; angle < 360; angle += 45)
      {
        float rad = angle * Mathf.Deg2Rad;
        Vector3 pos = basePos + new Vector3(
          Mathf.Cos(rad) * radius,
          0f,
          Mathf.Sin(rad) * radius
        );

        if (!IsPositionObstructed(pos, dimensions) && !gridLayout.IsPositionUsed(pos))
        {
          float distance = Vector3.Distance(basePos, pos);
          candidates.Add(new SpawnCandidate { Position = pos, Distance = distance });

          // Early exit if we found a good candidate at this radius
          // (no need to test all angles if we found one)
          if (candidates.Count >= 3)
          {
            break;
          }
        }
      }

      // If we found candidates at this radius, we can stop expanding
      if (candidates.Count > 0)
      {
        break;
      }
    }

    // If we found any valid candidates, return the nearest one
    if (candidates.Count > 0)
    {
      // Sort by distance (ascending)
      candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

      Vector3 nearestPos = candidates[0].Position;
      gridLayout.MarkPositionUsed(nearestPos);

      ZoopLog.Debug($"[ItemRecovery] Selected nearest from {candidates.Count} candidates, distance: {candidates[0].Distance:F2}m");

      return nearestPos;
    }

    // ABSOLUTE LAST RESORT - spawn above
    Vector3 abovePos = basePos + new Vector3(0f, 2.0f, 0f);
    ZoopLog.Error($"[ItemRecovery] LAST RESORT: No spawnable point found, spawning above at {abovePos}");
    gridLayout.MarkPositionUsed(abovePos);

    return abovePos;
  }

  /// <summary>
  /// Helper struct to store spawn position candidates with their distances.
  /// </summary>
  private struct SpawnCandidate
  {
    public Vector3 Position;
    public float Distance;
  }

  /// <summary>
  /// Checks if a position is obstructed by other objects.
  /// Uses Unity Bounds for precise collision detection.
  /// </summary>
  private bool IsPositionObstructed(Vector3 position, ItemDimensions dimensions)
  {
    try
    {
      // Add safety margin (10cm on each side)
      const float SAFETY_MARGIN = 0.1f;

      // Create bounds for the item we want to spawn
      Bounds itemBounds = new Bounds(
        position + new Vector3(0f, dimensions.Height * 0.5f, 0f), // Center
        new Vector3(dimensions.Width + SAFETY_MARGIN * 2, dimensions.Height + SAFETY_MARGIN * 2, dimensions.Width + SAFETY_MARGIN * 2) // Size
      );

      // Check the grid cell at this position using the game's grid system
      Grid3 gridPos = new Grid3(position);

      // Get the cell from GridController
      Cell cell = GridController.World?.GetCell(gridPos);

      // If there's a cell with structures, check them
      if (cell != null && cell.AllStructures != null && cell.AllStructures.Count > 0)
      {
        foreach (Structure structure in cell.AllStructures)
        {
          if (structure == null)
            continue;

          // Block if structure doesn't allow gravity to pass (solid object)
          if (!structure.CanGravityPass)
          {
            // Try to get structure's renderer bounds for precise detection
            if (TryGetStructureBounds(structure, out Bounds structureBounds))
            {
              if (itemBounds.Intersects(structureBounds))
              {
                ZoopLog.Debug($"[ItemRecovery] Position blocked by Structure bounds: {structure.PrefabName}");
                return true;
              }
            }
            else
            {
              // Fallback: block if no bounds available
              ZoopLog.Debug($"[ItemRecovery] Position blocked by Structure: {structure.PrefabName} (blocks gravity, no bounds)");
              return true;
            }
          }

          // Block if structure is a wall or similar
          if (structure.StructureCollisionType == CollisionType.BlockGrid)
          {
            if (TryGetStructureBounds(structure, out Bounds structureBounds))
            {
              if (itemBounds.Intersects(structureBounds))
              {
                ZoopLog.Debug($"[ItemRecovery] Position blocked by Structure bounds: {structure.PrefabName} (blocks grid)");
                return true;
              }
            }
            else
            {
              ZoopLog.Debug($"[ItemRecovery] Position blocked by Structure: {structure.PrefabName} (blocks grid, no bounds)");
              return true;
            }
          }
        }
      }

      // Check neighboring cells for structures with bounds
      List<Grid3> neighbors = new List<Grid3>();
      GridController.World.GetGridNeighbours(gridPos, ref neighbors, horizontalOnly: false, includeCorners: false);

      foreach (Grid3 neighborGrid in neighbors)
      {
        Cell neighborCell = GridController.World.GetCell(neighborGrid);
        if (neighborCell != null && neighborCell.AllStructures != null)
        {
          foreach (Structure neighborStruct in neighborCell.AllStructures)
          {
            if (neighborStruct == null)
              continue;

            // Only check solid structures
            if (!neighborStruct.CanGravityPass || neighborStruct.StructureCollisionType == CollisionType.BlockGrid)
            {
              // Use bounds for precise intersection check
              if (TryGetStructureBounds(neighborStruct, out Bounds neighborBounds))
              {
                // Expand neighbor bounds slightly for safety margin
                Bounds expandedNeighborBounds = neighborBounds;
                expandedNeighborBounds.Expand(SAFETY_MARGIN * 2);

                if (itemBounds.Intersects(expandedNeighborBounds))
                {
                  ZoopLog.Debug($"[ItemRecovery] Position blocked by nearby Structure bounds: {neighborStruct.PrefabName}");
                  return true;
                }
              }
            }
          }
        }
      }

      // Additional physics check for non-Structure obstacles
      Vector3 halfExtents = new Vector3(
        (dimensions.Width * 0.5f) + SAFETY_MARGIN,
        (dimensions.Height * 0.5f) + SAFETY_MARGIN,
        (dimensions.Width * 0.5f) + SAFETY_MARGIN
      );

      Vector3 checkPosition = position + new Vector3(0f, dimensions.Height * 0.5f, 0f);
      Collider[] colliders = Physics.OverlapBox(checkPosition, halfExtents, Quaternion.identity);

      foreach (Collider collider in colliders)
      {
        if (collider.isTrigger)
          continue;

        // Check for static world geometry
        if (collider.gameObject.isStatic)
        {
          ZoopLog.Debug($"[ItemRecovery] Position blocked by static object: {collider.gameObject.name}");
          return true;
        }

        // Check for non-stackable items (machines, etc.)
        Item item = collider.GetComponentInParent<Item>();
        if (item != null)
        {
          Stackable stackableItem = item as Stackable;
          if (stackableItem == null)
          {
            ZoopLog.Debug($"[ItemRecovery] Position blocked by non-stackable Item: {item.PrefabName}");
            return true;
          }
        }
      }

      return false;
    }
    catch (System.Exception ex)
    {
      ZoopLog.Warn($"[ItemRecovery] Error checking position obstruction: {ex.Message}");
      return false; // Assume not obstructed on error
    }
  }

  /// <summary>
  /// Tries to get the actual bounds of a structure from its renderers or colliders.
  /// </summary>
  private bool TryGetStructureBounds(Structure structure, out Bounds bounds)
  {
    bounds = new Bounds();

    if (structure == null || structure.gameObject == null)
      return false;

    // Method 1: Try to get bounds from Renderer (most accurate for visual representation)
    Renderer[] renderers = structure.GetComponentsInChildren<Renderer>();
    if (renderers != null && renderers.Length > 0)
    {
      bool initialized = false;
      foreach (Renderer renderer in renderers)
      {
        if (renderer == null || !renderer.enabled)
          continue;

        if (!initialized)
        {
          bounds = renderer.bounds;
          initialized = true;
        }
        else
        {
          bounds.Encapsulate(renderer.bounds);
        }
      }

      if (initialized)
      {
        ZoopLog.Debug($"[ItemRecovery] Got bounds from Renderer: {structure.PrefabName} - Size: {bounds.size}");
        return true;
      }
    }

    // Method 2: Try to get bounds from Collider (fallback)
    Collider[] colliders = structure.GetComponentsInChildren<Collider>();
    if (colliders != null && colliders.Length > 0)
    {
      bool initialized = false;
      foreach (Collider collider in colliders)
      {
        if (collider == null || collider.isTrigger)
          continue;

        if (!initialized)
        {
          bounds = collider.bounds;
          initialized = true;
        }
        else
        {
          bounds.Encapsulate(collider.bounds);
        }
      }

      if (initialized)
      {
        ZoopLog.Debug($"[ItemRecovery] Got bounds from Collider: {structure.PrefabName} - Size: {bounds.size}");
        return true;
      }
    }

    // Method 3: Fallback to transform position with default size
    if (structure.transform != null)
    {
      bounds = new Bounds(structure.transform.position, Vector3.one * 2f); // Default 2x2x2m
      ZoopLog.Debug($"[ItemRecovery] Using default bounds for: {structure.PrefabName}");
      return true;
    }

    return false;
  }

  /// <summary>
  /// Gets item dimensions based on prefab name.
  /// </summary>
  private ItemDimensions GetItemDimensions(string prefabName)
  {
    // Heavy cables
    if (prefabName.Contains("Heavy") || prefabName.Contains("SuperHeavy"))
    {
      return new ItemDimensions { Height = 0.6f, Width = 0.6f }; // Marge de sécurité: 0.5 + 0.1
    }

    // Pipes
    if (prefabName.Contains("Pipe"))
    {
      return new ItemDimensions { Height = 0.85f, Width = 0.6f }; // Marge de sécurité: 0.75 + 0.1
    }

    // Regular cables and chutes
    if (prefabName.Contains("Cable") || prefabName.Contains("Chute"))
    {
      return new ItemDimensions { Height = 0.35f, Width = 0.6f }; // Marge de sécurité: 0.25 + 0.1
    }

    // Default dimensions
    return new ItemDimensions { Height = 0.4f, Width = 0.6f };
  }

  /// <summary>
  /// Gets the maximum stack size for an item.
  /// </summary>
  private int GetMaxStackSize(Thing itemPrefab)
  {
    try
    {
      // Try to cast to Stackable and use GetMaxQuantity property
      Stackable stackable = itemPrefab as Stackable;
      if (stackable != null)
      {
        return (int)stackable.GetMaxQuantity;
      }

      // Default for non-stackable items
      return 1;
    }
    catch
    {
      return 1; // Safe default for non-stackable
    }
  }
}

/// <summary>
/// Item dimensions for grid layout.
/// </summary>
internal struct ItemDimensions
{
  public float Height;
  public float Width;
}

/// <summary>
/// Grid layout manager for spawning stacks without overlapping.
/// Max 5 stacks vertically, then finds next position from last valid spawn point.
/// </summary>
internal class StackGridLayout
{
  private const int MAX_VERTICAL_STACKS = 5;

  private Vector3 _basePosition;
  private Vector3 _currentColumnBase;      // Base de la colonne actuelle
  private int _currentColumnStacks;        // Nombre de stacks dans la colonne actuelle
  private float _currentColumnHeight;      // Hauteur accumulée dans la colonne
  private HashSet<Vector3Int> _usedPositions = new HashSet<Vector3Int>();

  public StackGridLayout(Vector3 basePosition)
  {
    _basePosition = basePosition;
    _currentColumnBase = basePosition;
    _currentColumnStacks = 0;
    _currentColumnHeight = 0f;
  }

  /// <summary>
  /// Gets the base position (for emergency spawns).
  /// </summary>
  public Vector3 GetBasePosition()
  {
    return _basePosition;
  }

  /// <summary>
  /// Checks if a position has already been used for spawning.
  /// </summary>
  public bool IsPositionUsed(Vector3 position)
  {
    Vector3Int gridPos = Vector3Int.RoundToInt(position * 10f); // 0.1m precision
    return _usedPositions.Contains(gridPos);
  }

  /// <summary>
  /// Marks a position as used.
  /// </summary>
  public void MarkPositionUsed(Vector3 position)
  {
    Vector3Int gridPos = Vector3Int.RoundToInt(position * 10f); // 0.1m precision
    _usedPositions.Add(gridPos);
  }

  /// <summary>
  /// Gets the next spawn position based on item dimensions.
  /// First tries to stack vertically, then finds nearest point from current column base.
  /// </summary>
  public Vector3 GetNextPosition(ItemDimensions dimensions)
  {
    Vector3 position;

    // Try to stack vertically in current column
    if (_currentColumnStacks < MAX_VERTICAL_STACKS)
    {
      position = _currentColumnBase + new Vector3(0f, _currentColumnHeight, 0f);

      // This position will be validated by IsPositionObstructed in the caller
      // If valid, update column state
      _currentColumnHeight += dimensions.Height;
      _currentColumnStacks++;

      MarkPositionUsed(position);
      return position;
    }

    // Column is full, need to find a new column base
    // This will be called by the spawn logic after finding a valid position
    // For now, just return a position that will be replaced
    position = _currentColumnBase;
    return position;
  }

  /// <summary>
  /// Starts a new column from a specific base position.
  /// Called when a new valid horizontal position is found.
  /// </summary>
  public void StartNewColumn(Vector3 newColumnBase, ItemDimensions dimensions)
  {
    _currentColumnBase = newColumnBase;
    _currentColumnStacks = 1;
    _currentColumnHeight = dimensions.Height;

    MarkPositionUsed(newColumnBase);
  }

  /// <summary>
  /// Gets the current column base (last valid horizontal position).
  /// </summary>
  public Vector3 GetCurrentColumnBase()
  {
    return _currentColumnBase;
  }

  /// <summary>
  /// Checks if current column is full.
  /// </summary>
  public bool IsCurrentColumnFull()
  {
    return _currentColumnStacks >= MAX_VERTICAL_STACKS;
  }
}
