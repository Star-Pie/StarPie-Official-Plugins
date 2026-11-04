using System.Runtime.ExceptionServices;

namespace StarPie.Plugin.TemporaryKeyboard;

internal static class KeyboardDispatch
{
    // The legacy host dispatcher catches callback errors. Preserve this operation's
    // result inside the callback, then report it to the awaiting action instead.
    internal static async Task RunAsync(Func<Action, Task> dispatch, Action action, CancellationToken token)
    {
        Exception? failure = null;
        bool ran = false;
        await dispatch(() =>
        {
            try { token.ThrowIfCancellationRequested(); action(); }
            catch (Exception ex) { failure = ex; }
            finally { ran = true; }
        }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (!ran) throw new OperationCanceledException("Keyboard UI dispatch was not executed.", token);
    }
}
