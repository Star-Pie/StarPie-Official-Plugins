using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 桌宠窗口的生命周期控制器。<b>所有窗口操控方法仅限 UI 线程调用</b>。
/// <para>
/// 负责素材加载协调、窗口创建与重建、位置持久化、轮盘服务转发与停用清理。
/// </para>
/// </summary>
internal sealed class PetController
{
    private readonly IPluginContext _context;
    private readonly PetAssetManager _assetManager;
    private readonly PetLibraryManager _libraryManager;

    private PetWindow? _window;
    private PetLibraryWindow? _libraryWindow;
    private double _currentSize;
    private bool _isShutdown;
    private int _sessionGenerationCounter;
    private WheelRequestSession? _activeSession;
    private long _applyGeneration;
    private ApplyTransaction? _activeTransaction;
    private int _settingsSaveThreadId;
    private string? _settingsSaveDirectory;
    private readonly object _txGate = new();
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    public PetController(IPluginContext context)
    {
        _context = context;
        _assetManager = new PetAssetManager();
        _libraryManager = new PetLibraryManager(context.DataDirectory);
    }

    public bool HasWindow => _window != null;
    public bool IsShutdown => _isShutdown;
    public PetAssetManager Assets => _assetManager;
    public PetLibraryManager LibraryManager => _libraryManager;
    internal bool IsHeadlessTestMode { get; set; }
    internal PetWindow? Window => _window;
    internal PetLibraryWindow? LibraryWindow => _libraryWindow;

    /// <summary>
    /// 是否处于忙碌交互状态（鼠标拖动或轮盘请求正在处理/呈现中）。
    /// </summary>
    public bool IsBusy => (_window != null && (_window.IsDragging || _window.IsPressed))
        || (_activeSession != null && !_activeSession.IsTerminated);

    /// <summary>
    /// 显示桌宠。若素材未配置或加载失败，给出提示并返回 false。
    /// </summary>
    public bool Show(
        double? customSize = null,
        string? customAssetDir = null,
        bool? customEnableAnimation = null,
        bool forceReloadAssets = false)
    {
        if (_isShutdown) return false;

        double size = customSize ?? PetPreference.Size(_context);
        string assetDir = !string.IsNullOrWhiteSpace(customAssetDir)
            ? customAssetDir
            : PetPreference.AssetDirectory(_context);
        bool enableAnim = customEnableAnimation ?? PetPreference.EnableAnimation(_context);

        bool needLoadAssets = forceReloadAssets
            || !_assetManager.HasIdleImage
            || !string.Equals(_assetManager.CurrentDirectory, assetDir, StringComparison.OrdinalIgnoreCase);

        // 1. 素材加载校验：失败时保留已有窗口与有效素材状态
        if (needLoadAssets)
        {
            var loadResult = _assetManager.LoadFromDirectory(assetDir);
            if (!loadResult.Success)
            {
                _context.Log.Warn($"无法显示/刷新桌宠：{loadResult.Message}");
                _context.Notify.Notify("桌宠", loadResult.Message ?? "未找到有效的 idle.png 素材。");
                return false;
            }
        }

        // 2. 实例复用与重建判定
        if (_window != null && Math.Abs(_currentSize - size) < 0.1)
        {
            _window.EnableAnimation = enableAnim;
            _window.AnimationSpeed = PetPreference.AnimationSpeed(_context);
            _window.DragMode = string.Equals(PetPreference.DragMode(_context), Defaults.DragModeLongPress, StringComparison.OrdinalIgnoreCase)
                ? DragMode.LongPress
                : DragMode.Immediate;
            _window.LongPressDurationMs = PetPreference.LongPressDuration(_context);
            _window.EnableDragPose = PetPreference.EnableDragPose(_context);
            _window.EnablePointingPose = PetPreference.EnablePointingPose(_context);
            if (needLoadAssets)
            {
                _window.RefreshAssets();
            }
            if (!_window.IsVisible && !IsHeadlessTestMode)
            {
                _window.Show();
            }
            MarkVisible();
            return true;
        }

        int? keepLeft = null;
        int? keepTop = null;
        if (_window != null)
        {
            PetWindow.PhysicalRect rect = _window.ReadPhysicalRect();
            if (rect.Width > 0 && rect.Height > 0)
            {
                keepLeft = rect.X;
                keepTop = rect.Y;
            }
            CloseWindow();
        }

        CreateAndShowWindow(size, enableAnim, keepLeft, keepTop);
        MarkVisible();
        return true;
    }

    /// <summary>
    /// 隐藏桌宠并记录状态（关窗释放资源，防止内存驻留）。
    /// </summary>
    public void Hide()
    {
        if (_isShutdown) return;

        _context.Settings.Set(PetPreference.VisibleKey, "false");
        _context.Settings.Save();

        CloseWindow();
    }

