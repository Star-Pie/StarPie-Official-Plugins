using System;
using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// StarPie 桌宠插件入口。
/// <para>
/// 实现了轻量透明桌宠窗口、轻点呼出宿主轮盘、拖动跟随与防误触、
/// 声明式设置页配置素材目录、尺寸与动画开关。
/// </para>
/// </summary>
public sealed class DesktopPetPlugin : IStarPiePlugin
{
    private IPluginContext? _context;
    private PetController? _controller;
    private readonly List<IDisposable> _subscriptions = new();

    public void Initialize(IPluginContext context)
    {
        _context = context;

        RegisterLocalization(context);

        PetController controller = new(context);
        _controller = controller;

        string? iconKey = RegisterIcon(context);
        context.Actions.Register(new TogglePetContribution(context, controller, iconKey));
        context.Actions.Register(new ShowPetContribution(context, controller, iconKey));
        context.Actions.Register(new HidePetContribution(context, controller));
        context.Actions.Register(new ManagePetContribution(context, controller));

        RegisterSettingsPage(context);

        // 订阅设置项即时变更
        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.EnableAnimationKey,
            change =>
            {
                if (bool.TryParse(change.NewValue, out bool anim))
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                        {
                            return;
                        }
                        controller.SetAnimationEnabled(anim);
                    });
                }
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.SizeKey,
            change =>
            {
                if (double.TryParse(change.NewValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double size))
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                        {
                            return;
                        }
                        controller.SetSize(PetPreference.Clamp(size, Defaults.SizeMin, Defaults.SizeMax));
                    });
                }
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.AssetDirectoryKey,
            change =>
            {
                // Observe even a cleared directory before scheduling UI work. External saves
                // supersede any pending apply, so neither commit nor compensation can overwrite them.
                if (!controller.RegisterAssetDirectoryChange(change.NewValue)) return;
                if (!string.IsNullOrWhiteSpace(change.NewValue))
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                        {
                            return;
                        }

                        // 若此回调落后于当前最新持久化设置（例如已被回滚/补偿），则忽略迟到通知
                        string? currentPersisted = context.Settings.Get(PetPreference.AssetDirectoryKey);
                        if (!string.Equals(currentPersisted, change.NewValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        controller.ReloadAssets(change.NewValue);
                    });
                }
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.DragModeKey,
            change =>
            {
                context.Dispatcher.Post(() =>
                {
                    if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                    {
                        return;
                    }
                    controller.SetDragMode(change.NewValue);
                });
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.LongPressDurationKey,
            change =>
            {
                double duration;
                if (string.IsNullOrWhiteSpace(change.NewValue))
                {
                    duration = Defaults.DefaultLongPressDurationMs;
                }
                else if (double.TryParse(change.NewValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                         && double.IsFinite(parsed))
                {
                    duration = Math.Clamp(parsed, Defaults.MinLongPressDurationMs, Defaults.MaxLongPressDurationMs);
                }
                else
                {
                    duration = Defaults.DefaultLongPressDurationMs;
                }

                context.Dispatcher.Post(() =>
                {
                    if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                    {
                        return;
                    }
                    controller.SetLongPressDuration(duration);
                });
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.EnableDragPoseKey,
            change =>
            {
                if (bool.TryParse(change.NewValue, out bool enablePose))
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                        {
                            return;
                        }
                        controller.SetEnableDragPose(enablePose);
                    });
                }
            }));

        _subscriptions.Add(context.Settings.OnChanged(
            PetPreference.EnablePointingPoseKey,
            change =>
            {
                if (bool.TryParse(change.NewValue, out bool enablePose))
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (!ReferenceEquals(_context, context) || !ReferenceEquals(_controller, controller) || controller.IsShutdown)
                        {
                            return;
                        }
                        controller.SetEnablePointingPose(enablePose);
                    });
                }
            }));

        _subscriptions.Add(context.Settings.OnChanged(PetPreference.AnimationSpeedKey, _ =>
        {
            double speed = PetPreference.AnimationSpeed(context);
            context.Dispatcher.Post(() =>
            {
                if (ReferenceEquals(_context, context) && ReferenceEquals(_controller, controller) && !controller.IsShutdown)
                    controller.SetAnimationSpeed(speed);
            });
        }));
        WarnIfCapabilitiesMissing(context);

        // 本阶段规范约束：首次加载只注册贡献点，不自动弹出窗口。用户明确执行显示动作后才创建桌宠。
        context.Log.Info("Desktop pet initialized: toggle, settings commands and legacy actions registered.");
    }

    public void Shutdown()
    {
        foreach (var sub in _subscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _subscriptions.Clear();

        PetController? controller = _controller;
        _controller = null;
        IPluginContext? context = _context;
        _context = null;

        if (controller != null)
        {
            try
            {
                if (controller.HasWindow && context != null && !context.Dispatcher.IsOnUiThread)
                {
                    context.Dispatcher.InvokeAsync(controller.Shutdown).Wait(TimeSpan.FromSeconds(2));
                }
                else
                {
                    controller.Shutdown();
                }
            }
            catch (Exception ex)
            {
                context?.Log.Warn($"关闭桌宠窗口失败：{ex.Message}");
            }
        }
    }

    internal static void RegisterLocalization(IPluginContext context)
    {
        II18nRegistry i18n = context.I18n;

        i18n.Register("action.show-pet.name", "显示桌宠", "Show desktop pet");
        i18n.Register("action.hide-pet.name", "隐藏桌宠", "Hide desktop pet");
        i18n.Register("action.manage-pet.name", "管理桌宠", "Manage desktop pet");

        i18n.Register("window.library.title", "桌宠形象库", "Desktop Pet Library");
        i18n.Register("window.library.header", "桌宠形象管理", "Desktop Pet Library");
        i18n.Register("window.library.desc", " · 本地 PNG 文件夹导入、12 状态预览与形象切换", " · Local PNG folder import, 12-state preview and character switching");
        i18n.Register("window.library.btn.import", "导入本地文件夹...", "Import local folder...");
        i18n.Register("window.library.btn.refresh", "刷新", "Refresh");
        i18n.Register("window.library.list.title", "形象列表", "Characters");
        i18n.Register("window.library.empty", "形象库为空。\n点击上方「导入本地文件夹...」添加素材。", "Library is empty.\nClick 'Import local folder...' above to add characters.");
        i18n.Register("window.library.btn.current", "当前正在使用", "Currently Active");
        i18n.Register("window.library.status.active", "当前桌宠正在使用此形象。", "Desktop pet is currently using this character.");
        i18n.Register("window.library.status.ready", "点击右侧按钮将当前桌宠切换至该形象。", "Click the button to switch desktop pet to this character.");
        i18n.Register("window.library.btn.apply", "使用此形象", "Use This Character");
        i18n.Register("window.library.status.switching", "正在切换形象...", "Switching character...");
        i18n.Register("window.library.status.success", "形象切换成功！", "Character switched successfully!");
        i18n.Register("window.library.dialog.title", "选择包含桌宠 PNG 关键帧的本地文件夹", "Select local folder containing desktop pet PNG frames");
        i18n.Register("window.library.status.importing", "正在校验与导入素材...", "Validating and importing assets...");
        i18n.Register("window.library.error.import", "导入失败：", "Import failed: ");
        i18n.Register("window.library.meta.format", "ID: {0} · 画布: {1}×{2} · 已覆盖 {3}/12 状态", "ID: {0} · Canvas: {1}×{2} · Covered {3}/12 states");
        i18n.Register("window.library.missing.suffix", " (缺)", " (Missing)");
        i18n.Register("window.library.fallback.idle", "（未包含，回退至常驻）", "(Not included, fallback to idle)");
        i18n.Register("window.library.badge.active", "当前使用", "Active");
        i18n.Register("window.library.card.states", "{0} 状态", "{0} states");
        i18n.Register("window.library.error.exception", "导入异常：{0}", "Import exception: {0}");
        i18n.Register("window.library.status.canceled", "已取消切换。", "Character switch was canceled.");
        i18n.Register("window.library.error.apply", "切换异常：{0}", "Apply exception: {0}");
        i18n.Register("window.library.page.format", "{0} / {1} 页", "Page {0} / {1}");
        i18n.Register("window.library.btn.prev", "上一页", "Previous");
        i18n.Register("window.library.btn.next", "下一页", "Next");
        i18n.Register("controller.apply.shutdown", "插件已停止。", "Desktop pet is shutdown.");
        i18n.Register("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。", "Desktop pet is currently busy interacting, please try again later.");
        i18n.Register("controller.apply.superseded", "已被后续切换请求取代。", "Superseded by a newer apply request.");
        i18n.Register("controller.apply.success", "已切换至形象「{0}」。", "Switched to character '{0}'.");
        i18n.Register("controller.apply.missing_idle", "形象素材目录缺少必需的 idle.png：{0}", "Character asset directory is missing idle.png: {0}");
        i18n.Register("controller.apply.not_found", "形象素材目录不存在：{0}", "Character asset directory does not exist: {0}");
        i18n.Register("controller.apply.save_failed", "设置保存失败：{0}", "Failed to save settings: {0}");
        i18n.Register("controller.apply.compensate_failed", "回滚设置保存失败：{0}", "Failed to compensate settings rollback: {0}");
        i18n.Register("library.import.success", "形象「{0}」导入成功。", "Character '{0}' imported successfully.");
        i18n.Register("library.import.duplicate", "形象库中已存在相同内容的形象「{0}」。", "Character library already contains character '{0}' with identical content.");
        i18n.Register("library.import.dir_exists", "形象目录已存在有效内容且未被当前索引引用，已拒绝覆盖：{0}", "Character directory already exists with valid content and is not in index; overwrite refused: {0}");
        i18n.Register("library.import.not_found", "素材文件夹不存在：{0}", "Asset directory does not exist: {0}");
        i18n.Register("library.import.missing_idle", "素材文件夹缺少必需的 idle.png 关键帧：{0}", "Asset directory is missing required idle.png keyframe: {0}");
        i18n.Register("library.import.reparse_point", "安全限制：禁止导入符号链接或重解析点：{0}", "Security limit: Reparse points or symbolic links prohibited: {0}");
        i18n.Register("library.import.corrupt_png", "解码图帧文件 {0} 失败，可能已损坏：{1}", "Failed to decode frame file {0}, possibly corrupt: {1}");
        i18n.Register("library.import.canvas_mismatch", "画布尺寸不一致：idle.png 为 {0}x{1}，而 {2} 为 {3}x{4}。", "Canvas size mismatch: idle.png is {0}x{1}, but {2} is {3}x{4}.");
        i18n.Register("library.import.index_failed", "形象库索引保存失败：{0}", "Failed to save character library index: {0}");
        i18n.Register("library.import.empty_source", "指定的素材文件夹路径为空。", "Specified asset folder path is empty.");
        i18n.Register("library.import.invalid_path", "路径格式无效：{0}", "Invalid path format: {0}");
        i18n.Register("library.import.check_attributes_failed", "检查目录属性失败：{0}", "Failed to check directory attributes: {0}");
        i18n.Register("library.import.file_size_exceeded", "图帧文件 {0} 超过单文件大小上限（{1} 字节，上限 {2} 字节）。", "Frame file {0} exceeds single file size limit ({1} bytes, limit {2} bytes).");
        i18n.Register("library.import.read_file_failed", "读取图帧文件 {0} 失败：{1}", "Failed to read frame file {0}: {1}");
        i18n.Register("library.import.no_valid_png", "图帧文件 {0} 中未找到有效 PNG 帧。", "No valid PNG frame found in file {0}.");
        i18n.Register("library.import.dimension_exceeded", "图帧文件 {0} 尺寸超出合法范围（{1}x{2}，单边上限 {3}）。", "Frame file {0} dimensions exceed valid range ({1}x{2}, max dimension {3}).");
        i18n.Register("library.import.no_canvas_size", "无法确定有效的主图画布尺寸。", "Cannot determine valid canvas size for base frame.");
        i18n.Register("library.import.failed_exception", "导入形象时发生异常：{0}", "Exception occurred while importing character: {0}");

        i18n.Register("settings.page.title", "桌宠设置", "Desktop pet settings");
        i18n.Register("settings.page.description",
            "这里是桌宠的本地素材目录、显示尺寸、动画与拖动设置："
            + "本地素材目录存放 idle.png 以及可选的 blink.png、react.png、drag.png；"
            + "支持配置立即拖动与长按后拖动；"
            + "保存设置后，尺寸、动画开关与拖动配置即时生效，修改素材目录将在下一次显示或重载时生效。",
            "Configure local asset folder, size, animation, and drag behavior for the desktop pet. "
            + "The asset directory should contain idle.png and optional blink.png/react.png/drag.png. "
            + "Supports immediate drag and long-press drag modes. "
            + "Size, animation, and drag changes take effect immediately; asset directory changes apply on next show or reload.");

        i18n.Register("field.asset-directory.label", "本地素材目录", "Local asset directory");
        i18n.Register("field.size.label", "桌宠尺寸", "Pet size");
        i18n.Register("field.animation.label", "播放动画", "Play animation");
        i18n.Register("field.drag-mode.label", "拖动方式", "Drag mode");
        i18n.Register("field.drag-mode.immediate", "立即拖动", "Immediate drag");
        i18n.Register("field.drag-mode.long-press", "长按后拖动", "Long-press drag");
        i18n.Register("field.long-press-duration.label", "长按时长", "Long-press duration");
        i18n.Register("field.drag-pose.label", "启用拖动姿势", "Enable drag pose");
        i18n.Register("field.pointing-pose.label", "启用轮盘指向姿势", "Enable wheel pointing pose");

        i18n.Register("preview.show-pet", "显示桌宠 · 尺寸 {0}px", "Show desktop pet · Size {0}px");
        i18n.Register("preview.hide-pet", "收掉桌宠", "Dismiss desktop pet");

        i18n.Register("notify.no-ui-capability", "清单未声明「Ui（界面）」能力，无法创建桌宠窗口。", "The manifest does not declare 'Ui' capability, cannot create desktop pet window.");
        i18n.Register("notify.asset-missing", "桌宠素材未就绪，请在插件设置中选择包含 idle.png 的素材目录。", "Desktop pet assets not ready. Please select an asset folder containing idle.png in settings.");

        i18n.Register("error.cancelled", "动作已被取消。", "The action was cancelled.");
        i18n.Register("error.size-number", "尺寸必须是一个数值。", "Size must be a valid number.");
        i18n.Register("error.size-range", "尺寸必须在 {0} 到 {1} 之间。", "Size must be between {0} and {1}.");
        i18n.Register("error.show-failed", "桌宠无法显示：{0}", "Desktop pet could not be shown: {0}");
        i18n.Register("error.hide-failed", "桌宠没能收起来：{0}", "Desktop pet could not be hidden: {0}");
        PetControlText.Register(i18n);
    }

    private static void RegisterSettingsPage(IPluginContext context)
    {
        context.SettingsPage.Register(new SettingsPageDescriptor
        {
            Title = "桌宠设置",
            TitleKey = "settings.page.title",
            Description = "这里是桌宠的本地素材目录、显示尺寸、动画与拖动设置："
                + "本地素材目录存放 idle.png 以及可选的 blink.png、react.png、drag.png；"
                + "支持配置立即拖动与长按后拖动；"
                + "保存设置后，尺寸、动画开关与拖动配置即时生效，修改素材目录将在下一次显示或重载时生效。",
            DescriptionKey = "settings.page.description",
            Fields = PetSettingsFields.Build(),
            ActionIds = new[] { "managePet" },
            Sections = new[]
            {
                new SettingsSectionDescriptor
                {
                    Id = "animation",
                    Title = "动画设置",
                    TitleKey = "settings.animation.title",
                    DescriptionKey = "settings.animation.description",
                    FieldKeys = new[] { PetPreference.EnableAnimationKey, PetPreference.AnimationSpeedKey, PetPreference.EnableDragPoseKey, PetPreference.EnablePointingPoseKey },
                },
            },
        });
    }

    private static string? RegisterIcon(IPluginContext context)
    {
        try
        {
            // 朴素安全的 M/C/L/Z 矢量图标：小猫/爪印剪影
            return context.Icons.RegisterSvg(
                "pet",
                "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 14.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm2-6c-.83 0-1.5-.67-1.5-1.5S12.17 7.5 13 7.5s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z");
        }
        catch (Exception ex)
        {
            context.Log.Warn($"桌宠图标注册失败（动作仍可用）：{ex.Message}");
            return null;
        }
    }

    private static void WarnIfCapabilitiesMissing(IPluginContext context)
    {
        var missing = new List<string>(2);
        if (!context.Info.HasCapability(PluginCapability.Ui)) missing.Add("Ui（画桌宠）");
        if (!context.Info.HasCapability(PluginCapability.Wheel)) missing.Add("Wheel（呼出轮盘）");

        if (missing.Count > 0)
        {
            context.Log.Warn($"清单缺少必要能力：{string.Join("、", missing)}。请在 plugin.json 中补齐。");
        }
    }
}
