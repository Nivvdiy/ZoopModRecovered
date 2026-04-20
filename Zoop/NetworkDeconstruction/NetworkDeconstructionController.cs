using System.Collections;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using UnityEngine;
using ZoopMod.Zoop.EntryPoints.Input;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.NetworkDeconstruction;

/// <summary>
/// Controller for network deconstruction system.
/// Handles detection, validation, and progressive deconstruction of network elements.
/// Similar to ZoopController but for deconstruction instead of construction.
/// </summary>
public class NetworkDeconstructionController : MonoBehaviour
{
  private static NetworkDeconstructionController _instance;
  public static NetworkDeconstructionController Instance => _instance;

  private readonly NetworkDetector _detector = new NetworkDetector();
  private readonly NetworkValidator _validator = new NetworkValidator();
  private readonly NetworkDeconstructionUI _ui = new NetworkDeconstructionUI();
  private readonly NetworkItemRecovery _itemRecovery = new NetworkItemRecovery();

  private bool _isActive;
  private Thing _lastHeldTool;
  private Structure _currentTarget;
  private List<Structure> _currentNetwork;
  private NetworkValidator.ValidationResult _currentValidation;
  private bool _isDeconstructing; // Currently executing deconstruction
  private Coroutine _deconstructionCoroutine;

  // Public properties for patches
  public bool IsActive => _isActive;
  public Structure CurrentTarget => _currentTarget;
  public NetworkValidator.ValidationResult CurrentValidation => _currentValidation;
  public bool IsDeconstructing => _isDeconstructing;
  public int CurrentNetworkSize => _currentNetwork?.Count ?? 0;

  public static void Initialize()
  {
    if (_instance != null)
    {
      ZoopLog.Info("[NetworkDeconstruction] Already initialized");
      return;
    }

    var gameObject = new GameObject("NetworkDeconstructionController");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<NetworkDeconstructionController>();

    // Register with runtime
    Patches.NetworkDeconstructionRuntime.Initialize(_instance);

    ZoopLog.Info("[NetworkDeconstruction] Initialized successfully");
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

    // Handle DIAGNOSTIC key press - shows diagnostic window without deconstructing
    if (UnityEngine.Input.GetKeyDown(ZoopKeyBindings.Diagnostic))
    {
      ShowDiagnosticForTargetedElement();
    }

    // Handle key press to toggle mode
    if (UnityEngine.Input.GetKeyDown(ZoopKeyBindings.NetworkDeconstruct))
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

  private void OnGUI()
  {
    // Always render diagnostic window if it's visible (independent of mode)
    DiagnosticWindow.Render();

    // Only render tooltip if mode is active
    if (!_isActive || _currentTarget == null || _currentNetwork == null)
      return;

    _ui.Render(_currentTarget, _currentNetwork, _currentValidation, _isDeconstructing);
  }

  private void TryActivateMode()
  {
    if (!HasValidTool())
    {
      ZoopLog.Info("[NetworkDeconstruction] Cannot activate: No valid tool equipped");
      return;
    }

    _isActive = true;
    ZoopLog.Info("[NetworkDeconstruction] Mode activated");
  }

  private void DeactivateMode(string reason)
  {
    _isActive = false;
    _currentTarget = null;
    _currentNetwork = null;
    _currentValidation = null;

    // Reset patch state
    Patches.NetworkDeconstructionRuntime.Reset();

    // Clear deconstructing flag
    _isDeconstructing = false;

    ZoopLog.Info($"[NetworkDeconstruction] Mode deactivated: {reason}");
  }

  /// <summary>
  /// Called by patch AFTER the first structure was deconstructed normally (items spawned by game).
  /// Starts progressive deconstruction of remaining structures.
  /// TODO: Find a way to spawn items properly for each structure.
  /// </summary>
  public void StartProgressiveDeconstruction()
  {
    if (_currentNetwork == null || _currentNetwork.Count == 0)
    {
      ZoopLog.Info("[NetworkDeconstruction] Cannot start: No network");
      return;
    }

    if (_isDeconstructing)
    {
      ZoopLog.Info("[NetworkDeconstruction] Already deconstructing");
      return;
    }

    ZoopLog.Info($"[NetworkDeconstruction] Starting progressive deconstruction of {_currentNetwork.Count} structures");
    _deconstructionCoroutine = StartCoroutine(DeconstructProgressively());
  }

  /// <summary>
  /// Progressively destroys structures and spawns items.
  /// </summary>
  private IEnumerator DeconstructProgressively()
  {
    _isDeconstructing = true;

    // Count total structures for logging
    int totalStructures = _currentNetwork?.Count ?? 0;

    if (_currentNetwork == null || _currentNetwork.Count == 0)
    {
      ZoopLog.Error("[NetworkDeconstruction] Network is NULL or empty - cannot deconstruct");
      _isDeconstructing = false;
      _deconstructionCoroutine = null;
      yield break;
    }

    ZoopLog.Info($"[NetworkDeconstruction] Starting deconstruction of {totalStructures} structures");

    // Step 1: Collect all items from the network BEFORE destroying anything
    ZoopLog.Info($"[NetworkDeconstruction] Collecting items from {totalStructures} structures...");

    Dictionary<Thing, int> collectedItems = null;
    Vector3 spawnPosition = _currentNetwork[0]?.transform.position ?? Vector3.zero;

    try
    {
      collectedItems = _itemRecovery.CollectItemsFromNetwork(_currentNetwork);
      ZoopLog.Info("[NetworkDeconstruction] Item collection complete");
    }
    catch (System.Exception ex)
    {
      ZoopLog.Error($"[NetworkDeconstruction] Item collection failed: {ex.Message}");
      ZoopLog.Error($"[NetworkDeconstruction] Stack trace: {ex.StackTrace}");
    }

    // Step 2: Remove all structures from the network
    ZoopLog.Info($"[NetworkDeconstruction] Removing {totalStructures} structures...");

    int removed = 0;
    foreach (Structure structure in _currentNetwork)
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
        ZoopLog.Error($"[NetworkDeconstruction] Error removing {structure.PrefabName}: {ex.Message}");
      }

      // Small delay every few structures
      if (removed % 10 == 0)
      {
        yield return null;
      }
    }