    /// <summary>
    /// 响应动画设置即时变更。
    /// </summary>
    public void SetAnimationEnabled(bool enabled)
    {
        if (_isShutdown) return;

        if (_window != null)
        {
            _window.EnableAnimation = enabled;
        }
    }

    public void SetAnimationSpeed(double speed)
    {
        if (_isShutdown) return;
        if (_window != null) _window.AnimationSpeed = speed;
    }

    /// <summary>
    /// 响应尺寸设置即时变更。
    /// </summary>
    public void SetSize(double newSize)
    {
        if (_isShutdown) return;

        if (_window != null && Math.Abs(_currentSize - newSize) >= 0.1)
        {
            Show(customSize: newSize);
        }
    }

    /// <summary>
    /// 响应拖动方式设置即时变更。
    /// </summary>
    public void SetDragMode(string? mode)
    {
        if (_isShutdown) return;
        if (_window != null)
        {
            _window.DragMode = string.Equals(mode, Defaults.DragModeLongPress, StringComparison.OrdinalIgnoreCase)
                ? DragMode.LongPress
                : DragMode.Immediate;
        }
    }

    /// <summary>
    /// 响应长按时长设置即时变更。
    /// </summary>
    public void SetLongPressDuration(double duration)
    {
        if (_isShutdown) return;
        double safeValue = (!double.IsFinite(duration))
            ? Defaults.DefaultLongPressDurationMs
            : Math.Clamp(duration, Defaults.MinLongPressDurationMs, Defaults.MaxLongPressDurationMs);
        if (_window != null)
        {
            _window.LongPressDurationMs = safeValue;
        }
    }

    /// <summary>
    /// 响应启用拖动姿势设置即时变更。
    /// </summary>
    public void SetEnableDragPose(bool enable)
    {
        if (_isShutdown) return;
        if (_window != null)
        {
            _window.EnableDragPose = enable;
        }
    }

    /// <summary>
    /// 响应启用轮盘指向姿势设置即时变更。
    /// </summary>
    public void SetEnablePointingPose(bool enable)
    {
        if (_isShutdown) return;
        if (_window != null)
        {
            _window.EnablePointingPose = enable;
        }
    }

