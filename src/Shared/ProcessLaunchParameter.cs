using System;
using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.OfficialPlugins;

/// <summary>官方插件内部参数约定；不成为宿主的字段或 SDK 公共常量。</summary>
internal static class ProcessLaunchParameter
{
    internal const string Key = "launchMode";

    internal static ParameterField Field(bool legacyLaunch = false) => new()
    {
        Key = Key,
        Label = "启动权限",
        LabelKey = "processLaunch.label",
        Type = ParameterFieldType.Enum,
        DefaultValue = nameof(ProcessLaunchMode.Default),
        FallbackParameterKey = legacyLaunch ? HostActionFields.RunAsStandardUser : null,
        FallbackValueMap = legacyLaunch ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["true"] = nameof(ProcessLaunchMode.StandardUser),
            ["false"] = nameof(ProcessLaunchMode.Default),
        } : null,
        Options = new[]
        {
            new ParameterOption { Value = nameof(ProcessLaunchMode.Default), Label = "默认", LabelKey = "processLaunch.default" },
            new ParameterOption { Value = nameof(ProcessLaunchMode.Administrator), Label = "固定以管理员权限启动", LabelKey = "processLaunch.admin" },
            new ParameterOption { Value = nameof(ProcessLaunchMode.StandardUser), Label = "固定以非管理员权限启动", LabelKey = "processLaunch.standard" },
        },
    };

    internal static bool TryRead(IReadOnlyDictionary<string, string>? parameters, bool legacyLaunch, out ProcessLaunchMode mode)
    {
        mode = ProcessLaunchMode.Default;
        if (parameters != null && parameters.TryGetValue(Key, out string? value))
        {
            // 不接受数字、未知值或空值，防止损坏配置静默变成默认权限。
            foreach (ProcessLaunchMode candidate in Enum.GetValues<ProcessLaunchMode>())
                if (string.Equals(value, candidate.ToString(), StringComparison.Ordinal))
                { mode = candidate; return true; }
            return false;
        }
        if (legacyLaunch && parameters != null && parameters.TryGetValue(HostActionFields.RunAsStandardUser, out string? legacy) &&
            bool.TryParse(legacy, out bool standard) && standard)
            mode = ProcessLaunchMode.StandardUser;
        return true;
    }

    internal static string Invalid(IPluginContext context) => context.I18n.T("processLaunch.invalid", "启动权限选项无效，请重新选择。");
    internal static ActionResult NotStarted(IPluginContext context) => ActionResult.Ok(
        context.I18n.T("processLaunch.notStarted", "未能按所选权限启动：可能已取消授权、目标不支持该模式或启动失败；未改用其他权限重试。详情见日志。"), silent: false);

    internal static void RegisterTexts(IPluginContext context)
    {
        context.I18n.Register("processLaunch.label", "启动权限", "Launch permissions");
        context.I18n.Register("processLaunch.default", "默认", "Default");
        context.I18n.Register("processLaunch.admin", "固定以管理员权限启动", "Always launch as administrator");
        context.I18n.Register("processLaunch.standard", "固定以非管理员权限启动", "Always launch as standard user");
        context.I18n.Register("processLaunch.invalid", "启动权限选项无效，请重新选择。", "Invalid launch permissions. Please select a valid mode.");
        context.I18n.Register("processLaunch.notStarted", "未能按所选权限启动：可能已取消授权、目标不支持该模式或启动失败；未改用其他权限重试。详情见日志.",
            "Could not start with the selected permissions: authorization was canceled, the mode is unsupported, or launch failed. No other mode was attempted. See the log.");
        context.I18n.RegisterTable("zh-TW", new Dictionary<string, string>
        {
            ["processLaunch.label"] = "啟動權限", ["processLaunch.default"] = "預設",
            ["processLaunch.admin"] = "固定以系統管理員權限啟動", ["processLaunch.standard"] = "固定以非系統管理員權限啟動",
            ["processLaunch.invalid"] = "啟動權限選項無效，請重新選擇。",
            ["processLaunch.notStarted"] = "無法依所選權限啟動：授權已取消、模式不支援或啟動失敗；未改用其他權限重試。詳情請見記錄。",
        });
        context.I18n.RegisterTable("ja", new Dictionary<string, string>
        {
            ["processLaunch.label"] = "起動権限", ["processLaunch.default"] = "既定",
            ["processLaunch.admin"] = "常に管理者として起動", ["processLaunch.standard"] = "常に標準ユーザーとして起動",
            ["processLaunch.invalid"] = "起動権限が無効です。選び直してください。",
            ["processLaunch.notStarted"] = "指定した権限で起動できませんでした。承認のキャンセル、未対応のモード、または起動失敗です。別の権限では再試行していません。ログを確認してください。",
        });
    }
}
