using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StarPie.Plugin.Macro;

/// <summary>
/// 为宿主插件参数面板注入可视化多步骤宏编排器：
///   1. 原生风格热键录制框（含内联清除按钮 ×、⏸️ 暂停全局热键独占录制、⚙️ 拼装组合、单步重复次数）；
///   2. 粘贴指定文本 / 逐字输入 / 复制指定文本：支持行内预览与「⚙️ 配置文本」多行大文本弹窗编辑；
///   3. 逐字输入支持每字打字间隔 (CharDelayMs) 行内与弹窗双重配置；
///   4. 100% 通过底层 TextBox.Text 双向同步，兼容 SDK 1.4 (1.8.0-beta.1+)。
/// </summary>
internal static class MacroVisualBuilder
{
    private static readonly List<AttachedInstance> AttachedInstances = new();
    private static bool _attachQueued;

    private static readonly Brush FallbackWindowBg = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)));
    private static readonly Brush FallbackCardBg = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x88, 0x98)));
    private static readonly Brush FallbackCardBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x88, 0x98)));
    private static readonly Brush FallbackInputBg = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush FallbackInputBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)));
    private static readonly Brush FallbackAccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
    private static readonly Brush FallbackRecordingBg = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0x25, 0x63, 0xEB)));
    private static readonly Brush FallbackRecordingFg = Freeze(new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48)));
    private static readonly Brush FallbackPrimaryText = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)));
    private static readonly Brush FallbackSecondaryText = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));
    private static readonly Brush FallbackMutedText = Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)));
    private static readonly Brush FallbackBadgeBg = Freeze(new SolidColorBrush(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6)));
    private static readonly Brush FallbackWhiteBrush = Freeze(new SolidColorBrush(Colors.White));

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static void RequestAttach(Func<string, string, string>? localize)
    {
        try
        {
            Application? app = Application.Current;
            Dispatcher? dispatcher = app?.Dispatcher;
            if (app == null || dispatcher == null || !dispatcher.CheckAccess())
            {
                return;
            }

            if (_attachQueued)
            {
                return;
            }

            _attachQueued = true;
            dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _attachQueued = false;
                    TryAttachToOpenWindows(localize);
                }));
        }
        catch
        {
            _attachQueued = false;
        }
    }

    public static void DetachAll()
    {
        try
        {
            Application? app = Application.Current;
            Dispatcher? dispatcher = app?.Dispatcher;
            if (dispatcher == null)
            {
                lock (AttachedInstances)
                {
                    AttachedInstances.Clear();
                }
                return;
            }

            if (dispatcher.CheckAccess())
            {
                DetachAllOnUiThread();
            }
            else
            {
                dispatcher.Invoke(DetachAllOnUiThread);
            }
        }
        catch
        {
        }
    }

    private static void DetachAllOnUiThread()
    {
        List<AttachedInstance> snapshot;
        lock (AttachedInstances)
        {
            snapshot = new List<AttachedInstance>(AttachedInstances);
            AttachedInstances.Clear();
        }

        foreach (AttachedInstance inst in snapshot)
        {
            inst.Detach();
        }
    }

    private static void TryAttachToOpenWindows(Func<string, string, string>? localize)
    {
        try
        {
            Application? app = Application.Current;
            if (app == null)
            {
                return;
            }

            PruneDeadInstances();

            foreach (Window? window in app.Windows)
            {
                if (window == null || !window.IsLoaded)
                {
                    continue;
                }

                var scriptBoxes = new List<TextBox>();
                FindMacroScriptTextBoxes(window, scriptBoxes);
                foreach (TextBox scriptBox in scriptBoxes)
                {
                    if (!IsAlreadyAttached(scriptBox))
                    {
                        TryAttachSingle(scriptBox, localize);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static void PruneDeadInstances()
    {
        for (int i = AttachedInstances.Count - 1; i >= 0; i--)
        {
            if (!AttachedInstances[i].IsAlive)
            {
                AttachedInstances[i].CleanupExclusiveHook();
                AttachedInstances.RemoveAt(i);
            }
        }
    }

    private static bool IsAlreadyAttached(TextBox scriptBox)
    {
        for (int i = 0; i < AttachedInstances.Count; i++)
        {
            if (ReferenceEquals(AttachedInstances[i].ScriptBox, scriptBox))
            {
                return true;
            }
        }
        return false;
    }

    private static void FindMacroScriptTextBoxes(DependencyObject root, List<TextBox> results)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox tb &&
                tb.AcceptsReturn &&
                tb.ToolTip is string tip &&
                string.Equals(tip, Texts.FieldScriptPlaceholder, StringComparison.Ordinal))
            {
                results.Add(tb);
            }
            else
            {
                FindMacroScriptTextBoxes(child, results);
            }
        }
    }

    private static void TryAttachSingle(TextBox scriptBox, Func<string, string, string>? localize)
    {
        if (scriptBox.Parent is not StackPanel scriptContainer)
        {
            return;
        }

        Panel? hostPanel = scriptContainer.Parent as Panel;
        StackPanel? preDelayRow = null;
        StackPanel? stepDelayRow = null;
        StackPanel? repeatCountRow = null;
        TextBox? preDelayBox = null;
        TextBox? stepDelayBox = null;
        TextBox? repeatCountBox = null;

        if (hostPanel != null)
        {
            int scriptRowIdx = hostPanel.Children.IndexOf(scriptContainer);
            if (scriptRowIdx >= 0)
            {
                preDelayRow = TryGetSiblingRow(hostPanel, scriptRowIdx + 1, "50", out preDelayBox);
                stepDelayRow = TryGetSiblingRow(hostPanel, scriptRowIdx + 2, "30", out stepDelayBox);
                repeatCountRow = TryGetSiblingRow(hostPanel, scriptRowIdx + 3, "1", out repeatCountBox);
            }
        }

        var instance = new AttachedInstance(
            scriptContainer,
            scriptBox,
            preDelayRow,
            preDelayBox,
            stepDelayRow,
            stepDelayBox,
            repeatCountRow,
            repeatCountBox,
            localize);

        instance.Attach();
        AttachedInstances.Add(instance);
    }

    private static StackPanel? TryGetSiblingRow(
        Panel hostPanel,
        int index,
        string expectedPlaceholder,
        out TextBox? innerBox)
    {
        innerBox = null;
        if (index < 0 || index >= hostPanel.Children.Count)
        {
            return null;
        }

        if (hostPanel.Children[index] is not StackPanel row)
        {
            return null;
        }

        foreach (object? child in row.Children)
        {
            if (child is TextBox tb &&
                !tb.AcceptsReturn &&
                string.Equals(tb.ToolTip as string, expectedPlaceholder, StringComparison.Ordinal))
            {
                innerBox = tb;
                return row;
            }
        }

        return null;
    }

    private sealed class EditableStep
    {
        public MacroStepKind Kind { get; set; } = MacroStepKind.Hotkey;
        public string Value { get; set; } = "Enter";
        public int WaitMs { get; set; } = 100;
        public int Repeat { get; set; } = 1;
        public int CharDelayMs { get; set; } = MacroScriptParser.DefaultCharDelayMs;
        public bool AutoStartRecording { get; set; }
        public bool AutoOpenTextDialog { get; set; }

        public MacroStep ToMacroStep(int lineNumber) => Kind switch
        {
            MacroStepKind.Wait => new MacroStep(
                lineNumber,
                MacroStepKind.Wait,
                $"{WaitMs}ms",
                WaitMs: Math.Clamp(WaitMs, 0, MacroScriptParser.MaxSingleWaitMs),
                Repeat: 1),
            MacroStepKind.Hotkey or MacroStepKind.Key => new MacroStep(
                lineNumber,
                MacroScriptParser.IsSingleKeyExpression(Value) ? MacroStepKind.Key : MacroStepKind.Hotkey,
                string.IsNullOrWhiteSpace(Value) ? "Enter" : Value.Trim(),
                WaitMs: 0,
                Repeat: Math.Clamp(Repeat, 1, MacroScriptParser.MaxSingleRepeat)),
            _ => new MacroStep(
                lineNumber,
                Kind,
                string.IsNullOrEmpty(Value) ? "Text" : Value,
                WaitMs: 0,
                Repeat: 1,
                CharDelayMs: Math.Clamp(CharDelayMs, 0, MacroScriptParser.MaxCharDelayMs)),
        };
    }

    private sealed class AttachedInstance
    {
        private readonly StackPanel _scriptContainer;
        private readonly TextBox _scriptBox;
        private readonly StackPanel? _preDelayRow;
        private readonly TextBox? _preDelayBox;
        private readonly StackPanel? _stepDelayRow;
        private readonly TextBox? _stepDelayBox;
        private readonly StackPanel? _repeatCountRow;
        private readonly TextBox? _repeatCountBox;
        private readonly Func<string, string, string>? _localize;

        private readonly List<EditableStep> _steps = new();
        private readonly List<UIElement> _collapsedScriptHelpElements = new();

        private Border? _rootCard;
        private StackPanel? _stepsHost;
        private TextBox? _compactPreDelayBox;
        private TextBox? _compactStepDelayBox;
        private TextBox? _compactRepeatBox;
        private bool _syncingFromVisual;
        private bool _syncingFromRaw;
        private bool _rawSourceVisible;

        // Exclusive recording state via host's MainKeyboardHook (reflection-bridged, zero StarPie.dll compile dependency)
        private EditableStep? _exclusiveRecordingStep;
        private Action<string>? _exclusiveVisualUpdate;
        private Action? _exclusiveVisualExit;
        private object? _subscribedHookInstance;
        private Delegate? _completedHandler;
        private Delegate? _cancelledHandler;
        private Delegate? _modifiersHandler;

        public TextBox ScriptBox => _scriptBox;
        public bool IsAlive => _scriptBox.IsLoaded || _scriptContainer.IsLoaded;

        public AttachedInstance(
            StackPanel scriptContainer,
            TextBox scriptBox,
            StackPanel? preDelayRow,
            TextBox? preDelayBox,
            StackPanel? stepDelayRow,
            TextBox? stepDelayBox,
            StackPanel? repeatCountRow,
            TextBox? repeatCountBox,
            Func<string, string, string>? localize)
        {
            _scriptContainer = scriptContainer;
            _scriptBox = scriptBox;
            _preDelayRow = preDelayRow;
            _preDelayBox = preDelayBox;
            _stepDelayRow = stepDelayRow;
            _stepDelayBox = stepDelayBox;
            _repeatCountRow = repeatCountRow;
            _repeatCountBox = repeatCountBox;
            _localize = localize;
        }

        private string L(string key, string fallback) =>
            _localize != null ? _localize(key, fallback) : fallback;

        public void Attach()
        {
            for (int i = 0; i < _scriptContainer.Children.Count; i++)
            {
                if (_scriptContainer.Children[i] is TextBlock tb &&
                    tb.Visibility == Visibility.Visible &&
                    tb.Text != null &&
                    !tb.Text.StartsWith("⚠️", StringComparison.Ordinal))
                {
                    _collapsedScriptHelpElements.Add(tb);
                    tb.Visibility = Visibility.Collapsed;
                }
            }

            _scriptBox.Visibility = Visibility.Collapsed;
            _scriptBox.Margin = new Thickness(0, 6, 0, 0);

            if (_preDelayRow != null) _preDelayRow.Visibility = Visibility.Collapsed;
            if (_stepDelayRow != null) _stepDelayRow.Visibility = Visibility.Collapsed;
            if (_repeatCountRow != null) _repeatCountRow.Visibility = Visibility.Collapsed;

            _rootCard = BuildVisualCard();

            int editorIdx = _scriptContainer.Children.IndexOf(_scriptBox);
            if (editorIdx >= 0)
            {
                _scriptContainer.Children.Insert(editorIdx, _rootCard);
            }
            else
            {
                _scriptContainer.Children.Add(_rootCard);
            }

            _scriptBox.TextChanged += OnRawScriptBoxTextChanged;
            if (_preDelayBox != null) _preDelayBox.TextChanged += OnUnderlyingTimingBoxChanged;
            if (_stepDelayBox != null) _stepDelayBox.TextChanged += OnUnderlyingTimingBoxChanged;
            if (_repeatCountBox != null) _repeatCountBox.TextChanged += OnUnderlyingTimingBoxChanged;

            LoadFromRawScript(_scriptBox.Text);
            SyncCompactTimingFromUnderlying();
        }

        public void Detach()
        {
            CleanupExclusiveHook();

            _scriptBox.TextChanged -= OnRawScriptBoxTextChanged;
            if (_preDelayBox != null) _preDelayBox.TextChanged -= OnUnderlyingTimingBoxChanged;
            if (_stepDelayBox != null) _stepDelayBox.TextChanged -= OnUnderlyingTimingBoxChanged;
            if (_repeatCountBox != null) _repeatCountBox.TextChanged -= OnUnderlyingTimingBoxChanged;

            if (_rootCard != null && _scriptContainer.Children.Contains(_rootCard))
            {
                _scriptContainer.Children.Remove(_rootCard);
            }

            _scriptBox.Visibility = Visibility.Visible;
            foreach (UIElement el in _collapsedScriptHelpElements)
            {
                el.Visibility = Visibility.Visible;
            }
            _collapsedScriptHelpElements.Clear();

            if (_preDelayRow != null) _preDelayRow.Visibility = Visibility.Visible;
            if (_stepDelayRow != null) _stepDelayRow.Visibility = Visibility.Visible;
            if (_repeatCountRow != null) _repeatCountRow.Visibility = Visibility.Visible;
        }

        public void CleanupExclusiveHook()
        {
            try
            {
                if (_subscribedHookInstance != null)
                {
                    Type hookType = _subscribedHookInstance.GetType();
                    if (_completedHandler != null)
                    {
                        hookType.GetEvent("OnExclusiveRecordCompleted")?.RemoveEventHandler(_subscribedHookInstance, _completedHandler);
                    }
                    if (_cancelledHandler != null)
                    {
                        hookType.GetEvent("OnExclusiveRecordCancelled")?.RemoveEventHandler(_subscribedHookInstance, _cancelledHandler);
                    }
                    if (_modifiersHandler != null)
                    {
                        hookType.GetEvent("OnExclusiveRecordModifiersChanged")?.RemoveEventHandler(_subscribedHookInstance, _modifiersHandler);
                    }

                    bool isSuppressing = hookType.GetProperty("SuppressGlobalHotkeysForRecording")?.GetValue(_subscribedHookInstance) is true;
                    if (isSuppressing && _exclusiveRecordingStep != null)
                    {
                        hookType.GetMethod("CancelExclusiveRecording", Type.EmptyTypes)?.Invoke(_subscribedHookInstance, null);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _subscribedHookInstance = null;
                _completedHandler = null;
                _cancelledHandler = null;
                _modifiersHandler = null;
                _exclusiveRecordingStep = null;
                _exclusiveVisualUpdate = null;
                _exclusiveVisualExit = null;
            }
        }

        private Border BuildVisualCard()
        {
            var outerStack = new StackPanel();

            // 1. Header hint
            var headerText = new TextBlock
            {
                Text = L("macro.ui.header", Texts.UiHeader),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            BindForeground(headerText, "TextSecondaryBrush", FallbackSecondaryText);
            outerStack.Children.Add(headerText);

            // 2. Step rows container
            _stepsHost = new StackPanel();
            outerStack.Children.Add(_stepsHost);

            // 3. Quick-add & Raw Script toggle toolbar
            var toolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
            };

            Button addKeyBtn = CreateToolbarButton(L("macro.ui.addKeyBtn", Texts.UiAddKeyBtn));
            addKeyBtn.Click += (_, _) =>
            {
                if (_steps.Count >= MacroScriptParser.MaxSteps) return;
                _steps.Add(new EditableStep
                {
                    Kind = MacroStepKind.Hotkey,
                    Value = "Enter",
                    Repeat = 1,
                    AutoStartRecording = true,
                });
                RebuildStepRows();
                PushStepsToRawScript();
            };
            toolbar.Children.Add(addKeyBtn);

            Button addPasteBtn = CreateToolbarButton(L("macro.ui.addPasteBtn", Texts.UiAddPasteBtn));
            addPasteBtn.Click += (_, _) =>
            {
                if (_steps.Count >= MacroScriptParser.MaxSteps) return;
                _steps.Add(new EditableStep
                {
                    Kind = MacroStepKind.Paste,
                    Value = "",
                    Repeat = 1,
                    AutoOpenTextDialog = true,
                });
                RebuildStepRows();
                PushStepsToRawScript();
            };
            toolbar.Children.Add(addPasteBtn);

            Button addTextBtn = CreateToolbarButton(L("macro.ui.addTextBtn", Texts.UiAddTextBtn));
            addTextBtn.Click += (_, _) =>
            {
                if (_steps.Count >= MacroScriptParser.MaxSteps) return;
                _steps.Add(new EditableStep
                {
                    Kind = MacroStepKind.Text,
                    Value = "",
                    CharDelayMs = MacroScriptParser.DefaultCharDelayMs,
                    Repeat = 1,
                    AutoOpenTextDialog = true,
                });
                RebuildStepRows();
                PushStepsToRawScript();
            };
            toolbar.Children.Add(addTextBtn);

            Button addWaitBtn = CreateToolbarButton(L("macro.ui.addWaitBtn", Texts.UiAddWaitBtn));
            addWaitBtn.Click += (_, _) =>
            {
                if (_steps.Count >= MacroScriptParser.MaxSteps) return;
                _steps.Add(new EditableStep
                {
                    Kind = MacroStepKind.Wait,
                    WaitMs = 100,
                });
                RebuildStepRows();
                PushStepsToRawScript();
            };
            toolbar.Children.Add(addWaitBtn);

            Button toggleSourceBtn = CreateToolbarButton(L("macro.ui.toggleSourceBtn", Texts.UiToggleSourceBtn));
            toggleSourceBtn.Click += (_, _) =>
            {
                _rawSourceVisible = !_rawSourceVisible;
                _scriptBox.Visibility = _rawSourceVisible ? Visibility.Visible : Visibility.Collapsed;
            };
            toolbar.Children.Add(toggleSourceBtn);

            outerStack.Children.Add(toolbar);

            // 4. Compact Timing & Loop bar
            if (_preDelayBox != null || _stepDelayBox != null || _repeatCountBox != null)
            {
                var timingBorder = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 6, 0, 0),
                    Background = FallbackCardBg,
                    BorderBrush = FallbackCardBorder,
                };
                timingBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
                timingBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

                var timingWrap = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                if (_preDelayBox != null)
                {
                    timingWrap.Children.Add(CreateCompactTimingGroup(
                        L("macro.ui.preDelayShort", Texts.UiPreDelayShort),
                        "ms",
                        L("macro.preDelayHelp", Texts.FieldPreDelayHelp),
                        _preDelayBox,
                        out _compactPreDelayBox));
                }

                if (_stepDelayBox != null)
                {
                    timingWrap.Children.Add(CreateCompactTimingGroup(
                        L("macro.ui.stepDelayShort", Texts.UiStepDelayShort),
                        "ms",
                        L("macro.stepDelayHelp", Texts.FieldStepDelayHelp),
                        _stepDelayBox,
                        out _compactStepDelayBox));
                }

                if (_repeatCountBox != null)
                {
                    timingWrap.Children.Add(CreateCompactTimingGroup(
                        L("macro.ui.repeatShort", Texts.UiRepeatShort),
                        "×",
                        L("macro.repeatCountHelp", Texts.FieldRepeatCountHelp),
                        _repeatCountBox,
                        out _compactRepeatBox));
                }

                timingBorder.Child = timingWrap;
                outerStack.Children.Add(timingBorder);
            }

            return new Border
            {
                Child = outerStack,
                Margin = new Thickness(0, 2, 0, 2),
            };
        }

        private UIElement CreateCompactTimingGroup(
            string label,
            string unit,
            string tooltip,
            TextBox underlyingBox,
            out TextBox compactBox)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 12, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip,
            };

            var lbl = new TextBlock
            {
                Text = label + ":",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            BindForeground(lbl, "TextSecondaryBrush", FallbackSecondaryText);
            panel.Children.Add(lbl);

            var box = new TextBox
            {
                Width = 44,
                Height = 24,
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0, 2, 0),
                Text = underlyingBox.Text,
            };
            ApplyInputStyle(box);
            box.TextChanged += (_, _) =>
            {
                if (_syncingFromRaw) return;
                _syncingFromVisual = true;
                try
                {
                    underlyingBox.Text = box.Text;
                }
                finally
                {
                    _syncingFromVisual = false;
                }
            };
            panel.Children.Add(box);

            var unitText = new TextBlock
            {
                Text = unit,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
            };
            BindForeground(unitText, "TextSecondaryBrush", FallbackSecondaryText);
            panel.Children.Add(unitText);

            compactBox = box;
            return panel;
        }

        private void OnUnderlyingTimingBoxChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingFromVisual) return;
            SyncCompactTimingFromUnderlying();
        }

        private void SyncCompactTimingFromUnderlying()
        {
            _syncingFromRaw = true;
            try
            {
                if (_compactPreDelayBox != null && _preDelayBox != null && _compactPreDelayBox.Text != _preDelayBox.Text)
                {
                    _compactPreDelayBox.Text = _preDelayBox.Text;
                }
                if (_compactStepDelayBox != null && _stepDelayBox != null && _compactStepDelayBox.Text != _stepDelayBox.Text)
                {
                    _compactStepDelayBox.Text = _stepDelayBox.Text;
                }
                if (_compactRepeatBox != null && _repeatCountBox != null && _compactRepeatBox.Text != _repeatCountBox.Text)
                {
                    _compactRepeatBox.Text = _repeatCountBox.Text;
                }
            }
            finally
            {
                _syncingFromRaw = false;
            }
        }

        private void OnRawScriptBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingFromVisual)
            {
                return;
            }

            LoadFromRawScript(_scriptBox.Text);
        }

        private void LoadFromRawScript(string? rawScript)
        {
            _syncingFromRaw = true;
            try
            {
                MacroParseResult parsed = MacroScriptParser.Parse(rawScript, _localize);
                if (parsed.IsValid && parsed.Steps.Count > 0)
                {
                    _steps.Clear();
                    foreach (MacroStep step in parsed.Steps)
                    {
                        _steps.Add(new EditableStep
                        {
                            Kind = step.Kind == MacroStepKind.Key ? MacroStepKind.Hotkey : step.Kind,
                            Value = step.Value,
                            WaitMs = step.WaitMs,
                            Repeat = step.Repeat,
                            CharDelayMs = step.CharDelayMs,
                        });
                    }
                    RebuildStepRows();
                }
                else if (string.IsNullOrWhiteSpace(rawScript) && _steps.Count == 0)
                {
                    _steps.Add(new EditableStep
                    {
                        Kind = MacroStepKind.Hotkey,
                        Value = "Alt+F",
                        Repeat = 1,
                    });
                    RebuildStepRows();
                }
            }
            finally
            {
                _syncingFromRaw = false;
            }
        }

        private void PushStepsToRawScript()
        {
            if (_syncingFromRaw)
            {
                return;
            }

            var macroSteps = new List<MacroStep>(_steps.Count);
            for (int i = 0; i < _steps.Count; i++)
            {
                macroSteps.Add(_steps[i].ToMacroStep(i + 1));
            }

            string formatted = MacroScriptParser.FormatSteps(macroSteps);
            _syncingFromVisual = true;
            try
            {
                if (!string.Equals(_scriptBox.Text, formatted, StringComparison.Ordinal))
                {
                    _scriptBox.Text = formatted;
                }
            }
            finally
            {
                _syncingFromVisual = false;
            }
        }

        private void RebuildStepRows()
        {
            if (_stepsHost == null)
            {
                return;
            }

            CancelStepExclusiveRecording();

            _stepsHost.Children.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                int index = i;
                EditableStep step = _steps[i];
                _stepsHost.Children.Add(BuildSingleStepRow(index, step));
            }
        }

        private UIElement BuildSingleStepRow(int index, EditableStep step)
        {
            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 0, 0, 5),
                Background = FallbackCardBg,
                BorderBrush = FallbackCardBorder,
            };
            rowBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            rowBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: Index badge
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) }); // 1: Step Kind ComboBox (wide enough for 粘贴指定文本 / 快捷键 / 按键)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: Value editor
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: Up/Down/Delete buttons

            // 0: Step Index Badge
            var badge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = FallbackBadgeBg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            if (badge.Child is TextBlock badgeText)
            {
                BindForeground(badgeText, "TextPrimaryBrush", FallbackPrimaryText);
            }
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            // 1: Step Kind ComboBox
            var kindCombo = new ComboBox
            {
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(6, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            ApplyInputStyle(kindCombo);

            MacroStepKind normalizedKind = step.Kind == MacroStepKind.Key ? MacroStepKind.Hotkey : step.Kind;
            var kindItems = new (MacroStepKind Kind, string Label)[]
            {
                (MacroStepKind.Hotkey, L("macro.ui.kindKey", Texts.UiKindKey)),
                (MacroStepKind.Paste, L("macro.ui.kindPaste", Texts.UiKindPaste)),
                (MacroStepKind.Text, L("macro.ui.kindText", Texts.UiKindText)),
                (MacroStepKind.Wait, L("macro.ui.kindWait", Texts.UiKindWait)),
                (MacroStepKind.Clipboard, L("macro.ui.kindCopy", Texts.UiKindCopy)),
            };

            int selectedKindIdx = 0;
            for (int k = 0; k < kindItems.Length; k++)
            {
                kindCombo.Items.Add(new ComboBoxItem
                {
                    Content = kindItems[k].Label,
                    Tag = kindItems[k].Kind,
                });
                if (kindItems[k].Kind == normalizedKind)
                {
                    selectedKindIdx = k;
                }
            }
            kindCombo.SelectedIndex = selectedKindIdx;
            kindCombo.SelectionChanged += (_, _) =>
            {
                if (kindCombo.SelectedItem is ComboBoxItem item && item.Tag is MacroStepKind newKind)
                {
                    step.Kind = newKind;
                    if (newKind == MacroStepKind.Wait && step.WaitMs <= 0)
                    {
                        step.WaitMs = 100;
                    }
                    else if (newKind == MacroStepKind.Hotkey &&
                             (string.IsNullOrWhiteSpace(step.Value) || !IsPlausibleKeyOrHotkey(step.Value)))
                    {
                        step.Value = "Enter";
                    }
                    else if ((newKind == MacroStepKind.Paste || newKind == MacroStepKind.Text || newKind == MacroStepKind.Clipboard) &&
                             string.IsNullOrEmpty(step.Value))
                    {
                        step.Value = "Text";
                    }
                    RebuildStepRows();
                    PushStepsToRawScript();
                }
            };
            Grid.SetColumn(kindCombo, 1);
            grid.Children.Add(kindCombo);

            // 2: Value Editor (by kind)
            UIElement valueEditor = normalizedKind switch
            {
                MacroStepKind.Wait => BuildWaitEditor(step),
                MacroStepKind.Hotkey => BuildKeyRecorderEditor(step),
                _ => BuildTextValueEditor(step),
            };
            Grid.SetColumn(valueEditor, 2);
            grid.Children.Add(valueEditor);

            // 3: Row action buttons (Up, Down, Delete)
            var actionBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };

            Button upBtn = CreateIconButton("↑", L("macro.ui.moveUpTip", Texts.UiMoveUpTip), index > 0);
            upBtn.Click += (_, _) =>
            {
                if (index <= 0 || index >= _steps.Count) return;
                (_steps[index - 1], _steps[index]) = (_steps[index], _steps[index - 1]);
                RebuildStepRows();
                PushStepsToRawScript();
            };
            actionBtns.Children.Add(upBtn);

            Button downBtn = CreateIconButton("↓", L("macro.ui.moveDownTip", Texts.UiMoveDownTip), index < _steps.Count - 1);
            downBtn.Click += (_, _) =>
            {
                if (index < 0 || index >= _steps.Count - 1) return;
                (_steps[index + 1], _steps[index]) = (_steps[index], _steps[index + 1]);
                RebuildStepRows();
                PushStepsToRawScript();
            };
            actionBtns.Children.Add(downBtn);

            Button delBtn = CreateIconButton("✕", L("macro.ui.deleteTip", Texts.UiDeleteTip), _steps.Count > 1);
            delBtn.Click += (_, _) =>
            {
                if (_steps.Count <= 1 || index < 0 || index >= _steps.Count) return;
                _steps.RemoveAt(index);
                RebuildStepRows();
                PushStepsToRawScript();
            };
            actionBtns.Children.Add(delBtn);

            Grid.SetColumn(actionBtns, 3);
            grid.Children.Add(actionBtns);

            rowBorder.Child = grid;
            return rowBorder;
        }

        /// <summary>
        /// 对齐主程序图 2 原生快捷热键录入体验：
        /// [原生风格热键录制框 (左对齐文本 + 右侧 × 清除按钮)] + [⏸️ 暂停全局热键] + [⚙️ 拼装组合] + [×N 重复次数]
        /// </summary>
        private UIElement BuildKeyRecorderEditor(EditableStep step)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0: Native-style HotkeyRecorderBox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: ⏸️ 暂停全局热键
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: ⚙️ 拼装组合
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: ×N 重复按钮

            // 0: Native-style HotkeyRecorderBox (Border + Grid with left TextBlock and right '×' clear Button)
            var recorderBorder = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                Background = FallbackInputBg,
                BorderBrush = FallbackInputBorder,
                Cursor = Cursors.Hand,
                Focusable = true,
                FocusVisualStyle = null,
                ToolTip = L("macro.ui.header", Texts.UiHeader),
            };
            recorderBorder.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
            recorderBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            KeyboardNavigation.SetTabNavigation(recorderBorder, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(recorderBorder, KeyboardNavigationMode.None);

            var innerGrid = new Grid();
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var displayTextBlock = new TextBlock
            {
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(displayTextBlock, 0);
            innerGrid.Children.Add(displayTextBlock);

            var clearBtn = new Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                FontSize = 13,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = L("macro.ui.clearKeyTip", Texts.UiClearKeyTip),
            };
            clearBtn.SetResourceReference(Control.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(clearBtn, 1);
            innerGrid.Children.Add(clearBtn);

            recorderBorder.Child = innerGrid;

            var pauseHotkeysBtn = new Button
            {
                Content = L("macro.ui.pauseHotkeysBtn", Texts.UiPauseHotkeysBtn),
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = L("macro.ui.pauseHotkeysTip", Texts.UiPauseHotkeysTip),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(pauseHotkeysBtn);

            bool isRecording = false;
            bool isExclusive = false;
            string? customPrompt = null;
            var sessionModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void RefreshRecorderVisual()
            {
                if (isRecording)
                {
                    displayTextBlock.Text = !string.IsNullOrEmpty(customPrompt)
                        ? customPrompt
                        : L("macro.ui.recordPrompt", Texts.UiRecordPrompt);
                    displayTextBlock.Foreground = customPrompt != null && customPrompt.EndsWith("...", StringComparison.Ordinal) && !customPrompt.StartsWith("🔴", StringComparison.Ordinal)
                        ? FallbackAccentBrush
                        : FallbackRecordingFg;
                    recorderBorder.BorderBrush = FallbackAccentBrush;
                    recorderBorder.Background = FallbackRecordingBg;
                    clearBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    customPrompt = null;
                    if (string.IsNullOrWhiteSpace(step.Value))
                    {
                        displayTextBlock.Text = L("macro.ui.recordEmpty", Texts.UiRecordEmpty);
                        BindForeground(displayTextBlock, "TextMutedBrush", FallbackMutedText);
                        clearBtn.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        displayTextBlock.Text = step.Value;
                        BindForeground(displayTextBlock, "TextPrimaryBrush", FallbackPrimaryText);
                        clearBtn.Visibility = Visibility.Visible;
                    }
                    recorderBorder.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
                    recorderBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
                }

                if (isExclusive)
                {
                    pauseHotkeysBtn.Content = L("macro.ui.resumeHotkeysBtn", Texts.UiResumeHotkeysBtn);
                    pauseHotkeysBtn.Foreground = FallbackRecordingFg;
                    pauseHotkeysBtn.BorderBrush = FallbackRecordingFg;
                }
                else
                {
                    pauseHotkeysBtn.Content = L("macro.ui.pauseHotkeysBtn", Texts.UiPauseHotkeysBtn);
                    pauseHotkeysBtn.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                    ApplyButtonStyle(pauseHotkeysBtn);
                }
            }

            void EnterNormalRecording()
            {
                isRecording = true;
                sessionModifiers.Clear();
                customPrompt = isExclusive
                    ? L("macro.ui.exclusiveRecordPrompt", Texts.UiExclusiveRecordPrompt)
                    : L("macro.ui.recordPrompt", Texts.UiRecordPrompt);
                RefreshRecorderVisual();
                recorderBorder.Focus();
                Keyboard.Focus(recorderBorder);
            }

            void ExitRecording(bool cancelExclusive = true)
            {
                if (cancelExclusive && isExclusive)
                {
                    isExclusive = false;
                    CancelStepExclusiveRecording();
                }
                isRecording = false;
                sessionModifiers.Clear();
                customPrompt = null;
                RefreshRecorderVisual();
            }

            void CommitOrderedModifiersIfAny()
            {
                var mods = new List<string>(4);
                if (sessionModifiers.Contains("Ctrl")) mods.Add("Ctrl");
                if (sessionModifiers.Contains("Shift")) mods.Add("Shift");
                if (sessionModifiers.Contains("Alt")) mods.Add("Alt");
                if (sessionModifiers.Contains("Win")) mods.Add("Win");
                if (mods.Count > 0)
                {
                    step.Value = string.Join(" + ", mods);
                    PushStepsToRawScript();
                }
            }

            clearBtn.Click += (s, e) =>
            {
                e.Handled = true;
                step.Value = "";
                EnterNormalRecording();
            };

            recorderBorder.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (!isRecording)
                {
                    EnterNormalRecording();
                }
                else
                {
                    CommitOrderedModifiersIfAny();
                    ExitRecording();
                    Keyboard.ClearFocus();
                }
            };

            recorderBorder.LostKeyboardFocus += (_, _) =>
            {
                if (isRecording && !isExclusive)
                {
                    CommitOrderedModifiersIfAny();
                    ExitRecording(cancelExclusive: false);
                }
            };

            recorderBorder.PreviewKeyDown += (_, e) =>
            {
                if (!isRecording)
                {
                    return;
                }

                e.Handled = true;
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                if (actualKey == Key.ImeProcessed) actualKey = e.ImeProcessedKey;

                // Bare Esc cancels recording when no modifier is held
                if (actualKey == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && sessionModifiers.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(step.Value))
                    {
                        ExitRecording();
                        Keyboard.ClearFocus();
                        return;
                    }
                    // If step.Value was cleared via ×, pressing Esc records "Esc"
                    step.Value = "Esc";
                    ExitRecording();
                    Keyboard.ClearFocus();
                    PushStepsToRawScript();
                    return;
                }

                if (IsModifierKey(actualKey, out string modName))
                {
                    sessionModifiers.Add(modName);
                    var heldList = GetOrderedModifierNames(sessionModifiers, Keyboard.Modifiers);
                    customPrompt = string.Join(" + ", heldList) + " + ...";
                    RefreshRecorderVisual();
                    return;
                }

                string? mainToken = MapWpfKeyToStarPieToken(actualKey);
                if (string.IsNullOrEmpty(mainToken))
                {
                    return;
                }

                var finalParts = GetOrderedModifierNames(sessionModifiers, Keyboard.Modifiers);
                finalParts.Add(mainToken);
                step.Value = string.Join(" + ", finalParts);
                ExitRecording();
                Keyboard.ClearFocus();
                PushStepsToRawScript();
            };

            recorderBorder.PreviewKeyUp += (_, e) =>
            {
                if (!isRecording)
                {
                    return;
                }

                e.Handled = true;
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                if (IsModifierKey(actualKey, out string _) && sessionModifiers.Count > 0)
                {
                    bool anyModStillDown =
                        Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                        Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ||
                        Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt) ||
                        Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

                    if (!anyModStillDown)
                    {
                        CommitOrderedModifiersIfAny();
                        ExitRecording();
                        Keyboard.ClearFocus();
                    }
                }
            };

            // 1: ⏸️ 暂停全局热键 (Exclusive recording via host's MainKeyboardHook)
            pauseHotkeysBtn.Click += (_, _) =>
            {
                if (isExclusive)
                {
                    ExitRecording(cancelExclusive: true);
                }
                else
                {
                    isExclusive = true;
                    EnterNormalRecording();
                    StartStepExclusiveRecording(
                        step,
                        onModifierLive: liveText =>
                        {
                            customPrompt = liveText;
                            RefreshRecorderVisual();
                        },
                        onCompleted: recordedHotkey =>
                        {
                            step.Value = recordedHotkey;
                            isExclusive = false;
                            ExitRecording(cancelExclusive: false);
                            PushStepsToRawScript();
                        },
                        onCancelled: () =>
                        {
                            isExclusive = false;
                            ExitRecording(cancelExclusive: false);
                        });
                }
            };

            // 2: ⚙️ 拼装组合 (Open host's HotkeyBuilderDialog or fallback builder dialog)
            var buildComboBtn = new Button
            {
                Content = L("macro.ui.buildHotkeyBtn", Texts.UiBuildHotkeyBtn),
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = L("macro.ui.buildHotkeyTip", Texts.UiBuildHotkeyTip),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(buildComboBtn);
            buildComboBtn.Click += (_, _) =>
            {
                ExitRecording(cancelExclusive: true);
                Window? owner = Window.GetWindow(_scriptContainer);
                string? built = ShowHotkeyBuilderDialog(owner, step.Value, L);
                if (!string.IsNullOrWhiteSpace(built))
                {
                    step.Value = built.Trim();
                    RefreshRecorderVisual();
                    PushStepsToRawScript();
                }
            };

            // 3: Repeat Selector Button (×1 ~ ×20, never clips text, supports click menu & mouse wheel)
            int currentRepeat = Math.Clamp(step.Repeat, 1, MacroScriptParser.MaxSingleRepeat);
            var repeatBtn = new Button
            {
                Content = $"×{currentRepeat}",
                MinWidth = 38,
                Height = 28,
                FontSize = 11,
                FontWeight = currentRepeat > 1 ? FontWeights.SemiBold : FontWeights.Normal,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = L("macro.ui.repeatTip", Texts.UiRepeatTip),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(repeatBtn);

            void UpdateRepeatValue(int newRepeat)
            {
                int clamped = Math.Clamp(newRepeat, 1, MacroScriptParser.MaxSingleRepeat);
                step.Repeat = clamped;
                repeatBtn.Content = $"×{clamped}";
                repeatBtn.FontWeight = clamped > 1 ? FontWeights.SemiBold : FontWeights.Normal;
                PushStepsToRawScript();
            }

            repeatBtn.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                int[] repeatChoices = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20 };
                foreach (int choice in repeatChoices)
                {
                    int captured = choice;
                    var item = new MenuItem
                    {
                        Header = captured == 1 ? "×1 (单次执行)" : $"×{captured} (连续按 {captured} 次)",
                        IsChecked = step.Repeat == captured,
                    };
                    item.Click += (_, _) => UpdateRepeatValue(captured);
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = repeatBtn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            repeatBtn.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                int delta = e.Delta > 0 ? 1 : -1;
                UpdateRepeatValue(step.Repeat + delta);
            };

            RefreshRecorderVisual();

            if (step.AutoStartRecording)
            {
                step.AutoStartRecording = false;
                recorderBorder.Loaded += (_, _) => EnterNormalRecording();
            }

            Grid.SetColumn(recorderBorder, 0);
            grid.Children.Add(recorderBorder);

            Grid.SetColumn(pauseHotkeysBtn, 1);
            grid.Children.Add(pauseHotkeysBtn);

            Grid.SetColumn(buildComboBtn, 2);
            grid.Children.Add(buildComboBtn);

            Grid.SetColumn(repeatBtn, 3);
            grid.Children.Add(repeatBtn);

            return grid;
        }

        /// <summary>
        /// 粘贴指定文本 / 逐字输入 / 复制指定文本 行内编辑器 + 「⚙️ 配置文本」多行弹窗按钮
        /// （逐字输入额外提供行内「字间隔 [35] ms」设置）
        /// </summary>
        private UIElement BuildTextValueEditor(EditableStep step)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 0: Text preview/edit box
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: CharDelayMs (for Text kind)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: ⚙️ 配置文本 button

            bool updatingInlineBox = false;
            var box = new TextBox
            {
                Height = 28,
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
            };
            ApplyInputStyle(box);

            void RefreshInlineBoxFromStep()
            {
                updatingInlineBox = true;
                try
                {
                    string raw = step.Value ?? "";
                    bool isMultiline = raw.Contains('\n') || raw.Contains('\r');
                    box.IsReadOnly = isMultiline;
                    box.Text = isMultiline
                        ? raw.Replace("\r\n", " ↵ ", StringComparison.Ordinal).Replace("\n", " ↵ ", StringComparison.Ordinal).Replace("\r", " ↵ ", StringComparison.Ordinal)
                        : raw;
                    box.ToolTip = string.IsNullOrEmpty(raw)
                        ? L("macro.ui.textPlaceholder", Texts.UiTextPlaceholder)
                        : raw;
                }
                finally
                {
                    updatingInlineBox = false;
                }
            }

            RefreshInlineBoxFromStep();

            box.TextChanged += (_, _) =>
            {
                if (updatingInlineBox || box.IsReadOnly) return;
                step.Value = box.Text ?? "";
                box.ToolTip = string.IsNullOrEmpty(step.Value)
                    ? L("macro.ui.textPlaceholder", Texts.UiTextPlaceholder)
                    : step.Value;
                PushStepsToRawScript();
            };

            TextBox? charDelayInlineBox = null;

            // 1: Per-character delay input (only for MacroStepKind.Text "🔤 逐字输入")
            if (step.Kind == MacroStepKind.Text)
            {
                var charDelayPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0),
                    ToolTip = L("macro.ui.charDelayTip", Texts.UiCharDelayTip),
                };

                var charDelayLbl = new TextBlock
                {
                    Text = L("macro.ui.charDelayLabel", Texts.UiCharDelayLabel) + ":",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0),
                };
                BindForeground(charDelayLbl, "TextSecondaryBrush", FallbackSecondaryText);
                charDelayPanel.Children.Add(charDelayLbl);

                charDelayInlineBox = new TextBox
                {
                    Width = 40,
                    Height = 26,
                    FontSize = 11,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(2, 0, 2, 0),
                    Text = step.CharDelayMs.ToString(CultureInfo.InvariantCulture),
                };
                ApplyInputStyle(charDelayInlineBox);
                charDelayInlineBox.TextChanged += (_, _) =>
                {
                    if (int.TryParse(charDelayInlineBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cd) &&
                        cd >= 0 && cd <= MacroScriptParser.MaxCharDelayMs)
                    {
                        step.CharDelayMs = cd;
                        PushStepsToRawScript();
                    }
                };
                charDelayPanel.Children.Add(charDelayInlineBox);

                var msLbl = new TextBlock
                {
                    Text = "ms",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 0, 0),
                };
                BindForeground(msLbl, "TextSecondaryBrush", FallbackSecondaryText);
                charDelayPanel.Children.Add(msLbl);

                Grid.SetColumn(charDelayPanel, 1);
                grid.Children.Add(charDelayPanel);
            }

            // 2: ⚙️ 配置文本 button (opens spacious multi-line text editor dialog)
            var configBtn = new Button
            {
                Content = L("macro.ui.configTextBtn", Texts.UiConfigTextBtn),
                Height = 28,
                FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = L("macro.ui.configTextTip", Texts.UiConfigTextTip),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(configBtn);

            void OpenTextConfigModal()
            {
                Window? owner = Window.GetWindow(_scriptContainer);
                if (ShowTextConfigDialog(
                    owner,
                    step.Kind,
                    step.Value,
                    step.CharDelayMs,
                    L,
                    out string newText,
                    out int newCharDelayMs))
                {
                    step.Value = string.IsNullOrEmpty(newText) ? "Text" : newText;
                    if (step.Kind == MacroStepKind.Text)
                    {
                        step.CharDelayMs = newCharDelayMs;
                        if (charDelayInlineBox != null)
                        {
                            charDelayInlineBox.Text = newCharDelayMs.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    RefreshInlineBoxFromStep();
                    PushStepsToRawScript();
                }
                else if (string.IsNullOrEmpty(step.Value))
                {
                    step.Value = "Text";
                    RefreshInlineBoxFromStep();
                    PushStepsToRawScript();
                }
            }

            configBtn.Click += (_, _) => OpenTextConfigModal();
            box.MouseDoubleClick += (_, e) =>
            {
                e.Handled = true;
                OpenTextConfigModal();
            };

            if (step.AutoOpenTextDialog)
            {
                step.AutoOpenTextDialog = false;
                configBtn.Loaded += (_, _) =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(OpenTextConfigModal));
                };
            }

            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            Grid.SetColumn(configBtn, 2);
            grid.Children.Add(configBtn);

            return grid;
        }

        private UIElement BuildWaitEditor(EditableStep step)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var msBox = new TextBox
            {
                Height = 28,
                FontSize = 11.5,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = step.WaitMs.ToString(CultureInfo.InvariantCulture),
            };
            ApplyInputStyle(msBox);

            msBox.TextChanged += (_, _) =>
            {
                if (int.TryParse(msBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) &&
                    ms >= 0 && ms <= MacroScriptParser.MaxSingleWaitMs)
                {
                    step.WaitMs = ms;
                    PushStepsToRawScript();
                }
            };
            Grid.SetColumn(msBox, 0);
            grid.Children.Add(msBox);

            var unitLabel = new TextBlock
            {
                Text = "ms",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0),
            };
            BindForeground(unitLabel, "TextSecondaryBrush", FallbackSecondaryText);
            Grid.SetColumn(unitLabel, 1);
            grid.Children.Add(unitLabel);

            var presetsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            int[] presets = { 50, 100, 200, 500, 1000 };
            foreach (int preset in presets)
            {
                int captured = preset;
                var pBtn = new Button
                {
                    Content = $"{captured}",
                    Height = 24,
                    FontSize = 10.5,
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                };
                ApplyButtonStyle(pBtn);
                pBtn.Click += (_, _) =>
                {
                    step.WaitMs = captured;
                    msBox.Text = captured.ToString(CultureInfo.InvariantCulture);
                    PushStepsToRawScript();
                };
                presetsPanel.Children.Add(pBtn);
            }

            Grid.SetColumn(presetsPanel, 2);
            grid.Children.Add(presetsPanel);

            return grid;
        }

        private void StartStepExclusiveRecording(
            EditableStep step,
            Action<string> onModifierLive,
            Action<string> onCompleted,
            Action onCancelled)
        {
            CancelStepExclusiveRecording();

            try
            {
                object? hook = Application.Current?.GetType()
                    .GetProperty("MainKeyboardHook", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);

                if (hook == null)
                {
                    return;
                }

                Type hookType = hook.GetType();
                EventInfo? completedEvt = hookType.GetEvent("OnExclusiveRecordCompleted");
                EventInfo? cancelledEvt = hookType.GetEvent("OnExclusiveRecordCancelled");
                EventInfo? modifiersEvt = hookType.GetEvent("OnExclusiveRecordModifiersChanged");
                MethodInfo? startMethod = hookType.GetMethod("StartExclusiveRecording", Type.EmptyTypes);

                if (completedEvt == null || cancelledEvt == null || startMethod == null)
                {
                    return;
                }

                _subscribedHookInstance = hook;
                _exclusiveRecordingStep = step;
                _exclusiveVisualUpdate = onModifierLive;
                _exclusiveVisualExit = onCancelled;

                Action<string> completedAction = hotkeyStr =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (!ReferenceEquals(_exclusiveRecordingStep, step)) return;
                        CleanupExclusiveHook();
                        onCompleted(hotkeyStr);
                    }));
                };

                Action cancelledAction = () =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (!ReferenceEquals(_exclusiveRecordingStep, step)) return;
                        CleanupExclusiveHook();
                        onCancelled();
                    }));
                };

                Action<ModifierKeys> modifiersAction = mods =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (!ReferenceEquals(_exclusiveRecordingStep, step)) return;
                        var modNames = GetOrderedModifierNames(null, mods);
                        string liveText = modNames.Count > 0
                            ? "🔴 " + string.Join(" + ", modNames) + " + ..."
                            : L("macro.ui.exclusiveRecordPrompt", Texts.UiExclusiveRecordPrompt);
                        onModifierLive(liveText);
                    }));
                };

                _completedHandler = completedAction;
                _cancelledHandler = cancelledAction;
                _modifiersHandler = modifiersAction;

                completedEvt.AddEventHandler(hook, _completedHandler);
                cancelledEvt.AddEventHandler(hook, _cancelledHandler);
                modifiersEvt?.AddEventHandler(hook, _modifiersHandler);

                startMethod.Invoke(hook, null);
            }
            catch
            {
                CleanupExclusiveHook();
            }
        }

        private void CancelStepExclusiveRecording()
        {
            Action? exitCb = _exclusiveVisualExit;
            CleanupExclusiveHook();
            try
            {
                exitCb?.Invoke();
            }
            catch
            {
            }
        }

        private static Button CreateToolbarButton(string text)
        {
            var btn = new Button
            {
                Content = text,
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(btn);
            return btn;
        }

        private static Button CreateIconButton(string symbol, string tooltip, bool enabled)
        {
            var btn = new Button
            {
                Content = symbol,
                Width = 22,
                Height = 26,
                FontSize = 10.5,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                IsEnabled = enabled,
                ToolTip = tooltip,
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(btn);
            return btn;
        }

        private static void ApplyButtonStyle(Button button)
        {
            if (Application.Current?.TryFindResource("ModernButtonStyle") is Style style)
            {
                button.Style = style;
            }
        }

        private static void ApplyPrimaryButtonStyle(Button button)
        {
            if (Application.Current?.TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
            {
                button.Style = primaryStyle;
            }
            else
            {
                ApplyButtonStyle(button);
                button.Background = FallbackAccentBrush;
                button.Foreground = FallbackWhiteBrush;
                button.FontWeight = FontWeights.SemiBold;
            }
        }

        private static void ApplyInputStyle(Control control)
        {
            control.Background = FallbackInputBg;
            control.Foreground = FallbackPrimaryText;
            control.BorderBrush = FallbackInputBorder;
            control.SetResourceReference(Control.BackgroundProperty, "InputBackgroundBrush");
            control.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            control.SetResourceReference(Control.BorderBrushProperty, "InputBorderBrush");
        }

        private static void BindForeground(TextBlock block, string resourceKey, Brush fallback)
        {
            block.Foreground = fallback;
            block.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);
        }

        private static bool IsPlausibleKeyOrHotkey(string value)
        {
            MacroParseResult res = MacroScriptParser.Parse("HOTKEY: " + value);
            return res.IsValid;
        }

        private static void ApplyHostThemeToWindow(Window window)
        {
            window.Background = FallbackWindowBg;
            window.Foreground = FallbackPrimaryText;
            window.SetResourceReference(Control.BackgroundProperty, "WindowBackgroundBrush");
            window.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");

            try
            {
                Assembly? hostAsm = Application.Current?.GetType().Assembly;
                if (hostAsm == null) return;

                Type? configMgrType = hostAsm.GetType("WinPieGestures.ConfigManager");
                object? currentConfig = configMgrType?.GetProperty("CurrentConfig", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                string theme = currentConfig?.GetType().GetProperty("AppTheme")?.GetValue(currentConfig) as string ?? "System";

                Type? themeMgrType = hostAsm.GetType("WinPieGestures.AppThemeManager");
                MethodInfo? applyMethod = themeMgrType?.GetMethod("ApplyTheme", new[] { typeof(Window), typeof(string) });
                applyMethod?.Invoke(null, new object[] { window, theme });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 优先调用主程序原生的 HotkeyBuilderDialog（与主程序「⚙️ 拼装组合」100% 同源）；
        /// 若宿主版本不存在该类型，则自动回退到内置拼装窗口。
        /// </summary>
        private static string? ShowHotkeyBuilderDialog(
            Window? owner,
            string initialHotkey,
            Func<string, string, string> L)
        {
            try
            {
                Assembly? hostAsm = Application.Current?.GetType().Assembly;
                Type? dlgType = hostAsm?.GetType("WinPieGestures.HotkeyBuilderDialog");
                if (dlgType != null &&
                    Activator.CreateInstance(dlgType, new object[] { initialHotkey ?? "" }) is Window hostDlg)
                {
                    if (owner != null)
                    {
                        hostDlg.Owner = owner;
                    }
                    if (hostDlg.ShowDialog() == true)
                    {
                        string? result = dlgType.GetProperty("ResultHotkey")?.GetValue(hostDlg) as string;
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            return result.Trim();
                        }
                    }
                    return null;
                }
            }
            catch
            {
            }

            return ShowFallbackHotkeyBuilderDialog(owner, initialHotkey ?? string.Empty, L);
        }

        private static string? ShowFallbackHotkeyBuilderDialog(
            Window? owner,
            string initialHotkey,
            Func<string, string, string> L)
        {
            var win = new Window
            {
                Title = L("macro.ui.buildHotkeyTip", Texts.UiBuildHotkeyTip),
                Width = 480,
                Height = 420,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
            };
            ApplyHostThemeToWindow(win);

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "⌨️ " + L("macro.ui.buildHotkeyTip", Texts.UiBuildHotkeyTip),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
            };
            BindForeground(title, "TextPrimaryBrush", FallbackPrimaryText);
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var bodyStack = new StackPanel();

            var ctrlCheck = new CheckBox { Content = "Ctrl", Margin = new Thickness(0, 0, 16, 8) };
            var shiftCheck = new CheckBox { Content = "Shift", Margin = new Thickness(0, 0, 16, 8) };
            var altCheck = new CheckBox { Content = "Alt", Margin = new Thickness(0, 0, 16, 8) };
            var winCheck = new CheckBox { Content = "Win", Margin = new Thickness(0, 0, 16, 8) };

            var modRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            modRow.Children.Add(ctrlCheck);
            modRow.Children.Add(shiftCheck);
            modRow.Children.Add(altCheck);
            modRow.Children.Add(winCheck);
            bodyStack.Children.Add(modRow);

            var keyCombo = new ComboBox { Height = 30, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
            ApplyInputStyle(keyCombo);
            string[] commonKeys =
            {
                "Enter", "Esc", "Tab", "Space", "Backspace", "Delete", "Insert",
                "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown",
                "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
                "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
                "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
                "PrintScreen", "Pause", "CapsLock", "VolumeUp", "VolumeDown", "VolumeMute",
            };
            foreach (string k in commonKeys)
            {
                keyCombo.Items.Add(k);
            }
            keyCombo.SelectedIndex = 0;
            bodyStack.Children.Add(keyCombo);

            var customBox = new TextBox
            {
                Height = 30,
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                Text = string.IsNullOrWhiteSpace(initialHotkey) ? "Enter" : initialHotkey,
            };
            ApplyInputStyle(customBox);
            bodyStack.Children.Add(customBox);

            void SyncToBox()
            {
                var parts = new List<string>();
                if (ctrlCheck.IsChecked == true) parts.Add("Ctrl");
                if (shiftCheck.IsChecked == true) parts.Add("Shift");
                if (altCheck.IsChecked == true) parts.Add("Alt");
                if (winCheck.IsChecked == true) parts.Add("Win");
                if (keyCombo.SelectedItem is string mk && !string.IsNullOrEmpty(mk))
                {
                    parts.Add(mk);
                }
                if (parts.Count > 0)
                {
                    customBox.Text = string.Join(" + ", parts);
                }
            }

            ctrlCheck.Checked += (_, _) => SyncToBox();
            ctrlCheck.Unchecked += (_, _) => SyncToBox();
            shiftCheck.Checked += (_, _) => SyncToBox();
            shiftCheck.Unchecked += (_, _) => SyncToBox();
            altCheck.Checked += (_, _) => SyncToBox();
            altCheck.Unchecked += (_, _) => SyncToBox();
            winCheck.Checked += (_, _) => SyncToBox();
            winCheck.Unchecked += (_, _) => SyncToBox();
            keyCombo.SelectionChanged += (_, _) => SyncToBox();

            Grid.SetRow(bodyStack, 1);
            root.Children.Add(bodyStack);

            var bottomRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            ApplyButtonStyle(cancelBtn);
            var okBtn = new Button { Content = "确定应用", Width = 90, Height = 30, IsDefault = true };
            ApplyPrimaryButtonStyle(okBtn);
            okBtn.Click += (_, _) => win.DialogResult = true;
            bottomRow.Children.Add(cancelBtn);
            bottomRow.Children.Add(okBtn);
            Grid.SetRow(bottomRow, 2);
            root.Children.Add(bottomRow);

            win.Content = root;
            return win.ShowDialog() == true ? customBox.Text : null;
        }

        /// <summary>
        /// 弹出多行文本输入与打字配置窗口（支持粘贴指定文本、逐字输入文本、复制指定文本）。
        /// </summary>
        private static bool ShowTextConfigDialog(
            Window? owner,
            MacroStepKind kind,
            string initialText,
            int initialCharDelayMs,
            Func<string, string, string> L,
            out string resultText,
            out int resultCharDelayMs)
        {
            resultText = initialText ?? "";
            resultCharDelayMs = initialCharDelayMs;

            (string titleText, string subtitleText) = kind switch
            {
                MacroStepKind.Paste => (
                    "📋 粘贴指定文本 — 多行内容配置",
                    "执行此步骤时，将自动把下方文本写入剪贴板并发送 Ctrl+V 粘贴到目标窗口（适合大段文字、多行模板与中文内容）。"),
                MacroStepKind.Text => (
                    "🔤 逐字输入 — 多行内容与打字速度配置",
                    "执行此步骤时，将按照设定的「字间打字间隔」逐个字符依次模拟输入（换行将自动触发 Enter 键，Tab 触发 Tab 键，真正还原人工逐字打字效果）。"),
                MacroStepKind.Clipboard => (
                    "📎 复制指定文本 — 多行内容配置",
                    "执行此步骤时，将下方指定文本直接写入系统剪贴板（不自动发送粘贴按键，便于后续手动粘贴或配合其他步骤）。"),
                _ => ("配置文本内容", "输入步骤文本内容"),
            };

            var win = new Window
            {
                Title = titleText,
                Width = 540,
                Height = kind == MacroStepKind.Text ? 460 : 400,
                MinWidth = 420,
                MinHeight = 320,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResize,
            };
            ApplyHostThemeToWindow(win);

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Multi-line TextBox
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Optional CharDelay card (for Text kind)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Footer bar

            // 0: Header
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var headerTitle = new TextBlock
            {
                Text = titleText,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
            };
            BindForeground(headerTitle, "TextPrimaryBrush", FallbackPrimaryText);
            headerPanel.Children.Add(headerTitle);

            var headerSub = new TextBlock
            {
                Text = subtitleText,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            BindForeground(headerSub, "TextSecondaryBrush", FallbackSecondaryText);
            headerPanel.Children.Add(headerSub);
            Grid.SetRow(headerPanel, 0);
            root.Children.Add(headerPanel);

            // 1: Multi-line TextBox
            var editorBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontSize = 12.5,
                Padding = new Thickness(10, 8, 10, 8),
                VerticalContentAlignment = VerticalAlignment.Top,
                Text = initialText ?? "",
            };
            ApplyInputStyle(editorBox);
            Grid.SetRow(editorBox, 1);
            root.Children.Add(editorBox);

            // 2: Per-character typing speed card (only for MacroStepKind.Text "🔤 逐字输入")
            int chosenCharDelay = Math.Clamp(initialCharDelayMs, 0, MacroScriptParser.MaxCharDelayMs);
            TextBox? dialogCharDelayBox = null;

            if (kind == MacroStepKind.Text)
            {
                var speedCard = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 10, 0, 0),
                    Background = FallbackCardBg,
                    BorderBrush = FallbackCardBorder,
                };
                speedCard.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
                speedCard.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

                var speedWrap = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var speedLbl = new TextBlock
                {
                    Text = "⏱️ 每字打字间隔:",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                BindForeground(speedLbl, "TextPrimaryBrush", FallbackPrimaryText);
                speedWrap.Children.Add(speedLbl);

                dialogCharDelayBox = new TextBox
                {
                    Width = 54,
                    Height = 26,
                    FontSize = 11.5,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Text = chosenCharDelay.ToString(CultureInfo.InvariantCulture),
                };
                ApplyInputStyle(dialogCharDelayBox);
                dialogCharDelayBox.TextChanged += (_, _) =>
                {
                    if (int.TryParse(dialogCharDelayBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) &&
                        v >= 0 && v <= MacroScriptParser.MaxCharDelayMs)
                    {
                        chosenCharDelay = v;
                    }
                };
                speedWrap.Children.Add(dialogCharDelayBox);

                var msText = new TextBlock
                {
                    Text = "ms / 字",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 12, 0),
                };
                BindForeground(msText, "TextSecondaryBrush", FallbackSecondaryText);
                speedWrap.Children.Add(msText);

                (string Label, int Ms)[] speedPresets =
                {
                    ("⚡ 极速 15ms", 15),
                    ("✨ 自然 35ms", 35),
                    ("⌨️ 适中 60ms", 60),
                    ("🐢 慢速 120ms", 120),
                };

                foreach (var (presetLabel, presetMs) in speedPresets)
                {
                    int capturedMs = presetMs;
                    var pBtn = new Button
                    {
                        Content = presetLabel,
                        Height = 24,
                        FontSize = 10.5,
                        Padding = new Thickness(7, 0, 7, 0),
                        Margin = new Thickness(0, 2, 4, 2),
                        Cursor = Cursors.Hand,
                    };
                    ApplyButtonStyle(pBtn);
                    pBtn.Click += (_, _) =>
                    {
                        chosenCharDelay = capturedMs;
                        if (dialogCharDelayBox != null)
                        {
                            dialogCharDelayBox.Text = capturedMs.ToString(CultureInfo.InvariantCulture);
                        }
                    };
                    speedWrap.Children.Add(pBtn);
                }

                speedCard.Child = speedWrap;
                Grid.SetRow(speedCard, 2);
                root.Children.Add(speedCard);
            }

            // 3: Footer bar (char/line stats + quick actions + Cancel / OK)
            var footerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftTools = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var statsText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            BindForeground(statsText, "TextSecondaryBrush", FallbackSecondaryText);

            void UpdateStats()
            {
                string t = editorBox.Text ?? "";
                int charCount = t.Replace("\r", "", StringComparison.Ordinal).Length;
                int lineCount = string.IsNullOrEmpty(t) ? 0 : t.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                statsText.Text = $"共 {charCount} 字符 · {lineCount} 行";
            }

            UpdateStats();
            editorBox.TextChanged += (_, _) => UpdateStats();
            leftTools.Children.Add(statsText);

            var pasteClipBtn = new Button
            {
                Content = "📋 从剪贴板填入",
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(pasteClipBtn);
            pasteClipBtn.Click += (_, _) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(clip))
                        {
                            int caret = editorBox.CaretIndex;
                            editorBox.SelectedText = clip;
                            editorBox.CaretIndex = caret + clip.Length;
                            editorBox.Focus();
                        }
                    }
                }
                catch
                {
                }
            };
            leftTools.Children.Add(pasteClipBtn);

            var clearAllBtn = new Button
            {
                Content = "🗑️ 清空",
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(clearAllBtn);
            clearAllBtn.Click += (_, _) =>
            {
                editorBox.Text = "";
                editorBox.Focus();
            };
            leftTools.Children.Add(clearAllBtn);

            Grid.SetColumn(leftTools, 0);
            footerGrid.Children.Add(leftTools);

            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var cancelBtn = new Button
            {
                Content = "取消",
                Width = 76,
                Height = 30,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true,
                Cursor = Cursors.Hand,
            };
            ApplyButtonStyle(cancelBtn);

            var okBtn = new Button
            {
                Content = "确定保存",
                Width = 88,
                Height = 30,
                FontSize = 11.5,
                IsDefault = false, // Enter inside multi-line TextBox inserts newline
                Cursor = Cursors.Hand,
            };
            ApplyPrimaryButtonStyle(okBtn);
            okBtn.Click += (_, _) => win.DialogResult = true;

            rightButtons.Children.Add(cancelBtn);
            rightButtons.Children.Add(okBtn);
            Grid.SetColumn(rightButtons, 1);
            footerGrid.Children.Add(rightButtons);

            Grid.SetRow(footerGrid, 3);
            root.Children.Add(footerGrid);

            win.Content = root;
            win.Loaded += (_, _) =>
            {
                editorBox.Focus();
                editorBox.CaretIndex = editorBox.Text.Length;
            };

            if (win.ShowDialog() == true)
            {
                resultText = editorBox.Text ?? "";
                resultCharDelayMs = chosenCharDelay;
                return true;
            }

            return false;
        }
    }

    private static bool IsModifierKey(Key key, out string modifierName)
    {
        switch (key)
        {
            case Key.LeftCtrl:
            case Key.RightCtrl:
                modifierName = "Ctrl";
                return true;
            case Key.LeftShift:
            case Key.RightShift:
                modifierName = "Shift";
                return true;
            case Key.LeftAlt:
            case Key.RightAlt:
                modifierName = "Alt";
                return true;
            case Key.LWin:
            case Key.RWin:
                modifierName = "Win";
                return true;
            default:
                modifierName = "";
                return false;
        }
    }

    private static List<string> GetOrderedModifierNames(HashSet<string>? sessionModifiers, ModifierKeys mods)
    {
        var list = new List<string>(4);
        if ((sessionModifiers != null && sessionModifiers.Contains("Ctrl")) ||
            (mods & ModifierKeys.Control) != 0 ||
            Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            list.Add("Ctrl");
        }
        if ((sessionModifiers != null && sessionModifiers.Contains("Shift")) ||
            (mods & ModifierKeys.Shift) != 0 ||
            Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            list.Add("Shift");
        }
        if ((sessionModifiers != null && sessionModifiers.Contains("Alt")) ||
            (mods & ModifierKeys.Alt) != 0 ||
            Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
        {
            list.Add("Alt");
        }
        if ((sessionModifiers != null && sessionModifiers.Contains("Win")) ||
            (mods & ModifierKeys.Windows) != 0 ||
            Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            list.Add("Win");
        }
        return list;
    }

    internal static string? MapWpfKeyToStarPieToken(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            int fNum = (key - Key.F1) + 1;
            return "F" + fNum.ToString(CultureInfo.InvariantCulture);
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            int nNum = key - Key.NumPad0;
            return "NumPad" + nNum.ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Space => "Space",
            Key.Back => "Back",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Snapshot => "PrintScreen",
            Key.Pause => "Pause",
            Key.Capital => "CapsLock",
            Key.Scroll => "ScrollLock",
            Key.NumLock => "NumLock",
            Key.Add => "Num+",
            Key.Subtract => "Num-",
            Key.Multiply => "Num*",
            Key.Divide => "Num/",
            Key.Decimal => "Num.",
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemTilde => "`",
            Key.VolumeUp => "VolumeUp",
            Key.VolumeDown => "VolumeDown",
            Key.VolumeMute => "VolumeMute",
            Key.MediaPlayPause => "MediaPlayPause",
            Key.MediaNextTrack => "MediaNext",
            Key.MediaPreviousTrack => "MediaPrev",
            Key.MediaStop => "MediaStop",
            Key.BrowserBack => "BrowserBack",
            Key.BrowserForward => "BrowserForward",
            Key.BrowserRefresh => "BrowserRefresh",
            Key.BrowserStop => "BrowserStop",
            Key.BrowserSearch => "BrowserSearch",
            Key.BrowserFavorites => "BrowserFavorites",
            Key.BrowserHome => "BrowserHome",
            _ => null,
        };
    }
}
