# Awaitable WinForms UI work

1. Put the operation in a Task-returning method that callers/tests can await through success,
   failure, and cancellation. Let a required async void event handler await that operation.
2. A WinForms event normally resumes on its captured UI synchronization context after await.
   Direct control access there is valid unless the context was deliberately bypassed.
3. For genuinely off-context work, select dispatch using the actual target API:
   - .NET 9 and later: choose and await the Control.InvokeAsync overload matching synchronous
     or asynchronous work and whether a result is returned. Pass cancellation deliberately;
     do not accidentally select an Action overload for an async delegate.
   - Older targets: preserve an established awaitable dispatch adapter or capture the UI context
     at a valid UI lifecycle point. Do not assume InvokeAsync exists. If implementing a legacy
     BeginInvoke-to-Task adapter is necessary, propagate callback exceptions/cancellation and
     handle control disposal; a bare unawaited BeginInvoke is not an awaitable completion path.
4. Restore enabled/busy state in finally. Prevent overlapping commands according to the actual
   UX contract, and cancel/finish pending work before disposing the controls it uses.
5. Async void is acceptable only for event handlers/overrides that must return void. Catch
   around awaited work at that UI boundary, handle expected cancellation intentionally, and
   surface unexpected errors through the repository's established notification/logging path.

Do not use `_ =`, unawaited posts, dead cancellation catches, or application-wide exception hooks
as normal error/completion handling. Application.ThreadException covers UI-thread exceptions;
AppDomain.UnhandledException is primarily last-chance logging, not permission to continue safely.

## Verify

Await the Task-returning method under success, failure, and cancellation. Verify that the UI event
delegates to it, UI state is restored, control access occurs on the UI thread, and closing during
work does not leave callbacks targeting disposed controls. Compile against the actual target.
