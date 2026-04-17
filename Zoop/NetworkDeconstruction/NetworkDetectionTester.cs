using System.Collections.Generic;
using Assets.Scripts.Inventory;
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
/// MonoBehaviour for testing network detection with visual feedback.
/// Shows detected neighbors when aiming at a cable with cutters.
/// </summary>
public class NetworkDetectionTester : MonoBehaviour
{
  private static NetworkDetectionTester _instance;
  public static NetworkDetectionTester Instance => _instance;

  private Structure _currentTarget;
  private List<Structure> _connectedNetwork; // Stores the entire connected network
  private bool _isActive;

  public static void Initialize()
  {
    if (_instance != null)
    {
      ZoopLog.Info("[NetworkDetectionTester] Already initialized");
      return;
    }

    var gameObject = new GameObject("NetworkDetectionTester");
    DontDestroyOnLoad(gameObject);
    _instance = gameObject.AddComponent<NetworkDetectionTester>();
    ZoopLog.Info("[NetworkDetectionTester] Initialized successfully");
  }

  private void Update()
  {
    // Toggle mode: press key to toggle on/off
    bool keyPressed = UnityEngine.Input.GetKeyDown(ZoopKeyBindings.NetworkDeconstruct);

    if (keyPressed)
    {
      _isActive = !_isActive;
      ZoopLog.Info($"[NetworkDetectionTester] Mode {(_isActive ? "ON" : "OFF")}");

      if (!_isActive)
      {
        _currentTarget = null;
        _connectedNetwork = null;
      }
    }

    if (_isActive)
    {
      DetectTargetedNetwork();
    }
  }

  private bool IsHoldingCutters()
  {
    if (InventoryManager.Instance == null || InventoryManager.Instance.ActiveHand == null)
      return false;

    var heldItem = InventoryManager.Instance.ActiveHand.Slot.Get();
    if (heldItem == null)
      return false;

    // Use class type instead of name checking for robustness
    return heldItem is WireCutter;
  }

  private bool IsHoldingWrench()
  {
    if (InventoryManager.Instance == null || InventoryManager.Instance.ActiveHand == null)
      return false;

    var heldItem = InventoryManager.Instance.ActiveHand.Slot.Get();
    if (heldItem == null)
      return false;

    // Use class type instead of name checking for robustness
    return heldItem is Wrench;
  }

  private void DetectTargetedNetwork()
  {
    var camera = Camera.main;
    if (camera == null)
      return;

    var ray = camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
    RaycastHit hit;

    if (Physics.Raycast(ray, out hit, 10f))
    {
      var structure = hit.collider.GetComponentInParent<Structure>();
      if (structure != null && IsNetworkElement(structure))
      {
        if (_currentTarget != structure)
        {
          _currentTarget = structure;
          ExploreConnectedNetwork();
        }
      }
      else
      {
        _currentTarget = null;
        _connectedNetwork = null;
      }
    }
    else
    {
      _currentTarget = null;
      _connectedNetwork = null;
    }
  }

  private bool IsCable(Structure structure)
  {
    if (structure == null)
      return false;

    // Use class type instead of name checking for robustness
    return structure is Cable;
  }

  private bool IsPipe(Structure structure)
  {
    if (structure == null)
      return false;

    // Use class type instead of name checking for robustness
    return structure is Pipe;
  }

  private bool IsChute(Structure structure)
  {
    if (structure == null)
      return false;

    // Use class type instead of name checking for robustness
    return structure is Chute;
  }

  /// <summary>
  /// Check if the network can be safely deconstructed.
  /// </summary>
  private bool CanDeconstructNetwork(Structure target, out string reason)
  {
    reason = "";

    if (target == null)
    {
      reason = "No target";
      return false;
    }

    // Check based on network type
    if (target is Cable cable)
    {
      return CanDeconstructCableNetwork(cable, out reason);
    }
    else if (target is Pipe pipe)
    {
      return CanDeconstructPipeNetwork(pipe, out reason);
    }
    else if (target is Chute chute)
    {
      return CanDeconstructChuteNetwork(chute, out reason);
    }

    reason = "Unknown network type";
    return false;
  }