    /// <summary>
    /// 仅识别本次内部 Save 同步调用栈中的通知，不按整个事务的目录值过滤。
    /// </summary>
    internal bool IsSelfOriginatedDirectoryChange(string? dir)
    {
        lock (_txGate)
        {
            return _settingsSaveThreadId == Environment.CurrentManagedThreadId
                && (_activeTransaction == null || !_activeTransaction.IsExternallySuperseded)
                && string.Equals(_settingsSaveDirectory, dir, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal bool RegisterAssetDirectoryChange(string? dir)
    {
        lock (_txGate)
        {
            if (IsSelfOriginatedDirectoryChange(dir)) return false;
            if (_activeTransaction != null && !_activeTransaction.IsCommitted)
            {
                _activeTransaction.IsExternallySuperseded = true;
                Interlocked.Increment(ref _applyGeneration);
            }
            return true;
        }
    }

    private void SaveApplySettings(string? directory)
    {
        lock (_txGate)
        {
            _settingsSaveThreadId = Environment.CurrentManagedThreadId;
            _settingsSaveDirectory = directory;
        }
        try
        {
            // Host Save notifies synchronously. Do not hold _txGate across its IO/save lock.
            _context.Settings.Save();
        }
        finally
        {
            lock (_txGate)
            {
                _settingsSaveThreadId = 0;
                _settingsSaveDirectory = null;
            }
        }
    }

    /// <summary>
    /// 响应素材目录变更或显式重载。若未显式指定目录，则默认重载当前已加载目录。
    /// </summary>
    public bool ReloadAssets(string? newDir = null)
    {
        if (_isShutdown) return false;

        // 1. 若显式传入与当前已加载目录完全相同，说明素材已在内存中就绪，无需重复读盘
        if (!string.IsNullOrWhiteSpace(newDir) &&
            string.Equals(_assetManager.CurrentDirectory, newDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string targetDir = !string.IsNullOrWhiteSpace(newDir)
            ? newDir
            : (_assetManager.CurrentDirectory ?? PetPreference.AssetDirectory(_context));

        if (_window != null)
        {
            return Show(customAssetDir: targetDir, forceReloadAssets: true);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(targetDir)) return false;
            var result = _assetManager.LoadFromDirectory(targetDir);
            return result.Success;
        }
    }

    /// <summary>
    /// 彻底销毁窗口并清理资源。
    /// </summary>
    public void Shutdown()
    {
        _isShutdown = true;
        CloseManagerWindow();
        DisposeWheelSession();
        CloseWindow();
    }

    /// <summary>
    /// 打开形象管理面板。
    /// </summary>
    public void OpenManagerWindow()
    {
        if (_isShutdown) return;

        if (_libraryWindow != null)
        {
            if (!IsHeadlessTestMode)
            {
                if (_libraryWindow.IsVisible)
                {
                    _libraryWindow.Activate();
                }
                else
                {
                    _libraryWindow.Show();
                }
            }
            return;
        }

        var win = new PetLibraryWindow(_context, this, _libraryManager);
        win.Closed += (s, e) =>
        {
            if (ReferenceEquals(_libraryWindow, win))
            {
                _libraryWindow = null;
            }
        };

        _libraryWindow = win;

        if (!IsHeadlessTestMode)
        {
            win.Show();
        }
    }

    /// <summary>
    /// 关闭形象管理面板。
    /// </summary>
    public void CloseManagerWindow()
    {
        if (_libraryWindow == null) return;

        var win = _libraryWindow;
        _libraryWindow = null;

        try
        {
            win.Close();
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"关闭桌宠形象库管理窗口异常：{ex.Message}");
        }
    }

    internal async Task<PetLibraryEditResult> RemoveLibraryItemAsync(string id, CancellationToken cancellationToken = default)
    {
        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            if (_isShutdown || IsBusy) return new(false, "library.edit.busy");
            return await _libraryManager.RemoveItemAsync(id, IsLibraryItemProtected, cancellationToken);
        }
        finally { _applyGate.Release(); }
    }

    internal bool IsLibraryItemProtected(PetLibraryItem item)
    {
        string directory = Path.Combine(_libraryManager.LibraryRoot, item.RelativeDirectory);
        return _isShutdown || IsBusy || item.Id == PetPreference.ActiveLibraryId(_context) ||
            SameDirectory(directory, _assetManager.CurrentDirectory) || SameDirectory(directory, _context.Settings.Get(PetPreference.AssetDirectoryKey));
    }

    private static bool SameDirectory(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>
    /// 安全切换当前桌宠形象。
    /// 处于忙碌状态（拖动中或轮盘交互会话活跃）时返回 Busy；若失败则保持旧图与旧设置。
    /// </summary>
    public PetApplyResult ApplyLibraryItem(PetLibraryItem item)
    {
        if (_isShutdown) return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
        if (item == null) return PetApplyResult.Fail("指定形象为空。");

        if (IsBusy)
        {
            return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");
        }

        if (!_applyGate.Wait(0))
        {
            return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");
        }

        try
        {
            var registeredItem = _libraryManager.FindItemById(item.Id);
            if (registeredItem == null) return PetApplyResult.Fail(_context.I18n.T("library.edit.missing"), "library.edit.missing");
            item = registeredItem;
            string targetDir = Path.Combine(_libraryManager.LibraryRoot, item.RelativeDirectory);
            if (!Directory.Exists(targetDir))
            {
                return PetApplyResult.Fail(_context.I18n.T("controller.apply.not_found", $"形象素材目录不存在：{targetDir}"), "controller.apply.not_found", targetDir);
            }

            string idlePath = Path.Combine(targetDir, "idle.png");
            if (!File.Exists(idlePath))
            {
                return PetApplyResult.Fail(_context.I18n.T("controller.apply.missing_idle", $"形象素材目录缺少必需的 idle.png：{targetDir}"), "controller.apply.missing_idle", targetDir);
            }

            long reqGen = Interlocked.Increment(ref _applyGeneration);
            var (loadResult, candidateSnapshot) = PetAssetManager.LoadSnapshotFromDirectory(targetDir);
            if (!loadResult.Success || candidateSnapshot == null)
            {
                return PetApplyResult.Fail(loadResult.Message ?? "素材加载失败。");
            }

            return CommitApplyCandidate(item, targetDir, candidateSnapshot, reqGen);
        }
        finally
        {
            lock (_txGate)
            {
                _activeTransaction = null;
            }
            _applyGate.Release();
        }
    }

    /// <summary>
    /// 真正后台准备素材快照与持久化设置，短 UI 提交仅做内存位图替换与窗口刷新（UI 线程 0 磁盘 IO）。
    /// </summary>
    public async Task<PetApplyResult> ApplyLibraryItemAsync(PetLibraryItem item, CancellationToken cancellationToken = default)
    {
        if (item == null) return PetApplyResult.Fail("指定形象为空。");
        if (_isShutdown) return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
        if (IsBusy) return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");

        cancellationToken.ThrowIfCancellationRequested();

        long reqGen = Interlocked.Increment(ref _applyGeneration);
        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            var registeredItem = _libraryManager.FindItemById(item.Id);
            if (registeredItem == null) return PetApplyResult.Fail(_context.I18n.T("library.edit.missing"), "library.edit.missing");
            item = registeredItem;
            string targetDir = Path.Combine(_libraryManager.LibraryRoot, item.RelativeDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            if (_isShutdown) return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
            if (IsBusy) return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");
            if (reqGen != Interlocked.Read(ref _applyGeneration))
            {
                return PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded");
            }

            // 在后台工作线程执行解码、校验、冻结与设置持久化，确保 UI 线程 0 磁盘 IO
            var (backgroundResult, transaction) = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(targetDir))
                {
                    return (PetApplyResult.Fail(_context.I18n.T("controller.apply.not_found", $"形象素材目录不存在：{targetDir}"), "controller.apply.not_found", targetDir), (ApplyTransaction?)null);
                }
                string idlePath = Path.Combine(targetDir, "idle.png");
                if (!File.Exists(idlePath))
                {
                    return (PetApplyResult.Fail(_context.I18n.T("controller.apply.missing_idle", $"形象素材目录缺少必需的 idle.png：{targetDir}"), "controller.apply.missing_idle", targetDir), (ApplyTransaction?)null);
                }

                var (loadResult, candidateSnapshot) = PetAssetManager.LoadSnapshotFromDirectory(targetDir);
                if (!loadResult.Success || candidateSnapshot == null)
                {
                    return (PetApplyResult.Fail(loadResult.Message ?? "素材加载失败。"), (ApplyTransaction?)null);
                }

                if (_isShutdown)
                {
                    return (PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown"), (ApplyTransaction?)null);
                }
                if (IsBusy)
                {
                    return (PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy"), (ApplyTransaction?)null);
                }
                if (reqGen != Interlocked.Read(ref _applyGeneration))
                {
                    return (PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded"), (ApplyTransaction?)null);
                }

                string? prevActiveId = _context.Settings.Get(PetPreference.ActiveLibraryIdKey);
                string? prevAssetDir = _context.Settings.Get(PetPreference.AssetDirectoryKey);
                string nonceKey = "desktop_pet_settings_nonce";
                string? prevNonce = _context.Settings.Get(nonceKey);
                var prevAssetSnapshot = _assetManager.CreateSnapshot();

                var currentTx = new ApplyTransaction(
                    reqGen,
                    item.Id,
                    item.Name,
                    targetDir,
                    candidateSnapshot,
                    prevActiveId,
                    prevAssetDir,
                    prevNonce,
                    prevAssetSnapshot);

                lock (_txGate)
                {
                    _activeTransaction = currentTx;
                }

                string newNonce = Guid.NewGuid().ToString("N");
                bool confirmed = false;
                using var subNonce = _context.Settings.OnChanged(nonceKey, _ => confirmed = true);
                using var subId = _context.Settings.OnChanged(PetPreference.ActiveLibraryIdKey, change =>
                {
                    if (string.Equals(change.NewValue, item.Id, StringComparison.Ordinal))
                    {
                        confirmed = true;
                    }
                });

                bool saveSuccess = false;
                string? saveErrorMessage = null;

                try
                {
                    _context.Settings.Set(PetPreference.ActiveLibraryIdKey, item.Id);
                    _context.Settings.Set(PetPreference.AssetDirectoryKey, targetDir);
                    _context.Settings.Set(nonceKey, newNonce);

                    SaveApplySettings(targetDir);

                    if (confirmed)
                    {
                        saveSuccess = true;
                    }
                    else
                    {
                        saveErrorMessage = "设置保存未收到持久化通知（可能被宿主静默忽略或写入未生效）。";
                    }
                }
                catch (Exception ex)
                {
                    saveErrorMessage = ex.Message;
                }

                if (!saveSuccess)
                {
                    lock (_txGate)
                    {
                        if (!currentTx.IsExternallySuperseded)
                        {
                            _context.Settings.Set(PetPreference.ActiveLibraryIdKey, prevActiveId);
                            _context.Settings.Set(PetPreference.AssetDirectoryKey, prevAssetDir);
                            _context.Settings.Set(nonceKey, prevNonce);
                        }
                        if (ReferenceEquals(_activeTransaction, currentTx)) _activeTransaction = null;
                    }
                    return (PetApplyResult.Fail($"设置保存失败：{saveErrorMessage}", "controller.apply.save_failed", saveErrorMessage ?? ""), (ApplyTransaction?)null);
                }

                currentTx.IsPersisted = true;
                return (PetApplyResult.Ok(item.Id, "保存完成"), currentTx);
            }, cancellationToken);

            if (!backgroundResult.IsSuccess || transaction == null)
            {
                return backgroundResult;
            }

            // 步骤 2：在同一个 _applyGate 锁内执行 UI 最终提交或回滚补偿
            return await FinalizeApplyCommitUnderGateAsync(transaction, cancellationToken);
        }
        finally
        {
            lock (_txGate)
            {
                if (_activeTransaction != null && _activeTransaction.Generation == reqGen)
                {
                    _activeTransaction = null;
                }
            }
            _applyGate.Release();
        }
    }

    private async Task<PetApplyResult> FinalizeApplyCommitUnderGateAsync(ApplyTransaction tx, CancellationToken cancellationToken)
    {
        bool isCancelled = cancellationToken.IsCancellationRequested;
        bool isShutdown = _isShutdown;
        bool isBusy = IsBusy;
        bool isSuperseded = tx.Generation != Interlocked.Read(ref _applyGeneration);

        if (isCancelled || isShutdown || isBusy || isSuperseded)
        {
            bool compensated = await CompensateTransactionUnderGateAsync(tx);

            if (!compensated)
            {
                return PetApplyResult.Fail(
                    _context.I18n.T("controller.apply.compensate_failed", "回滚设置保存失败。"),
                    "controller.apply.compensate_failed",
                    "回滚未确认");
            }

            if (isCancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (isShutdown)
            {
                return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
            }

            if (isBusy)
            {
                return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");
            }

            return PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded");
        }

        // 短 UI 提交（仅内存位图指针切换与 Window.RefreshAssets()，0 磁盘 IO）
        if (System.Windows.Application.Current?.Dispatcher != null &&
            !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                bool uiCancelled = cancellationToken.IsCancellationRequested;
                bool uiShutdown = _isShutdown;
                bool uiBusy = IsBusy;
                bool uiSuperseded = tx.Generation != Interlocked.Read(ref _applyGeneration);

                if (uiCancelled || uiShutdown || uiBusy || uiSuperseded)
                {
                    bool compensated = await CompensateTransactionUnderGateAsync(tx);
                    if (!compensated)
                    {
                        return PetApplyResult.Fail(
                            _context.I18n.T("controller.apply.compensate_failed", "回滚设置保存失败。"),
                            "controller.apply.compensate_failed",
                            "回滚未确认");
                    }
                    if (uiCancelled)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (uiShutdown)
                    {
                        return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
                    }
                    if (uiBusy)
                    {
                        return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");
                    }
                    return PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded");
                }

                return CommitTransactionMemoryOnly(tx);
            }).Task.Unwrap();
        }

        return CommitTransactionMemoryOnly(tx);
    }

    private PetApplyResult CommitTransactionMemoryOnly(ApplyTransaction tx)
    {
        lock (_txGate)
        {
            if (tx.IsExternallySuperseded || tx.Generation != Interlocked.Read(ref _applyGeneration))
            {
                return PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded");
            }

            // The final ownership check and bitmap swap must be atomic with external notifications.
            _assetManager.RestoreSnapshot(tx.CandidateSnapshot);
            _window?.RefreshAssets();
            tx.IsCommitted = true;
            if (ReferenceEquals(_activeTransaction, tx))
            {
                _activeTransaction = null;
            }
        }

        string okMsg = string.Format(
            _context.I18n.T("controller.apply.success", "已切换至形象「{0}」。"),
            tx.ItemName);
        return PetApplyResult.Ok(tx.ItemId, okMsg, "controller.apply.success", tx.ItemName);
    }

    private async Task<bool> CompensateTransactionUnderGateAsync(ApplyTransaction tx)
    {
        lock (_txGate)
        {
            if (tx.IsCompensated) return true;
            // An external persisted choice owns the settings now; compensation must not overwrite it.
            if (tx.IsExternallySuperseded) return true;
        }

        if (!tx.IsPersisted)
        {
            lock (_txGate)
            {
                if (ReferenceEquals(_activeTransaction, tx))
                {
                    _context.Settings.Set(PetPreference.ActiveLibraryIdKey, tx.PrevActiveId);
                    _context.Settings.Set(PetPreference.AssetDirectoryKey, tx.PrevAssetDir);
                    _context.Settings.Set("desktop_pet_settings_nonce", tx.PrevNonce);
                    _activeTransaction = null;
                }
                tx.IsCompensated = true;
            }
            return true;
        }

        return await Task.Run(() =>
        {
            lock (_txGate)
            {
                if (tx.IsExternallySuperseded) return true;
            }
            string nonceKey = "desktop_pet_settings_nonce";
            string rollbackNonce = Guid.NewGuid().ToString("N");
            bool confirmed = false;
            using var subNonce = _context.Settings.OnChanged(nonceKey, _ => confirmed = true);
            using var subId = _context.Settings.OnChanged(PetPreference.ActiveLibraryIdKey, change =>
            {
                if (string.Equals(change.NewValue, tx.PrevActiveId, StringComparison.Ordinal))
                {
                    confirmed = true;
                }
            });

            try
            {
                _context.Settings.Set(PetPreference.ActiveLibraryIdKey, tx.PrevActiveId);
                _context.Settings.Set(PetPreference.AssetDirectoryKey, tx.PrevAssetDir);
                _context.Settings.Set(nonceKey, rollbackNonce);
                SaveApplySettings(tx.PrevAssetDir);

                if (confirmed)
                {
                    lock (_txGate)
                    {
                        tx.IsCompensated = true;
                        if (ReferenceEquals(_activeTransaction, tx))
                        {
                            _activeTransaction = null;
                        }
                    }
                    return true;
                }
                else
                {
                    _context.Log.Warn("回滚形象设置持久化未收到确认通知。");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _context.Log.Warn($"回滚形象设置持久化失败：{ex.Message}");
                return false;
            }
            finally
            {
                lock (_txGate)
                {
                    if (ReferenceEquals(_activeTransaction, tx))
                    {
                        _activeTransaction = null;
                    }
                }
            }
        });
    }

    private PetApplyResult CommitApplyCandidate(
        PetLibraryItem item,
        string targetDir,
        PetAssetSnapshot candidateSnapshot,
        long reqGen)
    {
        if (_isShutdown) return PetApplyResult.Fail(_context.I18n.T("controller.apply.shutdown", "插件已停止。"), "controller.apply.shutdown");
        if (IsBusy) return PetApplyResult.Busy(_context.I18n.T("controller.apply.busy", "桌宠当前正在交互中，请稍候再试。"), "controller.apply.busy");

        if (reqGen != Interlocked.Read(ref _applyGeneration))
        {
            return PetApplyResult.Fail(_context.I18n.T("controller.apply.superseded", "已被后续切换请求取代。"), "controller.apply.superseded");
        }

        string? prevActiveId = _context.Settings.Get(PetPreference.ActiveLibraryIdKey);
        string? prevAssetDir = _context.Settings.Get(PetPreference.AssetDirectoryKey);
        var prevAssetSnapshot = _assetManager.CreateSnapshot();
        string nonceKey = "desktop_pet_settings_nonce";
        string? prevNonce = _context.Settings.Get(nonceKey);

        var tx = new ApplyTransaction(
            reqGen,
            item.Id,
            item.Name,
            targetDir,
            candidateSnapshot,
            prevActiveId,
            prevAssetDir,
            prevNonce,
            prevAssetSnapshot);

        lock (_txGate)
        {
            _activeTransaction = tx;
        }

        bool confirmed = false;
        string newNonce = Guid.NewGuid().ToString("N");

        using var subNonce = _context.Settings.OnChanged(nonceKey, _ => confirmed = true);
        using var subId = _context.Settings.OnChanged(PetPreference.ActiveLibraryIdKey, change =>
        {
            if (string.Equals(change.NewValue, item.Id, StringComparison.Ordinal))
            {
                confirmed = true;
            }
        });

        bool saveSuccess = false;
        string? saveErrorMessage = null;

        try
        {
            _context.Settings.Set(PetPreference.ActiveLibraryIdKey, item.Id);
            _context.Settings.Set(PetPreference.AssetDirectoryKey, targetDir);
            _context.Settings.Set(nonceKey, newNonce);

            SaveApplySettings(targetDir);

            if (confirmed)
            {
                saveSuccess = true;
            }
            else
            {
                saveErrorMessage = "设置保存未收到持久化通知（可能被宿主静默忽略或写入未生效）。";
            }
        }
        catch (Exception ex)
        {
            saveErrorMessage = ex.Message;
        }
        if (!saveSuccess)
        {
            _context.Log.Warn($"保存形象设置失败，正在回滚内存设置与素材：{saveErrorMessage}");

            lock (_txGate)
            {
                if (!tx.IsExternallySuperseded)
                {
                    _context.Settings.Set(PetPreference.ActiveLibraryIdKey, prevActiveId);
                    _context.Settings.Set(PetPreference.AssetDirectoryKey, prevAssetDir);
                    _context.Settings.Set(nonceKey, prevNonce);
                    _assetManager.RestoreSnapshot(prevAssetSnapshot);
                }
            }

            return PetApplyResult.Fail($"设置保存失败：{saveErrorMessage}", "controller.apply.save_failed", saveErrorMessage ?? "");
        }

        tx.IsPersisted = true;
        return CommitTransactionMemoryOnly(tx);
    }

    private void CreateAndShowWindow(double sizeDiu, bool enableAnim, int? left, int? top)
    {
        if (_isShutdown) return;

        var window = new PetWindow(sizeDiu, _assetManager, enableAnim)
        {
            AnimationSpeed = PetPreference.AnimationSpeed(_context),
            DragMode = string.Equals(PetPreference.DragMode(_context), Defaults.DragModeLongPress, StringComparison.OrdinalIgnoreCase)
                ? DragMode.LongPress
                : DragMode.Immediate,
            LongPressDurationMs = PetPreference.LongPressDuration(_context),
            EnableDragPose = PetPreference.EnableDragPose(_context),
            EnablePointingPose = PetPreference.EnablePointingPose(_context),
        };

        int posX = left ?? ReadInt(PetPreference.LeftKey) ?? int.MinValue;
        int posY = top ?? ReadInt(PetPreference.TopKey) ?? int.MinValue;

        if (posX != int.MinValue && posY != int.MinValue)
        {
            window.RequestPhysicalLocation(posX, posY);
        }

        window.WheelRequested += OnWheelRequested;
        window.Moved += OnMoved;
        window.DragStarted += OnDragStarted;
        window.ManageRequested += OnManageRequested;

        _window = window;
        _currentSize = sizeDiu;

        if (!IsHeadlessTestMode)
        {
            window.Show();
        }
    }

    private void CloseWindow()
    {
        if (_window == null) return;

        var window = _window;
        _window = null;

        DisposeWheelSession();
        window.WheelRequested -= OnWheelRequested;
        window.Moved -= OnMoved;
        window.DragStarted -= OnDragStarted;
        window.ManageRequested -= OnManageRequested;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"关闭桌宠窗口异常：{ex.Message}");
        }
    }

    private void OnManageRequested()
    {
        OpenManagerWindow();
    }

    private void OnDragStarted()
    {
        DisposeWheelSession();
        _window?.OnWheelSessionEnded();
    }

    /// <summary>
    /// 生产环境呼出轮盘转发方法（经由控制器守卫抵达宿主轮盘服务，优先使用追踪会话）。
    /// </summary>
    internal bool RequestWheel(double physicalCenterX, double physicalCenterY)
    {
        if (_isShutdown) return false;

        try
        {
            if (!_context.Info.HasCapability(PluginCapability.Wheel))
            {
                _context.Log.Warn("清单缺少 Wheel 能力，无法呼出轮盘。");
                return false;
            }

            DisposeWheelSession();

            if (_context.Wheel is IHostWheelSessionService sessionService)
            {
                int genId = Interlocked.Increment(ref _sessionGenerationCounter);
                var session = new WheelRequestSession(genId, _window);
                _activeSession = session;

                IDisposable? token = null;
                try
                {
                    token = sessionService.RequestTrackedWheel(
                        physicalCenterX,
                        physicalCenterY,
                        args => OnWheelSessionStateChanged(session, args),
                        args => OnWheelSelectionChanged(session, args));
                }
                catch (Exception ex)
                {
                    session.MarkTerminated();
                    if (ReferenceEquals(_activeSession, session))
                    {
                        _activeSession = null;
                    }
                    _context.Log.Warn($"呼出追踪轮盘异常：{ex.Message}");
                    return false;
                }

                if (token != null)
                {
                    session.AttachToken(token);
                    return true;
                }

                session.MarkTerminated();
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
                _context.Log.Debug("宿主追踪轮盘服务未受理呼出请求。");
                return false;
            }

            bool accepted = _context.Wheel.ShowWheel(physicalCenterX, physicalCenterY);
            if (!accepted)
            {
                _context.Log.Debug("宿主轮盘服务未受理呼出请求（可能正在执行手势或无可用显示区域）。");
            }
            return accepted;
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"呼出轮盘异常：{ex.Message}");
            return false;
        }
    }

