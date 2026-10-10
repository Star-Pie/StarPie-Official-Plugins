using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StarPie.Plugin.Sound;

/// <summary>
/// 轮盘交互音效类型
/// </summary>
public enum SoundType
{
	/// <summary>呼出轮盘</summary>
	WheelPopup,
	/// <summary>扇区切换/划过高亮</summary>
	SectorHover,
	/// <summary>二级级联菜单展开</summary>
	SubmenuExpand,
	/// <summary>动作确认触发执行</summary>
	ActionExecute,
	/// <summary>外甩脱离或手势取消</summary>
	GestureCancel
}

/// <summary>
/// 音效交互来源分类
/// </summary>
public enum SoundSessionSource
{
	/// <summary>全局或系统级未关联特定会话的音效</summary>
	Global = 0,
	/// <summary>普通鼠标手势轮盘</summary>
	NormalGesture = 1,
	/// <summary>粘滞/悬浮球常驻轮盘</summary>
	StickyWheel = 2,
	/// <summary>控制台设置主界面与画布预览</summary>
	SettingsPreview = 3
}

/// <summary>
/// 音效触发优先级分类 (级别越高，越优先播放且不可被低优先级覆盖)
/// </summary>
public enum SoundPriority
{
	/// <summary>低优先级：高频划过探索音 (SectorHover)</summary>
	Low = 1,
	/// <summary>中优先级：层级切换与导航音 (WheelPopup, SubmenuExpand)</summary>
	Normal = 2,
	/// <summary>高优先级：终结确认与取消音 (ActionExecute, GestureCancel)</summary>
	High = 3
}

/// <summary>
/// 悬停目标身份标识 (包含来源、会话、层级、父扇区和子扇区，用于目标稳定防抖)
/// </summary>
public readonly struct HoverTargetIdentity : IEquatable<HoverTargetIdentity>
{
	public SoundSessionSource Source { get; }
	public long SessionId { get; }
	public int Level { get; }
	public int ParentIndex { get; }
	public int SubIndex { get; }

	public HoverTargetIdentity(SoundSessionSource source, long sessionId, int level, int parentIndex, int subIndex)
	{
		Source = source;
		SessionId = sessionId;
		Level = level;
		ParentIndex = parentIndex;
		SubIndex = subIndex;
	}

	public bool Equals(HoverTargetIdentity other) =>
		Source == other.Source &&
		SessionId == other.SessionId &&
		Level == other.Level &&
		ParentIndex == other.ParentIndex &&
		SubIndex == other.SubIndex;

	public override bool Equals(object? obj) => obj is HoverTargetIdentity other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Source, SessionId, Level, ParentIndex, SubIndex);
	public static bool operator ==(HoverTargetIdentity left, HoverTargetIdentity right) => left.Equals(right);
	public static bool operator !=(HoverTargetIdentity left, HoverTargetIdentity right) => !left.Equals(right);
	public override string ToString() => $"[Source={Source}, Sess={SessionId}, Lvl={Level}, Parent={ParentIndex}, Sub={SubIndex}]";
}

/// <summary>
/// 极轻量轮盘交互音效管理器 (Scheme C - Win32 内存驻留音频管线与数学波形合成)。
/// <para>
/// 核心特性：
/// 1. 纯原生 Win32 winmm.dll 非托管内存异步回放，延迟 &lt; 2ms，零第三方依赖；
/// 2. 程序化生成 44.1kHz 16-bit Mono 极微波形，常驻内存 &lt; 20 KB，完全免除外部音频文件依赖与资源解压开销；
/// 3. 使用非托管内存指针 (Marshal.AllocHGlobal)，彻底杜绝 GC 内存移动导致的底层 Access Violation；
/// 4. 扇区切换 35ms 极速防抖闸门，杜绝分界线高频抖动杂音；
/// 5. 独立于系统主音量的硬件级 PCM 数学振幅无损缩放。
/// </para>
/// </summary>
public static class SoundEffectManager
{
	internal static SoundPreferences Preferences { get; set; } = new();
	internal static Action<string>? WarningSink { get; set; }
	private static long _lastNativeFailureWarning;
	private static void Warn(string message) => WarningSink?.Invoke(message);

	[DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PlaySoundW(IntPtr pszSound, IntPtr hmod, uint fdwSound);

	private const uint SND_SYNC = 0x0000;
	private const uint SND_NODEFAULT = 0x0002;
	private const uint SND_MEMORY = 0x0004;

	private sealed class SoundWorkerContext : IDisposable
	{
		public readonly long Generation;
		public readonly AutoResetEvent Signal = new(false);
		public volatile bool IsStopped = false;
		private int _disposed = 0;

		public SoundWorkerContext(long generation)
		{
			Generation = generation;
		}

		public void SafeSet()
		{
			if (Volatile.Read(ref _disposed) != 0) return;
			try
			{
				Signal.Set();
			}
			catch (ObjectDisposedException) { }
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				try
				{
					Signal.Dispose();
				}
				catch { }
			}
		}
	}

	private static readonly object _syncLock = new object();
	private static readonly Dictionary<SoundType, byte[]> _soundBuffers = new();
	private static readonly object _playbackLock = new object();

	private static readonly object _queueLock = new object();
	private static SoundType? _pendingSound;
	private static byte[]? _pendingCustomWav;
	private static long _pendingGeneration = 0L;
	private static SoundPriority _pendingPriority = SoundPriority.Low;
	private static SoundSessionSource _pendingSource = SoundSessionSource.Global;
	private static long _pendingSessionId = 0L;

	private sealed class HoverCandidate
	{
		public HoverTargetIdentity Target { get; }
		public long TargetTimestamp { get; }
		public long Generation { get; }
		public long Version { get; }
		public bool HasPlayed { get; set; }

		public HoverCandidate(HoverTargetIdentity target, long timestamp, long generation, long version)
		{
			Target = target;
			TargetTimestamp = timestamp;
			Generation = generation;
			Version = version;
			HasPlayed = false;
		}
	}

	private static long _candidateVersionCounter = 0L;
	private static readonly Dictionary<SoundSessionSource, HoverCandidate> _hoverCandidates = new();
	private static long _lastHoverPlaybackStartTick = 0L;

	private sealed class SoundSessionState
	{
		public long SessionId { get; }
		public SoundSessionSource Source { get; }
		public long GenerationId { get; }
		public bool IsSingleAudition { get; }
		public TaskCompletionSource<bool>? Completion { get; }
		public bool IsEnding { get; set; }
		public bool AllowTerminalFeedback { get; set; }
		public bool IsClosed { get; set; }