  private bool CanDeconstructCableNetwork(Cable cable, out string reason)
  {
    reason = "";

    if (cable.CableNetwork != null)
    {
      var potential = cable.CableNetwork.PotentialLoad;
      if(potential > 0.01f)
      {
        reason = $"Network has potential load ({potential:F2} W)";
        return false;
      }
    }

    // All checks passed
    return true;
  }

  private bool CanDeconstructPipeNetwork(Pipe pipe, out string reason)
  {
    reason = "";

    // Check if pipe has significant pressure
    if (pipe.PipeNetwork != null)
    {
      var atmosphere = pipe.PipeNetwork.Atmosphere;
      if (atmosphere != null && atmosphere.PressureGassesAndLiquidsInPa > 10f) // > 10 Pa
      {
        reason = $"Network has pressure ({atmosphere.PressureGassesAndLiquidsInPa:F1} Pa)";
        return false;
      }
    }

    // All checks passed
    return true;
  }

  private bool CanDeconstructChuteNetwork(Chute chute, out string reason)
  {
    reason = "";

    // Check if chute network has items
    // TODO: Find the correct way to check for items in chutes
    // Note: This is a simplified check, actual implementation may need to check all chutes in network

    // For now, allow deconstruction
    return true;
  }

  private string GetNetworkType(Structure structure)
  {
    if (structure == null)
      return "none";

    if (IsCable(structure))
      return "cable";
    if (IsPipe(structure))
      return "pipe";
    if (IsChute(structure))
      return "chute";

    return "none";
  }

  private bool IsSameNetworkType(Structure structure1, Structure structure2)
  {
    if (structure1 == null || structure2 == null)
      return false;

    string type1 = GetNetworkType(structure1);
    string type2 = GetNetworkType(structure2);

    return type1 == type2 && type1 != "none";
  }

  private bool IsNetworkElement(Structure structure)
  {
    if (structure == null)
      return false;

    bool isCable = IsCable(structure);
    bool isPipe = IsPipe(structure);
    bool isChute = IsChute(structure);

    // Cables require cutters
    if (isCable && IsHoldingCutters())
      return true;

    // Pipes and chutes require wrench
    if ((isPipe || isChute) && IsHoldingWrench())
      return true;

    return false;
  }

  private void ExploreConnectedNetwork()
  {
    if (_currentTarget == null)
      return;

    // Explore the entire connected network recursively
    HashSet<Structure> visited = new HashSet<Structure>();
    _connectedNetwork = new List<Structure>();

    ExploreNetworkRecursive(_currentTarget, visited, _connectedNetwork);

    var networkType = GetNetworkType(_currentTarget);
    ZoopLog.Info($"[NetworkDetectionTester] ==========================================");
    ZoopLog.Info($"[NetworkDetectionTester] Analyzing: {_currentTarget.PrefabName} (Type: {networkType.ToUpper()})");
    ZoopLog.Info($"[NetworkDetectionTester] Position: ({_currentTarget.transform.position.x:F2}, {_currentTarget.transform.position.y:F2}, {_currentTarget.transform.position.z:F2})");
    ZoopLog.Info($"[NetworkDetectionTester] Total connected network size: {_connectedNetwork.Count} structures");

    if (_connectedNetwork.Count > 0)
    {
      ZoopLog.Info($"[NetworkDetectionTester] Connected network:");
      for (int i = 0; i < _connectedNetwork.Count; i++)
      {
        var connected = _connectedNetwork[i];
        if (connected != null)
        {
          var pos = connected.transform.position;
          var delta = pos - _currentTarget.transform.position;
          string marker = (connected == _currentTarget) ? " <-- START" : "";
          ZoopLog.Info($"[NetworkDetectionTester]   [{i}] {connected.PrefabName} at ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) - Delta: ({delta.x:F2}, {delta.y:F2}, {delta.z:F2}){marker}");
        }
      }
    }

    ZoopLog.Info($"[NetworkDetectionTester] ==========================================");
  }

