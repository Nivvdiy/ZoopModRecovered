using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Objects.Items;
using ZoopMod.Zoop.Logging;

namespace ZoopMod.Zoop.NetworkDeconstruction
{
  /// <summary>
  /// Fenêtre de diagnostic affichant toutes les propriétés d'un objet au centre de l'écran
  /// </summary>
  public class DiagnosticWindow
  {
    private static bool _isVisible = false;
    private static string _diagnosticText = "";
    private static Vector2 _scrollPosition = Vector2.zero;
    private static Rect _windowRect = new Rect(Screen.width / 2 - 400, Screen.height / 2 - 300, 800, 600);
    private static GUIStyle _windowStyle;
    private static GUIStyle _textAreaStyle;
    private static GUIStyle _buttonStyle;
    private static GUIStyle _labelStyle;
    private static bool _stylesInitialized = false;
    private static NetworkDetector _networkDetector = new NetworkDetector();

    public static void Show(object target, string title = "Diagnostic")
    {
      if(target == null)
      {
        _diagnosticText = $"========== {title} ==========\n\n⚠️ NULL OBJECT ⚠️\n\nL'objet passé à la fenêtre de diagnostic est NULL.";
        _isVisible = true;
        ZoopLog.Error($"DiagnosticWindow.Show called with NULL object (title: {title})");
        return;
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"========== {title} ==========");
      sb.AppendLine($"Type complet: {target.GetType().FullName}");
      sb.AppendLine($"ToString: {target}");
      sb.AppendLine();

      // Section spéciale pour les données de déconstruction critiques
      if(target is Structure structure)
      {
        sb.AppendLine("╔═══════════════════════════════════════════════════════════════");
        sb.AppendLine("║ 📦 ÉLÉMENT CIBLÉ");
        sb.AppendLine("╚═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        DeconstructionInfo deconInfo = GetDeconstructionInfo(structure);

        if(deconInfo != null)
        {
          sb.AppendLine($"Structure : {deconInfo.StructureName}");
          sb.AppendLine($"Quantité par élément : {deconInfo.QuantityPerStructure}");
          sb.AppendLine();
          sb.AppendLine($"Item à récupérer : {deconInfo.ItemName}");
          sb.AppendLine($"Stack maximum : {deconInfo.MaxStackSize}");

          if(!string.IsNullOrEmpty(deconInfo.SecondaryItemName))
          {
            sb.AppendLine();
            sb.AppendLine($"Item secondaire : {deconInfo.SecondaryItemName}");
            sb.AppendLine($"Quantité secondaire : {deconInfo.SecondaryQuantity}");
          }
        } else
        {
          sb.AppendLine("⚠ Impossible de récupérer les informations de déconstruction");
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Detect and display network information
        try
        {
          List<Structure> network = _networkDetector.ExploreNetwork(structure);
          if(network != null && network.Count > 0)
          {
            sb.AppendLine("╔═══════════════════════════════════════════════════════════════");
            sb.AppendLine("║ 📊 RÉSEAU DÉTECTÉ");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Nombre total d'éléments : {network.Count}");
            sb.AppendLine();

            // Calculate total items that will be recovered
            Dictionary<string, int> itemTotals = new Dictionary<string, int>();
            DeconstructionInfo firstInfo = null;

            foreach(Structure netStructure in network)
            {
              if(netStructure == null) continue;

              DeconstructionInfo info = GetDeconstructionInfo(netStructure);
              if(info != null)
              {
                if(firstInfo == null)
                  firstInfo = info;

                if(!string.IsNullOrEmpty(info.ItemName))
                {
                  if(!itemTotals.ContainsKey(info.ItemName))
                    itemTotals[info.ItemName] = 0;
                  itemTotals[info.ItemName] += info.QuantityPerStructure;
                }

                if(!string.IsNullOrEmpty(info.SecondaryItemName) && info.SecondaryQuantity > 0)
                {
                  if(!itemTotals.ContainsKey(info.SecondaryItemName))
                    itemTotals[info.SecondaryItemName] = 0;
                  itemTotals[info.SecondaryItemName] += info.SecondaryQuantity;
                }
              }
            }

            // Display recovery info
            if(firstInfo != null && itemTotals.Count > 0)
            {
              sb.AppendLine("═══ RÉCUPÉRATION TOTALE ═══");
              sb.AppendLine();

              foreach(var kvp in itemTotals.OrderBy(x => x.Key))
              {
                string itemName = kvp.Key;
                int totalQuantity = kvp.Value;
                int maxStackSize = firstInfo.MaxStackSize;

                // Calculate number of stacks needed
                int fullStacks = totalQuantity / maxStackSize;
                int remainder = totalQuantity % maxStackSize;
                int totalStacks = fullStacks + (remainder > 0 ? 1 : 0);

                sb.AppendLine($"📦 {itemName}");
                sb.AppendLine($"   Quantité totale : {totalQuantity}");
                sb.AppendLine($"   Stack maximum : {maxStackSize}");
                sb.AppendLine($"   Nombre de stacks : {totalStacks}");

                if(totalStacks > 1)
                {
                  sb.Append($"   Répartition : ");
                  for(int i = 0; i < fullStacks; i++)
                  {
                    sb.Append($"{maxStackSize}");
                    if(i < fullStacks - 1 || remainder > 0)
                      sb.Append(" + ");
                  }
                  if(remainder > 0)
                    sb.Append($"{remainder}");
                  sb.AppendLine();
                }
                sb.AppendLine();
              }
            }
          }
        } catch(Exception ex)
        {
          sb.AppendLine($"⚠ Erreur lors de la détection du réseau : {ex.Message}");
          sb.AppendLine();
        }

        sb.AppendLine("─────────────────────────────────────────────────────────────");
        sb.AppendLine();
      }

      _diagnosticText = sb.ToString();
      _scrollPosition = Vector2.zero;
      _isVisible = true;

      // Also log to console
      ZoopLog.Info($"\n{_diagnosticText}");
    }

    public static void Hide()
    {
      _isVisible = false;
    }

    public static bool IsVisible()
    {
      return _isVisible;
    }

    private static void InitializeStyles()
    {
      if(_stylesInitialized) return;

      _windowStyle = new GUIStyle(GUI.skin.window)
      {
        fontSize = 14,
        fontStyle = FontStyle.Bold,
        normal = { textColor = Color.white }
      };

      _textAreaStyle = new GUIStyle(GUI.skin.textArea)
      {
        fontSize = 12,
        wordWrap = false,
        richText = true,
        normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.95f)) },
        padding = new RectOffset(10, 10, 10, 10)
      };

