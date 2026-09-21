using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.CadCommand;

/// <summary>
/// CAD 模拟按键与命令输入动作实现。
/// <para>
/// 负责将用户配置的命令文本注入到当前前台 CAD 窗口中，模拟真实的键盘交互。
/// 必须采用 <see cref="ActionKind.Sequential"/> 调度，在专用的前台动作线程上按序执行。
/// </para>
/// </summary>
internal sealed class CadCommandAction : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly string? _iconKey;

    public CadCommandAction(IPluginContext context, string? iconKey = null)
    {
        _context = context;
        _iconKey = iconKey;
    }

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "cadCommand",
        DisplayName = T("cad.title", Texts.ActionTitle),
        Description = T("cad.desc", Texts.ActionDesc),
        Category = T("cad.category", "CAD 绘图工具"),
        IconKey = _iconKey,
        // 关键：串行动作，保证与前台键盘、焦点交互严格保序
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = "text",
            Label = T("cad.text", Texts.FieldText),
            LabelKey = "cad.text",
            HelpText = T("cad.textHelp", Texts.FieldTextHelp),
            Type = ParameterFieldType.MultilineText,
            Required = true,
            DefaultValue = "REC",
            Placeholder = "例如: REC、LINE、C、PL、_ZWMDIAMETERDIM，或多行宏",
        },
        new()
        {
            Key = "submitKey",
            Label = T("cad.submitKey", Texts.FieldSubmitKey),
            LabelKey = "cad.submitKey",
            HelpText = T("cad.submitKeyHelp", Texts.FieldSubmitKeyHelp),
            Type = ParameterFieldType.Enum,
            DefaultValue = "Space",
            Options = new ParameterOption[]
            {
                new() { Value = "Space", Label = Texts.SubmitSpace, LabelKey = "cad.opt.space" },
                new() { Value = "Enter", Label = Texts.SubmitEnter, LabelKey = "cad.opt.enter" },
                new() { Value = "None", Label = Texts.SubmitNone, LabelKey = "cad.opt.none" },
                new() { Value = "Tab", Label = Texts.SubmitTab, LabelKey = "cad.opt.tab" },
            },
        },
        new()
        {
            Key = "sendMode",
            Label = T("cad.sendMode", Texts.FieldSendMode),
            LabelKey = "cad.sendMode",
            HelpText = T("cad.sendModeHelp", Texts.FieldSendModeHelp),
            Type = ParameterFieldType.Enum,
            DefaultValue = "FastUnicode",
            Options = new ParameterOption[]
            {
                new() { Value = "FastUnicode", Label = Texts.ModeFastUnicode, LabelKey = "cad.opt.modeFastUnicode" },
                new() { Value = "Unicode", Label = Texts.ModeUnicode, LabelKey = "cad.opt.modeUnicode" },
                new() { Value = "Paste", Label = Texts.ModePaste, LabelKey = "cad.opt.modePaste" },
            },
        },
        new()
        {
            Key = "prefixEsc",
            Label = T("cad.prefixEsc", Texts.FieldPrefixEsc),
            LabelKey = "cad.prefixEsc",
            HelpText = T("cad.prefixEscHelp", Texts.FieldPrefixEscHelp),
            Type = ParameterFieldType.Enum,
            DefaultValue = "Twice",
            Options = new ParameterOption[]
            {
                new() { Value = "Twice", Label = Texts.EscTwice, LabelKey = "cad.opt.escTwice" },
                new() { Value = "Once", Label = Texts.EscOnce, LabelKey = "cad.opt.escOnce" },
                new() { Value = "None", Label = Texts.EscNone, LabelKey = "cad.opt.escNone" },
            },
        },
        new()
        {
            Key = "preDelayMs",
            Label = T("cad.preDelay", Texts.FieldPreDelay),
            LabelKey = "cad.preDelay",
            HelpText = T("cad.preDelayHelp", Texts.FieldPreDelayHelp),
            Type = ParameterFieldType.Number,
            DefaultValue = "0",
            Min = 0,
            Max = 1000,
        },
        new()
        {
            Key = "multiLine",
            Label = T("cad.multiLine", Texts.FieldMultiLine),
            LabelKey = "cad.multiLine",
            HelpText = T("cad.multiLineHelp", Texts.FieldMultiLineHelp),
            Type = ParameterFieldType.Bool,
            DefaultValue = "false",
        },
    };

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string text = GetText(parameters);
        return string.IsNullOrWhiteSpace(text) ? T("cad.empty", Texts.TextEmpty) : null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string text = GetText(parameters);
        if (string.IsNullOrWhiteSpace(text)) return "";

        string prefix = parameters.TryGetValue("prefixEsc", out string? esc) && esc == "Twice" ? "^C^C " : "";
        string submit = parameters.TryGetValue("submitKey", out string? sub) ? sub : "Space";
        string submitTag = submit switch
        {
            "Space" => " [Space]",
            "Enter" => " [Enter]",
            "Tab" => " [Tab]",
            _ => "",
        };

        return Shorten($"{prefix}{text}{submitTag}");
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string text = input.Parameter("text") ?? input.Parameter(HostActionFields.Parameter) ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(ActionResult.Fail(T("cad.empty", Texts.TextEmpty)));
        }

        string submitKey = input.Parameter("submitKey") ?? "Space";
        string prefixEsc = input.Parameter("prefixEsc") ?? "Twice";
        string sendMode = input.Parameter("sendMode") ?? "FastUnicode";
        int preDelayMs = input.Int("preDelayMs", 0);
        bool multiLine = input.Bool("multiLine", false);

        // ① 轮盘关闭后等待延时：默认 0ms 瞬发，用户如遇焦点切回慢可微调
        if (preDelayMs > 0)
        {
            Thread.Sleep(Math.Clamp(preDelayMs, 0, 1000));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ActionResult.Empty);
        }

        // ② 前置清理命令行：模拟 CAD 宏的 ^C^C（连续两次 ESC），强制退出当前正在运行的命令
        if (string.Equals(prefixEsc, "Twice", StringComparison.OrdinalIgnoreCase))
        {
            SendEsc();
            Thread.Sleep(5);
            SendEsc();
            Thread.Sleep(5);
        }
        else if (string.Equals(prefixEsc, "Once", StringComparison.OrdinalIgnoreCase))
        {
            SendEsc();
            Thread.Sleep(5);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ActionResult.Empty);
        }

        // ③ 发送命令文本与提交
        if (multiLine)
        {
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string line = lines[i];
                if (line.Length > 0)
                {
                    SendContent(line, sendMode);
                }

                SendSubmit(submitKey);
                Thread.Sleep(10);
            }
        }
        else
        {
            SendContent(text, sendMode);
            SendSubmit(submitKey);
        }

        return Task.FromResult(ActionResult.Empty);
    }

    private void SendContent(string content, string sendMode)
    {
        if (string.Equals(sendMode, "Paste", StringComparison.OrdinalIgnoreCase))
        {
            _context.Host.SetClipboardText(content);
            _context.Host.SendHotkey("Ctrl+V");
        }
        else if (string.Equals(sendMode, "FastUnicode", StringComparison.OrdinalIgnoreCase))
        {
            // 极速瞬发模式：单批次打包全量 Unicode 字符直接调用 Win32 SendInput 塞进消息队列（<1ms）
            // 失败时优雅降级到宿主通道
            if (!FastSendUnicode(content))
            {
                _context.Host.SendText(content);
            }
        }
        else
        {
            // 宿主原生带安全间隔逐字注入模式
            _context.Host.SendText(content);
        }
    }

    private void SendEsc()
    {
        if (!FastSendVirtualKey(0x1B)) // VK_ESCAPE
        {
            if (!_context.Host.SendHotkey("Esc"))
            {
                _context.Host.SendHotkey("Escape");
            }
        }
    }

    private void SendSubmit(string submitKey)
    {
        if (string.Equals(submitKey, "Space", StringComparison.OrdinalIgnoreCase))
        {
            if (!FastSendVirtualKey(0x20)) // VK_SPACE
            {
                if (!_context.Host.SendHotkey("Space"))
                {
                    _context.Host.SendText(" ");
                }
            }
        }
        else if (string.Equals(submitKey, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            if (!FastSendVirtualKey(0x0D)) // VK_RETURN
            {
                if (!_context.Host.SendHotkey("Enter"))
                {
                    _context.Host.SendHotkey("Return");
                }
            }
        }
        else if (string.Equals(submitKey, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            if (!FastSendVirtualKey(0x09)) // VK_TAB
            {
                _context.Host.SendHotkey("Tab");
            }
        }
    }

    private static string GetText(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters == null) return "";
        if (parameters.TryGetValue("text", out string? text) && !string.IsNullOrWhiteSpace(text))
            return text;
        if (parameters.TryGetValue(HostActionFields.Parameter, out string? param) && !string.IsNullOrWhiteSpace(param))
            return param;
        return "";
    }

    private static string Shorten(string text)
    {
        string trimmed = (text ?? "").Trim().Replace('\r', ' ').Replace('\n', ' ');
        return trimmed.Length <= 48 ? trimmed : trimmed.Substring(0, 47) + "…";
    }

    #region Win32 Batch SendInput (0-delay Instant Input)

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput
    {
        [FieldOffset(0)]
        public uint type; // 1 = INPUT_KEYBOARD

        [FieldOffset(8)]
        public NativeKeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    private static readonly int NativeInputSize = Marshal.SizeOf<NativeInput>();

    private static bool FastSendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;

        var inputs = new NativeInput[text.Length * 2];
        int idx = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // KeyDown
            inputs[idx].type = INPUT_KEYBOARD;
            inputs[idx].ki.wVk = 0;
            inputs[idx].ki.wScan = c;
            inputs[idx].ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[idx].ki.time = 0;
            inputs[idx].ki.dwExtraInfo = IntPtr.Zero;
            idx++;

            // KeyUp
            inputs[idx].type = INPUT_KEYBOARD;
            inputs[idx].ki.wVk = 0;
            inputs[idx].ki.wScan = c;
            inputs[idx].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            inputs[idx].ki.time = 0;
            inputs[idx].ki.dwExtraInfo = IntPtr.Zero;
            idx++;
        }

        try
        {
            uint sent = SendInput((uint)inputs.Length, inputs, NativeInputSize);
            return sent == inputs.Length;
        }
        catch
        {
            return false;
        }
    }

    private static bool FastSendVirtualKey(ushort vk)
    {
        var inputs = new NativeInput[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        inputs[0].ki.wScan = 0;
        inputs[0].ki.dwFlags = 0;
        inputs[0].ki.time = 0;
        inputs[0].ki.dwExtraInfo = IntPtr.Zero;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki.wVk = vk;
        inputs[1].ki.wScan = 0;
        inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
        inputs[1].ki.time = 0;
        inputs[1].ki.dwExtraInfo = IntPtr.Zero;

        try
        {
            uint sent = SendInput(2, inputs, NativeInputSize);
            return sent == 2;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