  /// <summary>
  /// Recursively explore the network starting from a structure.
  /// Uses HashSet to avoid infinite loops.
  /// </summary>
  private void ExploreNetworkRecursive(Structure current, HashSet<Structure> visited, List<Structure> result)
  {
    if (current == null || visited.Contains(current))
      return;

    // Mark as visited
    visited.Add(current);
    result.Add(current);

    // Get direct neighbors using the game's built-in Connected() method
    // IMPORTANT: Connected() returns a static list that gets reused!
    // We MUST create a copy before iterating to avoid "Collection was modified" errors
    List<SmallGrid> neighbors = null;

    try
    {
      if (current is SmallGrid smallGrid)
      {
        var tempNeighbors = smallGrid.Connected();
        if (tempNeighbors != null && tempNeighbors.Count > 0)
        {
          // Create a copy to avoid collection modification during recursive calls
          neighbors = new List<SmallGrid>(tempNeighbors);
        }
      }
    }
    catch (System.Exception ex)
    {
      ZoopLog.Info($"[NetworkDetectionTester] Error accessing Connected() for {current.PrefabName}: {ex.Message}");
      return;
    }

    if (neighbors != null)
    {
      // Filter to keep only the same network type (Cable/Pipe/Chute)
      string currentNetworkType = GetNetworkType(current);

      // Recursively explore each neighbor of the same type
      foreach (var neighbor in neighbors)
      {
        if (neighbor != null && !visited.Contains(neighbor))
        {
          string neighborType = GetNetworkType(neighbor);

          // Only explore if same network type (cables with cables, pipes with pipes, etc.)
          if (neighborType == currentNetworkType)
          {
            ExploreNetworkRecursive(neighbor, visited, result);
          }
        }
      }
    }
  }

  /// <summary>
  /// Determines valid connection directions based on cable type and orientation.
  /// A straight cable on X axis can only connect in X+ and X- directions.
  /// </summary>
  private void OnGUI()
  {
    // Only show UI when active and targeting something with the right tool
    if (!_isActive || _currentTarget == null || !IsNetworkElement(_currentTarget))
      return;

    // Get network info
    int networkSize = (_connectedNetwork != null) ? _connectedNetwork.Count : 0;
    string networkType = GetNetworkType(_currentTarget).ToUpper();

    // Check if network can be deconstructed
    bool canDeconstruct = CanDeconstructNetwork(_currentTarget, out string reason);

    // Position near cursor (offset to avoid covering the target)
    Vector2 mousePos = Input.mousePosition;
    float offsetX = 20f;
    float offsetY = 20f;
    float posX = mousePos.x + offsetX;
    float posY = Screen.height - mousePos.y + offsetY; // Flip Y because GUI is top-down

    // Create style with rich text support (like the game's tooltips)
    var style = new GUIStyle(GUI.skin.box);
    style.richText = true;
    style.fontSize = 16; // Increased from 13 to 16
    style.alignment = TextAnchor.UpperLeft;
    style.padding = new RectOffset(10, 10, 8, 8);
    style.normal.background = MakeTex(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.9f)); // Dark semi-transparent background

    var labelStyle = new GUIStyle(GUI.skin.label);
    labelStyle.richText = true;
    labelStyle.fontSize = 16; // Increased from 13 to 16
    labelStyle.alignment = TextAnchor.UpperLeft;
    labelStyle.padding = new RectOffset(10, 10, 8, 8);

    // Build the message with rich text formatting
    string message = $"<b><color=#00DDFF>{networkType} Network</color></b>\n";
    message += $"<size=15>Size: {networkSize} structure{(networkSize > 1 ? "s" : "")}</size>\n\n"; // Size 15 (was 12)

    if (canDeconstruct)
    {
      message += $"<color=#00FF00>✓ Ready to deconstruct</color>";
    }
    else
    {
      message += $"<color=#FF4444>✗ Cannot deconstruct</color>\n";
      message += $"<color=#FFCC00><size=14>{reason}</size></color>"; // Size 14 (was 11)
    }

    // Calculate content size
    GUIContent content = new GUIContent(message);
    Vector2 contentSize = labelStyle.CalcSize(content);
    float boxWidth = contentSize.x + 20;
    float boxHeight = contentSize.y + 16;

    // Draw background box
    GUI.Box(new Rect(posX, posY, boxWidth, boxHeight), "", style);

    // Draw text on top
    GUI.Label(new Rect(posX, posY, boxWidth, boxHeight), message, labelStyle);
  }

  // Helper to create a solid color texture for GUI backgrounds
  private Texture2D MakeTex(int width, int height, Color col)
  {
    Color[] pix = new Color[width * height];
    for (int i = 0; i < pix.Length; i++)
      pix[i] = col;

    Texture2D result = new Texture2D(width, height);
    result.SetPixels(pix);
    result.Apply();
    return result;
  }
}
