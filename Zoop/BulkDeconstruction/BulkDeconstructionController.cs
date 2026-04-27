using System.Collections;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;
using ZoopMod.Zoop.EntryPoints.Input;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.BulkDeconstruction;

/// <summary>
/// Controller for bulk deconstruction system.
/// Handles detection, validation, and progressive deconstruction of bulk elements.
/// Similar to ZoopController but for deconstruction instead of construction.
/// </summary>
public class BulkDeconstructionController : MonoBehaviour
{
  private static BulkDeconstructionController _instance;
  public static BulkDeconstructionController Instance => _instance;

  private readonly BulkDetector _detector = new BulkDetector();
  private readonly BulkValidator _validator = new BulkValidator();
  private readonly BulkItemRecovery _itemRecovery = new BulkItemRecovery();
  private readonly BulkDeconstructionStatusIndicator _statusIndicator = new BulkDeconstructionStatusIndicator();
  private readonly BulkDeconstructionTooltip _tooltip = new BulkDeconstructionTooltip();

  private bool _isActive;
  private Thing _lastHeldTool;
  private Structure _currentTarget;
  private int _currentTargetInstanceID; // Track by InstanceID instead of reference
  private List<Structure> _currentBulk;
  private BulkValidator.ValidationResult _currentValidation;
  private bool _isDeconstructing; // Currently executing deconstruction
  private Coroutine _deconstructionCoroutine;

  // Async exploration tracking
  private Coroutine _explorationCoroutine;
  private bool _isExploring;
  private Structure _explorationTarget; // Track what we're currently exploring

  // Optimization: throttle raycast to reduce per-frame cost
  private int _raycastThrottleFrames = 0;
  private const int RaycastThrottleInterval = 2; // Only raycast every N frames (lowered from 3 to reduce detection lag)

  // Public properties for patches
  public bool IsActive => _isActive;
  public Structure CurrentTarget => _currentTarget;
  public BulkValidator.ValidationResult CurrentValidation => _currentValidation;
  public bool IsDeconstructing => _isDeconstructing;
  public int CurrentBulkSize => _currentBulk?.Count ?? 0;

  public static void Initialize()
  {
    if (_instance != null)
    {
      ZoopLog.Info("[BulkDeconstruction] Already initialized");
      return;
    }

    var gameObject = new GameObject("BulkDeconstructionController");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<BulkDeconstructionController>();

    // Register with runtime
    Patches.BulkDeconstructionRuntime.Initialize(_instance);

    // Initialize status indicator after a short delay to ensure UI is loaded
    _instance.StartCoroutine(_instance.InitializeStatusIndicatorDelayed());

    ZoopLog.Info("[BulkDeconstruction] Initialized successfully");
  }

  private IEnumerator InitializeStatusIndicatorDelayed()
  {
    // Wait until a save is loaded (InventoryManager is available)
    while (InventoryManager.Instance == null)
    {
      yield return new WaitForSeconds(0.5f);
    }

    // Wait an additional second for UI to be fully ready
    yield return new WaitForSeconds(1f);

    _statusIndicator.Initialize();
  }

  private void Update()
  {
    // Only run when a save is loaded and inventory system is available
    if (InventoryManager.Instance == null || InventoryManager.Instance.ActiveHand == null)
    {
      if (_isActive)
      {
        DeactivateMode("Save unloaded or inventory not ready");
      }
      return;
    }

    // Update status indicator visibility based on UI state
    _statusIndicator.Update();

    // Detect tool changes and auto-disable mode
    var currentTool = GetCurrentTool();
    if (_lastHeldTool != currentTool)
    {
      _lastHeldTool = currentTool;
      if (_isActive)
      {
        DeactivateMode("Tool changed");
      }
    }

    // Handle key press to toggle mode
    if (UnityEngine.Input.GetKeyDown(ZoopKeyBindings.BulkDeconstruct))
    {
      if (!_isActive)
      {
        TryActivateMode();
      }
      else
      {
        DeactivateMode("User toggled off");
      }
    }

    // Update detection if active and not currently deconstructing
    if (_isActive && !_isDeconstructing)
    {
      UpdateDetection();
    }
  }

  private void TryActivateMode()
  {
    if (!HasValidTool())
    {
      ZoopLog.Info("[BulkDeconstruction] Cannot activate: No valid tool equipped");
      return;
    }

    _isActive = true;
    _raycastThrottleFrames = RaycastThrottleInterval; // Force immediate raycast on next Update()
    _statusIndicator.SetVisible(true);
    ZoopLog.Info("[BulkDeconstruction] Mode activated");
  }

