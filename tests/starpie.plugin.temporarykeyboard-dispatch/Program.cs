using StarPie.Plugin.TemporaryKeyboard;

int checks = 0;
void Check(bool condition) { if (!condition) throw new InvalidOperationException("Dispatch assertion failed"); checks++; }
Func<Action, Task> swallowing = callback => { try { callback(); } catch { } return Task.CompletedTask; };
int executed = 0;
await KeyboardDispatch.RunAsync(swallowing, () => executed++, CancellationToken.None); Check(executed == 1);
bool legacyReportedSuccess = false;
await swallowing(() => { legacyReportedSuccess = true; throw new IOException("UI failed"); });
Check(legacyReportedSuccess); // Legacy completion alone does not prove a window opened.
try { await KeyboardDispatch.RunAsync(swallowing, () => throw new IOException("sentinel"), CancellationToken.None); throw new InvalidOperationException("Expected UI failure"); }
catch (IOException ex) { Check(ex.Message == "sentinel"); }
try { await KeyboardDispatch.RunAsync(_ => Task.CompletedTask, () => executed++, CancellationToken.None); throw new InvalidOperationException("Expected skipped dispatch failure"); }
catch (OperationCanceledException) { Check(executed == 1); }
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
try { await KeyboardDispatch.RunAsync(swallowing, () => executed++, cancellation.Token); throw new InvalidOperationException("Expected cancellation"); }
catch (OperationCanceledException) { Check(executed == 1); }
try { await KeyboardDispatch.RunAsync(_ => Task.FromException(new IOException("dispatcher")), () => executed++, CancellationToken.None); throw new InvalidOperationException("Expected dispatcher failure"); }
catch (IOException ex) { Check(ex.Message == "dispatcher"); }
Console.WriteLine($"PASS {checks} dispatcher-result checks; no window, input, SDK or plugin action");
