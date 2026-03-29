using System;
using BepInEx.Logging;
using ZoopMod.Zoop.EntryPoints.Configuration;

namespace ZoopMod.Zoop.Logging;

/// <summary>
/// Centralizes mod logging on top of the BepInEx logger and the mod's own verbosity settings.
/// </summary>
internal static class ZoopLog
{
  private const string DiagnosticPrefix = "[diagnostic] ";
  private static ManualLogSource logger = null!;
  private static bool isInitialized;

  public static void Initialize(ManualLogSource source)
  {
    logger = source;
    isInitialized = true;
  }

  public static void Debug(string message)
  {
    if (!isInitialized || !AreDiagnosticLogsEnabled())
    {
      return;
    }

    logger.LogInfo(DiagnosticPrefix + message);
  }

  public static void Info(string message)
  {
    if (!isInitialized)
    {
      return;
    }

    logger.LogInfo(message);
  }

  public static void Warn(string message)
  {
    if (!isInitialized)
    {
      return;
    }

    logger.LogWarning(message);
  }

  public static void Error(string message)
  {
    if (!isInitialized)
    {
      return;
    }

    logger.LogError(message);
  }

  public static void Error(Exception exception, string context = null)
  {
    var message = context == null ? exception.ToString() : $"{context}{Environment.NewLine}{exception}";
    Error(message);
  }

  private static bool AreDiagnosticLogsEnabled()
  {
    return !ZoopConfig.IsInitialized || ZoopConfig.EnableDiagnosticLogs.Value;
  }
}