    ZoopLog.Info($"[NetworkDeconstruction] Removed {removed}/{totalStructures} structures");

    // Step 3: Spawn all collected items as stacks
    if (collectedItems != null && collectedItems.Count > 0)
    {
      ZoopLog.Info($"[NetworkDeconstruction] Spawning collected items...");

      try
      {
        _itemRecovery.SpawnCollectedItems(collectedItems, spawnPosition);
        ZoopLog.Info("[NetworkDeconstruction] Item spawning complete");
      }
      catch (System.Exception ex)
      {
        ZoopLog.Error($"[NetworkDeconstruction] Item spawning failed: {ex.Message}");
      }
    }

    ZoopLog.Info($"[NetworkDeconstruction] Complete: {removed}/{totalStructures} structures removed with item recovery");

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

    if (Physics.Raycast(ray, out RaycastHit hit, NetworkDeconstructionConfig.RaycastDistance))
    {
      var structure = hit.collider.GetComponentInParent<Structure>();

      if (structure != null && IsValidNetworkElement(structure))
      {
        if (_currentTarget != structure)
        {
          _currentTarget = structure;
          _currentNetwork = _detector.ExploreNetwork(structure);
          _currentValidation = _validator.Validate(structure);

          ZoopLog.Debug($"[NetworkDeconstruction] Detected {GetNetworkTypeName(structure)} network: {_currentNetwork.Count} structures");
        }
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
    _currentNetwork = null;
    _currentValidation = null;
  }

  private bool IsValidNetworkElement(Structure structure)
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

  private string GetNetworkTypeName(Structure structure)
  {
    if (structure is Cable)
      return "Cable";
    if (structure is Pipe)
      return "Pipe";
    if (structure is Chute)
      return "Chute";

    return "Unknown";
  }

  /// <summary>
  /// Shows diagnostic window for the currently targeted element (triggered by diagnostic key).
  /// Works independently from the deconstruction mode - just aims at an element and press the key.
  /// </summary>
  private void ShowDiagnosticForTargetedElement()
  {
    // Raycast from camera center to find what we're looking at
    var camera = Camera.main;
    if (camera == null)
    {
      ZoopLog.Info("[Diagnostic] No camera found");
      return;
    }

    var ray = camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));

    if (!Physics.Raycast(ray, out RaycastHit hit, NetworkDeconstructionConfig.RaycastDistance))
    {
      ZoopLog.Info("[Diagnostic] No object in sight");
      return;
    }

    var structure = hit.collider.GetComponentInParent<Structure>();

    if (structure == null)
    {
      ZoopLog.Info("[Diagnostic] Targeted object is not a Structure");
      return;
    }

    // Check if we have the right tool for this element
    bool hasCorrectTool = false;
    string requiredTool = "";

    if (structure is Cable)
    {
      hasCorrectTool = IsHoldingWireCutters();
      requiredTool = "Wire Cutter";
    }
    else if (structure is Pipe || structure is Chute)
    {
      hasCorrectTool = IsHoldingWrench();
      requiredTool = "Wrench";
    }
    else
    {
      ZoopLog.Info($"[Diagnostic] Targeted structure is not a Cable/Pipe/Chute: {structure.GetType().Name}");
      return;
    }

    if (!hasCorrectTool)
    {
      ZoopLog.Info($"[Diagnostic] Wrong tool! You need a {requiredTool} to inspect this {GetNetworkTypeName(structure)}");

      // Show a message in the diagnostic window
      DiagnosticWindow.Show(null, $"⚠️ WRONG TOOL ⚠️\n\nYou need a {requiredTool} to inspect this {GetNetworkTypeName(structure)}");
      return;
    }

    // We have the right tool and a valid target - show diagnostic!
    ZoopLog.Info($"[Diagnostic] Showing diagnostic for {structure.PrefabName} ({GetNetworkTypeName(structure)})");

    // Show detailed diagnostic of the structure
    DiagnosticWindow.ShowStructureDiagnostic(structure);

    // Also explore and show the network if you want
    var network = _detector.ExploreNetwork(structure);
    if (network != null && network.Count > 0)
    {
      ZoopLog.Info($"[Diagnostic] Network detected: {network.Count} elements");
      // You can optionally show network diagnostic too after a delay, but for now we just log it
    }
  }
}
