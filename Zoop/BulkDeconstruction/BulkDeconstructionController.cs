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
using ZoopMod.Zoop.UI;

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
  private readonly BulkDeconstructionStatusIndicator _statusIndicator = new BulkDeconstructionStatusIndicator();
  private readonly BulkDeconstructionTooltip _tooltip = new BulkDeconstructionTooltip();

  private bool _isActive;
  private Thing _lastHeldTool;
  private Structure _currentTarget;
  private int _currentTargetInstanceID; // Track by InstanceID instead of reference
  private List<Structure> _currentBulk;
  private BulkValidator.ValidationResult _currentValidation;
  private bool _isDeconstructing; // Currently executing deconstruction

  // Async exploration tracking
  private Coroutine _explorationCoroutine;
  private bool _isExploring;
  private Structure _explorationTarget; // Track what we're currently exploring

  // Optimization: throttle raycast to reduce per-frame cost
  private int _raycastThrottleFrames = 0;
  private const int RaycastThrottleInterval = 2; // Only raycast every N frames (lowered from 3 to reduce detection lag)
  private const float BulkDropSpacing = 0.65f;
  private const int BulkDropTowerHeight = 5;
  private const float BulkDropVerticalSpacing = 0.35f;
  private const float OffhandDropWaitTimeoutSeconds = 1.5f;

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
  /// Called by patch when a valid bulk deconstruction action is requested.
  /// Starts progressive deconstruction through the game's normal networked attack path.
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
    StartCoroutine(DeconstructProgressively());
  }

  /// <summary>
  /// Progressively deconstructs structures through the game's normal multiplayer-aware action path.
  /// </summary>
  private IEnumerator DeconstructProgressively()
  {
    _isDeconstructing = true;

    if (_currentBulk == null || _currentBulk.Count == 0)
    {
      ZoopLog.Error("[BulkDeconstruction] Bulk is NULL or empty - cannot deconstruct");
      _isDeconstructing = false;
      yield break;
    }

    var bulkSnapshot = new List<Structure>(_currentBulk.Count);
    foreach (var structure in _currentBulk)
    {
      if (structure != null && structure.gameObject != null)
      {
        bulkSnapshot.Add(structure);
      }
    }

    if (bulkSnapshot.Count == 0)
    {
      ZoopLog.Error("[BulkDeconstruction] Bulk contains no valid structures - cannot deconstruct");
      _isDeconstructing = false;
      yield break;
    }

    int totalStructures = bulkSnapshot.Count;
    // Vanilla ToolUse.SpawnItem drops leftovers at the ConstructionEventInstance position.
    // We keep drops centered around the clicked structure for multiplayer-safe vanilla item
    // recovery, but spread individual deconstructs into short towers so physics objects do
    // not all spawn at the exact same coordinate or carpet the whole network path.
    var dropOrigin = ResolveBulkDropOrigin(bulkSnapshot);
    var expectedReturn = ResolvePrimaryReturnedStack(_currentTarget ?? bulkSnapshot[0]);
    var offhandValidation = ValidateOffhandForBulk(_currentTarget ?? bulkSnapshot[0]);
    if (!offhandValidation.CanDeconstruct)
    {
      ZoopLog.Info($"[BulkDeconstruction] Blocked before execution: {offhandValidation.Reason}");
      _isDeconstructing = false;
      DeactivateMode("Offhand no longer valid");
      yield break;
    }

    ZoopLog.Info($"[BulkDeconstruction] Starting deconstruction of {totalStructures} structures; floor drops will be centered at {dropOrigin}");

    int removed = 0;
    var dropStackIndex = 0;
    var structureIndex = 0;
    while (structureIndex < bulkSnapshot.Count)
    {
      var batchSize = ResolveNextBatchSize(expectedReturn, bulkSnapshot.Count - structureIndex);
      if (batchSize <= 0)
      {
        var offhandDrop = new OffhandDropState();
        yield return DropOffhandStackIfNeeded(expectedReturn, dropOrigin, totalStructures, dropStackIndex, offhandDrop);
        if (offhandDrop.DroppedStack)
        {
          dropStackIndex++;
          continue;
        }

        batchSize = 1;
      }

      var expectedOffhandQuantityAfterBatch = ResolveExpectedOffhandQuantityAfterBatch(expectedReturn, batchSize);
      var requestedInBatch = 0;
      while (structureIndex < bulkSnapshot.Count && requestedInBatch < batchSize)
      {
        var structure = bulkSnapshot[structureIndex++];

        var dropPosition = ResolveBulkDropPosition(dropOrigin, removed, totalStructures);
        try
        {
          if (TryRequestVanillaDeconstruct(structure, dropPosition))
          {
            removed++;
            requestedInBatch++;
          }
        }
        catch (System.Exception ex)
        {
          ZoopLog.Error($"[BulkDeconstruction] Error deconstructing {structure.PrefabName}: {ex.Message}");
        }
      }

      if (requestedInBatch <= 0)
      {
        continue;
      }

      var hasMoreStructures = structureIndex < bulkSnapshot.Count;
      if (hasMoreStructures && expectedReturn != null)
      {
        var offhandDrop = new OffhandDropState();
        yield return DropOffhandStackAfterBatch(expectedReturn, expectedOffhandQuantityAfterBatch, dropOrigin,
          totalStructures, dropStackIndex, offhandDrop);
        if (offhandDrop.DroppedStack)
        {
          dropStackIndex++;
        }
      }

      yield return null;
    }

    ZoopLog.Info($"[BulkDeconstruction] Complete: requested vanilla deconstruction for {removed}/{totalStructures} structures");

    _isDeconstructing = false;

    // Deactivate mode after successful deconstruction
    DeactivateMode("Deconstruction complete");
  }

  private Vector3 ResolveBulkDropOrigin(List<Structure> bulkSnapshot)
  {
    // Prefer the live target because it is the structure the player intentionally clicked.
    // That makes the drop pile appear where the action started, even for large networks.
    if (_currentTarget?.transform != null)
      return _currentTarget.transform.position;

    // Position is a safe fallback if Unity has detached the transform while the Thing is still valid.
    if (_currentTarget != null)
      return _currentTarget.Position;

    // If the target was cleared during execution, use the first still-valid structure from the snapshot.
    foreach (var structure in bulkSnapshot)
    {
      if (structure?.transform != null)
        return structure.transform.position;

      if (structure != null)
        return structure.Position;
    }

    // Last resort: keep drops near the player instead of sending them to an arbitrary world origin.
    return InventoryManager.Parent?.Position ?? Vector3.zero;
  }

  private static Vector3 ResolveBulkDropPosition(Vector3 origin, int dropIndex, int totalDrops)
  {
    if (totalDrops <= 1)
      return origin;

    var towerCount = Mathf.CeilToInt(totalDrops / (float)BulkDropTowerHeight);
    var columns = Mathf.CeilToInt(Mathf.Sqrt(towerCount));
    var rows = Mathf.CeilToInt(towerCount / (float)columns);
    var towerIndex = dropIndex / BulkDropTowerHeight;
    var layer = dropIndex % BulkDropTowerHeight;
    var row = towerIndex / columns;
    var column = towerIndex % columns;

    // Center the tower grid on the clicked structure. Each tower receives a few vertical
    // layers before the layout moves sideways to the next tower.
    var xOffset = (column - ((columns - 1) * 0.5f)) * BulkDropSpacing;
    var zOffset = (row - ((rows - 1) * 0.5f)) * BulkDropSpacing;
    var yOffset = layer * BulkDropVerticalSpacing;

    return origin + new Vector3(xOffset, yOffset, zOffset);
  }

  private static Vector3 ResolveBulkStackDropPosition(Vector3 origin, int stackIndex, int totalStacks)
  {
    if (totalStacks <= 1)
      return origin;

    var columns = Mathf.CeilToInt(Mathf.Sqrt(totalStacks));
    var rows = Mathf.CeilToInt(totalStacks / (float)columns);
    var row = stackIndex / columns;
    var column = stackIndex % columns;
    var xOffset = (column - ((columns - 1) * 0.5f)) * BulkDropSpacing;
    var zOffset = (row - ((rows - 1) * 0.5f)) * BulkDropSpacing;

    return origin + new Vector3(xOffset, 0f, zOffset);
  }

  private static int ResolveNextBatchSize(ReturnedStack expectedReturn, int remainingStructures)
  {
    if (remainingStructures <= 0)
      return 0;

    if (expectedReturn == null)
      return remainingStructures;

    var inactiveSlot = InventoryManager.Instance?.InactiveHand?.Slot;
    var heldStack = inactiveSlot?.Get() as Stackable;
    if (heldStack == null)
    {
      var emptySlotCapacity = expectedReturn.MaxQuantity / expectedReturn.Quantity;
      return emptySlotCapacity <= 0 ? 1 : Mathf.Min(remainingStructures, emptySlotCapacity);
    }

    if (!IsMatchingStack(heldStack, expectedReturn))
      return 0;

    var availableQuantity = heldStack.MaxQuantity - heldStack.Quantity;
    if (availableQuantity < expectedReturn.Quantity)
      return 0;

    return Mathf.Min(remainingStructures, availableQuantity / expectedReturn.Quantity);
  }

  private static int ResolveExpectedOffhandQuantityAfterBatch(ReturnedStack expectedReturn, int batchSize)
  {
    if (expectedReturn == null || batchSize <= 0)
      return 0;

    var inactiveSlot = InventoryManager.Instance?.InactiveHand?.Slot;
    var heldStack = inactiveSlot?.Get() as Stackable;
    var currentQuantity = heldStack != null && IsMatchingStack(heldStack, expectedReturn)
      ? heldStack.Quantity
      : 0;

    return Mathf.Min(expectedReturn.MaxQuantity, currentQuantity + (batchSize * expectedReturn.Quantity));
  }

  private IEnumerator DropOffhandStackAfterBatch(ReturnedStack expectedReturn, int expectedQuantity, Vector3 dropOrigin,
    int totalStructures, int dropStackIndex, OffhandDropState dropState)
  {
    var timeoutAt = Time.time + OffhandDropWaitTimeoutSeconds;
    while (Time.time < timeoutAt)
    {
      var heldStack = InventoryManager.Instance?.InactiveHand?.Slot?.Get() as Stackable;
      if (heldStack != null && IsMatchingStack(heldStack, expectedReturn))
      {
        if (heldStack.Quantity >= expectedQuantity || heldStack.Quantity >= heldStack.MaxQuantity)
        {
          yield return DropOffhandStack(expectedReturn, dropOrigin, totalStructures, dropStackIndex, dropState, heldStack);
          yield break;
        }

        // If the stack exists but is not full yet, continue the main loop immediately.
        // The next batch will send only the amount that still fits, avoiding a long wait at 99/100.
        yield break;
      }

      yield return null;
    }

    ZoopLog.Warn("[BulkDeconstruction] Timed out waiting for offhand stack to finish filling after batch; continuing without dropping it");
  }

  private IEnumerator DropOffhandStackIfNeeded(ReturnedStack expectedReturn, Vector3 dropOrigin, int totalStructures,
    int dropStackIndex, OffhandDropState dropState)
  {
    if (expectedReturn == null)
      yield break;

    var inactiveSlot = InventoryManager.Instance?.InactiveHand?.Slot;
    var heldStack = inactiveSlot?.Get() as Stackable;
    if (heldStack == null || !IsMatchingStack(heldStack, expectedReturn))
      yield break;

    if (heldStack.Quantity + expectedReturn.Quantity <= heldStack.MaxQuantity)
      yield break;

    yield return DropOffhandStack(expectedReturn, dropOrigin, totalStructures, dropStackIndex, dropState, heldStack);
  }

  private IEnumerator DropOffhandStack(ReturnedStack expectedReturn, Vector3 dropOrigin, int totalStructures,
    int dropStackIndex, OffhandDropState dropState, Stackable heldStack)
  {
    var inactiveSlot = InventoryManager.Instance?.InactiveHand?.Slot;
    if (inactiveSlot == null || heldStack == null)
      yield break;

    var estimatedStackDrops = EstimateOffhandStackDropCount(totalStructures, heldStack.MaxQuantity, expectedReturn.Quantity);
    var towerDropPosition = ResolveBulkStackDropPosition(dropOrigin, dropStackIndex, estimatedStackDrops);
    dropState.DroppedStack = true;
    OnServer.MoveToWorld(heldStack, towerDropPosition, heldStack.transform.rotation, Vector3.zero, Vector3.zero);

    var timeoutAt = Time.time + OffhandDropWaitTimeoutSeconds;
    while (inactiveSlot.Get() == heldStack && Time.time < timeoutAt)
    {
      yield return null;
    }

    if (inactiveSlot.Get() == heldStack)
    {
      ZoopLog.Warn("[BulkDeconstruction] Timed out waiting for offhand stack to drop; continuing with vanilla overflow behavior");
    }
  }

  private static int EstimateOffhandStackDropCount(int structureCount, int maxQuantity, int returnedQuantity)
  {
    var quantityPerStack = Mathf.Max(1, maxQuantity);
    var totalReturnedQuantity = Mathf.Max(1, structureCount * Mathf.Max(1, returnedQuantity));
    return Mathf.Max(1, Mathf.CeilToInt(totalReturnedQuantity / (float)quantityPerStack));
  }

  private BulkValidator.ValidationResult ValidateOffhandForBulk(Structure structure)
  {
    var inactiveSlot = InventoryManager.Instance?.InactiveHand?.Slot;
    var heldThing = inactiveSlot?.Get();
    if (heldThing == null)
      return BulkValidator.ValidationResult.Success();

    var expectedReturn = ResolvePrimaryReturnedStack(structure);
    if (expectedReturn != null && heldThing is Stackable heldStack && IsMatchingStack(heldStack, expectedReturn))
      return BulkValidator.ValidationResult.Success();

    return BulkValidator.ValidationResult.Failure(ZoopText.BulkReasonOffhandMismatch);
  }

  private static bool IsMatchingStack(Stackable stack, ReturnedStack expectedReturn)
  {
    return stack != null
           && expectedReturn != null
           && stack.GetPrefabHash() == expectedReturn.PrefabHash;
  }

  private static ReturnedStack ResolvePrimaryReturnedStack(Structure structure)
  {
    if (structure?.BuildStates == null || structure.BuildStates.Count == 0)
      return null;

    var tool = structure.BuildStates[0]?.Tool;
    if (tool == null)
      return null;

    if (tool.ToolEntry is Stackable primary && tool.EntryQuantity > 0)
    {
      return new ReturnedStack(primary.GetPrefabHash(), tool.EntryQuantity, primary.MaxQuantity);
    }

    if (tool.ToolEntry2 is Stackable secondary && tool.EntryQuantity2 > 0)
    {
      return new ReturnedStack(secondary.GetPrefabHash(), tool.EntryQuantity2, secondary.MaxQuantity);
    }

    return null;
  }

  private static bool TryRequestVanillaDeconstruct(Structure structure, Vector3 dropPosition)
  {
    var inventoryManager = InventoryManager.Instance;
    if (inventoryManager?.ActiveHand == null || inventoryManager.InactiveHand == null || InventoryManager.Parent == null)
    {
      ZoopLog.Error("[BulkDeconstruction] Unable to deconstruct: inventory state is not ready");
      return false;
    }

    var activeSlotId = (byte)inventoryManager.ActiveHand.SlotId;
    var inactiveSlotId = (byte)inventoryManager.InactiveHand.SlotId;

    // AttackWith is Stationeers' normal networked tool-on-thing path. On clients it sends
    // AttackWithMessage to the server, unlike direct OnServer.Destroy/Create calls.
    Patches.BulkDeconstructionRuntime.RunVanillaDeconstruct(() =>
      OnServer.AttackWith(
        InventoryManager.Parent,
        activeSlotId,
        inactiveSlotId,
        structure.ReferenceId,
        dropPosition,
        1f,
        isDestroy: false,
        isCopy: false));

    return true;
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
    if (_currentValidation?.CanDeconstruct == true)
    {
      _currentValidation = ValidateOffhandForBulk(structure);
    }

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

  private sealed class ReturnedStack
  {
    public ReturnedStack(int prefabHash, int quantity, int maxQuantity)
    {
      PrefabHash = prefabHash;
      Quantity = quantity;
      MaxQuantity = maxQuantity;
    }

    public int PrefabHash { get; }
    public int Quantity { get; }
    public int MaxQuantity { get; }
  }

  private sealed class OffhandDropState
  {
    public bool DroppedStack { get; set; }
  }
}
