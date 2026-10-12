using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;
using StarPie.Plugin.Macro;

namespace StarPie.Plugin.Macro.Tests;

internal static class Program
{
    private static int _assertions;

    private static async Task<int> Main()
    {
        try
        {
            TestPluginLifecycleAndI18n();
            TestParserAndValidatePositiveCases();
            TestShorthandArrowChainAndFormatSteps();
            TestSdk14BinaryCompatibilityGuard();
            TestParserAndValidateNegativeCases();
            TestPreviewFormatting();
            await TestExecuteTimelineAndFeaturesAsync();
            await TestExecuteCancellationAndErrorHandlingAsync();

            Console.WriteLine($"[PASS] StarPie.Plugin.Macro.Tests completed with {_assertions} assertions passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] StarPie.Plugin.Macro.Tests failed after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void TestPluginLifecycleAndI18n()
    {
        var context = new FakePluginContext();
        var plugin = new MacroPlugin();
        Assert(plugin.ActionInstance == null, "Constructor must not eagerly create ActionInstance.");

        plugin.Initialize(context);
        Assert(plugin.ActionInstance != null, "Initialize must create and register MacroAction.");
        Assert(context.ActionsRegistry.Registered.Count == 1, "Exactly one action must be registered.");
        Assert(context.IconsRegistry.RegisteredSvgs.ContainsKey("macro"), "SVG icon 'macro' must be registered.");

        IActionContribution action = context.ActionsRegistry.Registered[0];
        ActionDescriptor desc = action.Descriptor;
        AssertEqual("macro", desc.Id, "ActionDescriptor.Id");
        AssertEqual(ActionKind.Sequential, desc.Kind, "ActionDescriptor.Kind");
        AssertEqual(35, desc.TimeoutSeconds, "ActionDescriptor.TimeoutSeconds");
        AssertEqual("plugin:starpie.plugin.macro:macro", desc.IconKey, "ActionDescriptor.IconKey");
        AssertEqual(4, action.Parameters.Count, "Parameters count");

        // Verify i18n registration completeness across zh-CN, en, zh-TW, ja
        AssertEqual(59, Texts.Base.Length, "Base i18n key count");
        AssertEqual(Texts.Base.Length, Texts.ZhTw.Count, "zh-TW key count must match Base");
        AssertEqual(Texts.Base.Length, Texts.Ja.Count, "ja key count must match Base");

        foreach (var (key, zhCn, en) in Texts.Base)
        {
            Assert(!string.IsNullOrWhiteSpace(zhCn), $"zh-CN text for '{key}' must be non-empty.");
            Assert(!string.IsNullOrWhiteSpace(en), $"en text for '{key}' must be non-empty.");
            Assert(Texts.ZhTw.TryGetValue(key, out string? zhTw) && !string.IsNullOrWhiteSpace(zhTw),
                $"zh-TW text for '{key}' must exist and be non-empty.");
            Assert(Texts.Ja.TryGetValue(key, out string? ja) && !string.IsNullOrWhiteSpace(ja),
                $"ja text for '{key}' must exist and be non-empty.");
        }

        // Verify localized resolution in each language
        context.I18nRegistry.CurrentLanguage = "en";
        AssertEqual("Key Macro / Multi-Step Script", action.Descriptor.DisplayName, "English DisplayName");

        context.I18nRegistry.CurrentLanguage = "zh-TW";
        AssertEqual("按鍵巨集 / 多步驟腳本", action.Descriptor.DisplayName, "zh-TW DisplayName");

        context.I18nRegistry.CurrentLanguage = "ja";
        AssertEqual("キーマクロ / マルチステップスクリプト", action.Descriptor.DisplayName, "Japanese DisplayName");

        context.I18nRegistry.CurrentLanguage = "zh-CN";
        AssertEqual("按键宏 / 多步骤脚本", action.Descriptor.DisplayName, "zh-CN DisplayName");

        // Verify updated UI step labels per user requirement
        AssertEqual("📋 粘贴指定文本", Texts.UiKindPaste, "Updated Paste step label");
        AssertEqual("📎 复制指定文本", Texts.UiKindCopy, "Updated Copy step label");

        // Idempotent Shutdown
        plugin.Shutdown();
        Assert(plugin.ActionInstance == null, "Shutdown must clear ActionInstance.");
        Assert(context.ActionsRegistry.DisposedCount == 1, "Shutdown must dispose action registration token.");

        plugin.Shutdown();
        Assert(context.ActionsRegistry.DisposedCount == 1, "Second Shutdown must be a no-op.");
    }

    private static void TestShorthandArrowChainAndFormatSteps()
    {
        // 1. Single-line arrow chain with prefix-free shorthand
        string chainScript = "Alt+F -> 100ms -> Down * 3 -> \"Report -> 2026\" -> Enter";
        MacroParseResult chainParsed = MacroScriptParser.Parse(chainScript);
        Assert(chainParsed.IsValid, $"Arrow chain script should be valid, got: {chainParsed.ErrorMessage}");
        AssertEqual(5, chainParsed.Steps.Count, "Arrow chain step count");

        AssertEqual(MacroStepKind.Hotkey, chainParsed.Steps[0].Kind, "Chain step 0 kind");
        AssertEqual("Alt+F", chainParsed.Steps[0].Value, "Chain step 0 value");
        AssertEqual(MacroStepKind.Wait, chainParsed.Steps[1].Kind, "Chain step 1 kind");
        AssertEqual(100, chainParsed.Steps[1].WaitMs, "Chain step 1 WaitMs");
        AssertEqual(MacroStepKind.Key, chainParsed.Steps[2].Kind, "Chain step 2 kind");
        AssertEqual("Down", chainParsed.Steps[2].Value, "Chain step 2 value");
        AssertEqual(3, chainParsed.Steps[2].Repeat, "Chain step 2 repeat");
        AssertEqual(MacroStepKind.Paste, chainParsed.Steps[3].Kind, "Chain step 3 kind");
        AssertEqual("Report -> 2026", chainParsed.Steps[3].Value, "Chain step 3 value (preserves arrow inside quotes)");
        AssertEqual(MacroStepKind.Key, chainParsed.Steps[4].Kind, "Chain step 4 kind");
        AssertEqual("Enter", chainParsed.Steps[4].Value, "Chain step 4 value");

        // 2. FormatSteps round-trip (including custom CharDelayMs for TEXT@<ms>:)
        var stepsWithCustomTyping = new List<MacroStep>(chainParsed.Steps)
        {
            new(0, MacroStepKind.Text, "Line1\nLine2", WaitMs: 0, Repeat: 1, CharDelayMs: 65),
        };
        string formatted = MacroScriptParser.FormatSteps(stepsWithCustomTyping);
        Assert(formatted.Contains("TEXT@65:", StringComparison.Ordinal), $"Formatted steps should emit TEXT@65:, got: {formatted}");
        MacroParseResult roundTrip = MacroScriptParser.Parse(formatted);
        Assert(roundTrip.IsValid, $"Formatted script should round-trip cleanly, got: {roundTrip.ErrorMessage}");
        AssertEqual(stepsWithCustomTyping.Count, roundTrip.Steps.Count, "Round-trip step count");
        for (int i = 0; i < stepsWithCustomTyping.Count; i++)
        {
            AssertEqual(stepsWithCustomTyping[i].Kind, roundTrip.Steps[i].Kind, $"Round-trip step {i} kind");
            AssertEqual(stepsWithCustomTyping[i].Value, roundTrip.Steps[i].Value, $"Round-trip step {i} value");
            AssertEqual(stepsWithCustomTyping[i].WaitMs, roundTrip.Steps[i].WaitMs, $"Round-trip step {i} WaitMs");
            AssertEqual(stepsWithCustomTyping[i].Repeat, roundTrip.Steps[i].Repeat, $"Round-trip step {i} Repeat");
            AssertEqual(stepsWithCustomTyping[i].CharDelayMs, roundTrip.Steps[i].CharDelayMs, $"Round-trip step {i} CharDelayMs");
        }

        // 3. DecomposeTypingUnits verification (surrogate pairs, \r\n, \n, \t)
        var units = MacroScriptParser.DecomposeTypingUnits("A\r\n\t🌟B");
        AssertEqual(5, units.Count, "DecomposeTypingUnits count for 'A\\r\\n\\t🌟B'");
        AssertEqual("A", units[0].Value, "Unit 0 is 'A'");
        Assert(!units[0].IsKey, "Unit 0 is text");
        AssertEqual("Enter", units[1].Value, "Unit 1 (\\r\\n) maps to Enter");
        Assert(units[1].IsKey, "Unit 1 is key action");
        AssertEqual("Tab", units[2].Value, "Unit 2 (\\t) maps to Tab");
        Assert(units[2].IsKey, "Unit 2 is key action");
        AssertEqual("🌟", units[3].Value, "Unit 3 keeps surrogate pair intact");
        Assert(!units[3].IsKey, "Unit 3 is text");
        AssertEqual("B", units[4].Value, "Unit 4 is 'B'");

        // 4. WPF Key mapping verification
        AssertEqual("Enter", MacroVisualBuilder.MapWpfKeyToStarPieToken(System.Windows.Input.Key.Return), "Map Return -> Enter");
        AssertEqual("Esc", MacroVisualBuilder.MapWpfKeyToStarPieToken(System.Windows.Input.Key.Escape), "Map Escape -> Esc");
        AssertEqual("F12", MacroVisualBuilder.MapWpfKeyToStarPieToken(System.Windows.Input.Key.F12), "Map F12");
        AssertEqual("PgDn", MacroVisualBuilder.MapWpfKeyToStarPieToken(System.Windows.Input.Key.PageDown), "Map PageDown -> PgDn");
        AssertEqual("NumPad5", MacroVisualBuilder.MapWpfKeyToStarPieToken(System.Windows.Input.Key.NumPad5), "Map NumPad5");
    }

    private static void TestSdk14BinaryCompatibilityGuard()
    {
        var context = new FakePluginContext();
        var action = new MacroAction(context, "icon");

        // Ensure ActionDescriptor and ParameterField instances leave post-SDK-1.4 properties at default/null
        ActionDescriptor desc = action.Descriptor;
        Assert(string.IsNullOrEmpty(desc.DescriptionKey), "ActionDescriptor.DescriptionKey must not be set (added in SDK 1.11).");

        foreach (ParameterField field in action.Parameters)
        {
            Assert(string.IsNullOrEmpty(field.HelpTextKey), $"ParameterField '{field.Key}' HelpTextKey must not be set (added in SDK 1.11).");
            Assert(string.IsNullOrEmpty(field.Unit), $"ParameterField '{field.Key}' Unit must not be set (added in SDK 1.11).");
            Assert(string.IsNullOrEmpty(field.FallbackParameterKey), $"ParameterField '{field.Key}' FallbackParameterKey must not be set (added in SDK 1.8).");
            Assert(field.FallbackValueMap == null, $"ParameterField '{field.Key}' FallbackValueMap must not be set (added in SDK 1.8).");
            Assert(field.Type != ParameterFieldType.KeyMap && field.Type != ParameterFieldType.Slider,
                $"ParameterField '{field.Key}' must only use SDK 1.0/1.4 ParameterFieldType values.");
        }
    }

    private static void TestParserAndValidatePositiveCases()
    {
        var context = new FakePluginContext();
        var action = new MacroAction(context, "icon");

        string richScript = @"
            # 1. Comment lines and blank lines
            // C-style comment
            ; Semicolon comment

            HOTKEY: Alt+F          # Open File menu
            WAIT: 120ms            // Wait with ms suffix
            KEY: Down * 3          # Press Down 3 times
            快捷键：Ctrl+Shift+S x 2
            单键：F12
            延时：80
            TEXT: Hello World      # Unquoted text strips inline comment
            输入：""Hello # Literal & \""Quoted\""\tLine1\nLine2"" # Quoted preserves # and escapes
            PASTE: ""Fast Paste Text""
            剪贴板：Copied_Value
            HOTKEY: Alt, H, V      # Multi-chord hotkey
            KEY: NumPad5
            KEY: Num+
            KEY: Print Screen
            KEY: VolumeUp
        ";

        MacroParseResult parsed = MacroScriptParser.Parse(richScript);
        Assert(parsed.IsValid, $"Rich script should be valid, got error: {parsed.ErrorMessage}");
        AssertEqual(15, parsed.Steps.Count, "Parsed step count");

        AssertEqual(MacroStepKind.Hotkey, parsed.Steps[0].Kind, "Step 0 kind");
        AssertEqual("Alt+F", parsed.Steps[0].Value, "Step 0 value");
        AssertEqual(1, parsed.Steps[0].Repeat, "Step 0 repeat");

        AssertEqual(MacroStepKind.Wait, parsed.Steps[1].Kind, "Step 1 kind");
        AssertEqual(120, parsed.Steps[1].WaitMs, "Step 1 WaitMs");

        AssertEqual(MacroStepKind.Key, parsed.Steps[2].Kind, "Step 2 kind");
        AssertEqual("Down", parsed.Steps[2].Value, "Step 2 value");
        AssertEqual(3, parsed.Steps[2].Repeat, "Step 2 repeat");

        AssertEqual(MacroStepKind.Hotkey, parsed.Steps[3].Kind, "Step 3 kind");
        AssertEqual("Ctrl+Shift+S", parsed.Steps[3].Value, "Step 3 value");
        AssertEqual(2, parsed.Steps[3].Repeat, "Step 3 repeat");

        AssertEqual(MacroStepKind.Key, parsed.Steps[4].Kind, "Step 4 kind");
        AssertEqual("F12", parsed.Steps[4].Value, "Step 4 value");

        AssertEqual(MacroStepKind.Wait, parsed.Steps[5].Kind, "Step 5 kind");
        AssertEqual(80, parsed.Steps[5].WaitMs, "Step 5 WaitMs");

        AssertEqual(MacroStepKind.Text, parsed.Steps[6].Kind, "Step 6 kind");
        AssertEqual("Hello World", parsed.Steps[6].Value, "Step 6 unquoted text");

        AssertEqual(MacroStepKind.Text, parsed.Steps[7].Kind, "Step 7 kind");
        AssertEqual("Hello # Literal & \"Quoted\"\tLine1\nLine2", parsed.Steps[7].Value, "Step 7 quoted text");

        AssertEqual(MacroStepKind.Paste, parsed.Steps[8].Kind, "Step 8 kind");
        AssertEqual("Fast Paste Text", parsed.Steps[8].Value, "Step 8 paste value");

        AssertEqual(MacroStepKind.Clipboard, parsed.Steps[9].Kind, "Step 9 kind");
        AssertEqual("Copied_Value", parsed.Steps[9].Value, "Step 9 clipboard value");

        string? validationError = action.Validate(new Dictionary<string, string>
        {
            ["script"] = richScript,
            ["preDelayMs"] = "50",
            ["stepDelayMs"] = "30",
            ["repeatCount"] = "2",
        });
        Assert(validationError == null, $"Validate should pass for valid parameters, got: {validationError}");
    }

    private static void TestParserAndValidateNegativeCases()
    {
        var context = new FakePluginContext();
        var action = new MacroAction(context, "icon");

        // 1. Empty / comment-only script
        AssertInvalid(action, "", "未填写有效的宏步骤");
        AssertInvalid(action, "# only comment\n  // another comment", "未填写有效的宏步骤");

        // 2. Missing colon on line 2
        AssertInvalid(action, "HOTKEY: Alt+F\nWAIT 100", "第 2 行");

        // 3. Unknown directive on line 3
        AssertInvalid(action, "HOTKEY: Alt+F\nWAIT: 50\nEXEC: cmd.exe", "第 3 行", "EXEC");

        // 4. Empty argument
        AssertInvalid(action, "HOTKEY:   # nothing here", "第 1 行", "HOTKEY");
        AssertInvalid(action, "TEXT:   ", "第 1 行", "TEXT");
        AssertInvalid(action, "WAIT:   ", "第 1 行", "WAIT");

        // 5. Unclosed or malformed quoted string
        AssertInvalid(action, "TEXT: \"unclosed quote", "第 1 行");
        AssertInvalid(action, "TEXT: \"closed\" trailing_garbage", "第 1 行");

        // 6. Invalid WAIT values
        AssertInvalid(action, "WAIT: -10", "第 1 行", "-10");
        AssertInvalid(action, "WAIT: 5001", "第 1 行", "5001");
        AssertInvalid(action, "WAIT: fast", "第 1 行", "fast");

        // 7. Invalid repeat multiplier
        AssertInvalid(action, "KEY: Down * 0", "第 1 行", "0");
        AssertInvalid(action, "KEY: Down * 21", "第 1 行", "21");
        AssertInvalid(action, "KEY: Down *", "第 1 行", "*");
        AssertInvalid(action, "HOTKEY: Ctrl+Z x 99", "第 1 行", "99");

        // 8. Multi-key combo inside KEY directive
        AssertInvalid(action, "KEY: Ctrl+C", "第 1 行", "HOTKEY");
        AssertInvalid(action, "KEY: Alt, F", "第 1 行", "HOTKEY");

        // 9. Unknown key token
        AssertInvalid(action, "KEY: Entr", "第 1 行", "Entr");
        AssertInvalid(action, "HOTKEY: Ctrl+Shift+SuperKey", "第 1 行", "SuperKey");
        AssertInvalid(action, "HOTKEY: Ctrl+", "第 1 行");

        // 10. Too many steps (> 100)
        string hundredOneSteps = string.Join("\n", Enumerable.Repeat("KEY: Enter", 101));
        AssertInvalid(action, hundredOneSteps, "101", "100");

        // 11. Total wait exceeded via explicit WAITs (> 30000ms)
        string sevenFiveSecondWaits = string.Join("\n", Enumerable.Repeat("WAIT: 5000", 7));
        AssertInvalid(action, sevenFiveSecondWaits, "30000");

        // 12. Total wait exceeded via repeatCount multiplier
        string? repeatOverflowErr = action.Validate(new Dictionary<string, string>
        {
            ["script"] = "KEY: A\nWAIT: 2000\nKEY: B",
            ["preDelayMs"] = "100",
            ["stepDelayMs"] = "50",
            ["repeatCount"] = "16", // 16 * 2000 = 32000 > 30000
        });
        Assert(!string.IsNullOrEmpty(repeatOverflowErr) && repeatOverflowErr.Contains("30000"),
            $"Expected totalWaitExceeded error, got: {repeatOverflowErr}");

        // 13. Invalid numeric options
        AssertOptionInvalid(action, "preDelayMs", "-1");
        AssertOptionInvalid(action, "preDelayMs", "1001");
        AssertOptionInvalid(action, "preDelayMs", "abc");

        AssertOptionInvalid(action, "stepDelayMs", "-1");
        AssertOptionInvalid(action, "stepDelayMs", "501");

        AssertOptionInvalid(action, "repeatCount", "0");
        AssertOptionInvalid(action, "repeatCount", "21");
    }

    private static void TestPreviewFormatting()
    {
        var context = new FakePluginContext();
        var action = new MacroAction(context, "icon");

        string p1 = action.Preview(new Dictionary<string, string>
        {
            ["script"] = "HOTKEY: Alt+F\nWAIT: 100\nKEY: Down * 3\nKEY: Enter",
            ["repeatCount"] = "1",
        });
        AssertEqual("Alt+F ➔ 100ms ➔ Down×3 ➔ Enter", p1, "Standard preview");

        string p2 = action.Preview(new Dictionary<string, string>
        {
            ["script"] = "HOTKEY: Ctrl+C\nWAIT: 50\nHOTKEY: Ctrl+V",
            ["repeatCount"] = "3",
        });
        AssertEqual("Ctrl+C ➔ 50ms ➔ Ctrl+V (×3)", p2, "Preview with repeatCount > 1");

        string longScript = string.Join("\n", Enumerable.Range(1, 15).Select(i => $"HOTKEY: Ctrl+Shift+F{i}"));
        string pLong = action.Preview(new Dictionary<string, string>
        {
            ["script"] = longScript,
            ["repeatCount"] = "4",
        });
        Assert(pLong.Length <= 48, $"Truncated preview length must be <= 48, got {pLong.Length}: '{pLong}'");
        Assert(pLong.EndsWith("(×4)", StringComparison.Ordinal), $"Truncated preview should preserve repeat suffix: '{pLong}'");

        string pInvalid = action.Preview(new Dictionary<string, string>
        {
            ["script"] = "INVALID_LINE_WITHOUT_COLON",
        });
        AssertEqual(Texts.ActionTitle, pInvalid, "Fallback preview for invalid script");
    }

    private static async Task TestExecuteTimelineAndFeaturesAsync()
    {
        var context = new FakePluginContext();
        var action = new MacroAction(context, "icon");
        var timeline = new List<string>();

        context.HostInvoker.OnCall = entry => timeline.Add(entry);
        action.DelayOverride = (ms, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            timeline.Add($"Wait({ms})");
        };

        var input = new PluginActionInput
        {
            ContributionId = "starpie.plugin.macro.macro",
            Parameters = new Dictionary<string, string>
            {
                ["script"] = @"
                    HOTKEY: Alt+F
                    WAIT: 100
                    KEY: Down * 2
                    TEXT@45: ""A\nB""
                    CLIPBOARD: Clip_Data
                    PASTE: Paste_Data
                    KEY: Enter
                ",
                ["preDelayMs"] = "50",
                ["stepDelayMs"] = "30",
                ["repeatCount"] = "1",
            },
        };

        ActionResult result = await action.ExecuteAsync(input, CancellationToken.None);
        Assert(result.Success, $"ExecuteAsync should succeed, got: {result.Message}");

        string[] expected =
        {
            "Wait(50)",                     // preDelayMs
            "SendHotkey(Alt+F)",            // Step 1
            "Wait(100)",                    // Explicit WAIT (no extra stepDelay before or after)
            "SendHotkey(Down)",             // Step 3 (repeat 1/2)
            "Wait(30)",                     // Inter-repeat stepDelayMs
            "SendHotkey(Down)",             // Step 3 (repeat 2/2)
            "Wait(30)",                     // Inter-step stepDelayMs
            "SendText(A)",                  // Step 4: TEXT@45 unit 1 ('A')
            "Wait(45)",                     // Step 4: inter-character delay (45ms)
            "SendHotkey(Enter)",            // Step 4: TEXT@45 unit 2 ('\n' -> Enter)
            "Wait(45)",                     // Step 4: inter-character delay (45ms)
            "SendText(B)",                  // Step 4: TEXT@45 unit 3 ('B')
            "Wait(30)",                     // Inter-step stepDelayMs
            "SetClipboard(Clip_Data)",      // Step 5
            "Wait(30)",                     // Inter-step stepDelayMs
            "SetClipboard(Paste_Data)",     // Step 6 (Paste part 1)
            "SendHotkey(Ctrl+V)",           // Step 6 (Paste part 2)
            "Wait(30)",                     // Inter-step stepDelayMs
            "SendHotkey(Enter)",            // Step 7
        };

        AssertSequenceEqual(expected, timeline, "Single pass execution timeline");

        // Verify repeatCount = 2 timeline
        timeline.Clear();
        var repeatInput = new PluginActionInput
        {
            ContributionId = "starpie.plugin.macro.macro",
            Parameters = new Dictionary<string, string>
            {
                ["script"] = "KEY: Tab\nKEY: Space",
                ["preDelayMs"] = "40",
                ["stepDelayMs"] = "25",
                ["repeatCount"] = "2",
            },
        };

        ActionResult repeatResult = await action.ExecuteAsync(repeatInput, CancellationToken.None);
        Assert(repeatResult.Success, "Repeat ExecuteAsync should succeed");

        string[] expectedRepeat =
        {
            "Wait(40)",             // preDelayMs once at start
            "SendHotkey(Tab)",      // Loop 1, Step 1
            "Wait(25)",
            "SendHotkey(Space)",    // Loop 1, Step 2
            "Wait(25)",             // Inter-step delay between Loop 1 Step 2 and Loop 2 Step 1
            "SendHotkey(Tab)",      // Loop 2, Step 1
            "Wait(25)",
            "SendHotkey(Space)",    // Loop 2, Step 2
        };
        AssertSequenceEqual(expectedRepeat, timeline, "Repeat execution timeline");
    }

    private static async Task TestExecuteCancellationAndErrorHandlingAsync()
    {
        // 1. Mid-execution cancellation via CancellationToken
        {
            var context = new FakePluginContext();
            var action = new MacroAction(context, "icon");
            var timeline = new List<string>();
            using var cts = new CancellationTokenSource();

            context.HostInvoker.OnCall = entry => timeline.Add(entry);
            action.DelayOverride = (ms, ct) =>
            {
                timeline.Add($"Wait({ms})");
                if (ms == 150)
                {
                    cts.Cancel();
                }
                ct.ThrowIfCancellationRequested();
            };

            var input = new PluginActionInput
            {
                Parameters = new Dictionary<string, string>
                {
                    ["script"] = "HOTKEY: Alt+F\nWAIT: 150\nKEY: Enter",
                    ["preDelayMs"] = "20",
                    ["stepDelayMs"] = "10",
                },
            };

            bool threwCanceled = false;
            try
            {
                await action.ExecuteAsync(input, cts.Token);
            }
            catch (OperationCanceledException)
            {
                threwCanceled = true;
            }

            Assert(threwCanceled, "ExecuteAsync must throw OperationCanceledException when cancelled during wait.");
            AssertSequenceEqual(
                new[] { "Wait(20)", "SendHotkey(Alt+F)", "Wait(150)" },
                timeline,
                "Steps after cancellation point must never execute.");
        }

        // 2. Host SendHotkey returning false aborts subsequent steps
        {
            var context = new FakePluginContext();
            var action = new MacroAction(context, "icon");
            var timeline = new List<string>();

            context.HostInvoker.OnCall = entry => timeline.Add(entry);
            context.HostInvoker.FailOnHotkey = "F5";
            action.DelayOverride = (ms, _) => timeline.Add($"Wait({ms})");

            var input = new PluginActionInput
            {
                Parameters = new Dictionary<string, string>
                {
                    ["script"] = "KEY: Tab\nKEY: F5\nKEY: Enter",
                    ["preDelayMs"] = "0",
                    ["stepDelayMs"] = "0",
                },
            };

            ActionResult res = await action.ExecuteAsync(input, CancellationToken.None);
            Assert(!res.Success, "ExecuteAsync should return Fail when SendHotkey returns false.");
            Assert(res.Message != null && res.Message.Contains("第 2 行") && res.Message.Contains("F5"),
                $"Failure message should identify line 2 and F5, got: {res.Message}");
            AssertSequenceEqual(
                new[] { "SendHotkey(Tab)", "SendHotkey(F5)" },
                timeline,
                "Step 3 (Enter) must not execute after Step 2 fails.");
        }

        // 3. PluginCapabilityDeniedException handling
        {
            var context = new FakePluginContext();
            var action = new MacroAction(context, "icon");
            context.HostInvoker.ThrowCapabilityDenied = true;
            action.DelayOverride = (_, _) => { };

            var input = new PluginActionInput
            {
                Parameters = new Dictionary<string, string>
                {
                    ["script"] = "KEY: Enter",
                    ["preDelayMs"] = "0",
                },
            };

            ActionResult res = await action.ExecuteAsync(input, CancellationToken.None);
            Assert(!res.Success, "ExecuteAsync must fail gracefully when capability is denied.");
            Assert(res.Message != null && res.Message.Contains("InputSimulation"),
                $"Failure message should mention InputSimulation capability, got: {res.Message}");
            Assert(context.Logger.Warnings.Count == 1, "Capability denial must be logged as warning.");
        }
    }

    private static void AssertInvalid(MacroAction action, string script, params string[] expectedSubstrings)
    {
        string? err = action.Validate(new Dictionary<string, string> { ["script"] = script });
        Assert(!string.IsNullOrWhiteSpace(err), $"Expected validation error for script '{script}', but got null/empty.");
        foreach (string sub in expectedSubstrings)
        {
            Assert(err!.Contains(sub, StringComparison.OrdinalIgnoreCase),
                $"Expected error '{err}' to contain '{sub}'.");
        }
    }

    private static void AssertOptionInvalid(MacroAction action, string key, string badValue)
    {
        var dict = new Dictionary<string, string>
        {
            ["script"] = "KEY: Enter",
            [key] = badValue,
        };
        string? err = action.Validate(dict);
        Assert(!string.IsNullOrWhiteSpace(err), $"Expected option '{key}={badValue}' to fail validation.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
        _assertions++;
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', actual '{actual}'.");
        }
        _assertions++;
    }

    private static void AssertSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string label)
    {
        if (expected.Count != actual.Count || !expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label} mismatch.\nExpected ({expected.Count}): [{string.Join(", ", expected)}]\nActual   ({actual.Count}): [{string.Join(", ", actual)}]");
        }
        _assertions++;
    }

    private sealed class FakePluginContext : IPluginContext
    {
        public FakeActionRegistry ActionsRegistry { get; } = new();
        public FakeI18nRegistry I18nRegistry { get; } = new();
        public FakeIconRegistry IconsRegistry { get; } = new();
        public FakeHostActionInvoker HostInvoker { get; } = new();
        public FakeLogger Logger { get; } = new();

        public PluginMetadata Me { get; } = new()
        {
            Id = "starpie.plugin.macro",
            Name = "按键宏与多步骤脚本",
            Version = "1.0.0",
            ApiVersion = "1.4",
            Capabilities = PluginCapability.InputSimulation | PluginCapability.Clipboard,
        };

        public string PluginDirectory => AppContext.BaseDirectory;
        public string DataDirectory => AppContext.BaseDirectory;
        public IPluginLogger Log => Logger;
        public IPluginSettings Settings => null!;
        public IActionRegistry Actions => ActionsRegistry;
        public II18nRegistry I18n => I18nRegistry;
        public IIconRegistry Icons => IconsRegistry;
        public ISettingsPageRegistry SettingsPage => null!;
        public IHostActionInvoker Host => HostInvoker;
        public IHostCommandService Commands => null!;
        public IHostShellService Shell => null!;
        public IHostWindowService Windows => null!;
        public IHostScreenCaptureService ScreenCapture => null!;
        public IHostSystemService System => null!;
        public IHostWheelService Wheel => null!;
        public IHostKeyboardRemapService KeyboardRemap => null!;
        public IHostInfo Info => null!;
        public INotificationService Notify => null!;
        public IPluginEvents Events => null!;
        public IDispatcherFacade Dispatcher => null!;
    }

    private sealed class FakeActionRegistry : IActionRegistry
    {
        public List<IActionContribution> Registered { get; } = new();
        public int DisposedCount { get; private set; }

        public IDisposable Register(IActionContribution contribution)
        {
            Registered.Add(contribution);
            return new ActionDisposable(() => DisposedCount++);
        }

        private sealed class ActionDisposable(Action onDispose) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    onDispose();
                }
            }
        }
    }

    private sealed class FakeI18nRegistry : II18nRegistry
    {
        private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
        public string CurrentLanguage { get; set; } = "zh-CN";

        public void Register(string key, string zhCn, string? en = null)
        {
            Set("zh-CN", key, zhCn);
            if (!string.IsNullOrEmpty(en))
            {
                Set("en", key, en);
            }
        }

        public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table)
        {
            foreach (var kv in table)
            {
                Set(languageCode, kv.Key, kv.Value);
            }
        }

        public string T(string key, string? fallback = null)
        {
            if (_tables.TryGetValue(CurrentLanguage, out var langTable) &&
                langTable.TryGetValue(key, out string? val))
            {
                return val;
            }
            if (_tables.TryGetValue("zh-CN", out var zhTable) &&
                zhTable.TryGetValue(key, out string? zhVal))
            {
                return zhVal;
            }
            return fallback ?? key;
        }

        private void Set(string lang, string key, string value)
        {
            if (!_tables.TryGetValue(lang, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _tables[lang] = dict;
            }
            dict[key] = value;
        }
    }

    private sealed class FakeIconRegistry : IIconRegistry
    {
        public Dictionary<string, string> RegisteredSvgs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string RegisterSvg(string key, string svgPathData)
        {
            RegisteredSvgs[key] = svgPathData;
            return $"plugin:starpie.plugin.macro:{key}";
        }

        public string? ResolveKey(string key) =>
            RegisteredSvgs.ContainsKey(key) ? $"plugin:starpie.plugin.macro:{key}" : null;
    }

    private sealed class FakeHostActionInvoker : IHostActionInvoker
    {
        public Action<string>? OnCall { get; set; }
        public string? FailOnHotkey { get; set; }
        public bool ThrowCapabilityDenied { get; set; }

        public bool SendHotkey(string hotkey)
        {
            if (ThrowCapabilityDenied)
            {
                throw new PluginCapabilityDeniedException(
                    PluginCapability.InputSimulation,
                    nameof(IHostActionInvoker),
                    "starpie.plugin.macro");
            }
            OnCall?.Invoke($"SendHotkey({hotkey})");
            return !string.Equals(hotkey, FailOnHotkey, StringComparison.OrdinalIgnoreCase);
        }

        public bool SendText(string text)
        {
            OnCall?.Invoke($"SendText({text})");
            return true;
        }

        public bool SetClipboardText(string text)
        {
            OnCall?.Invoke($"SetClipboard({text})");
            return true;
        }

        public string? GetClipboardText() => null;
        public bool Launch(string path, string arguments = "", bool runAsStandardUser = false) => false;
        public bool OpenFolder(string folderPath) => false;
        public bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null) => false;
    }

    private sealed class FakeLogger : IPluginLogger
    {
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public string LogFilePath => "";
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
