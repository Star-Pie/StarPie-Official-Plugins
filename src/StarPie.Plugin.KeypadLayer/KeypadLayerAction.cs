using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.KeypadLayer;

/// <summary>
/// 按键映射动作贡献点实现。
/// <para>
/// 负责响应轮盘扇区触发，调用宿主集中管理的 <see cref="IHostKeyboardRemapService"/>
/// 切换前台窗口的按键映射层。默认预设为左手空间数字小键盘。
/// </para>
/// </summary>
public sealed class KeypadLayerAction : IActionContribution
{
    public const string DefaultKeyMap = "v1|Q:Num7,W:Num8,E:Num9,A:Num4,S:Num5,D:Num6,Z:Num1,X:Num2,C:Num3,R:Num0";

    private readonly IPluginContext _context;
    private readonly string? _iconKey;

    public KeypadLayerAction(IPluginContext context, string? iconKey = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _iconKey = iconKey;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "keypadLayer",
        DisplayName = Texts.ActionTitle,
        DisplayNameKey = "keypad.title",
        Description = Texts.ActionDesc,
        Category = Texts.Category,
        IconKey = _iconKey,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 5,
    };

    public IReadOnlyList<ParameterField> Parameters => new[]
    {
        new ParameterField
        {
            Key = "keyMap",
            Label = Texts.FieldKeyMap,
            LabelKey = "keypad.field.keyMap",
            Type = ParameterFieldType.KeyMap,
            DefaultValue = DefaultKeyMap,
            Required = true,
            HelpText = Texts.FieldKeyMapHelp,
            Placeholder = DefaultKeyMap,
        }
    };

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        // 保持对宿主透明：插件端不复制解析器与校验器，由宿主统一做版本校验与约束检查
        return null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters == null || !parameters.TryGetValue("keyMap", out string? keyMap) || string.IsNullOrWhiteSpace(keyMap))
        {
            return _context.I18n.T("keypad.preview", Texts.Preview);
        }

        keyMap = keyMap.Trim();
        if (string.Equals(keyMap, DefaultKeyMap, StringComparison.OrdinalIgnoreCase))
        {
            return _context.I18n.T("keypad.preview", Texts.Preview);
        }

        return FormatCustomPreview(keyMap);
    }

    private string FormatCustomPreview(string keyMap)
    {
        string payload = keyMap;
        if (payload.StartsWith("v1|", StringComparison.OrdinalIgnoreCase))
        {
            payload = payload.Substring(3);
        }

        string[] pairs = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pairs.Length == 0)
        {
            return _context.I18n.T("keypad.preview", Texts.Preview);
        }

        var list = new List<string>(pairs.Length);
        foreach (string pair in pairs)
        {
            int colonIdx = pair.IndexOf(':');
            if (colonIdx > 0 && colonIdx < pair.Length - 1)
            {
                string src = pair.Substring(0, colonIdx).Trim();
                string dst = pair.Substring(colonIdx + 1).Trim();
                list.Add($"{src} ➔ {dst}");
            }
            else
            {
                list.Add(pair);
            }
        }

        if (list.Count <= 3)
        {
            return string.Join(", ", list);
        }

        string firstThree = string.Join(", ", list.GetRange(0, 3));
        string moreTemplate = _context.I18n.T("keypad.preview.more", Texts.PreviewMore);
        string suffix = string.Format(moreTemplate, list.Count);
        return $"{firstThree} {suffix}".Trim();
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        // 门禁检查：若未授权 InputRemapping 能力，返回带指引的非熔断提示
        if (!_context.Info.HasCapability(PluginCapability.InputRemapping))
        {
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.noCapability", Texts.ErrNoCapability), silent: false));
        }

        string foreground = input.Context?.ForegroundProcessName?.Trim() ?? "";

        // 查询当前映射状态
        KeyboardRemapStatus currentStatus;
        try
        {
            currentStatus = _context.KeyboardRemap.GetStatus();
        }
        catch (Exception ex)
        {
            _context.Log.Error("获取键盘映射状态失败", ex);
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.hostFailed", Texts.ErrHostFailed), silent: false));
        }

        bool isOwnedByUs = currentStatus.IsActive && string.Equals(currentStatus.ActivePluginId, _context.Me.Id, StringComparison.OrdinalIgnoreCase);

        // 激活路径前置检查：若当前未激活，前台进程为空时给出可见提示且不造成熔断
        if (!isOwnedByUs && string.IsNullOrEmpty(foreground))
        {
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.noForeground", Texts.ErrNoForeground), silent: false));
        }

        // 透传配置字符串，缺省时使用标准空间数字层默认预设
        string keyMap = input.Parameter("keyMap")?.Trim() ?? "";
        if (string.IsNullOrEmpty(keyMap))
        {
            keyMap = DefaultKeyMap;
        }

        // 执行宿主 Toggle 操作（一次触发只调用一次 Toggle）
        KeyboardRemapResult remapResult;
        try
        {
            remapResult = _context.KeyboardRemap.Toggle(foreground, keyMap);
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Warn($"调用键盘映射服务被宿主门禁拦截: {ex.Message}");
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.noCapability", Texts.ErrNoCapability), silent: false));
        }
        catch (Exception ex)
        {
            _context.Log.Error("调用键盘映射切换失败", ex);
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.hostFailed", Texts.ErrHostFailed), silent: false));
        }

        // 宿主拒绝（已有其他插件占用、前台不符、格式错误等）：必须返回 Ok(silent: false)，严禁 Fail 导致自动隔离
        // 原始错误信息落盘日志，用户界面只呈现本地化、简洁且可操作的反馈
        if (!remapResult.Success)
        {
            _context.Log.Warn($"宿主键盘映射操作被拒绝: {remapResult.Message}");

            KeyboardRemapStatus failStatus;
            try
            {
                failStatus = _context.KeyboardRemap.GetStatus();
            }
            catch
            {
                failStatus = currentStatus;
            }

            if (failStatus.IsActive && !string.Equals(failStatus.ActivePluginId, _context.Me.Id, StringComparison.OrdinalIgnoreCase))
            {
                string template = _context.I18n.T("keypad.err.busyByOther", Texts.ErrBusyByOther);
                string plugin = !string.IsNullOrEmpty(failStatus.ActivePluginId) ? failStatus.ActivePluginId : "unknown";
                return Task.FromResult(ActionResult.Ok(string.Format(template, plugin), silent: false));
            }

            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.err.toggleFailed", Texts.ErrToggleFailed), silent: false));
        }

        // 成功分支：根据最新会话状态反馈开启/关闭结果
        KeyboardRemapStatus newStatus;
        try
        {
            newStatus = _context.KeyboardRemap.GetStatus();
        }
        catch
        {
            newStatus = currentStatus;
        }

        if (newStatus.IsActive && string.Equals(newStatus.ActivePluginId, _context.Me.Id, StringComparison.OrdinalIgnoreCase))
        {
            string target = !string.IsNullOrEmpty(newStatus.TargetProcessName) ? newStatus.TargetProcessName : foreground;
            string template = _context.I18n.T("keypad.msg.activated", Texts.MsgActivated);
            return Task.FromResult(ActionResult.Ok(string.Format(template, target), silent: false));
        }
        else
        {
            return Task.FromResult(ActionResult.Ok(_context.I18n.T("keypad.msg.deactivated", Texts.MsgDeactivated), silent: false));
        }
    }
}