  private void DeactivateMode(string reason)
  {
    _isActive = false;
    _currentTarget = null;
    _currentTargetInstanceID = 0; // Reset instance ID to force re-detection on next activation
    _currentBulk = null;
    _currentValidation = null;
    _raycastThrottleFrames = 0; // Reset throttle for next activation

    // Cancel any ongoing exploration
    if (_explorationCoroutine != null)
    {
      StopCoroutine(_explorationCoroutine);
      _explorationCoroutine = null;
    }
    _isExploring = false;
    _explorationTarget = null;

    // Hide status indicator
    _statusIndicator.SetVisible(false);

    // Restore original tooltip
    _tooltip.RestoreOriginalTooltip();

    // Reset patch state
    Patches.BulkDeconstructionRuntime.Reset();

    // Clear deconstructing flag
    _isDeconstructing = false;

    ZoopLog.Info($"[BulkDeconstruction] Mode deactivated: {reason}");
  }

  /// <summary>
  /// Called by patch AFTER the first structure was deconstructed normally (items spawned by game).
  /// Starts progressive deconstruction of remaining structures.
  /// TODO: Find a way to spawn items properly for each structure.
  /// </summary>
  public void StartProgressiveDeconstruction()
  {
    if (_currentBulk == null || _currentBulk.Count == 0)
    {
      ZoopLog.Info("[BulkDeconstruction] Cannot start: No bulk");
      return;
    }

    if (_isDeconstructing)
    {
      ZoopLog.Info("[BulkDeconstruction] Already deconstructing");
      return;
    }

    ZoopLog.Info($"[BulkDeconstruction] Starting progressive deconstruction of {_currentBulk.Count} structures");
    _deconstructionCoroutine = StartCoroutine(DeconstructProgressively());
  }

  /// <summary>
  /// Progressively destroys structures and spawns items.
  /// </summary>
  private IEnumerator DeconstructProgressively()
  {
    _isDeconstructing = true;

    // Count total structures for logging
    int totalStructures = _currentBulk?.Count ?? 0;

    if (_currentBulk == null || _currentBulk.Count == 0)
    {
      ZoopLog.Error("[BulkDeconstruction] Bulk is NULL or empty - cannot deconstruct");
      _isDeconstructing = false;
      _deconstructionCoroutine = null;
      yield break;
    }

    ZoopLog.Info($"[BulkDeconstruction] Starting deconstruction of {totalStructures} structures");

    // Step 1: Collect all items from the bulk BEFORE destroying anything
    ZoopLog.Info($"[BulkDeconstruction] Collecting items from {totalStructures} structures...");

    Dictionary<Thing, int> collectedItems = null;
    Vector3 spawnPosition = _currentBulk[0]?.transform.position ?? Vector3.zero;

    try
    {
      collectedItems = _itemRecovery.CollectItemsFromBulk(_currentBulk);
      ZoopLog.Info("[BulkDeconstruction] Item collection complete");
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error($"[BulkDeconstruction] Item collection failed: {ex.Message}");
      ZoopLog.Error($"[BulkDeconstruction] Stack trace: {ex.StackTrace}");
    }

    // Step 2: Remove all structures from the bulk
    ZoopLog.Info($"[BulkDeconstruction] Removing {totalStructures} structures...");

    int removed = 0;
    foreach (Structure structure in _currentBulk)
    {
      if (structure == null || structure.gameObject == null)
        continue;

      try
      {
        OnServer.Destroy(structure);
        removed++;
      }
      catch (System.Exception ex)
      {
        ZoopLog.Error($"[BulkDeconstruction] Error removing {structure.PrefabName}: {ex.Message}");
      }

      // Small delay every few structures
      if (removed % 10 == 0)
      {
        yield return null;
      }
    }

    ZoopLog.Info($"[BulkDeconstruction] Removed {removed}/{totalStructures} structures");

    // Step 3: Spawn all collected items as stacks
    if (collectedItems != null && collectedItems.Count > 0)
    {
      ZoopLog.Info($"[BulkDeconstruction] Spawning collected items...");

      try
      {
        _itemRecovery.SpawnCollectedItems(collectedItems, spawnPosition);
        ZoopLog.Info("[BulkDeconstruction] Item spawning complete");
      }
      catch (System.Exception ex)
      {
        ZoopLog.Error($"[BulkDeconstruction] Item spawning failed: {ex.Message}");
      }
    }

    ZoopLog.Info($"[BulkDeconstruction] Complete: {removed}/{totalStructures} structures removed with item recovery");

    _isDeconstructing = false;
    _deconstructionCoroutine = null;

    // Deactivate mode after successful deconstruction
    DeactivateMode("Deconstruction complete");
  }