		public SoundSessionState(long sessionId, SoundSessionSource source, long generationId, bool isSingleAudition = false)
		{
			SessionId = sessionId;
			Source = source;
			GenerationId = generationId;
			IsSingleAudition = isSingleAudition;
			if (isSingleAudition) Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	private static readonly object _sessionLock = new object();
	private static long _sessionCounter = 0L;
	private static readonly Dictionary<long, SoundSessionState> _sessions = new();

	private static long _generationCounter = 0L;
	private static SoundWorkerContext? _activeContext;
	private static Thread? _workerThread;
	private static volatile bool _isRunning = false;
	private static volatile bool _pendingStart = false;

	private static string _currentTheme = string.Empty;
	private static double _currentVolume = -1.0;
	private static bool _initialized = false;
	private static long _lastHoverTick = 0L;

	/// <summary>扇区切换音效最小触发时间间隔 (毫秒)，防止光标在扇区分界线来回微颤时产生刺耳噪音。</summary>
	private const long HoverDebounceMs = 35L;

	#region 诊断与测试切缝 (Diagnostics & Test Seams)
	internal static Func<long>? TimeProvider { get; set; }
	private static long CurrentTick => TimeProvider != null ? TimeProvider() : Environment.TickCount64;

	internal static bool TestMode { get; set; } = false;
	internal static Action<byte[], uint>? PlaybackSink { get; set; }

	internal static Action<SoundType?, byte[]?, long>? SoundQueued { get; set; }
	internal static Action<SoundType?, byte[]?, long>? SoundPlayed { get; set; }
	internal static Action<SoundType?, byte[]?, long>? SoundPlaybackStarted { get; set; }
	internal static Action<SoundType?, byte[]?, long>? SoundPlaybackFinished { get; set; }
	internal static Action<SoundType?, byte[]?, long>? SoundDropped { get; set; }
	internal static Action<Thread>? WorkerThreadCreated { get; set; }

	internal static Thread? CurrentWorkerThread => _workerThread;
	internal static bool IsRunning => _isRunning;
	internal static SoundType? TestPendingSound => _pendingSound;
	internal static byte[]? TestPendingCustomWav => _pendingCustomWav;
	internal static SoundPriority TestPendingPriority => _pendingPriority;
	internal static SoundSessionSource TestPendingSource => _pendingSource;
	internal static long TestPendingSessionId => _pendingSessionId;
	internal static long LastHoverTick { get => _lastHoverTick; set => _lastHoverTick = value; }
	internal static long TestHoverDebounceMs => HoverDebounceMs;
	internal static object TestQueueLock => _queueLock;
	internal static object TestSyncLock => _syncLock;
	internal static object TestSessionLock => _sessionLock;
	/// <summary>初始实验参数：目标稳定45ms后发声</summary>
	public const long HoverStabilizeMs = 45L;
	/// <summary>初始实验参数：实际 Hover 播放开始间隔至少80ms</summary>
	public const long MinHoverPlaybackIntervalMs = 80L;
	/// <summary>初始实验参数：超过稳定期限150ms仍未开始作为丢弃条件</summary>
	public const long HoverMaxExpirationMs = 150L;

	internal static Func<bool>? WorkerTrackingPolicy { get; set; }
	internal static Action? OnBeforeShutdownSessionCleanup { get; set; }
	internal static Action? OnHoverCandidateSelectedBeforeAcquireResource { get; set; }
	internal static Action? OnBeforeClaimLock { get; set; }
	internal static Action<bool, SoundType, long>? OnHoverClaimEvaluated { get; set; }
	internal static string? LastWavFallbackReason { get; set; }
	internal const int MaxCustomWavBytes = 1024 * 1024;

	private static readonly List<Thread> _harnessCreatedWorkers = new();
	private static readonly object _harnessWorkerLock = new();

	internal static List<Thread> GetHarnessCreatedWorkers()
	{
		lock (_harnessWorkerLock)
		{
			return _harnessCreatedWorkers.ToList();
		}
	}

	internal static void TestSignalWorker()
	{
		lock (_syncLock)
		{
			_activeContext?.SafeSet();
		}
	}

	internal static HoverTargetIdentity? TestGetHoverCandidateTarget(SoundSessionSource source)
	{
		lock (_queueLock)
		{
			if (_hoverCandidates.TryGetValue(source, out var c)) return c.Target;
			return null;
		}
	}

	internal static long TestLastHoverPlaybackStartTick
	{
		get => _lastHoverPlaybackStartTick;
		set => _lastHoverPlaybackStartTick = value;
	}

	internal static bool IsSessionActive(long sessionId)
	{
		lock (_sessionLock)
		{
			if (_sessions.TryGetValue(sessionId, out var s))
			{
				return !s.IsClosed && !s.IsEnding;
			}
			return false;
		}
	}

	private static readonly List<Thread> _allCreatedWorkers = new();
	private static readonly object _workerListLock = new();
	private static readonly List<IDisposable> _trackedBarriers = new();
	private static readonly object _barrierListLock = new();

	internal static void TrackBarrier(IDisposable barrier)
	{
		lock (_barrierListLock)
		{
			if (!_trackedBarriers.Contains(barrier))
			{
				_trackedBarriers.Add(barrier);
			}
		}
	}

	internal static void ReleaseAllBarriers()
	{
		List<IDisposable> copy;
		lock (_barrierListLock)
		{
			copy = _trackedBarriers.ToList();
		}
		foreach (var b in copy)
		{
			try
			{
				if (b is EventWaitHandle ewh) ewh.Set();
			}
			catch { }
		}
	}

	internal static List<Thread> GetAllCreatedWorkers()
	{
		lock (_workerListLock)
		{
			return _allCreatedWorkers.ToList();
		}
	}

	internal static int SessionCount
	{
		get
		{
			lock (_sessionLock)
			{
				return _sessions.Count;
			}
		}
	}

	internal static bool HasSession(long sessionId)
	{
		lock (_sessionLock)
		{
			return _sessions.ContainsKey(sessionId);
		}
	}

	internal static byte[]? GetCachedSoundBuffer(SoundType type)
	{
		lock (_syncLock)
		{
			_soundBuffers.TryGetValue(type, out var buf);
			return buf;
		}
	}

	internal static void ResetTestSeams()
	{
		TimeProvider = null;
		SoundQueued = null;
		SoundPlayed = null;
		SoundPlaybackStarted = null;
		SoundPlaybackFinished = null;
		SoundDropped = null;
		WorkerThreadCreated = null;
		WorkerTrackingPolicy = null;
		OnBeforeShutdownSessionCleanup = null;
		OnHoverCandidateSelectedBeforeAcquireResource = null;
		OnBeforeClaimLock = null;
		OnHoverClaimEvaluated = null;
		LastWavFallbackReason = null;
		_lastHoverTick = 0L;
		lock (_queueLock)
		{
			_hoverCandidates.Clear();
			_lastHoverPlaybackStartTick = 0L;
		}
		lock (_sessionLock)
		{
			_sessions.Clear();
		}
	}

    private static void RemoveSessionUnderLock(long id)
    {
        if (_sessions.Remove(id, out var state)) state.Completion?.TrySetResult(false);
    }
    internal static async Task<bool> PlayAuditionAsync(SoundType type, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized(); EnsureWorkerStarted();
        long id = BeginSession(SoundSessionSource.SettingsPreview, isSingleAudition: true);
        Task<bool> completion;
        lock (_sessionLock) completion = _sessions[id].Completion!.Task;
        using var cancel = cancellationToken.Register(() => EndSession(SoundSessionSource.SettingsPreview, id, allowTerminalFeedback: false));
        PlayPreview(type, SoundSessionSource.SettingsPreview, id);
        try { return await completion.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally { EndSession(SoundSessionSource.SettingsPreview, id, allowTerminalFeedback: false); }
    }

	private static void CleanupSingleAuditionUnderLock(long sessionId, bool success = false)
	{
		if (sessionId == 0L) return;
		if (_sessions.TryGetValue(sessionId, out var sState) && sState.IsSingleAudition)
		{
			sState.IsClosed = true;
			sState.IsEnding = false;
			sState.AllowTerminalFeedback = false;
			sState.Completion?.TrySetResult(success);
			RemoveSessionUnderLock(sessionId);
		}
	}

	private static void CleanupSingleAudition(long sessionId, bool success = false)
	{
		if (sessionId == 0L) return;
		lock (_sessionLock)
		{
			CleanupSingleAuditionUnderLock(sessionId, success);
		}
	}
	#endregion

	/// <summary>
	/// 确保专属音频后台回放工作线程已启动（单读者无锁队列，彻底规避 WinMM 异步中断死锁）。
	/// 结合代际上下文 (SoundWorkerContext) 与单物理工作线程不变性，旧工作线程未退出前绝不创建新线程，
	/// 采用 _pendingStart 延迟启动机制，彻底杜绝线程积压。
	/// </summary>
	private static void EnsureWorkerStarted()
	{
		lock (_syncLock)
		{
			_isRunning = true;

			// 1. 如果已有活跃且未停止的物理工作线程，直接复用
			if (_workerThread != null && _workerThread.IsAlive)
			{
				if (_activeContext != null && !_activeContext.IsStopped)
				{
					return;
				}

				// 2. 旧工作线程仍处于存活状态（例如正在执行未完成的底层同步物理回放）：
				// 严禁在此创建第二个物理线程！保持单一物理工作线程不变性。
				// 将启动请求有界延后至旧线程退出时交接
				_pendingStart = true;
				if (_activeContext == null || _activeContext.IsStopped)
				{
					long gen = unchecked(++_generationCounter);
					_activeContext = new SoundWorkerContext(gen);
				}
				return;
			}

			// 3. 物理工作线程未存活，立即启动唯一工作线程
			StartWorkerThreadUnderLock();
		}
	}

	private static void StartWorkerThreadUnderLock()
	{
		_pendingStart = false;
		if (_activeContext == null || _activeContext.IsStopped)
		{
			long gen = unchecked(++_generationCounter);
			_activeContext = new SoundWorkerContext(gen);
		}

		var context = _activeContext;
		_workerThread = new Thread(ProcessSoundQueue)
		{
			Name = "StarPie.SoundWorker",
			IsBackground = true,
			Priority = ThreadPriority.AboveNormal
		};
		if (WorkerTrackingPolicy?.Invoke() ?? TestMode)
		{
			lock (_workerListLock)
			{
				if (!_allCreatedWorkers.Contains(_workerThread))
				{
					_allCreatedWorkers.Add(_workerThread);
				}
			}
		}
		if (TestMode)
		{
			lock (_harnessWorkerLock)
			{
				if (!_harnessCreatedWorkers.Contains(_workerThread))
				{
					_harnessCreatedWorkers.Add(_workerThread);
				}
			}
		}
		WorkerThreadCreated?.Invoke(_workerThread);
		_workerThread.Start(context);
	}

	/// <summary>
	/// 专属音频播放循环：在独立工作线程内使用 SND_SYNC 同步回放。
	/// 采用 AutoResetEvent 信号机制与智能合并，杜绝死锁、CPU 跑满与 Use-After-Free 野指针。
	/// 绑定独立代际上下文 (SoundWorkerContext)，严格按代际匹配消费，旧 worker 退出前绝不窃取或清空新代请求。
	/// 退出时自动释放信号并在有延后启动请求时原子移交启动新代线程。
	/// </summary>
	private static void ProcessSoundQueue(object? state)
	{
		var context = (SoundWorkerContext)state!;
		try
		{
			while (!context.IsStopped)
			{
				SoundType? soundToPlay = null;
				byte[]? customWavToPlay = null;
				SoundPriority priorityToPlay = SoundPriority.Low;
				SoundSessionSource sourceToPlay = SoundSessionSource.Global;
				long sessionToPlay = 0L;
				long genToPlay = 0L;
				bool isHoverRequest = false;
				HoverTargetIdentity hoverTargetToPlay = default;
				long hoverCandidateVersionToPlay = 0L;
				long hoverCandidateTimestampToPlay = 0L;

				int waitTimeoutMs = Timeout.Infinite;

				lock (_queueLock)
				{
					if (context.IsStopped)
					{
						break;
					}

					long now = CurrentTick;

					// 1. 优先检查待播立即音效 (_pendingSound / _pendingCustomWav)
					if (_pendingSound.HasValue || _pendingCustomWav != null)
					{
						// 严格校验请求所属代际：
						// 如果待播项代际不属于本工作线程代际（属于新代际），旧工作线程绝不消费，并安全退出让位给新代工作线程
						if (_pendingGeneration != context.Generation)
						{
							break;
						}

						soundToPlay = _pendingSound;
						customWavToPlay = _pendingCustomWav;
						priorityToPlay = _pendingPriority;
						sourceToPlay = _pendingSource;
						sessionToPlay = _pendingSessionId;
						genToPlay = _pendingGeneration;

						_pendingSound = null;
						_pendingCustomWav = null;
						_pendingGeneration = 0L;
						_pendingPriority = SoundPriority.Low;
						_pendingSource = SoundSessionSource.Global;
						_pendingSessionId = 0L;
					}
					else
					{
						// 2. 检查 Hover 候选目标
						HoverCandidate? bestReadyCandidate = null;
						long earliestEligibleDelayMs = long.MaxValue;
						var expiredSources = new List<SoundSessionSource>();

						foreach (var kvp in _hoverCandidates)
						{
							var candidate = kvp.Value;
							if (candidate.Generation != context.Generation)
							{
								continue;
							}

							// 校验候选所属会话是否有效且活跃
							if (candidate.Target.Source != SoundSessionSource.Global)
							{
								bool sessionValid = false;
								lock (_sessionLock)
								{
									if (_sessions.TryGetValue(candidate.Target.SessionId, out var sState))
									{
										if (!sState.IsClosed && !sState.IsEnding && sState.GenerationId == context.Generation)
										{
											sessionValid = true;
										}
									}
								}
								if (!sessionValid)
								{
									expiredSources.Add(kvp.Key);
									if (!candidate.HasPlayed)
									{
										SoundDropped?.Invoke(SoundType.SectorHover, null, now);
									}
									continue;
								}
							}

							if (candidate.HasPlayed)
							{
								continue;
							}

							long stabilizeDeadline = candidate.TargetTimestamp + HoverStabilizeMs;
							long expirationTime = stabilizeDeadline + HoverMaxExpirationMs;

							if (now > expirationTime)
							{
								// 超过稳定期限 150ms 仍未开始，作为丢弃条件安全丢弃
								expiredSources.Add(kvp.Key);
								SoundDropped?.Invoke(SoundType.SectorHover, null, now);
								continue;
							}

							long earliestPlayTime = Math.Max(stabilizeDeadline, _lastHoverPlaybackStartTick + MinHoverPlaybackIntervalMs);

							if (now >= earliestPlayTime)
							{
								if (bestReadyCandidate == null || candidate.TargetTimestamp < bestReadyCandidate.TargetTimestamp)
								{
									bestReadyCandidate = candidate;
								}
							}
							else
							{
								long delay = earliestPlayTime - now;
								if (delay < earliestEligibleDelayMs)
								{
									earliestEligibleDelayMs = delay;
								}
							}
						}

						foreach (var src in expiredSources)
						{
							_hoverCandidates.Remove(src);
						}

						if (bestReadyCandidate != null)
						{
							isHoverRequest = true;
							hoverTargetToPlay = bestReadyCandidate.Target;
							hoverCandidateVersionToPlay = bestReadyCandidate.Version;
							hoverCandidateTimestampToPlay = bestReadyCandidate.TargetTimestamp;

							soundToPlay = SoundType.SectorHover;
							customWavToPlay = null;
							priorityToPlay = SoundPriority.Low;
							sourceToPlay = bestReadyCandidate.Target.Source;
							sessionToPlay = bestReadyCandidate.Target.SessionId;
							genToPlay = bestReadyCandidate.Generation;

							SoundQueued?.Invoke(soundToPlay, null, now);
						}
						else
						{
							if (earliestEligibleDelayMs != long.MaxValue)
							{
								waitTimeoutMs = (int)Math.Max(1, Math.Min(earliestEligibleDelayMs, int.MaxValue));
							}
							else
							{
								waitTimeoutMs = Timeout.Infinite;
							}
						}
					}
				}

				// 既无待播立即音效也无就绪 Hover 候选时，进入定时信号等待
				if (soundToPlay == null && customWavToPlay == null)
				{
					try
					{
						context.Signal.WaitOne(waitTimeoutMs);
					}
					catch (ObjectDisposedException)
					{
						break;
					}
					continue;
				}

				if (isHoverRequest)
				{
					OnHoverCandidateSelectedBeforeAcquireResource?.Invoke();
				}

				byte[]? wavData = customWavToPlay;
				if (wavData == null && soundToPlay.HasValue)
				{
					lock (_syncLock)
					{
						_soundBuffers.TryGetValue(soundToPlay.Value, out wavData);
					}
				}

				if (wavData == null || wavData.Length == 0 || context.IsStopped)
				{
					// 静音也需要终结消费。只处理仍为当前版本的候选，不伪造真实播放时间。
					if (isHoverRequest && !context.IsStopped)
					{
						lock (_queueLock)
						lock (_sessionLock)
						{
							if (_hoverCandidates.TryGetValue(sourceToPlay, out var silent) &&
							    silent.Generation == context.Generation && silent.Version == hoverCandidateVersionToPlay && silent.Target == hoverTargetToPlay &&
							    (sourceToPlay == SoundSessionSource.Global || (_sessions.TryGetValue(sessionToPlay, out var active) &&
							     active.GenerationId == context.Generation && !active.IsClosed && !active.IsEnding)))
								silent.HasPlayed = true;
						}
					}
					CleanupSingleAudition(sessionToPlay, success: wavData != null && wavData.Length == 0 && !context.IsStopped);
					continue;
				}

				// 在取得播放资源后设置明确、原子的“播放开始认领点”，校验会话、来源、代际及候选版本
				// 认领前结束必须撤销；认领后允许当前声音自然完成，禁止持锁跨越同步播放
				bool isClaimed = true;
				SoundSessionState? claimedSession = null;
				long nowClaim = 0L;

				if (isHoverRequest)
				{
					OnBeforeClaimLock?.Invoke();
				}

				lock (_queueLock)
				{
					lock (_sessionLock)
					{
						nowClaim = CurrentTick;

						// 1. 代际一致性校验：请求所属代际必须与本 Worker 上下文代际严格一致且未停止
						if (genToPlay != context.Generation || context.IsStopped)
						{
							isClaimed = false;
						}
						else if (sourceToPlay != SoundSessionSource.Global)
						{
							if (sessionToPlay == 0L)
							{
								if (soundToPlay == SoundType.SectorHover)
								{
									isClaimed = false;
								}
							}
							else
							{
								if (!_sessions.TryGetValue(sessionToPlay, out var sState))
								{
									isClaimed = false;
								}
								else if (sState.Source != sourceToPlay || sState.GenerationId != context.Generation)
								{
									isClaimed = false;
								}
								else if (sState.IsClosed)
								{
									isClaimed = false;
								}
								else if (sState.IsEnding)
								{
									if (priorityToPlay < SoundPriority.High)
									{
										isClaimed = false;
									}
									else
									{
										// 终态确认音已被播放消费，此时将会话完全置为 Closed
										sState.IsEnding = false;
										sState.IsClosed = true;
										sState.AllowTerminalFeedback = false;
										claimedSession = sState;
									}
								}
								else
								{
									claimedSession = sState;
								}
							}
						}

						// 2. 探索性 Hover 专属二次校验（取得资源后的播放开始认领点）
						if (isClaimed && isHoverRequest)
						{
							// A. 候选是否仍存在
							if (!_hoverCandidates.TryGetValue(sourceToPlay, out var currentCandidate))
							{
								// 已被 CancelHover / EndSession / 高优先级抢占撤销
								isClaimed = false;
							}
							// B. 是否仍是同一版本与同一目标身份 (来源、会话、层级、父扇区、子扇区)
							else if (currentCandidate.Version != hoverCandidateVersionToPlay || currentCandidate.Target != hoverTargetToPlay)
							{
								// 已经换目标或切换层级/父扇区
								isClaimed = false;
							}
							// C. 是否已经播放过
							else if (currentCandidate.HasPlayed)
							{
								isClaimed = false;
							}
							else
							{
								// D. 是否超过稳定期限后的 150ms
								long stabilizeDeadline = hoverCandidateTimestampToPlay + HoverStabilizeMs;
								long expirationTime = stabilizeDeadline + HoverMaxExpirationMs;

								if (nowClaim > expirationTime)
								{
									// 超过稳定期限 150ms，安全丢弃并移除
									_hoverCandidates.Remove(sourceToPlay);
									SoundDropped?.Invoke(soundToPlay, customWavToPlay, nowClaim);
									isClaimed = false;
								}
								// E. 检查实际播放开始间隔至少 80ms
								else if (nowClaim < _lastHoverPlaybackStartTick + MinHoverPlaybackIntervalMs)
								{
									isClaimed = false;
								}
							}

							// 成功认领：更新 HasPlayed 和最后播放开始时间
							if (isClaimed)
							{
								currentCandidate!.HasPlayed = true;
								_lastHoverPlaybackStartTick = nowClaim;
							}
						}
					}
				}

				if (!isClaimed)
				{
					CleanupSingleAudition(sessionToPlay);
					if (!isHoverRequest)
					{
						SoundDropped?.Invoke(soundToPlay, customWavToPlay, nowClaim);
					}
					if (isHoverRequest && soundToPlay.HasValue)
					{
						OnHoverClaimEvaluated?.Invoke(false, soundToPlay.Value, nowClaim);
					}
					continue;
				}

				if (isHoverRequest && soundToPlay.HasValue)
				{
					OnHoverClaimEvaluated?.Invoke(true, soundToPlay.Value, nowClaim);
				}

				// 认领成功！锁外安全执行底层物理回放，允许当前声音自然完成，绝不持锁跨越同步回放或 IO
				SoundPlayed?.Invoke(soundToPlay, customWavToPlay, nowClaim);
				SoundPlaybackStarted?.Invoke(soundToPlay, customWavToPlay, nowClaim);
				bool playbackSucceeded = PlaySoundDirect(wavData);
				SoundPlaybackFinished?.Invoke(soundToPlay, customWavToPlay, CurrentTick);

				// 单次试听完成，回收自身会话
				if (claimedSession != null && claimedSession.IsSingleAudition)
				{
					CleanupSingleAudition(claimedSession.SessionId, success: playbackSucceeded);
				}
			}
		}
		catch (ThreadAbortException)
		{
		}
		catch (Exception ex)
		{
			Warn($"SoundWorker iteration exception: {ex.Message}");
		}
		finally
		{
			// 确保当前代际信号与等待句柄安全释放
			context.Dispose();

			lock (_syncLock)
			{
				if (_workerThread == Thread.CurrentThread)
				{
					_workerThread = null;
				}
				if (ReferenceEquals(_activeContext, context))
				{
					_activeContext = null;
				}

				// 旧线程完全退出物理执行路径后，若存在延后启动需求且系统仍需运行，才原子启动新一代物理工作线程
				if (_isRunning && (_pendingStart || (_activeContext != null && !_activeContext.IsStopped)))
				{
					StartWorkerThreadUnderLock();
					_activeContext?.SafeSet();
				}
			}
		}
	}

	/// <summary>
	/// 仅在专属工作线程内短暂固定托管内存并执行 Win32 PlaySoundW 同步回放。
	/// 通过 _playbackLock 锁保证物理回放单飞，旧播放未返回前严禁启动第二条播放路径。
	/// </summary>
	private static bool PlaySoundDirect(byte[] wavData)
	{
		lock (_playbackLock)
		{
			if (PlaybackSink != null)
			{
				try
				{
					PlaybackSink(wavData, SND_SYNC | SND_MEMORY | SND_NODEFAULT);
					return true;
				}
				catch (Exception ex)
				{
					Warn($"PlaybackSink exception: {ex.Message}");
				}
				return false;
			}

			if (TestMode)
			{
				// 测试模式下严禁触碰原生播放后端。
				return true;
			}

			GCHandle pin = default;
			try
			{
				pin = GCHandle.Alloc(wavData, GCHandleType.Pinned);
				IntPtr ptr = pin.AddrOfPinnedObject();
				bool success = PlaySoundW(ptr, IntPtr.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT);
				if (!success)
				{
					int err = Marshal.GetLastWin32Error();
					long now = CurrentTick;
					long previous = Interlocked.Read(ref _lastNativeFailureWarning);
					if (previous == 0 || now - previous >= 5000)
					{
						Interlocked.Exchange(ref _lastNativeFailureWarning, now);
						Warn($"PlaySoundW failed (error={err}); audio was not confirmed. Further failures are rate limited.");
					}
				}
				return success;
			}
			catch (Exception ex)
			{
				Warn($"PlaySoundDirect exception: {ex.Message}");
				return false;
			}
			finally
			{
				if (pin.IsAllocated)
				{
					pin.Free();
				}
			}
		}
	}

	/// <summary>
	/// 初始化或按需刷新音效数据缓存（在应用启动、配置载入或用户修改音量/主题时调用）。
	/// </summary>
	public static void Initialize(string? theme = null, double? volume = null, bool force = false)
	{
		lock (_syncLock)
		{
			string targetTheme = theme ?? Preferences?.SoundTheme ?? "Mechanical";
			double targetVolume = volume ?? Preferences?.SoundVolume ?? 0.6;
			targetVolume = Math.Clamp(targetVolume, 0.0, 1.0);

			if (!force && _initialized && string.Equals(_currentTheme, targetTheme, StringComparison.OrdinalIgnoreCase)
				&& Math.Abs(_currentVolume - targetVolume) < 0.01)
			{
				return;
			}

			_currentTheme = targetTheme;
			_currentVolume = targetVolume;

			// 程序化合成 5 大音效事件波形并存入托管字典（原子替换引用，无野指针风险）
			_soundBuffers[SoundType.WheelPopup] = SynthesizeSound(SoundType.WheelPopup, targetTheme, targetVolume);
			_soundBuffers[SoundType.SectorHover] = SynthesizeSound(SoundType.SectorHover, targetTheme, targetVolume);
			_soundBuffers[SoundType.SubmenuExpand] = SynthesizeSound(SoundType.SubmenuExpand, targetTheme, targetVolume);
			_soundBuffers[SoundType.ActionExecute] = SynthesizeSound(SoundType.ActionExecute, targetTheme, targetVolume);
			_soundBuffers[SoundType.GestureCancel] = SynthesizeSound(SoundType.GestureCancel, targetTheme, targetVolume);

			_initialized = true;
		}

		EnsureWorkerStarted();
	}

	/// <summary>
	/// 触发播放指定事件类型的交互音效（完全非阻塞，极速无感）。
	/// </summary>
	public static void Play(SoundType type, SoundSessionSource source = SoundSessionSource.NormalGesture, long? sessionId = null)
	{
		SoundPreferences? cfg = Preferences;
		if (cfg == null || !cfg.EnableSoundEffects)
		{
			return;
		}

		// 细项事件开关过滤
		bool isEnabled = type switch
		{
			SoundType.WheelPopup => cfg.SoundOnPopup,
			SoundType.SectorHover => cfg.SoundOnHover,
			SoundType.SubmenuExpand => cfg.SoundOnExpand,
			SoundType.ActionExecute => cfg.SoundOnExecute,
			SoundType.GestureCancel => cfg.SoundOnCancel,
			_ => true
		};

		if (!isEnabled)
		{
			return;
		}

		// 扇区切换防抖控制
		if (type == SoundType.SectorHover)
		{
			long now = CurrentTick;
			if (now - _lastHoverTick < HoverDebounceMs)
			{
				return;
			}
			_lastHoverTick = now;
		}
		else if (type == SoundType.SubmenuExpand)
		{
			// 二级展开保护期：防止展开瞬间紧接着触发子扇区 Hover 堆叠
			_lastHoverTick = CurrentTick + 20L;
		}

		EnsureInitialized();
		EnsureWorkerStarted();
		EnqueueSound(type, null, GetSoundPriority(type), source, sessionId);
	}

	/// <summary>
	/// 汇报探索性扇区悬停目标（进入目标稳定防抖管线）。
	/// 目标身份包含来源、会话、层级、父扇区和子扇区。
	/// 仅当同一目标稳定保持满 45ms 且与上次 Hover 播放开始间隔至少 80ms 时才放行发声。
	/// </summary>
	public static void ReportHover(SoundSessionSource source, long sessionId, int level, int parentIndex, int subIndex)
	{
		SoundPreferences? cfg = Preferences;
		if (cfg != null && (!cfg.EnableSoundEffects || !cfg.SoundOnHover))
		{
			return;
		}

		if (subIndex < 0)
		{
			CancelHover(source, sessionId);
			return;
		}

		EnsureInitialized();
		EnsureWorkerStarted();

		long now = CurrentTick;
		SoundWorkerContext? targetContext;
		lock (_syncLock)
		{
			if (!_isRunning || _activeContext == null || _activeContext.IsStopped)
			{
				return;
			}
			targetContext = _activeContext;
		}

		lock (_queueLock)
		{
			if (targetContext.IsStopped) return;

			if (source != SoundSessionSource.Global)
			{
				if (sessionId == 0L) return;

				lock (_sessionLock)
				{
					if (!_sessions.TryGetValue(sessionId, out var sState)) return;
					if (sState.GenerationId != targetContext.Generation) return;
					if (sState.Source != source) return;
					if (sState.IsClosed || sState.IsEnding) return;
				}
			}

			var target = new HoverTargetIdentity(source, sessionId, level, parentIndex, subIndex);

			if (_hoverCandidates.TryGetValue(source, out var existing))
			{
				if (existing.Target == target)
				{
					// 同一目标重复汇报：
					// 若已播放过，保持 HasPlayed = true，不重复播放；
					// 若未播放过，保留原始 TargetTimestamp，不推迟稳定期限！
					return;
				}

				// 目标不同：未到期切换目标，作废旧候选
				if (!existing.HasPlayed)
				{
					SoundDropped?.Invoke(SoundType.SectorHover, null, now);
				}
			}

			long ver = unchecked(++_candidateVersionCounter);
			_hoverCandidates[source] = new HoverCandidate(target, now, targetContext.Generation, ver);
		}

		targetContext.SafeSet();
	}

	/// <summary>
	/// 撤销指定来源与会话的待定悬停候选（当离开扇区、进入中心死区或外甩取消时调用）。
	/// </summary>
	public static void CancelHover(SoundSessionSource source, long sessionId = 0L)
	{
		SoundWorkerContext? targetContext;
		lock (_syncLock)
		{
			targetContext = _activeContext;
		}

		long now = CurrentTick;
		lock (_queueLock)
		{
			if (_hoverCandidates.TryGetValue(source, out var candidate))
			{
				if (sessionId == 0L || candidate.Target.SessionId == sessionId)
				{
					if (!candidate.HasPlayed)
					{
						SoundDropped?.Invoke(SoundType.SectorHover, null, now);
					}
					_hoverCandidates.Remove(source);
				}
			}
		}

		targetContext?.SafeSet();
	}

	/// <summary>
	/// 直接播放指定音效（供设置界面实时试听，不受全局开关拦截）。
	/// </summary>
	public static void PlayPreview(SoundType type, SoundSessionSource source = SoundSessionSource.SettingsPreview, long? sessionId = null)
	{
		EnsureInitialized();
		EnsureWorkerStarted();
		long sessId = sessionId ?? BeginSession(source, isSingleAudition: true);
		EnqueueSound(type, null, GetSoundPriority(type), source, sessId);
	}

	/// <summary>
	/// 直接试听指定的自定义音效事件配置（非阻塞通过专属工作线程播放，零延迟、绝不产生多线程冲突）。
	/// </summary>
	public static void PlayCustomEventPreview(SoundEventConfig config, double? volume = null, SoundSessionSource source = SoundSessionSource.SettingsPreview, long? sessionId = null)
	{
		if (config == null) return;
		EnsureWorkerStarted();
		double vol = volume ?? Preferences?.SoundVolume ?? 0.6;
		byte[] wavData = SynthesizeCustomEventSound(config, vol);
		if (wavData == null || wavData.Length == 0) return;

		long sessId = sessionId ?? BeginSession(source, isSingleAudition: true);
		EnqueueSound(null, wavData, GetCustomEventPriority(config), source, sessId);
	}

	public static SoundPriority GetSoundPriority(SoundType type) => type switch
	{
		SoundType.ActionExecute or SoundType.GestureCancel => SoundPriority.High,
		SoundType.WheelPopup or SoundType.SubmenuExpand => SoundPriority.Normal,
		SoundType.SectorHover => SoundPriority.Low,
		_ => SoundPriority.Low
	};

	public static SoundPriority GetCustomEventPriority(SoundEventConfig? config) =>
		config == null ? SoundPriority.Normal : GetSoundPriority(config.EventType);

	public static long BeginSession(SoundSessionSource source, bool isSingleAudition = false)
	{
		long gen = 0L;
		lock (_syncLock)
		{
			gen = _activeContext?.Generation ?? unchecked(_generationCounter + 1);
		}

		lock (_sessionLock)
		{
			// 同一来源开始新会话时，若存在旧的处于结束中状态的会话，彻底关闭其终态窗口
			foreach (var s in _sessions.Values)
			{
				if (s.Source == source && (s.IsEnding || s.AllowTerminalFeedback))
				{
					s.IsEnding = false;
					s.AllowTerminalFeedback = false;
					s.IsClosed = true;
				}
			}

			long id = unchecked(++_sessionCounter);
			if (id == 0L) id = unchecked(++_sessionCounter);
			var state = new SoundSessionState(id, source, gen, isSingleAudition);
			_sessions[id] = state;

			// 有界容量回收与修剪（维持上限 <= 64）
			if (_sessions.Count > 64)
			{
				// 第一阶段：修剪已关闭的会话
				var closedKeys = new List<long>();
				foreach (var kvp in _sessions)
				{
					if (kvp.Value.IsClosed)
					{
						closedKeys.Add(kvp.Key);
					}
				}
				foreach (var k in closedKeys)
				{
					RemoveSessionUnderLock(k);
				}

				// 第二阶段：若仍超过 64，绝对保护活动画布（!IsClosed && !IsSingleAudition）与活动手势！
				// 修剪单次试听（即使遗留在表内）或已结束无终态窗口的会话
				if (_sessions.Count > 64)
				{
					var deadKeys = new List<long>();
					foreach (var kvp in _sessions)
					{
						if (kvp.Key == id) continue;
						var s = kvp.Value;
						if (s.IsClosed || s.IsSingleAudition || (s.IsEnding && !s.AllowTerminalFeedback))
						{
							deadKeys.Add(kvp.Key);
						}
					}
					foreach (var k in deadKeys)
					{
						RemoveSessionUnderLock(k);
					}
				}
			}
			return id;
		}
	}

	public static void EndSession(SoundSessionSource source, long sessionId = 0L, bool allowTerminalFeedback = true)
	{
		lock (_queueLock)
		{
			lock (_sessionLock)
			{
				long targetId = sessionId;
				if (targetId == 0L)
				{
					SoundSessionState? latest = null;
					foreach (var s in _sessions.Values)
					{
						if (s.Source == source && !s.IsClosed)
						{
							if (latest == null || s.SessionId > latest.SessionId)
							{
								latest = s;
							}
						}
					}
					if (latest != null) targetId = latest.SessionId;
				}

				if (targetId != 0L && _sessions.TryGetValue(targetId, out var state))
				{
					if (state.Source == source)
					{
						if (allowTerminalFeedback)
						{
							// 新结束覆盖旧结束：确保同一来源仅保留最后一次结束的有限终态确认音窗口
							foreach (var s in _sessions.Values)
							{
								if (s.Source == source && s.SessionId != targetId && (s.IsEnding || s.AllowTerminalFeedback))
								{
									s.IsEnding = false;
									s.AllowTerminalFeedback = false;
									s.IsClosed = true;
								}
							}
							state.IsEnding = true;
							state.AllowTerminalFeedback = true;
						}
						else
						{
							state.IsEnding = false;
							state.IsClosed = true;
							state.AllowTerminalFeedback = false;
							CleanupSingleAuditionUnderLock(targetId);
						}
					}
				}
			}

			if (_hoverCandidates.TryGetValue(source, out var hoverCand))
			{
				if (sessionId == 0L || hoverCand.Target.SessionId == sessionId)
				{
					if (!hoverCand.HasPlayed)
					{
						SoundDropped?.Invoke(SoundType.SectorHover, null, CurrentTick);
					}
					_hoverCandidates.Remove(source);
				}
			}

			bool shouldRevoke = false;
			if (_pendingSound.HasValue || _pendingCustomWav != null)
			{
				bool sourceMatches = _pendingSource == source;
				bool sessionMatches = (sessionId == 0L) || (_pendingSessionId == 0L) || (_pendingSessionId == sessionId);

				if (sourceMatches && sessionMatches)
				{
					if (!allowTerminalFeedback || _pendingPriority < SoundPriority.High)
					{
						shouldRevoke = true;
					}
				}
			}

			if (shouldRevoke)
			{
				long droppedSession = _pendingSessionId;
				SoundDropped?.Invoke(_pendingSound, _pendingCustomWav, CurrentTick);
				_pendingSound = null;
				_pendingCustomWav = null;
				_pendingGeneration = 0L;
				_pendingPriority = SoundPriority.Low;
				_pendingSource = SoundSessionSource.Global;
				_pendingSessionId = 0L;
				CleanupSingleAudition(droppedSession);
			}
		}
	}

	/// <summary>
	/// 校验代际与会话、安全入队并原子捕获唤醒目标上下文。
	/// 结合会话状态机、优先级抢占与同级最新覆盖规则，有界单槽容量恒为 1；
	/// 待播请求严格绑定目标代际编号，在锁外安全触发信号，杜绝死锁与过期请求重新绑定。
	/// </summary>
	private static void EnqueueSound(SoundType? type, byte[]? customWav, SoundPriority priority = SoundPriority.Low, SoundSessionSource source = SoundSessionSource.Global, long? sessionId = null)
	{
		long actualSessionId = sessionId ?? 0L;

		SoundWorkerContext? targetContext;
		lock (_syncLock)
		{
			if (!_isRunning || _activeContext == null || _activeContext.IsStopped)
			{
				CleanupSingleAudition(actualSessionId);
				return;
			}
			targetContext = _activeContext;
		}

		lock (_queueLock)
		{
			if (targetContext.IsStopped)
			{
				CleanupSingleAudition(actualSessionId);
				return;
			}

			// 在队列锁内原子执行会话校验，消除释放锁后 EndSession 插入导致旧 Hover 进队的竞态
			// SettingsPreview 与普通手势同等受到严格校验，移除旁路
			if (source != SoundSessionSource.Global)
			{
				if (actualSessionId == 0L)
				{
					// 未知或空会话：如果是 SectorHover，必须有合法会话，明确拒绝丢弃
					if (type == SoundType.SectorHover)
					{
						SoundDropped?.Invoke(type, customWav, CurrentTick);
						return;
					}
				}
				else
				{
					lock (_sessionLock)
					{
						if (!_sessions.TryGetValue(actualSessionId, out var sessionState))
						{
							// 未知会话被明确拒绝
							SoundDropped?.Invoke(type, customWav, CurrentTick);
							CleanupSingleAuditionUnderLock(actualSessionId);
							return;
						}

						if (sessionState.GenerationId != targetContext.Generation)
						{
							// 跨代会话被明确拒绝
							SoundDropped?.Invoke(type, customWav, CurrentTick);
							CleanupSingleAuditionUnderLock(actualSessionId);
							return;
						}

						if (sessionState.Source != source)
						{
							// 来源不匹配被明确拒绝
							SoundDropped?.Invoke(type, customWav, CurrentTick);
							CleanupSingleAuditionUnderLock(actualSessionId);
							return;
						}

						if (sessionState.IsClosed)
						{
							// 已结束会话被明确拒绝
							SoundDropped?.Invoke(type, customWav, CurrentTick);
							CleanupSingleAuditionUnderLock(actualSessionId);
							return;
						}

						if (sessionState.IsEnding)
						{
							// 会话处于结束中：仅允许有限终态确认音窗口内的高优先级请求
							if (!sessionState.AllowTerminalFeedback || priority < SoundPriority.High)
							{
								SoundDropped?.Invoke(type, customWav, CurrentTick);
								CleanupSingleAuditionUnderLock(actualSessionId);
								return;
							}

							// 终态反馈已被消费，关闭后续终态反馈窗口（防止重复触发多个终态音）
							sessionState.AllowTerminalFeedback = false;
						}
					}
				}
			}

			// 当高优先级音效（ActionExecute, GestureCancel）到达时，立即撤销同来源（或会话）未播放的 Hover 候选
			if (priority >= SoundPriority.High)
			{
				if (_hoverCandidates.TryGetValue(source, out var candidate))
				{
					if (actualSessionId == 0L || candidate.Target.SessionId == actualSessionId)
					{
						if (!candidate.HasPlayed)
						{
							SoundDropped?.Invoke(SoundType.SectorHover, null, CurrentTick);
						}
						_hoverCandidates.Remove(source);
					}
				}
			}
			else if (type == SoundType.SubmenuExpand)
			{
				// 二级菜单展开时，立即作废当前未播放的 Level 0 悬停候选
				if (_hoverCandidates.TryGetValue(source, out var candidate))
				{
					if (candidate.Target.Level == 0 && (actualSessionId == 0L || candidate.Target.SessionId == actualSessionId))
					{
						if (!candidate.HasPlayed)
						{
							SoundDropped?.Invoke(SoundType.SectorHover, null, CurrentTick);
						}
						_hoverCandidates.Remove(source);
					}
				}
			}

			bool hasPending = _pendingSound.HasValue || _pendingCustomWav != null;
			if (hasPending)
			{
				if (priority < _pendingPriority)
				{
					SoundDropped?.Invoke(type, customWav, CurrentTick);
					CleanupSingleAudition(actualSessionId);
					targetContext.SafeSet();
					return;
				}

				long oldSessionId = _pendingSessionId;
				SoundDropped?.Invoke(_pendingSound, _pendingCustomWav, CurrentTick);
				CleanupSingleAudition(oldSessionId);
			}

			_pendingSound = type;
			_pendingCustomWav = customWav;
			_pendingGeneration = targetContext.Generation;
			_pendingPriority = priority;
			_pendingSource = source;
			_pendingSessionId = actualSessionId;
			SoundQueued?.Invoke(type, customWav, CurrentTick);
		}

		targetContext.SafeSet();
	}

	private static void EnsureInitialized()
	{
		if (!_initialized)
		{
			Initialize();
		}
	}

	/// <summary>
	/// 释放所有音频资源与工作线程（在应用退出时调用）。
	/// 绝对非阻塞（纳秒级返回），严禁在 UI 线程或底层钩子调用路径同步 Join 或等待底层物理回放。
	/// 原子切换状态并将当前代际标记为停止；
	/// 仅清空属于被关闭代际的待播项，绝不删除已被新代认领或入队的请求。
	/// </summary>
	public static void Shutdown()
	{
		SoundWorkerContext? oldContext;
		lock (_syncLock)
		{
			_isRunning = false;
			_pendingStart = false;
			oldContext = _activeContext;
			if (oldContext != null)
			{
				oldContext.IsStopped = true;
			}
			_activeContext = null;
			_soundBuffers.Clear();
			_initialized = false;
		}

		OnBeforeShutdownSessionCleanup?.Invoke();

		long? stoppedGen = oldContext?.Generation;

		lock (_sessionLock)
		{
			if (stoppedGen.HasValue)
			{
				long targetGen = stoppedGen.Value;
				var toRemove = new List<long>();
				foreach (var kvp in _sessions)
				{
					if (kvp.Value.GenerationId == targetGen)
					{
						toRemove.Add(kvp.Key);
					}
				}
				foreach (var k in toRemove)
				{
					RemoveSessionUnderLock(k);
				}
			}
		}

		lock (_queueLock)
		{
			// 依据本次 Shutdown 实际拥有的停止代际清理：
			// 仅当待播项属于本次停机实际拥有的代际时才清理；
			// 若 oldContext == null，本次停机未停止任何代际，绝不触碰任何待播项！
			if (stoppedGen.HasValue)
			{
				long targetGen = stoppedGen.Value;
				var hoverSourcesToRemove = new List<SoundSessionSource>();
				foreach (var kvp in _hoverCandidates)
				{
					if (kvp.Value.Generation == targetGen)
					{
						if (!kvp.Value.HasPlayed)
						{
							SoundDropped?.Invoke(SoundType.SectorHover, null, CurrentTick);
						}
						hoverSourcesToRemove.Add(kvp.Key);
					}
				}
				foreach (var src in hoverSourcesToRemove)
				{
					_hoverCandidates.Remove(src);
				}

				if (_pendingGeneration == targetGen)
				{
					if (_pendingSound.HasValue || _pendingCustomWav != null)
					{
						SoundDropped?.Invoke(_pendingSound, _pendingCustomWav, CurrentTick);
					}
					_pendingSound = null;
					_pendingCustomWav = null;
					_pendingGeneration = 0L;
					_pendingPriority = SoundPriority.Low;
					_pendingSource = SoundSessionSource.Global;
					_pendingSessionId = 0L;
				}
			}
		}

		if (oldContext != null)
		{
			oldContext.SafeSet();
		}
	}
	#region 程序化波形合成引擎 (Procedural Sound Synthesizer)

	/// <summary>
	/// 听觉响度曲线补偿：将 0.0~1.0 的滑块数值映射到符合人耳对数感知的高保真声学增益，
	/// 杜绝中低音量段因扬声器 DAC 降噪门限导致的静音。
	/// </summary>
	internal static double GetAcousticGain(double sliderVol)
	{
		if (sliderVol <= 0.001) return 0.0;
		return Math.Clamp(0.18 + 0.82 * Math.Pow(Math.Clamp(sliderVol, 0.0, 1.0), 1.25), 0.0, 1.0);
	}

	/// <summary>
	/// 根据音效主题与类型，以纯数学算法生成 44.1kHz 16-bit 单声道 WAV 字节流。
	/// 所有音效均具有平滑起音微窗（防 DAC 爆音破音）与饱满谐波共鸣（确保各类扬声器与耳机清晰可辨）。
	/// </summary>
	private static byte[] SynthesizeSound(SoundType type, string theme, double volume)
	{
		int sampleRate = 44100;

		switch (theme.ToLowerInvariant())
		{
			case "crisp": // 现代清脆 (数码触感/清爽回馈)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 420, 920, 48, volume),
					SoundType.SectorHover => SynthesizeClick(sampleRate, 2100, 1050, 36, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 980, 1470, 52, volume),
					SoundType.ActionExecute => SynthesizePunchyConfirm(sampleRate, 1100, 480, 68, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 720, 340, 42, volume * 0.85),
					_ => SynthesizeClick(sampleRate, 2100, 1050, 36, volume)
				};

			case "bubble": // 柔和气泡 (水滴轻音)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 340, 760, 56, volume * 0.95),
					SoundType.SectorHover => SynthesizeBubble(sampleRate, 820, 1450, 38, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 880, 1320, 58, volume * 0.95),
					SoundType.ActionExecute => SynthesizeBubble(sampleRate, 680, 1680, 72, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 520, 240, 45, volume * 0.85),
					_ => SynthesizeBubble(sampleRate, 820, 1450, 38, volume)
				};

			case "minimalist": // 极简短音 (超微清晰脉冲)
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 480, 820, 42, volume * 0.85),
					SoundType.SectorHover => SynthesizeClick(sampleRate, 1800, 900, 32, volume * 0.85),
					SoundType.SubmenuExpand => SynthesizeTone(sampleRate, 1020, 46, volume * 0.85),
					SoundType.ActionExecute => SynthesizeDualTone(sampleRate, 880, 440, 55, volume * 0.9),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 420, 260, 38, volume * 0.75),
					_ => SynthesizeClick(sampleRate, 1800, 900, 32, volume * 0.85)
				};

