using StarPie.Plugin;
namespace StarPie.Plugin.Sound;

public sealed class SoundPlugin : IStarPiePlugin
{
    private readonly object _gate = new();
    private readonly SoundEventRouter _router = new();
    private readonly List<IDisposable> _tokens = new();
    private SoundStore? _store;
    private long _revision, _appliedRevision = -1;
    private bool _stopped;
    internal SoundEventRouter Router => _router;

    public void Initialize(IPluginContext context)
    {
        if (context is not IInteractionPluginContext interactions)
            throw new PluginContractException("Sound requires interaction context (SDK 1.10).");
        if (!context.Info.HasCapability(PluginCapability.FileSystem))
            throw new PluginCapabilityDeniedException(PluginCapability.FileSystem, "Sound profile IO", context.Me.Id);
        SoundMessages.Register(context.I18n);
        _store = new SoundStore(context.Settings, context.DataDirectory);
        SoundEffectManager.WarningSink = context.Log.Warn;
        var initial = _store.ReadPreferences();
        _tokens.Add(context.SettingsPage.Register(new SettingsPageDescriptor
        {
            TitleKey = "settings.title", DescriptionKey = "settings.description",
            Fields = CreateFields(initial)
        }));
        foreach (string key in SoundStore.SettingKeys)
            _tokens.Add(context.Settings.OnChanged(key, _ => Interlocked.Increment(ref _revision)));
        _tokens.Add(interactions.Interactions.Register(new FeedbackContribution(this)));
        // 设置操作不是扇区动作；当前设置页只声明数据字段，不借用动作目录作为管理入口。
        // 这里只注册/迁移私有数据，不发声、不自动启用或改变宿主预加载偏好。
        context.Log.Info("Sound feedback registered. Enable host preload explicitly to receive startup wheel events.");
    }

    private static IReadOnlyList<ParameterField> CreateFields(SoundPreferences defaults)
    {
        var fields = new List<ParameterField>();
        bool[] values = { defaults.EnableSoundEffects, defaults.SoundOnPopup, defaults.SoundOnHover, defaults.SoundOnExpand, defaults.SoundOnExecute, defaults.SoundOnCancel };
        for (int i = 0; i < SoundStore.Switches.Length; i++)
            fields.Add(new() { Key = SoundStore.Switches[i].Key, LabelKey = "settings." + SoundStore.Switches[i].Key,
                Type = ParameterFieldType.Bool, DefaultValue = values[i] ? "true" : "false" });
        fields.Add(new() { Key = "volume", LabelKey = "settings.volume", Type = ParameterFieldType.Number, Min = 0, Max = 1,
            DefaultValue = defaults.SoundVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        fields.Add(new() { Key = "theme", LabelKey = "settings.theme", Type = ParameterFieldType.Enum, DefaultValue = defaults.SoundTheme,
            Options = new[] { "Mechanical", "Crisp", "Bubble", "Minimalist", "Custom" }.Select(t => new ParameterOption { Value = t, LabelKey = "theme." + t }).ToArray() });
        fields.Add(new() { Key = "profileId", LabelKey = "settings.profileId", Type = ParameterFieldType.Text, DefaultValue = defaults.ActiveCustomSoundProfileId, MaxLength = 128 });
        foreach (var setting in SoundStore.WavSettings)
            fields.Add(new() { Key = setting.Key, LabelKey = "settings." + setting.Key, Type = ParameterFieldType.File,
                DefaultValue = "", ValidationRegex = SoundStore.WavPathPattern, MaxLength = 2048 });
        return fields;
    }

    private bool Prepare(bool audition)
    {
        if (_stopped || _store == null) return false;
        long revision = Interlocked.Read(ref _revision);
        if (revision != _appliedRevision)
        {
            SoundEffectManager.Preferences = _store.ReadPreferences();
            _appliedRevision = revision;
            if (!SoundEffectManager.Preferences.EnableSoundEffects && !audition)
            { SoundEffectManager.Shutdown(); _router.Reset(); return false; }
            SoundEffectManager.Initialize(force: true);
        }
        if (!SoundEffectManager.Preferences.EnableSoundEffects && !audition) return false;
        return true;
    }
    internal void Handle(InteractionEvent input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) if (Prepare(audition: false)) _router.Handle(input);
    }
    internal async Task<bool> AuditionAsync(string eventName, CancellationToken cancellationToken)
    {
        lock (_gate) if (!Prepare(audition: true)) return false;
        SoundType[] types = eventName == "Flow" ? Enum.GetValues<SoundType>() : new[] { Enum.Parse<SoundType>(eventName) };
        bool completed = true;
        try
        {
            foreach (var type in types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed &= await SoundEffectManager.PlayAuditionAsync(type, cancellationToken).ConfigureAwait(false);
            }
            return completed;
        }
        finally
        {
            lock (_gate)
                if (!SoundEffectManager.Preferences.EnableSoundEffects) SoundEffectManager.Shutdown();
        }
    }
    internal string Import(string path, bool activate, bool replace, CancellationToken token)
    {
        lock (_gate)
        {
            if (_stopped || _store == null) throw new OperationCanceledException();
            string result = _store.Import(path, activate, replace, token);
            Interlocked.Increment(ref _revision); return result;
        }
    }
    internal void Export(string path, string? profileId, bool bundle, bool overwrite, CancellationToken token)
    {
        lock (_gate)
        {
            if (_stopped || _store == null) throw new OperationCanceledException();
            _store.Export(path, profileId, bundle, overwrite, token);
        }
    }
    public void Shutdown()
    {
        Thread? worker;
        lock (_gate)
        {
            _stopped = true;
            foreach (var token in _tokens) token.Dispose(); _tokens.Clear();
            worker = SoundEffectManager.CurrentWorkerThread;
            _router.Reset(); SoundEffectManager.Shutdown();
            _store = null;
        }
        // Host.StopPluginAsync 在后台执行 Shutdown；不能持有插件锁等待后端退出。
        // 保留同步 WinMM 单飞约束，不以原生中断冒险换取“已卸载”的假成功。
        if (worker != null && !worker.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Sound playback worker did not drain; the plugin may require a restart.");
        SoundEffectManager.WarningSink = null;
    }
    private sealed class FeedbackContribution(SoundPlugin owner) : IInteractionContribution
    {
        public InteractionDescriptor Descriptor => new() { Id = "feedback", Events = InteractionEventKind.All };
        public ValueTask OnInteractionAsync(InteractionEvent input, CancellationToken cancellationToken)
        {
            owner.Handle(input, cancellationToken);
            // 处理语义事件已完成；播放线程由模块生命周期拥有，停用封入口并取消所有待播。
            return ValueTask.CompletedTask;
        }
    }
}