      _buttonStyle = new GUIStyle(GUI.skin.button)
      {
        fontSize = 13,
        fontStyle = FontStyle.Bold,
        normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.3f, 0.6f, 0.3f, 0.9f)) },
        hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.4f, 0.7f, 0.4f, 0.9f)) },
        active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.2f, 0.5f, 0.2f, 0.9f)) },
        padding = new RectOffset(10, 10, 5, 5)
      };

      _labelStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 12,
        fontStyle = FontStyle.Normal,
        normal = { textColor = Color.yellow }
      };

      _stylesInitialized = true;
    }

    private static Texture2D MakeTex(int width, int height, Color color)
    {
      Color[] pix = new Color[width * height];
      for(int i = 0; i < pix.Length; i++)
        pix[i] = color;

      Texture2D result = new Texture2D(width, height);
      result.SetPixels(pix);
      result.Apply();
      return result;
    }

    public static void Render()
    {
      if(!_isVisible) return;

      InitializeStyles();

      _windowRect = GUI.Window(999999, _windowRect, DrawWindow, "Diagnostic des Propriétés", _windowStyle);
    }

    private static void DrawWindow(int windowID)
    {
      GUILayout.BeginVertical();

      // Info label
      GUILayout.Label("Toutes les propriétés de l'objet inspecté (utilisez Ctrl+C pour copier)", _labelStyle);

      GUILayout.Space(5);

      // Scrollable text area
      _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(480));
      GUILayout.TextArea(_diagnosticText, _textAreaStyle);
      GUILayout.EndScrollView();

      GUILayout.Space(10);

      // Buttons
      GUILayout.BeginHorizontal();

      if(GUILayout.Button("📋 Copier dans le presse-papier", _buttonStyle, GUILayout.Height(35)))
      {
        GUIUtility.systemCopyBuffer = _diagnosticText;
        ZoopLog.Info("Diagnostic copié dans le presse-papier!");
      }

      if(GUILayout.Button("❌ Fermer", _buttonStyle, GUILayout.Height(35)))
      {
        Hide();
      }

      GUILayout.EndHorizontal();

      GUILayout.EndVertical();

      // Make window draggable
      GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
    }

    /// <summary>
    /// Diagnostic pour un Structure (Cable, Pipe, Chute, etc.)
    /// </summary>
    public static void ShowStructureDiagnostic(Structure structure)
    {
      if(structure == null)
      {
        Show(null, "Structure NULL");
        return;
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"========== STRUCTURE DIAGNOSTIC ==========");
      sb.AppendLine($"Type concret: {structure.GetType().FullName}");
      sb.AppendLine();

      // Check specific types
      if(structure is Cable cable)
      {
        sb.AppendLine(">>> C'EST UN CABLE <<<");
        sb.AppendLine($"CableType: {cable.CableType}");
        sb.AppendLine();
      } else if(structure is Pipe pipe)
      {
        sb.AppendLine(">>> C'EST UN PIPE <<<");
        sb.AppendLine($"PipeType: {pipe.PipeType}");
        sb.AppendLine();
      } else if(structure is Chute chute)
      {
        sb.AppendLine(">>> C'EST UN CHUTE <<<");
        sb.AppendLine();
      }

      // Now show all properties
      Show(structure, "Structure Complete Diagnostic");
    }

    /// <summary>
    /// Diagnostic pour une liste de structures
    /// </summary>
    public static void ShowNetworkDiagnostic(List<Structure> network)
    {
      if(network == null || network.Count == 0)
      {
        Show(null, "Network vide");
        return;
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine("╔═══════════════════════════════════════════════════════════════");
      sb.AppendLine("║ 📊 DIAGNOSTIC DU RÉSEAU");
      sb.AppendLine("╚═══════════════════════════════════════════════════════════════");
      sb.AppendLine();
      sb.AppendLine($"Nombre total d'éléments : {network.Count}");
      sb.AppendLine();

      // Calculate total items that will be recovered
      var itemTotals = new Dictionary<string, int>();
      DeconstructionInfo firstInfo = null;

      foreach(var structure in network)
      {
        if(structure == null) continue;

        var info = GetDeconstructionInfo(structure);
        if(info != null)
        {
          if(firstInfo == null)
            firstInfo = info;

          if(!string.IsNullOrEmpty(info.ItemName))
          {
            if(!itemTotals.ContainsKey(info.ItemName))
              itemTotals[info.ItemName] = 0;
            itemTotals[info.ItemName] += info.QuantityPerStructure;
          }

          if(!string.IsNullOrEmpty(info.SecondaryItemName) && info.SecondaryQuantity > 0)
          {
            if(!itemTotals.ContainsKey(info.SecondaryItemName))
              itemTotals[info.SecondaryItemName] = 0;
            itemTotals[info.SecondaryItemName] += info.SecondaryQuantity;
          }
        }
      }

      // Display recovery info
      if(firstInfo != null && itemTotals.Count > 0)
      {
        sb.AppendLine("═══ RÉCUPÉRATION D'ITEMS ═══");
        sb.AppendLine();

        foreach(var kvp in itemTotals.OrderBy(x => x.Key))
        {
          string itemName = kvp.Key;
          int totalQuantity = kvp.Value;
          int maxStackSize = firstInfo.MaxStackSize; // Use first item's stack size

          // Calculate number of stacks needed
          int fullStacks = totalQuantity / maxStackSize;
          int remainder = totalQuantity % maxStackSize;
          int totalStacks = fullStacks + (remainder > 0 ? 1 : 0);

          sb.AppendLine($"📦 {itemName}");
          sb.AppendLine($"   Quantité totale : {totalQuantity}");
          sb.AppendLine($"   Stack maximum : {maxStackSize}");
          sb.AppendLine($"   Nombre de stacks : {totalStacks}");

          if(totalStacks > 1)
          {
            sb.Append($"   Répartition : ");
            for(int i = 0; i < fullStacks; i++)
            {
              sb.Append($"{maxStackSize}");
              if(i < fullStacks - 1 || remainder > 0)
                sb.Append(" + ");
            }
            if(remainder > 0)
              sb.Append($"{remainder}");
            sb.AppendLine();
          }
          sb.AppendLine();
        }
      } else
      {
        sb.AppendLine("⚠ Impossible de calculer la récupération d'items");
        sb.AppendLine();
      }

      _diagnosticText = sb.ToString();
      _scrollPosition = Vector2.zero;
      _isVisible = true;

      ZoopLog.Info($"\n{_diagnosticText}");
    }

    /// <summary>
    /// Extrait les informations de déconstruction d'une structure.
    /// </summary>
    private static DeconstructionInfo GetDeconstructionInfo(Structure structure)
    {
      try
      {
        if(structure.BuildStates == null || structure.BuildStates.Count == 0)
          return null;

        // BuildState is a public class
        BuildState buildState = structure.BuildStates[0];

        // ToolUse inherits from ToolBasic which has public fields
        ToolBasic tool = buildState.Tool;
        if(tool == null)
          return null;

        DeconstructionInfo info = new DeconstructionInfo
        {
          StructureName = structure.PrefabName
        };

        // Get primary item - ToolBasic has public fields ToolEntry and EntryQuantity
        if(tool.ToolEntry != null)
        {
          info.ItemName = tool.ToolEntry.PrefabName;
          info.MaxStackSize = GetMaxStackSize(tool.ToolEntry);
        }

        info.QuantityPerStructure = tool.EntryQuantity;

        // Get secondary item - ToolBasic has public fields ToolEntry2 and EntryQuantity2
        if(tool.ToolEntry2 != null)
        {
          info.SecondaryItemName = tool.ToolEntry2.PrefabName;
        }

        info.SecondaryQuantity = tool.EntryQuantity2;

        return info;
      } catch
      {
        return null;
      }
    }

    /// <summary>
    /// Obtient la taille maximale du stack pour un item.
    /// </summary>
    private static int GetMaxStackSize(Thing itemPrefab)
    {
      try
      {
        // Try to cast to Stackable and use GetMaxQuantity property
        Stackable stackable = itemPrefab as Stackable;
        if(stackable != null)
        {
          return (int)stackable.GetMaxQuantity;
        }

        // Default for non-stackable items
        return 1;
      } catch
      {
        return 1;
      }
    }
  }

  /// <summary>
  /// Informations de déconstruction pour une structure.
  /// </summary>
  internal class DeconstructionInfo
  {
    public string StructureName { get; set; }
    public string ItemName { get; set; }
    public int QuantityPerStructure { get; set; }
    public int MaxStackSize { get; set; }
    public string SecondaryItemName { get; set; }
    public int SecondaryQuantity { get; set; }
    public int TotalQuantity { get; set; }
    public int NumberOfStacks { get; set; }
    public List<int> StackSizes { get; set; } = new List<int>();
  }
}