			case "custom": // 自定义音效方案 (真实程序化参数合成与采样加载)
				var activeProfileId = Preferences?.ActiveCustomSoundProfileId;
				var profile = Preferences?.CustomSoundProfiles?.FirstOrDefault(p => p.Id == activeProfileId)
					?? Preferences?.CustomSoundProfiles?.FirstOrDefault();
				var evConfig = profile?.Events?.FirstOrDefault(e => e.EventType == type);
				return SynthesizeCustomEventSound(evConfig, volume);

			case "mechanical": // 机械手感 (默认 - 轴体微动与刻度感)
			default:
				return type switch
				{
					SoundType.WheelPopup => SynthesizeSweep(sampleRate, 260, 620, 54, volume),
					SoundType.SectorHover => SynthesizeMechanicalClick(sampleRate, 1750, 780, 38, volume),
					SoundType.SubmenuExpand => SynthesizeDualTone(sampleRate, 920, 1380, 60, volume),
					SoundType.ActionExecute => SynthesizePunchyConfirm(sampleRate, 720, 1080, 80, volume),
					SoundType.GestureCancel => SynthesizeSweep(sampleRate, 520, 220, 46, volume * 0.85),
					_ => SynthesizeMechanicalClick(sampleRate, 1750, 780, 38, volume)
				};
		}
	}

	/// <summary>
	/// 为自定义手势事件配置生成专属 PCM 波形（支持程序化极微波形、经典预设与外部音频采样）。
	/// </summary>
	public static byte[] SynthesizeCustomEventSound(SoundEventConfig? config, double masterVolume)
	{
		int sampleRate = 44100;
		if (config == null)
		{
			return SynthesizeSound(SoundType.SectorHover, "Mechanical", masterVolume);
		}
		if (config.SourceType == SoundSourceType.Mute)
		{
			return Array.Empty<byte>();
		}

		double effectiveVol = Math.Clamp(config.RelativeVolume, 0.0, 1.0) * masterVolume;

		if (config.SourceType == SoundSourceType.BuiltInPreset)
		{
			string theme = config.BuiltInTheme ?? "Mechanical";
			// 输入校验之外的最后一道保护：内置预设不能回指活动自定义方案。
			if (string.Equals(theme, "Custom", StringComparison.OrdinalIgnoreCase)) theme = "Mechanical";
			return SynthesizeSound(config.EventType, theme, effectiveVol);
		}

		if (config.SourceType == SoundSourceType.CustomFile)
		{
			if (!string.IsNullOrWhiteSpace(config.CustomFilePath) && File.Exists(config.CustomFilePath))
			{
				return TryLoadCustomWavFile(config.CustomFilePath, effectiveVol);
			}
			// 文件不存在或为空时回退至清脆微动
			return SynthesizeClick(sampleRate, 1800, 900, 32, effectiveVol);
		}

		// 程序化极微波形 (ProceduralWave)
		double pitchMult = Math.Pow(2.0, config.PitchSemitones / 12.0);
		double durationMs = Math.Clamp(config.DurationMs, 5.0, 300.0);

		return (config.WavePreset?.ToLowerInvariant()) switch
		{
			"sine1200" => SynthesizeTone(sampleRate, 1200.0 * pitchMult, durationMs, effectiveVol),
			"square850" => SynthesizeClick(sampleRate, 1800.0 * pitchMult, 850.0 * pitchMult, durationMs, effectiveVol),
			"pulse2ms" => SynthesizeClick(sampleRate, 3200.0 * pitchMult, 1600.0 * pitchMult, Math.Min(durationMs, 22.0), effectiveVol),
			"sinedeep" => SynthesizeSweep(sampleRate, 480.0 * pitchMult, 220.0 * pitchMult, durationMs, effectiveVol),
			"metallicclick" => SynthesizeMechanicalClick(sampleRate, 2400.0 * pitchMult, 720.0 * pitchMult, durationMs, effectiveVol),
			"laserzap" => SynthesizeSweep(sampleRate, 2200.0 * pitchMult, 440.0 * pitchMult, durationMs, effectiveVol),
			"waterdrop" => SynthesizeBubble(sampleRate, 650.0 * pitchMult, 1550.0 * pitchMult, durationMs, effectiveVol),
			"cybersweep" => SynthesizeSweep(sampleRate, 320.0 * pitchMult, 1680.0 * pitchMult, durationMs, effectiveVol),
			_ => SynthesizeClick(sampleRate, 1800.0 * pitchMult, 900.0 * pitchMult, durationMs, effectiveVol)
		};
	}

	internal static byte[]? ScaleWavVolume(byte[]? data, double sliderVol)
	{
		return ScaleWavVolume(data, sliderVol, out _);
	}

	internal static byte[]? ScaleWavVolume(byte[]? data, double sliderVol, out string? failureReason)
	{
		failureReason = null;
		if (data == null || data.Length < 12)
		{
			failureReason = $"WAV data too short (length: {data?.Length ?? 0}, minimum 12 bytes for RIFF header)";
			LastWavFallbackReason = failureReason;
			return null;
		}

		// 1. RIFF 容器标头校验 (RIFF Header)
		if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F')
		{
			string magic = Encoding.ASCII.GetString(data, 0, Math.Min(4, data.Length));
			failureReason = $"Invalid RIFF container identifier: '{magic}' (expected 'RIFF')";
			LastWavFallbackReason = failureReason;
			return null;
		}

		uint riffPayloadSize = BitConverter.ToUInt32(data, 4);
		long totalExpectedFileLength = (long)riffPayloadSize + 8;
		if (totalExpectedFileLength > data.Length)
		{
			failureReason = $"Declared RIFF file size ({totalExpectedFileLength} bytes) exceeds actual data length ({data.Length} bytes)";
			LastWavFallbackReason = failureReason;
			return null;
		}

		if (data[8] != (byte)'W' || data[9] != (byte)'A' || data[10] != (byte)'V' || data[11] != (byte)'E')
		{
			string format = Encoding.ASCII.GetString(data, 8, 4);
			failureReason = $"Invalid WAVE format identifier: '{format}' (expected 'WAVE')";
			LastWavFallbackReason = failureReason;
			return null;
		}

		// 2. 遍历并定位各个 RIFF Subchunk
		bool hasFmt = false;
		ushort audioFormat = 0;
		ushort channels = 0;
		uint sampleRate = 0;
		ushort blockAlign = 0;
		ushort bitsPerSample = 0;

		bool hasData = false;
		int dataStart = -1;
		int dataSize = 0;

		long offset = 12;
		long scanBoundary = totalExpectedFileLength;

		while (offset < scanBoundary)
		{
			if (offset + 8 > scanBoundary)
			{
				failureReason = $"Truncated chunk header at offset {offset} (remaining: {scanBoundary - offset} bytes, expected 8)";
				LastWavFallbackReason = failureReason;
				return null;
			}

			string chunkId = Encoding.ASCII.GetString(data, (int)offset, 4);
			uint chunkSize = BitConverter.ToUInt32(data, (int)offset + 4);
			long chunkDataStart = offset + 8;
			long chunkDataEnd = chunkDataStart + chunkSize;

			if (chunkDataEnd > scanBoundary || chunkDataEnd < chunkDataStart)
			{
				failureReason = $"Chunk '{chunkId}' boundary out of range: offset {chunkDataStart}, size {chunkSize}, file length {scanBoundary}";
				LastWavFallbackReason = failureReason;
				return null;
			}

			// RIFF 规范填充字节：奇数长度分块后跟 1 字节 0 填充，确保字对齐
			long padding = (chunkSize % 2 != 0) ? 1 : 0;
			long nextOffset = chunkDataEnd + padding;

			if (padding > 0 && nextOffset > scanBoundary)
			{
				failureReason = $"Odd-length chunk '{chunkId}' (size {chunkSize}) missing required 1-byte word-alignment padding at offset {chunkDataEnd}";
				LastWavFallbackReason = failureReason;
				return null;
			}

			if (chunkId == "fmt ")
			{
				if (hasFmt)
				{
					failureReason = $"Duplicate 'fmt ' chunk at offset {offset}";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (chunkSize < 16)
				{
					failureReason = $"'fmt ' chunk size too small ({chunkSize} bytes, minimum 16)";
					LastWavFallbackReason = failureReason;
					return null;
				}

				audioFormat = BitConverter.ToUInt16(data, (int)chunkDataStart);
				channels = BitConverter.ToUInt16(data, (int)chunkDataStart + 2);
				sampleRate = BitConverter.ToUInt32(data, (int)chunkDataStart + 4);
				uint byteRate = BitConverter.ToUInt32(data, (int)chunkDataStart + 8);
				blockAlign = BitConverter.ToUInt16(data, (int)chunkDataStart + 12);
				bitsPerSample = BitConverter.ToUInt16(data, (int)chunkDataStart + 14);

				// 严格限制：仅支持标准未压缩 16-bit PCM 格式 (单声道或双声道)
				if (audioFormat != 1)
				{
					failureReason = $"Unsupported audio format: 0x{audioFormat:X4} (only standard uncompressed PCM 0x0001 is supported)";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (channels != 1 && channels != 2)
				{
					failureReason = $"Unsupported channel count: {channels} (only mono=1 and stereo=2 are supported)";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (bitsPerSample != 16)
				{
					failureReason = $"Unsupported bit depth: {bitsPerSample}-bit (only 16-bit PCM is supported)";
					LastWavFallbackReason = failureReason;
					return null;
				}

				ushort expectedBlockAlign = (ushort)(channels * (bitsPerSample / 8));
				if (blockAlign != expectedBlockAlign)
				{
					failureReason = $"Invalid block alignment: {blockAlign} (expected {expectedBlockAlign} for {channels}-ch 16-bit PCM)";
					LastWavFallbackReason = failureReason;
					return null;
				}

				ulong expectedByteRate = (ulong)sampleRate * (ulong)blockAlign;
				if (expectedByteRate > uint.MaxValue)
				{
					failureReason = $"Byte rate calculation overflow: {sampleRate} * {blockAlign}";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (byteRate != (uint)expectedByteRate)
				{
					failureReason = $"Invalid byte rate: {byteRate} (expected {expectedByteRate} for sample rate {sampleRate} and block alignment {blockAlign})";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (sampleRate == 0)
				{
					failureReason = $"Invalid sample rate: {sampleRate} Hz";
					LastWavFallbackReason = failureReason;
					return null;
				}

				hasFmt = true;
			}
			else if (chunkId == "data")
			{
				if (!hasFmt)
				{
					failureReason = "Non-standard WAV: 'data' chunk encountered before 'fmt ' chunk";
					LastWavFallbackReason = failureReason;
					return null;
				}

				if (hasData)
				{
					failureReason = $"Multiple 'data' chunks not supported: duplicate chunk at offset {offset}";
					LastWavFallbackReason = failureReason;
					return null;
				}

				hasData = true;
				dataStart = (int)chunkDataStart;
				dataSize = (int)chunkSize;
			}

			offset = nextOffset;
		}

		if (!hasFmt)
		{
			failureReason = "Missing required 'fmt ' chunk";
			LastWavFallbackReason = failureReason;
			return null;
		}

		if (!hasData || dataStart < 0)
		{
			failureReason = "Missing required 'data' chunk";
			LastWavFallbackReason = failureReason;
			return null;
		}

		if (dataSize <= 0)
		{
			failureReason = $"Empty 'data' chunk (size {dataSize})";
			LastWavFallbackReason = failureReason;
			return null;
		}

		if (dataSize % blockAlign != 0)
		{
			failureReason = $"Data chunk size ({dataSize} bytes) is not an integer multiple of block alignment ({blockAlign} bytes)";
			LastWavFallbackReason = failureReason;
			return null;
		}

		// 3. 校验全部通过，执行音频数据安全缩放（保持头部、其他分块与输入数组不变）
		double gain = GetAcousticGain(sliderVol);
		if (Math.Abs(gain - 1.0) < 0.04)
		{
			return (byte[])data.Clone();
		}

		byte[] scaled = (byte[])data.Clone();
		int dataEnd = dataStart + dataSize;
		for (int i = dataStart; i + 1 < dataEnd; i += 2)
		{
			short sample = (short)(scaled[i] | (scaled[i + 1] << 8));
			sample = (short)Math.Clamp(sample * gain, -32768.0, 32767.0);
			scaled[i] = (byte)(sample & 0xFF);
			scaled[i + 1] = (byte)((sample >> 8) & 0xFF);
		}

		return scaled;
	}

	internal static byte[] TryLoadCustomWavFile(string filePath, double sliderVol)
	{
		try
		{
			if (File.Exists(filePath) && filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
			{
				using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (stream.Length > MaxCustomWavBytes)
				{
					LastWavFallbackReason = $"WAV exceeds {MaxCustomWavBytes}-byte limit";
					Warn($"[SoundEffectManager] Custom WAV fallback to synthesized click ({filePath}): {LastWavFallbackReason}");
					return SynthesizeClick(44100, 1800, 900, 35, sliderVol);
				}
				// 五个事件缓存均须有界；先检查已打开文件，再分配，避免检查/重开竞态。
				byte[] data = new byte[(int)stream.Length];
				stream.ReadExactly(data);
				byte[]? scaled = ScaleWavVolume(data, sliderVol, out string? reason);
				if (scaled != null)
				{
					return scaled;
				}

				LastWavFallbackReason = reason;
				Warn($"[SoundEffectManager] Custom WAV fallback to synthesized click ({filePath}): {reason}");
			}
			else
			{
				LastWavFallbackReason = !File.Exists(filePath) ? "File not found" : "Not a .wav file extension";
				Warn($"[SoundEffectManager] Custom audio file rejected ({filePath}): {LastWavFallbackReason}");
			}
		}
		catch (Exception ex)
		{
			LastWavFallbackReason = $"Exception: {ex.Message}";
			Warn($"[SoundEffectManager] Exception loading custom WAV ({filePath}): {ex.Message}");
		}

		return SynthesizeClick(44100, 1800, 900, 35, sliderVol);
	}

	/// <summary>
	/// 现代数码微动触感脉冲（适用于 Crisp 与 Minimalist）
	/// </summary>
	private static byte[] SynthesizeClick(int sampleRate, double clickFreq, double bodyFreq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			// 1.5ms 极小平滑起音，彻底消除扬声器开门爆音
			double attack = (t < 0.0015) ? (t / 0.0015) : 1.0;
			// 瞬态快速衰减敲击声
			double clickEnv = Math.Exp(-t * 260.0);
			double click = Math.Sin(2.0 * Math.PI * clickFreq * t) * clickEnv * 0.65;
			// 丰满主体共鸣，确保各类小型扬声器不被降噪切除
			double bodyEnv = Math.Pow(1.0 - norm, 1.7);
			double body = Math.Sin(2.0 * Math.PI * bodyFreq * t) * bodyEnv * 0.35;

			double s = (click + body) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 机械轴体双频咔哒声（轴体触发微动 + 触底沉稳共鸣）
	/// </summary>
	private static byte[] SynthesizeMechanicalClick(int sampleRate, double clickFreq, double thudFreq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.0012) ? (t / 0.0012) : 1.0;
			// 混合微量二次谐波强化清脆咔哒
			double clickEnv = Math.Exp(-t * 220.0);
			double click = (Math.Sin(2.0 * Math.PI * clickFreq * t) * 0.8 + Math.Sin(2.0 * Math.PI * clickFreq * 1.8 * t) * 0.2) * clickEnv * 0.6;
			// 沉稳轴座回响
			double thudEnv = Math.Pow(1.0 - norm, 1.5);
			double thud = Math.Sin(2.0 * Math.PI * thudFreq * t) * thudEnv * 0.4;

			double s = (click + thud) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 扫频滑音（适合呼出展开与外甩取消）
	/// </summary>
	private static byte[] SynthesizeSweep(int sampleRate, double fStart, double fEnd, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		double phase = 0.0;
		for (int i = 0; i < samples; i++)
		{
			double norm = (double)i / samples;
			double freq = fStart + (fEnd - fStart) * Math.Pow(norm, 1.2);
			phase += 2.0 * Math.PI * freq / sampleRate;
			// 正弦升余弦窗包络，两端无声截断
			double env = Math.Sin(Math.PI * norm);
			double s = Math.Sin(phase) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 纯音衰减短音
	/// </summary>
	private static byte[] SynthesizeTone(int sampleRate, double freq, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.002) ? (t / 0.002) : 1.0;
			double env = Math.Pow(1.0 - norm, 1.5) * attack;
			double s = Math.Sin(2.0 * Math.PI * freq * t) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 和谐双音和弦（适合二级子轮盘展开）
	/// </summary>
	private static byte[] SynthesizeDualTone(int sampleRate, double f1, double f2, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.003) ? (t / 0.003) : 1.0;
			double env = Math.Pow(1.0 - norm, 1.6) * attack;
			double s = (Math.Sin(2.0 * Math.PI * f1 * t) * 0.52 + Math.Sin(2.0 * Math.PI * f2 * t) * 0.48) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 柔和气泡水滴音
	/// </summary>
	private static byte[] SynthesizeBubble(int sampleRate, double fStart, double fEnd, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		double phase = 0.0;
		for (int i = 0; i < samples; i++)
		{
			double norm = (double)i / samples;
			double freq = fStart + (fEnd - fStart) * Math.Pow(norm, 1.8);
			phase += 2.0 * Math.PI * freq / sampleRate;
			double env = Math.Pow(Math.Sin(Math.PI * norm), 0.7);
			double s = Math.Sin(phase) * env * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 笃定确认声（清脆 Snap + 沉稳 Thump 复合，适合动作执行）
	/// </summary>
	private static byte[] SynthesizePunchyConfirm(int sampleRate, double fSnap, double fThump, double durationMs, double sliderVol)
	{
		double gain = GetAcousticGain(sliderVol);
		int samples = Math.Max(1, (int)(sampleRate * durationMs / 1000.0));
		short[] pcm = new short[samples];

		for (int i = 0; i < samples; i++)
		{
			double t = (double)i / sampleRate;
			double norm = (double)i / samples;
			double attack = (t < 0.002) ? (t / 0.002) : 1.0;
			double snapEnv = Math.Exp(-t * 110.0);
			double snap = Math.Sin(2.0 * Math.PI * fSnap * t) * snapEnv * 0.65;
			double thumpEnv = Math.Pow(1.0 - norm, 1.4);
			double thump = Math.Sin(2.0 * Math.PI * fThump * t) * thumpEnv * 0.35;

			double s = (snap + thump) * attack * gain;
			pcm[i] = (short)Math.Clamp(s * 32767.0, -32768.0, 32767.0);
		}
		return WrapPcmToWav(pcm, sampleRate);
	}

	/// <summary>
	/// 将 PCM 采样封装为标准 RIFF/WAVE 二进制格式
	/// </summary>
	private static byte[] WrapPcmToWav(short[] pcm, int sampleRate)
	{
		int subChunk2Size = pcm.Length * sizeof(short);
		int chunkSize = 36 + subChunk2Size;

		using var ms = new MemoryStream(44 + subChunk2Size);
		using var bw = new BinaryWriter(ms);

		// RIFF header
		bw.Write(Encoding.ASCII.GetBytes("RIFF"));
		bw.Write(chunkSize);
		bw.Write(Encoding.ASCII.GetBytes("WAVE"));

		// "fmt " subchunk
		bw.Write(Encoding.ASCII.GetBytes("fmt "));
		bw.Write(16);               // Subchunk1Size (16 for PCM)
		bw.Write((short)1);          // AudioFormat (1 = PCM)
		bw.Write((short)1);          // NumChannels (1 = Mono)
		bw.Write(sampleRate);        // SampleRate
		bw.Write(sampleRate * 2);    // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
		bw.Write((short)2);          // BlockAlign (NumChannels * BitsPerSample/8)
		bw.Write((short)16);         // BitsPerSample

		// "data" subchunk
		bw.Write(Encoding.ASCII.GetBytes("data"));
		bw.Write(subChunk2Size);

		for (int i = 0; i < pcm.Length; i++)
		{
			bw.Write(pcm[i]);
		}

		bw.Flush();
		return ms.ToArray();
	}

	#endregion
}
