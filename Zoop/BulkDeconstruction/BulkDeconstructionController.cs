using System.Collections;
using System.Collections.Generic;
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
  private List<Structure> _currentBulk;
  private BulkValidator.ValidationResult _currentValidation;
  private bool _isDeconstructing; // Currently executing deconstruction
  private Coroutine _deconstructionCoroutine;

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
    ZoopLog.Debug("[BulkDeconstruction] Waiting for game to be loaded before initializing status indicator...");

    // Wait until a save is loaded (InventoryManager is available)
    while (InventoryManager.Instance == null)
    {
      yield return new WaitForSeconds(0.5f);
    }

    // Wait an additional second for UI to be fully ready
    yield return new WaitForSeconds(1f);

    ZoopLog.Debug("[BulkDeconstruction] Game loaded, initializing status indicator now...");
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
    _statusIndicator.SetVisible(true);
    ZoopLog.Info("[BulkDeconstruction] Mode activated");
  }

  private void DeactivateMode(string reason)
  {
    _isActive = false;
    _currentTarget = null;
    _currentBulk = null;
    _currentValidation = null;

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
    var camera = Camera.main;
    if (camera == null)
      return;

    var ray = camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));

    if (Physics.Raycast(ray, out RaycastHit hit, BulkDeconstructionConfig.RaycastDistance))
    {
      var structure = hit.collider.GetComponentInParent<Structure>();

      if (structure != null && IsValidBulkElement(structure))
      {
        if (_currentTarget != structure)
        {
          _currentTarget = structure;
          _currentBulk = _detector.ExploreBulk(structure);
          _currentValidation = _validator.Validate(structure);

          ZoopLog.Debug($"[BulkDeconstruction] Detected {GetBulkTypeName(structure)} bulk: {_currentBulk.Count} structures");
        }

        // Update tooltip with current bulk info
        _tooltip.UpdateTooltip(
          GetBulkTypeName(structure),
          _currentBulk?.Count ?? 0,
          _currentValidation?.CanDeconstruct ?? false
        );
      }
      else
      {
        ClearCurrentTarget();
      }
    }
    else
    {
      ClearCurrentTarget();
    }
  }

  private void ClearCurrentTarget()
  {
    _currentTarget = null;
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