  private void UpdateDetection()
  {
    // Optimization: throttle detection to every N frames
    _raycastThrottleFrames++;
    if (_raycastThrottleFrames < RaycastThrottleInterval)
      return;
    _raycastThrottleFrames = 0;

    // Use the game's cursor system - CursorManager.CursorThing is what the player is currently targeting
    if (CursorManager.CursorThing == null)
    {
      ClearCurrentTarget();
      return;
    }

    // Get the Structure from the cursor thing
    Structure structure = CursorManager.CursorThing as Structure;

    if (structure != null && structure.gameObject != null && structure.gameObject.activeInHierarchy && IsValidBulkElement(structure))
    {
      int structureID = structure.GetInstanceID();

      if (_currentTargetInstanceID != structureID)
      {
        if (_explorationCoroutine != null)
        {
          StopCoroutine(_explorationCoroutine);
          _explorationCoroutine = null;
          _isExploring = false;
        }

        _currentTarget = structure;
        _currentTargetInstanceID = structureID;
        _explorationTarget = structure;

        _explorationCoroutine = StartCoroutine(ExploreAsync(structure));
      }

      if (_currentBulk != null && _currentBulk.Count > 0 && !_isExploring)
      {
        _tooltip.UpdateTooltip(
          GetBulkTypeName(structure),
          _currentBulk.Count,
          _currentValidation?.CanDeconstruct ?? false,
          _currentValidation?.Reason
        );
      }
      else if (_isExploring)
      {
        _tooltip.UpdateTooltip(
          GetBulkTypeName(structure),
          0,
          false,
          "Scanning network..."
        );
      }
    }
    else
    {
      ClearCurrentTarget();
    }
  }

  /// <summary>
  /// Explores bulk asynchronously to avoid blocking the main thread.
  /// </summary>
  private IEnumerator ExploreAsync(Structure structure)
  {
    _isExploring = true;
    _currentBulk = null;
    _currentValidation = null;

    // Verify structure is still the target before starting
    if (_explorationTarget != structure || _currentTarget != structure)
    {
      _isExploring = false;
      yield break;
    }

    // Perform exploration using game's Network system (instantaneous)
    List<Structure> bulk = _detector.ExploreBulk(structure);

    // Verify IMMEDIATELY after exploration
    if (_explorationTarget != structure || _currentTarget != structure)
    {
      _isExploring = false;
      yield break;
    }

    // Check if exploration succeeded
    if (bulk == null || bulk.Count == 0)
    {
      ZoopLog.Warn($"[BulkDeconstruction] Network exploration returned empty for {structure.PrefabName}");
      _isExploring = false;
      ClearCurrentTarget();
      yield break;
    }

    // Assign results
    _currentBulk = bulk;
    _currentValidation = _validator.Validate(structure);
    _isExploring = false;
    _explorationCoroutine = null;

    // Force immediate detection on next frame
    _raycastThrottleFrames = RaycastThrottleInterval;
  }

  private void ClearCurrentTarget()
  {
    // Cancel ongoing exploration
    if (_explorationCoroutine != null)
    {
      StopCoroutine(_explorationCoroutine);
      _explorationCoroutine = null;
    }

    _isExploring = false;
    _explorationTarget = null;
    _currentTarget = null;
    _currentTargetInstanceID = 0; // Reset instance ID
    _currentBulk = null;
    _currentValidation = null;

    // Restore original tooltip
    _tooltip.RestoreOriginalTooltip();
  }

  private bool IsValidBulkElement(Structure structure)
  {
    if (structure is Cable && IsHoldingWireCutters())
      return true;

    if ((structure is Pipe || structure is Chute) && IsHoldingWrench())
      return true;

    return false;
  }

  private Thing GetCurrentTool()
  {
    if (InventoryManager.Instance == null)
      return null;

    if (InventoryManager.Instance.ActiveHand == null)
      return null;

    return InventoryManager.Instance.ActiveHand.Slot?.Get();
  }

  private bool HasValidTool()
  {
    return IsHoldingWireCutters() || IsHoldingWrench();
  }

  private bool IsHoldingWireCutters()
  {
    var heldItem = GetCurrentTool();
    return heldItem is WireCutter;
  }

  private bool IsHoldingWrench()
  {
    var heldItem = GetCurrentTool();
    return heldItem is Wrench;
  }

  private string GetBulkTypeName(Structure structure)
  {
    if (structure is Cable)
      return "Cable";
    if (structure is Pipe)
      return "Pipe";
    if (structure is Chute)
      return "Chute";

    return "Unknown";
  }
}
