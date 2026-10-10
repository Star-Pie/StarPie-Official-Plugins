using StarPie.Plugin;
namespace StarPie.Plugin.Sound;

internal sealed class SoundEventRouter
{
    private readonly Dictionary<long, (SoundSessionSource Source, long Id)> _sessions = new();
    internal int SessionCount => _sessions.Count;
    internal long SoundSessionId(long id) => _sessions.TryGetValue(id, out var s) ? s.Id : 0;
    internal void Reset() { _sessions.Clear(); }
    internal void Handle(InteractionEvent input)
    {
        if (!_sessions.TryGetValue(input.SessionId, out var session))
        {
            // 未呈现的会话结束不需要创建音频状态；快速确认则允许没有Presented。
            if (input.Kind == InteractionEventKind.SessionEnded) return;
            var source = input.Source == InteractionSource.StickyWheel ? SoundSessionSource.StickyWheel : SoundSessionSource.NormalGesture;
            if (_sessions.Count >= 64)
            {
                long oldest = _sessions.Keys.First(); var stale = _sessions[oldest];
                SoundEffectManager.EndSession(stale.Source, stale.Id, allowTerminalFeedback: false); _sessions.Remove(oldest);
            }
            session = (source, SoundEffectManager.BeginSession(source)); _sessions[input.SessionId] = session;
        }
        switch (input.Kind)
        {
            case InteractionEventKind.Presented:
                SoundEffectManager.Play(SoundType.WheelPopup, session.Source, session.Id); break;
            case InteractionEventKind.SelectionChanged:
                if (input.Target.Kind == InteractionTargetKind.Sector)
                    SoundEffectManager.ReportHover(session.Source, session.Id, 0, -1, input.Target.Sector);
                else if (input.Target.Kind == InteractionTargetKind.SubSector)
                    SoundEffectManager.ReportHover(session.Source, session.Id, 1, input.Target.Sector, input.Target.SubSector);
                else SoundEffectManager.CancelHover(session.Source, session.Id);
                break;
            case InteractionEventKind.SubmenuExpanded:
                SoundEffectManager.CancelHover(session.Source, session.Id);
                SoundEffectManager.Play(SoundType.SubmenuExpand, session.Source, session.Id); break;
            case InteractionEventKind.NavigationCancelled:
                SoundEffectManager.CancelHover(session.Source, session.Id);
                if (input.Reason == "Escaped") SoundEffectManager.Play(SoundType.GestureCancel, session.Source, session.Id);
                break;
            case InteractionEventKind.ActionCommitted:
                SoundEffectManager.CancelHover(session.Source, session.Id);
                SoundEffectManager.Play(SoundType.ActionExecute, session.Source, session.Id); break;
            case InteractionEventKind.SessionCancelled:
                SoundEffectManager.CancelHover(session.Source, session.Id);
                if (input.Reason is "Escaped" or "Cancelled" or "NoAction" or "NoTarget")
                    SoundEffectManager.Play(SoundType.GestureCancel, session.Source, session.Id);
                break;
            case InteractionEventKind.SessionEnded:
                bool feedback = input.Reason is "ActionCommitted" or "Escaped" or "Cancelled" or "NoAction" or "NoTarget";
                SoundEffectManager.EndSession(session.Source, session.Id, allowTerminalFeedback: feedback);
                _sessions.Remove(input.SessionId); break;
        }
    }
}
