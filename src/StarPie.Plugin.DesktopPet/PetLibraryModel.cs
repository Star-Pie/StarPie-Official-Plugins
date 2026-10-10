using System;
using System.Collections.Generic;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 形象库单个形象元数据。
/// </summary>
public sealed class PetLibraryItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string RelativeDirectory { get; set; } = "";
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public List<string> AvailableFrames { get; set; } = new();
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 形象库索引数据结构（序列化至 library-index.json）。
/// </summary>
public sealed class PetLibraryIndex
{
    public int Version { get; set; } = 1;
    public List<PetLibraryItem> Items { get; set; } = new();
    public List<PetLibraryItem> RemovedItems { get; set; } = new();
}

public sealed record PetLibraryEditResult(bool Success, string MessageKey, params object[] Args)
{
    public string FormatMessage(IPluginContext context) => Args.Length == 0 ? context.I18n.T(MessageKey) :
        string.Format(System.Globalization.CultureInfo.InvariantCulture, context.I18n.T(MessageKey), Args);
}

/// <summary>
/// 形象导入操作结果。
/// </summary>
public sealed class PetLibraryImportResult
{
    public bool Success { get; init; }
    public bool IsDuplicate { get; init; }
    public string Message { get; init; } = "";
    public string? MessageKey { get; init; }
    public object[]? MessageArgs { get; init; }
    public PetLibraryItem? Item { get; init; }

    public string FormatMessage(IPluginContext? context)
    {
        if (context != null && !string.IsNullOrWhiteSpace(MessageKey))
        {
            string template = context.I18n.T(MessageKey, Message);
            if (MessageArgs != null && MessageArgs.Length > 0)
            {
                try { return string.Format(template, MessageArgs); } catch { return template; }
            }
            return template;
        }
        return Message;
    }

    public static PetLibraryImportResult Ok(PetLibraryItem item, string message = "导入成功。", string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Success = true,
            IsDuplicate = false,
            Item = item,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };

    public static PetLibraryImportResult Duplicate(PetLibraryItem existingItem, string message = "形象库中已存在相同内容的形象。", string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Success = true,
            IsDuplicate = true,
            Item = existingItem,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };

    public static PetLibraryImportResult Fail(string message, string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Success = false,
            IsDuplicate = false,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };
}

/// <summary>
/// 形象应用结果。
/// </summary>
public enum PetApplyStatus
{
    Success,
    Busy,
    Failed,
}

public sealed class PetApplyResult
{
    public PetApplyStatus Status { get; init; }
    public string Message { get; init; } = "";
    public string? MessageKey { get; init; }
    public object[]? MessageArgs { get; init; }
    public string? ItemId { get; init; }

    public bool IsSuccess => Status == PetApplyStatus.Success;
    public bool IsBusy => Status == PetApplyStatus.Busy;

    public string FormatMessage(IPluginContext? context)
    {
        if (context != null && !string.IsNullOrWhiteSpace(MessageKey))
        {
            string template = context.I18n.T(MessageKey, Message);
            if (MessageArgs != null && MessageArgs.Length > 0)
            {
                try { return string.Format(template, MessageArgs); } catch { return template; }
            }
            return template;
        }
        return Message;
    }

    public static PetApplyResult Ok(string itemId, string message = "形象应用成功。", string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Status = PetApplyStatus.Success,
            ItemId = itemId,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };

    public static PetApplyResult Busy(string message = "桌宠当前正在交互中（拖动或轮盘呼出中），无法切换形象，请稍后重试。", string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Status = PetApplyStatus.Busy,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };

    public static PetApplyResult Fail(string message, string? messageKey = null, params object[]? messageArgs) =>
        new()
        {
            Status = PetApplyStatus.Failed,
            Message = message,
            MessageKey = messageKey,
            MessageArgs = messageArgs,
        };
}