    private void OnWheelSessionStateChanged(WheelRequestSession session, WheelSessionStateChangedEventArgs args)
    {
        if (_isShutdown)
        {
            session.DisposeToken();
            return;
        }

        switch (args.State)
        {
            case WheelSessionState.Presented:
                if (ReferenceEquals(_activeSession, session) && !session.IsTerminated)
                {
                    var targetWindow = session.Window;
                    RunOnUi(targetWindow, () =>
                    {
                        if (ReferenceEquals(_activeSession, session) && !session.IsTerminated)
                        {
                            targetWindow?.OnWheelSessionPresented();
                        }
                    });
                }
                break;

            case WheelSessionState.Closed:
            case WheelSessionState.Rejected:
            case WheelSessionState.Superseded:
                session.MarkTerminated();
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                    var targetWindow = session.Window;
                    RunOnUi(targetWindow, () =>
                    {
                        targetWindow?.OnWheelSessionEnded();
                    });
                }
                break;
        }
    }

    private void OnWheelSelectionChanged(WheelRequestSession session, WheelSelectionChangedEventArgs args)
    {
        if (_isShutdown) return;
        if (!ReferenceEquals(_activeSession, session) || session.IsTerminated)
        {
            return;
        }

        var targetWindow = session.Window;
        RunOnUi(targetWindow, () =>
        {
            if (!ReferenceEquals(_activeSession, session) || session.IsTerminated)
            {
                return;
            }
            targetWindow?.OnWheelSelectionChanged(args);
        });
    }

    private static void RunOnUi(PetWindow? window, Action action)
    {
        if (window == null) return;
        if (window.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            window.Dispatcher.InvokeAsync(action);
        }
    }

    private void DisposeWheelSession()
    {
        WheelRequestSession? session = Interlocked.Exchange(ref _activeSession, null);
        if (session != null)
        {
            try
            {
                session.DisposeToken();
            }
            catch (Exception ex)
            {
                _context.Log.Warn($"释放轮盘会话凭证异常：{ex.Message}");
            }
        }
    }

    private sealed class WheelRequestSession
    {
        public readonly int GenerationId;
        public readonly PetWindow? Window;
        private readonly object _gate = new();
        private IDisposable? _token;
        private bool _isTerminated;
        private bool _isDisposed;

        public WheelRequestSession(int generationId, PetWindow? window)
        {
            GenerationId = generationId;
            Window = window;
        }

        public bool IsTerminated
        {
            get { lock (_gate) return _isTerminated; }
        }

        public bool IsDisposed
        {
            get { lock (_gate) return _isDisposed; }
        }

        public void AttachToken(IDisposable? token)
        {
            if (token == null) return;
            bool shouldDispose = false;
            lock (_gate)
            {
                if (_isTerminated || _isDisposed)
                {
                    shouldDispose = true;
                }
                else
                {
                    _token = token;
                }
            }

            if (shouldDispose)
            {
                try { token.Dispose(); } catch { }
            }
        }

        public bool MarkTerminated()
        {
            IDisposable? tokenToDispose = null;
            lock (_gate)
            {
                if (_isTerminated) return false;
                _isTerminated = true;
                tokenToDispose = _token;
                _token = null;
            }

            try { tokenToDispose?.Dispose(); } catch { }
            return true;
        }

        public void DisposeToken()
        {
            IDisposable? tokenToDispose = null;
            lock (_gate)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                tokenToDispose = _token;
                _token = null;
            }

            try { tokenToDispose?.Dispose(); } catch { }
        }
    }

    private void OnWheelRequested(double physicalCenterX, double physicalCenterY)
    {
        RequestWheel(physicalCenterX, physicalCenterY);
    }

    private void OnMoved()
    {
        if (_isShutdown || _window == null) return;

        PetWindow.PhysicalRect rect = _window.ReadPhysicalRect();
        if (rect.Width <= 0 || rect.Height <= 0) return;

        _context.Settings.Set(PetPreference.LeftKey, rect.X.ToString(CultureInfo.InvariantCulture));
        _context.Settings.Set(PetPreference.TopKey, rect.Y.ToString(CultureInfo.InvariantCulture));
        _context.Settings.Save();
    }

    private void MarkVisible()
    {
        _context.Settings.Set(PetPreference.VisibleKey, "true");
        _context.Settings.Save();
    }

    private int? ReadInt(string key)
    {
        string? raw = _context.Settings.Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) ? val : null;
    }

    private sealed class ApplyTransaction
    {
        public long Generation { get; }
        public string ItemId { get; }
        public string ItemName { get; }
        public string TargetDirectory { get; }
        public PetAssetSnapshot CandidateSnapshot { get; }
        public string? PrevActiveId { get; }
        public string? PrevAssetDir { get; }
        public string? PrevNonce { get; }
        public PetAssetSnapshot PrevAssetSnapshot { get; }

        public bool IsPersisted { get; set; }
        public bool IsCommitted { get; set; }
        public bool IsCompensated { get; set; }
        public bool IsExternallySuperseded { get; set; }

        public ApplyTransaction(
            long generation,
            string itemId,
            string itemName,
            string targetDirectory,
            PetAssetSnapshot candidateSnapshot,
            string? prevActiveId,
            string? prevAssetDir,
            string? prevNonce,
            PetAssetSnapshot prevAssetSnapshot)
        {
            Generation = generation;
            ItemId = itemId;
            ItemName = itemName;
            TargetDirectory = targetDirectory;
            CandidateSnapshot = candidateSnapshot;
            PrevActiveId = prevActiveId;
            PrevAssetDir = prevAssetDir;
            PrevNonce = prevNonce;
            PrevAssetSnapshot = prevAssetSnapshot;
        }
    }
}
