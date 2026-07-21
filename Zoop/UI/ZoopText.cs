using Assets.Scripts;
using UnityEngine;

namespace ZoopMod.Zoop.UI;

public static class ZoopText
{
  private static readonly int NoDoubleWaypointsHash = Animator.StringToHash("zoopNoDoubleWaypoints");
  private static readonly int LongPiecesTitleHash = Animator.StringToHash("zoopLongPiecesTitle");
  private static readonly int BulkStatusTitleHash = Animator.StringToHash("zoopBulkStatusTitle");
  private static readonly int BulkStatusActiveHash = Animator.StringToHash("zoopBulkStatusActive");
  private static readonly int BulkStructureTitleFormatHash = Animator.StringToHash("zoopBulkStructureTitleFormat");
  private static readonly int BulkLabelNetworkSizeHash = Animator.StringToHash("zoopBulkLabelNetworkSize");
  private static readonly int BulkLabelStatusHash = Animator.StringToHash("zoopBulkLabelStatus");
  private static readonly int BulkLabelReasonHash = Animator.StringToHash("zoopBulkLabelReason");
  private static readonly int BulkValueOkHash = Animator.StringToHash("zoopBulkValueOk");
  private static readonly int BulkValueInvalidHash = Animator.StringToHash("zoopBulkValueInvalid");
  private static readonly int BulkReasonNoTargetHash = Animator.StringToHash("zoopBulkReasonNoTarget");
  private static readonly int BulkReasonUnknownTypeHash = Animator.StringToHash("zoopBulkReasonUnknownType");
  private static readonly int BulkReasonPoweredHash = Animator.StringToHash("zoopBulkReasonPowered");
  private static readonly int BulkReasonUnderPressureHash = Animator.StringToHash("zoopBulkReasonUnderPressure");
  private static readonly int BulkReasonOffhandMismatchHash = Animator.StringToHash("zoopBulkReasonOffhandMismatch");

  public static string msgNoDoubleWaypoints =>
    GetInterfaceText(NoDoubleWaypointsHash, "You cannot add a waypoint at the same location as the previous one");

  public static string LongPiecesTitle => GetInterfaceText(LongPiecesTitleHash, "Long Pieces");
  public static string BulkStatusTitle => GetInterfaceText(BulkStatusTitleHash, "Bulk Deconstruction");
  public static string BulkStatusActive => GetInterfaceText(BulkStatusActiveHash, "Activated");
  public static string BulkStructureTitleFormat => GetInterfaceText(BulkStructureTitleFormatHash, "{0} Bulk");
  public static string BulkLabelNetworkSize => GetInterfaceText(BulkLabelNetworkSizeHash, "Network Size:");
  public static string BulkLabelStatus => GetInterfaceText(BulkLabelStatusHash, "Status:");
  public static string BulkLabelReason => GetInterfaceText(BulkLabelReasonHash, "Reason:");
  public static string BulkValueOk => GetInterfaceText(BulkValueOkHash, "OK");
  public static string BulkValueInvalid => GetInterfaceText(BulkValueInvalidHash, "INVALID");
  public static string BulkReasonNoTarget => GetInterfaceText(BulkReasonNoTargetHash, "No target structure");
  public static string BulkReasonUnknownType => GetInterfaceText(BulkReasonUnknownTypeHash, "Unknown network type");
  public static string BulkReasonPowered => GetInterfaceText(BulkReasonPoweredHash, "Network is powered");
  public static string BulkReasonUnderPressure => GetInterfaceText(BulkReasonUnderPressureHash, "Network under pressure");
  public static string BulkReasonOffhandMismatch => GetInterfaceText(BulkReasonOffhandMismatchHash, "Offhand item mismatch");

  private static string GetInterfaceText(int hash, string fallback)
  {
    return Localization.InterfaceExists(hash)
      ? Localization.GetInterface(hash)
      : fallback;
  }
}
