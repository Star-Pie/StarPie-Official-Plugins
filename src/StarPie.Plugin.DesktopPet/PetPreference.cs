using System;
using System.Collections.Generic;
using System.Globalization;
using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 默认配置常量。
/// </summary>
internal static class Defaults
{
    public const double SizeDiu = 140.0;
    public const double SizeMin = 48.0;
    public const double SizeMax = 360.0;
    public const bool EnableAnimation = true;
    public const string AssetDirectory = "";
    public const string DragModeImmediate = "Immediate";
    public const string DragModeLongPress = "LongPress";
    public const string DefaultDragMode = DragModeImmediate;
    public const double DefaultLongPressDurationMs = 350.0;
    public const double MinLongPressDurationMs = 200.0;
    public const double MaxLongPressDurationMs = 1000.0;
    public const bool DefaultEnableDragPose = true;
    public const bool DefaultEnablePointingPose = true;
    public const double AnimationSpeed = 1.0;
    public const double AnimationSpeedMin = 0.25;
    public const double AnimationSpeedMax = 3.0;
}

/// <summary>
/// 桌宠配置的参数键与优先级解析。
/// <para>
/// 优先级：动作参数 &gt; 插件级设置页 &gt; 内置默认。
/// 保证数值一律采用 InvariantCulture 读写，避免不同系统语言环境造成的小数点解析漂移。
/// </para>
/// </summary>
internal static class PetPreference
{
    public const string AssetDirectoryKey = "pet.assetDirectory";
    public const string SizeKey = "pet.size";
    public const string EnableAnimationKey = "pet.enableAnimation";
    public const string VisibleKey = "pet.visible";
    public const string LeftKey = "pet.left";
    public const string TopKey = "pet.top";
    public const string DragModeKey = "pet.dragMode";
    public const string LongPressDurationKey = "pet.longPressDuration";
    public const string EnableDragPoseKey = "pet.enableDragPose";
    public const string EnablePointingPoseKey = "pet.enablePointingPose";
    public const string ActiveLibraryIdKey = "pet.activeLibraryId";
    public const string AnimationSpeedKey = "pet.animationSpeed";

    public static double AnimationSpeed(IPluginContext context)
    {
        string? raw = context.Settings.Get(AnimationSpeedKey);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, Defaults.AnimationSpeedMin, Defaults.AnimationSpeedMax) : Defaults.AnimationSpeed;
    }

    /// <summary>当前选用的形象库条目 ID（若由形象库切换）。</summary>
    public static string ActiveLibraryId(IPluginContext context)
    {
        string? raw = context.Settings.Get(ActiveLibraryIdKey);
        return raw?.Trim() ?? "";
    }

    /// <summary>本地素材目录路径。</summary>
    public static string AssetDirectory(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(AssetDirectoryKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(AssetDirectoryKey);
        }
        return raw?.Trim() ?? Defaults.AssetDirectory;
    }

    /// <summary>桌宠尺寸（DIU，已钳位至合法区间）。</summary>
    public static double Size(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(SizeKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(SizeKey);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return Clamp(value, Defaults.SizeMin, Defaults.SizeMax);
        }

        return Defaults.SizeDiu;
    }

    /// <summary>是否播放动画。</summary>
    public static bool EnableAnimation(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(EnableAnimationKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(EnableAnimationKey);
        }

        if (bool.TryParse(raw, out bool value))
        {
            return value;
        }

        return Defaults.EnableAnimation;
    }

    /// <summary>桌宠当前是否处于显示状态。</summary>
    public static bool IsMarkedVisible(IPluginContext context) =>
        context.Settings.GetBool(VisibleKey, false);

    /// <summary>拖动模式（"Immediate" 或 "LongPress"，默认 "Immediate"）。</summary>
    public static string DragMode(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(DragModeKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(DragModeKey);
        }

        if (string.Equals(raw, Defaults.DragModeLongPress, StringComparison.OrdinalIgnoreCase))
        {
            return Defaults.DragModeLongPress;
        }

        return Defaults.DragModeImmediate;
    }

    /// <summary>长按时长（毫秒，200 ~ 1000ms，默认 350ms）。</summary>
    public static double LongPressDuration(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(LongPressDurationKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(LongPressDurationKey);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            if (!double.IsFinite(value)) return Defaults.DefaultLongPressDurationMs;
            return Clamp(value, Defaults.MinLongPressDurationMs, Defaults.MaxLongPressDurationMs);
        }

        return Defaults.DefaultLongPressDurationMs;
    }

    /// <summary>是否启用拖动姿势（默认 true，但受总 EnableAnimation 开关约束）。</summary>
    public static bool EnableDragPose(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(EnableDragPoseKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(EnableDragPoseKey);
        }

        if (bool.TryParse(raw, out bool value))
        {
            return value;
        }

        return Defaults.DefaultEnableDragPose;
    }

    /// <summary>是否启用轮盘扇区指向姿势（默认 true，但受总 EnableAnimation 开关约束）。</summary>
    public static bool EnablePointingPose(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(EnablePointingPoseKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = context.Settings.Get(EnablePointingPoseKey);
        }

        if (bool.TryParse(raw, out bool value))
        {
            return value;
        }

        return Defaults.DefaultEnablePointingPose;
    }

    /// <summary>从动作参数覆盖双精度浮点数，为空或解析失败时使用插件级设定。</summary>
    public static double FromAction(PluginActionInput input, string key, double pluginLevel, double min, double max)
    {
        string? raw = input.Parameter(key);
        if (string.IsNullOrWhiteSpace(raw)) return pluginLevel;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return pluginLevel;
        return Clamp(value, min, max);
    }

    /// <summary>从动作参数覆盖字符串，为空时使用插件级设定。</summary>
    public static string FromAction(PluginActionInput input, string key, string pluginLevel)
    {
        string? raw = input.Parameter(key);
        return string.IsNullOrWhiteSpace(raw) ? pluginLevel : raw.Trim();
    }

    /// <summary>从动作参数覆盖布尔值，为空时使用插件级设定。</summary>
    public static bool FromAction(PluginActionInput input, string key, bool pluginLevel)
    {
        string? raw = input.Parameter(key);
        if (string.IsNullOrWhiteSpace(raw)) return pluginLevel;
        return bool.TryParse(raw, out bool value) ? value : pluginLevel;
    }

    public static double Clamp(double value, double min, double max) =>
        value < min ? min : (value > max ? max : value);
}

