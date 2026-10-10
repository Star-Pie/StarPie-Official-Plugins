namespace StarPie.Plugin;

/// <summary>只观察轮盘语义，不改变选择、导航或动作结果的贡献。</summary>
public interface IInteractionContribution
{
    InteractionDescriptor Descriptor { get; }
    /// <summary>在宿主后台调度中串行调用；必须遵守取消，不能阻塞轮盘或保留可变宿主状态。</summary>
    ValueTask OnInteractionAsync(InteractionEvent input, CancellationToken cancellationToken);
}

/// <summary>Initialize 事务内注册；失败丢弃，停用兜底撤销。</summary>
public interface IInteractionRegistry
{
    IDisposable Register(IInteractionContribution contribution);
}

/// <summary>SDK 1.10 可选扩展；旧 IPluginContext 实现无需增加成员。</summary>
public interface IInteractionPluginContext : IPluginContext
{
    IInteractionRegistry Interactions { get; }
}

[Flags]
public enum InteractionEventKind
{
    None = 0,
    Presented = 1,
    SelectionChanged = 2,
    SubmenuExpanded = 4,
    NavigationCancelled = 8,
    ActionCommitted = 16,
    SessionCancelled = 32,
    SessionEnded = 64,
    All = Presented | SelectionChanged | SubmenuExpanded | NavigationCancelled |
          ActionCommitted | SessionCancelled | SessionEnded,
}

public enum InteractionSource { NormalGesture, StickyWheel }
public enum InteractionTargetKind { None, Core, Sector, SubSector }

/// <summary>几何目标身份。空槽位也可以被选中，但不因此表示已有可执行动作。</summary>
public readonly record struct InteractionTarget(
    InteractionTargetKind Kind, int Sector = -1, int SubSector = -1)
{
    public static InteractionTarget None => new(InteractionTargetKind.None);
    public static InteractionTarget Core => new(InteractionTargetKind.Core);
}

public sealed class InteractionDescriptor
{
    public string Id { get; init; } = "";
    public InteractionEventKind Events { get; init; } = InteractionEventKind.All;
}

/// <summary>
/// 宿主生成的不可变快照。Sequence 在合并/背压后可以有间隙；ActionCommitted 是有效目标确认，不是执行成功。
/// </summary>
public sealed class InteractionEvent
{
    public int SchemaVersion => 1;
    public InteractionEventKind Kind { get; }
    public long SessionId { get; }
    public long Sequence { get; }
    public DateTimeOffset Timestamp { get; }
    public InteractionSource Source { get; }
    /// <summary>宿主配置中用于匹配应用轮盘的键，不是可变配置对象。</summary>
    public string ProfileId { get; }
    public InteractionTarget Target { get; }
    public string Reason { get; }

    public InteractionEvent(InteractionEventKind kind, long sessionId, long sequence,
        InteractionSource source, string profileId, InteractionTarget target,
        string reason = "", DateTimeOffset? timestamp = null)
    {
        if (kind == InteractionEventKind.None || (kind & ~InteractionEventKind.All) != 0 ||
            (((int)kind & ((int)kind - 1)) != 0))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(target.Kind)) throw new ArgumentOutOfRangeException(nameof(target));
        Kind = kind;
        SessionId = sessionId;
        Sequence = sequence;
        Source = source;
        ProfileId = profileId ?? "";
        Target = target;
        Reason = reason ?? "";
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }
}
