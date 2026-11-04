using System.Reflection;
using System.Collections;
using Expression = System.Linq.Expressions.Expression;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("plugin-dll sha256 required");
        string path = Path.GetFullPath(args[0]);
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == args[1], "Candidate plugin hash");
        string? sdkHash = Environment.GetEnvironmentVariable("KEYBOARD_EXPECTED_SDK_SHA256");
        if (sdkHash != null)
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IPluginContext).Assembly.Location))) == sdkHash,
                "Loaded SDK matches the explicit validation candidate");
        Assembly assembly = Assembly.LoadFrom(path);
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var panels = new PanelFake();
        var words = new Dictionary<string, string>();
        II18nRegistry i18n = Stub<II18nRegistry>(method =>
        {
            if (method.Name == "Register") words[(string)method.Args![0]!] = (string)method.Args[1]!;
            return method.Name == "T" && method.Args![0] is string key ? words.GetValueOrDefault(key, key) : null;
        });
        IPluginLogger log = Stub<IPluginLogger>(_ => null);
        IPluginContext context = Stub<IPluginContext>(method => method.Name switch
        { "get_I18n" => i18n, "get_Log" => log, _ => throw new InvalidOperationException("Unexpected service " + method.Name) });
        Type Type(string name) => assembly.GetType("StarPie.Plugin.TemporaryKeyboard." + name, true)!;
        Type("KeyboardTexts").GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [context]);
        object schemes = Type("KeySchemes").GetMethod("CreateDefault")!.Invoke(null, ["Test scheme"])!;
        int count = 0;
        foreach (bool dark in new[] { false, true })
        {
            panels.Dark = dark;
            foreach (string mode in new[] { "Numeric", "Letters", "Functions", "Full", "Custom" })
            {
                var window = (Window)Activator.CreateInstance(Type("KeyboardWindow"), All, null,
                    [context, panels, schemes, Enum.Parse(Type("KeyboardMode"), mode), null, 0, 0, false], null)!;
                Layout(window);
                Check(!window.ShowActivated && !window.ShowInTaskbar && !window.Focusable, "Nonactivating flags");
                var buttons = Descendants(window.Content!).OfType<Button>().ToList();
                Check(buttons.All(button => !button.Focusable && button.Template != null), "Keys and mode buttons have templates, no focus");
                Check(window.Background is SolidColorBrush { IsFrozen: true }, "Frozen palette brush");
                if (mode == "Custom")
                {
                    UniformGrid grid = Descendants(window.Content!).OfType<UniformGrid>().Single();
                    Check(grid.Rows == 4 && grid.Columns == 3 && grid.Children.Count == 12, "3x4 custom area");
                    Check(grid.Children.OfType<Button>().All(button => !button.IsEnabled), "Unassigned disabled");
                }
                else Check(buttons.Count > 20, "Keys rendered");
                window.Close(); count++;
            }
        }
        foreach (bool dark in new[] { false, true })
        foreach (bool damaged in new[] { false, true })
        {
            panels.Dark = dark;
            object document = Activator.CreateInstance(Type("KeySchemeDocument"), All, null,
                [schemes, "test", false, damaged, null], null)!;
            var editor = (Window)Activator.CreateInstance(Type("KeyboardSchemeEditor"), All, null,
                [context, panels, document, CancellationToken.None], null)!;
            Layout(editor);
            var save = Descendants(editor.Content!).OfType<Button>().Single(button => button.Name == "SaveKeyboardSchemes");
            Check(save.IsEnabled != damaged, "Damaged source cannot ordinary-save");
            Check(Descendants(editor.Content!).OfType<UniformGrid>().Single().Children.Count == 12, "Editor keeps 12 cells");
            if (!damaged)
            {
                Check(Descendants(editor.Content!).OfType<Grid>().Any(control => control.Name == "FullKeyboardTemplate"), "Full keyboard template is present");
                Check(!Descendants(editor.Content!).OfType<Button>().Any(button => Equals(button.Content, words["addAction"])), "No plugin-action entry remains");
                Check(Descendants(editor.Content!).OfType<TextBox>().Any(control => control.Name == "SelectedKeyLabel"), "Selected key inspector");
                string? imageRoot = Environment.GetEnvironmentVariable("KEYBOARD_UI_OUTPUT");
                if (imageRoot != null)
                { Directory.CreateDirectory(imageRoot); Render(editor, System.IO.Path.Combine(imageRoot, dark ? "editor-dark.png" : "editor-light.png")); }
            }
            editor.Close(); count++;
        }
        object configured = Type("KeySchemes").GetMethod("CreateDefault")!.Invoke(null, ["测试方案"])!;
        object currentScheme = ((IList)Type("KeySchemes").GetProperty("Items")!.GetValue(configured)!)[0]!;
        IList configuredKeys = (IList)currentScheme.GetType().GetProperty("Keys")!.GetValue(currentScheme)!;
        object NewKey(string label, string action)
        {
            object item = Activator.CreateInstance(Type("KeyBinding"), true)!;
            item.GetType().GetProperty("Label")!.SetValue(item, label);
            item.GetType().GetProperty("KeyId")!.SetValue(item, action);
            return item;
        }
        configuredKeys[0] = NewKey("复制", "full-letter-c");
        configuredKeys[1] = NewKey("粘贴", "full-letter-v");
        string longLabel = new('W', 120);
        configuredKeys[2] = NewKey(longLabel, "full-return");
        var runtime = (Window)Activator.CreateInstance(Type("KeyboardWindow"), All, null,
            [context, panels, configured, Enum.Parse(Type("KeyboardMode"), "Custom"), null, 0, 0, false], null)!;
        double previousWidth = 0;
        foreach (var size in new[] { new Size(520, 420), new Size(900, 600) })
        {
            runtime.Width = size.Width; runtime.Height = size.Height; Layout(runtime);
            var cells = Descendants(runtime.Content!).OfType<UniformGrid>().Single().Children.OfType<Button>().ToArray();
            Check(cells.All(cell => Math.Abs(cell.ActualWidth - cells[0].ActualWidth) < 0.01 && Math.Abs(cell.ActualHeight - cells[0].ActualHeight) < 0.01), "Long label never changes key area");
            Check(cells[0].ActualWidth > previousWidth, "Keys expand with the actual panel"); previousWidth = cells[0].ActualWidth;
            Check(cells[2].Content is TextBlock { TextTrimming: TextTrimming.CharacterEllipsis, TextWrapping: TextWrapping.NoWrap } && Equals(cells[2].ToolTip, longLabel), "Long key caption trims with a complete tooltip");
            Check(!Descendants(runtime.Content!).OfType<ScrollViewer>().Any(scroll => scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto), "No unbounded horizontal canvas scroller");
        }
        var runtimePicker = Descendants(runtime.Content!).OfType<ComboBox>().Single();
        Check(runtimePicker.SelectedItem?.ToString() == "测试方案", "Runtime scheme selector shows the scheme name");
        var fakeInput = new List<string>();
        IInputPanelSession fakeSession = Stub<IInputPanelSession>(call => call.Name switch
        {
            "get_IsOpen" => true,
            "QueueHotkey" => Capture((string)call.Args![0]!),
            "Dispose" => null,
            _ => throw new InvalidOperationException("Unexpected input API " + call.Name),
        });
        InputPanelQueueResult Capture(string key) { fakeInput.Add(key); return InputPanelQueueResult.Accepted; }
        Type("KeyboardWindow").GetMethod("AttachSession", All)!.Invoke(runtime, [fakeSession]); Layout(runtime);
        Descendants(runtime.Content!).OfType<UniformGrid>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(fakeInput.SequenceEqual(new[] { "C" }) && runtime.Width == 900, "Custom key uses the keyboard backend once and preserves resized width");
        string? renderRoot = Environment.GetEnvironmentVariable("KEYBOARD_UI_OUTPUT");
        if (renderRoot != null) Render(runtime, System.IO.Path.Combine(renderRoot, "runtime-custom.png"));
        runtime.Close();
        object configuredDoc = Activator.CreateInstance(Type("KeySchemeDocument"), All, null, [configured, "fixture", false, false, null], null)!;
        panels.Dark = false;
        var interactive = (Window)Activator.CreateInstance(Type("KeyboardSchemeEditor"), All, null, [context, panels, configuredDoc, CancellationToken.None], null)!;
        Layout(interactive);
        string? output = Environment.GetEnvironmentVariable("KEYBOARD_UI_OUTPUT");
        if (output != null) Render(interactive, System.IO.Path.Combine(output, "editor-configured.png"));
        object state = Type("KeyboardSchemeEditor").GetField("_state", All)!.GetValue(interactive)!;
        string ActionAt(int index)
        {
            object snapshot = state.GetType().GetMethod("Snapshot", All)!.Invoke(state, null)!;
            object scheme = ((IList)snapshot.GetType().GetProperty("Items")!.GetValue(snapshot)!)[0]!;
            object? item = ((IList)scheme.GetType().GetProperty("Keys")!.GetValue(scheme)!)[index];
            return item?.GetType().GetProperty("KeyId")!.GetValue(item)?.ToString() ?? "";
        }
        Check((bool)Type("KeyboardSchemeEditor").GetMethod("MoveKey", All)!.Invoke(interactive, [0, 1])!, "UI move operation");
        Check(ActionAt(0) == "full-letter-v" && ActionAt(1) == "full-letter-c", "Canvas swap preserves bindings");
        var undo = Descendants(interactive.Content!).OfType<Button>().Single(button => Equals(button.Content, words["undo"]));
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(ActionAt(0) == "full-letter-c", "UI undo restores original position");
        var redo = Descendants(interactive.Content!).OfType<Button>().Single(button => Equals(button.Content, words["redo"]));
        redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(ActionAt(1) == "full-letter-c", "UI redo restores swapped position");
        var clear = Descendants(interactive.Content!).OfType<Button>().Single(button => Equals(button.Content, words["clearKey"]));
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(ActionAt(1) == "", "Inspector clear acts on selected key");
        var template = Descendants(interactive.Content!).OfType<Grid>().Single(grid => grid.Name == "FullKeyboardTemplate");
        var templateEnter = Descendants(template).OfType<Button>().Single(button => button.Tag?.GetType().GetProperty("KeyId")?.GetValue(button.Tag)?.ToString() == "full-return");
        templateEnter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Descendants(interactive.Content!).OfType<Button>().Single(button => Equals(button.Content, words["pasteKey"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(ActionAt(1) == "full-return", "Template copy and inspector paste bind the selected slot");
        var savedEvent = Type("KeyboardSchemeEditor").GetEvent("Saved", All)!;
        Delegate Handler(Action<object, bool> callback)
        {
            Type[] types = savedEvent.EventHandlerType!.GenericTypeArguments;
            var first = Expression.Parameter(types[0]); var second = Expression.Parameter(types[1]);
            return Expression.Lambda(savedEvent.EventHandlerType, Expression.Invoke(Expression.Constant(callback), Expression.Convert(first, typeof(object)), second), first, second).Compile();
        }
        bool closed = false; interactive.Closed += (_, _) => closed = true;
        Delegate failure = Handler((_, _) => throw new IOException("Simulated write failure"));
        savedEvent.GetAddMethod(true)!.Invoke(interactive, [failure]);
        var saveConfigured = Descendants(interactive.Content!).OfType<Button>().Single(button => button.Name == "SaveKeyboardSchemes");
        saveConfigured.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!closed, "Failed persistence must keep editor open");
        savedEvent.GetRemoveMethod(true)!.Invoke(interactive, [failure]);
        int saves = 0; Delegate success = Handler((_, _) => saves++); savedEvent.GetAddMethod(true)!.Invoke(interactive, [success]);
        saveConfigured.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(closed && saves == 1, "One successful save closes editor");
        Check(panels.InputCalls == 0, "Never opens input session or dispatches actions");
        Console.WriteLine($"PASS {count} candidate window layouts/templates, full keyboard template, canvas swap/undo/redo/clear/save failure; no Show, real input, real action or system change");
        Application.Current.Shutdown();
        return 0;
    }
    private static void Layout(Window window)
    {
        var root = (FrameworkElement)window.Content;
        double width = double.IsFinite(window.Width) ? window.Width : 1000;
        double height = double.IsFinite(window.Height) ? window.Height : 650;
        root.ApplyTemplate(); root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        foreach (var control in Descendants(root).OfType<Control>()) control.ApplyTemplate();
    }
    private static void Render(Window window, string path)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout();
        int width = (int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right);
        int height = (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<DependencyObject> Descendants(object root)
    {
        if (root is not DependencyObject node) yield break;
        yield return node;
        foreach (object child in LogicalTreeHelper.GetChildren(node))
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Stub<T>(Func<(string Name, object?[]? Args), object?> handler) where T : class
    { T proxy = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)proxy).Handler = handler; return proxy; }
}
public class TestProxy : DispatchProxy
{
    public Func<(string Name, object?[]? Args), object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler((targetMethod!.Name, args));
}
internal sealed class PanelFake : IHostInputPanelService
{
    internal bool Dark;
    internal int InputCalls;
    public InputPanelAppearance GetAppearance() => Dark
        ? new() { Background = "#111111", Surface = "#222222", Foreground = "#FFFFFF", Border = "#888888" } : new();
    public IInputPanelSession Open(ActionContext target, Action<InputPanelCloseReason> closed)
    { InputCalls++; throw new InvalidOperationException("No input allowed"); }
    public PluginActionBindingInfo DescribeAction(ConfiguredPluginAction binding) => new() { IsAvailable = false };
    public Task<PluginActionBindingInfo> ResolveActionAsync(ConfiguredPluginAction binding, CancellationToken token = default) => Task.FromResult(DescribeAction(binding));
    public Task<ConfiguredPluginAction?> EditActionAsync(ConfiguredPluginAction? current, long owner = 0, CancellationToken token = default)
    { InputCalls++; throw new InvalidOperationException("No editor activation allowed"); }
}