/// <summary>
/// 插件级设置页的字段声明。
/// </summary>
internal static class PetSettingsFields
{
    public static IReadOnlyList<ParameterField> Build() => new List<ParameterField>
    {
        new()
        {
            Key = PetPreference.AssetDirectoryKey,
            Label = "本地素材目录",
            LabelKey = "field.asset-directory.label",
            HelpTextKey = "field.asset-directory.help",
            Type = ParameterFieldType.Folder,
            DefaultValue = Defaults.AssetDirectory,
            HelpText = "存放 idle.png 以及可选的 blink.png、react.png、drag.png 的本地文件夹。修改后在下一次显示或重载时生效。",
        },
        new()
        {
            Key = PetPreference.SizeKey,
            Label = "桌宠尺寸",
            LabelKey = "field.size.label",
            HelpTextKey = "field.size.help",
            Type = ParameterFieldType.Slider,
            Step = 1,
            Unit = "px",
            DefaultValue = Defaults.SizeDiu.ToString("0.##", CultureInfo.InvariantCulture),
            Min = Defaults.SizeMin,
            Max = Defaults.SizeMax,
            HelpText = "桌宠宽度与高度（逻辑像素，48-360）。保存后即时调整尺寸。",
        },
        new()
        {
            Key = PetPreference.EnableAnimationKey,
            Label = "播放动画",
            LabelKey = "field.animation.label",
            HelpTextKey = "field.animation.help",
            Type = ParameterFieldType.Bool,
            DefaultValue = Defaults.EnableAnimation ? "true" : "false",
            HelpText = "是否播放呼吸、眨眼与反应表情动画。关闭后仅保持静态 idle 形态，避免图片对齐差异导致的微小闪跳。",
        },
        new()
        {
            Key = PetPreference.DragModeKey,
            Label = "拖动方式",
            LabelKey = "field.drag-mode.label",
            HelpTextKey = "field.drag-mode.help",
            Type = ParameterFieldType.Enum,
            DefaultValue = Defaults.DefaultDragMode,
            Options = new List<ParameterOption>
            {
                new() { Value = Defaults.DragModeImmediate, Label = "立即拖动", LabelKey = "field.drag-mode.immediate" },
                new() { Value = Defaults.DragModeLongPress, Label = "长按后拖动", LabelKey = "field.drag-mode.long-press" },
            },
            HelpText = "选择拖拽桌宠的触发方式。立即拖动（默认）：按下后移动超过阈值即开始拖拽；长按后拖动：长按指定时长后才允许拖拽，轻点呼出轮盘。",
        },
        new()
        {
            Key = PetPreference.LongPressDurationKey,
            Label = "长按时长",
            LabelKey = "field.long-press-duration.label",
            HelpTextKey = "field.long-press-duration.help",
            Type = ParameterFieldType.Slider,
            Step = 10,
            Unit = "ms",
            DefaultValue = Defaults.DefaultLongPressDurationMs.ToString("0.##", CultureInfo.InvariantCulture),
            Min = Defaults.MinLongPressDurationMs,
            Max = Defaults.MaxLongPressDurationMs,
            HelpText = "长按触发拖动的等待时长（毫秒，200 ~ 1000ms，默认 350ms）。仅在长按模式下生效。",
        },
        new()
        {
            Key = PetPreference.EnableDragPoseKey,
            Label = "启用拖动姿势",
            LabelKey = "field.drag-pose.label",
            HelpTextKey = "field.drag-pose.help",
            Type = ParameterFieldType.Bool,
            DefaultValue = Defaults.DefaultEnableDragPose ? "true" : "false",
            HelpText = "拖动桌宠时是否切换为 drag.png 抱紧玩偶姿势（默认开启）。若总「播放动画」已关闭，则优先保持静态 idle 形态。",
        },
        new()
        {
            Key = PetPreference.EnablePointingPoseKey,
            Label = "启用轮盘指向姿势",
            LabelKey = "field.pointing-pose.label",
            HelpTextKey = "field.pointing-pose.help",
            Type = ParameterFieldType.Bool,
            DefaultValue = Defaults.DefaultEnablePointingPose ? "true" : "false",
            HelpText = "展开轮盘时是否根据选中扇区方向切换为 8 方向指向姿势（默认开启）。若总「播放动画」已关闭，则优先保持静态 idle 形态。",
        },
        new()
        {
            Key = PetPreference.AnimationSpeedKey,
            Label = "动画速度",
            LabelKey = "field.animation-speed.label",
            HelpTextKey = "field.animation-speed.help",
            Type = ParameterFieldType.Slider,
            Min = Defaults.AnimationSpeedMin,
            Max = Defaults.AnimationSpeedMax,
            Step = 0.05,
            Unit = "×",
            DefaultValue = "1",
        },
    };
}
