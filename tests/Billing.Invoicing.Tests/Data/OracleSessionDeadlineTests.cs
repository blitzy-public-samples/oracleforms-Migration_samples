using System.Diagnostics;
using Billing.Invoicing.Data.Oracle;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Deadline, abandonment and background-release behaviour of <see cref="OracleSession"/> calls and the <see cref="OracleSessionFactory"/> timeout check.</summary>
[Trait("Category", "DataUnit")]
public sealed class OracleSessionDeadlineTests
{
    private const string CommandTimeoutSecondsKey = "Invoicing:CommandTimeoutSeconds";
    private const string Operation = "connection open";

    private static readonly TimeSpan ShortDeadline = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunWithinDeadline_CallSucceeds_ReturnsWithoutAbandoning()
    {
        var abandoned = new List<Task>();
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => Task.CompletedTask, abandoned.Add, CancellationToken.None);

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => later.Task, abandoned.Add, CancellationToken.None);
        later.SetResult();
        await run.WaitAsync(Prompt);

        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallFailsBeforeDeadline_PropagatesTheSameException()
    {
        var abandoned = new List<Task>();
        var immediate = new InvalidOperationException("driver failed at once");
        var later = new InvalidOperationException("driver failed later");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        InvalidOperationException first = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => Task.FromException(immediate), abandoned.Add, CancellationToken.None));

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => pending.Task, abandoned.Add, CancellationToken.None);
        pending.SetException(later);
        InvalidOperationException second = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Prompt));

        Assert.Same(immediate, first);
        Assert.Same(later, second);
        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallThrowsSynchronously_PropagatesTheSameException()
    {
        var abandoned = new List<Task>();
        var failure = new InvalidOperationException("driver refused the call");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => throw failure, abandoned.Add, CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallNeverCompletes_ThrowsTimeoutPromptlyAndHandsOverThePendingCall()
    {
        var abandoned = new List<Task>();
        var never = new TaskCompletionSource();
        CancellationToken callToken = default;
        var stopwatch = Stopwatch.StartNew();

        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => OracleSession.RunWithinDeadline(
            Operation,
            ShortDeadline,
            token =>
            {
                callToken = token;
                return never.Task;
            },
            abandoned.Add,
            CancellationToken.None));
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, ShortDeadline / 2, Prompt);
        Assert.Equal("The Oracle connection open did not complete within 0.1 seconds.", timeout.Message);
        Assert.NotNull(timeout.InnerException);
        Assert.Same(never.Task, Assert.Single(abandoned));
        Assert.True(callToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunWithinDeadline_CallHonouringItsToken_ThrowsTimeoutWithoutAbandoning()
    {
        var abandoned = new List<Task>();

        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => OracleSession.RunWithinDeadline(
            "commit",
            ShortDeadline,
            token => Task.Delay(Timeout.Infinite, token),
            abandoned.Add,
            CancellationToken.None));

        Assert.Equal("The Oracle commit did not complete within 0.1 seconds.", timeout.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);
        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallFailingWhenItsTokenFires_WrapsThatFailureInTheTimeout()
    {
        var abandoned = new List<Task>();
        var driverCancel = new InvalidOperationException("driver cancelled the call");

        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => OracleSession.RunWithinDeadline(
            "rollback",
            ShortDeadline,
            token => FailWhenCancelled(token, driverCancel),
            abandoned.Add,
            CancellationToken.None));

        Assert.Same(driverCancel, timeout.InnerException);
        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallerCancels_ThrowsOperationCanceledAndHandsOverThePendingCall()
    {
        var abandoned = new List<Task>();
        var never = new TaskCompletionSource();
        using var caller = new CancellationTokenSource();

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => never.Task, abandoned.Add, caller.Token);
        await caller.CancelAsync();
        Exception thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Prompt));

        Assert.IsNotType<TimeoutException>(thrown);
        Assert.Same(never.Task, Assert.Single(abandoned));
    }

    [Fact]
    public async Task RunWithinDeadline_CallerCancelsACallHonouringItsToken_ThrowsOperationCanceledWithoutAbandoning()
    {
        var abandoned = new List<Task>();
        using var caller = new CancellationTokenSource();

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, token => Task.Delay(Timeout.Infinite, token), abandoned.Add, caller.Token);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Prompt));

        Assert.Empty(abandoned);
    }

    [Fact]
    public async Task RunWithinDeadline_CallerAlreadyCancelled_ThrowsOperationCanceled()
    {
        var abandoned = new List<Task>();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OracleSession.RunWithinDeadline(Operation, LongDeadline, Task.FromCanceled, abandoned.Add, caller.Token));

        Assert.Empty(abandoned);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-10_000_000L)]
    public async Task RunWithinDeadline_DeadlineNotAboveZero_ThrowsWithoutCalling(long ticks)
    {
        bool called = false;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>("deadline", () => OracleSession.RunWithinDeadline(
            Operation,
            TimeSpan.FromTicks(ticks),
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            _ => { },
            CancellationToken.None));

        Assert.False(called);
    }

    [Fact]
    public async Task RunWithinDeadline_DeadlineAboveTheMaximum_ThrowsWithoutCalling()
    {
        bool called = false;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>("deadline", () => OracleSession.RunWithinDeadline(
            Operation,
            OracleSession.MaxDeadline + TimeSpan.FromMilliseconds(1),
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            _ => { },
            CancellationToken.None));

        Assert.False(called);
    }

    [Fact]
    public async Task RunWithinDeadline_NullOrMissingArguments_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("operation", () => OracleSession.RunWithinDeadline(null!, LongDeadline, _ => Task.CompletedTask, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>("operation", () => OracleSession.RunWithinDeadline(" ", LongDeadline, _ => Task.CompletedTask, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("call", () => OracleSession.RunWithinDeadline(Operation, LongDeadline, null!, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("onAbandoned", () => OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => Task.CompletedTask, null!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => null!, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseWhenSettled_DisposesNothingBeforeThePendingCallSettlesAndEveryResourceInOrderAfter()
    {
        var journal = new List<string>();
        var lastDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        OracleSession.ReleaseWhenSettled(
            pending.Task,
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal, signal: lastDisposed));
        await Task.Delay(ShortDeadline);

        Assert.Empty(Snapshot(journal));

        pending.SetResult();
        await lastDisposed.Task.WaitAsync(Prompt);

        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWhenSettled_PendingCallFaults_DisposesEveryResourceInOrder()
    {
        var journal = new List<string>();
        var lastDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        OracleSession.ReleaseWhenSettled(
            pending.Task,
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal, signal: lastDisposed));
        pending.SetException(new InvalidOperationException("ORA-03113: end-of-file on communication channel"));
        await lastDisposed.Task.WaitAsync(Prompt);

        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWhenSettled_PendingCallCancelled_DisposesEveryResourceInOrder()
    {
        var journal = new List<string>();
        var lastDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        OracleSession.ReleaseWhenSettled(pending.Task, new RecordingResource("connection", journal, signal: lastDisposed));
        pending.SetCanceled();
        await lastDisposed.Task.WaitAsync(Prompt);

        Assert.Equal(["connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWhenSettled_ThrowingResource_DoesNotStopTheNext()
    {
        var journal = new List<string>();
        var lastDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        OracleSession.ReleaseWhenSettled(
            Task.CompletedTask,
            new RecordingResource("throws", journal, new InvalidOperationException("dispose failed"), throwSynchronously: true),
            new RecordingResource("faults", journal, new InvalidOperationException("dispose faulted")),
            new RecordingResource("connection", journal, signal: lastDisposed));
        await lastDisposed.Task.WaitAsync(Prompt);

        Assert.Equal(["throws", "faults", "connection"], Snapshot(journal));
    }

    [Fact]
    public void ReleaseWhenSettled_NullArguments_Throw()
    {
        var journal = new List<string>();

        Assert.Throws<ArgumentNullException>("pending", () => OracleSession.ReleaseWhenSettled(null!, new RecordingResource("connection", journal)));
        Assert.Throws<ArgumentNullException>("resources", () => OracleSession.ReleaseWhenSettled(Task.CompletedTask, null!));
        Assert.Throws<ArgumentNullException>("resources", () => OracleSession.ReleaseWhenSettled(Task.CompletedTask, new RecordingResource("connection", journal), null!));
        Assert.Empty(journal);
    }

    [Fact]
    public async Task ReleaseWithinDeadline_RollbackSucceeds_RollsBackThenReleasesTransactionAndConnectionBeforeReturning()
    {
        var journal = new List<string>();

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            LongDeadline,
            null,
            _ => Record(journal, "rollback"),
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal));

        Assert.Null(failures);
        Assert.Equal(["rollback", "transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_CompletedTransaction_ReleasesWithoutRollingBack()
    {
        var journal = new List<string>();

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            LongDeadline,
            null,
            null,
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal));

        Assert.Null(failures);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_RollbackTimesOutAndSettles_ReturnsPromptlyAndReleasesInTheBackground()
    {
        var journal = new List<string>();
        var transactionReleaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transactionReleaseEnds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            ShortDeadline,
            null,
            token => FailWhenCancelled(token, new OperationCanceledException(token)),
            new HangingResource("transaction", journal, transactionReleaseStarted, transactionReleaseEnds.Task),
            new RecordingResource("connection", journal, signal: connectionReleased));
        watch.Stop();

        Assert.True(watch.Elapsed < Prompt, $"Returned after {watch.Elapsed}.");
        Exception failure = Assert.Single(failures!);
        TimeoutException timeout = Assert.IsType<TimeoutException>(failure);
        Assert.Contains("rollback", timeout.Message, StringComparison.Ordinal);

        await transactionReleaseStarted.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction"], Snapshot(journal));

        transactionReleaseEnds.SetResult();
        await connectionReleased.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_RollbackFails_ReturnsThatFailureAndReleasesInTheBackground()
    {
        var journal = new List<string>();
        var connectionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rollbackFailure = new InvalidOperationException("ORA-03113: end-of-file on communication channel");

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            LongDeadline,
            null,
            _ => Task.FromException(rollbackFailure),
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal, signal: connectionReleased));

        Assert.Same(rollbackFailure, Assert.Single(failures!));
        await connectionReleased.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_RollbackStillRunningAtItsDeadline_ReleasesOnlyAfterItSettles()
    {
        var journal = new List<string>();
        var rollback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            ShortDeadline,
            null,
            _ => rollback.Task,
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal, signal: connectionReleased));

        Assert.IsType<TimeoutException>(Assert.Single(failures!));
        await Task.Delay(ShortDeadline);
        Assert.Empty(Snapshot(journal));

        rollback.SetResult();
        await connectionReleased.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_CallStillRunning_SkipsTheRollbackAndReleasesAfterTheCallSettles()
    {
        var journal = new List<string>();
        var stillRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            LongDeadline,
            stillRunning.Task,
            _ => Record(journal, "rollback"),
            new RecordingResource("transaction", journal),
            new RecordingResource("connection", journal, signal: connectionReleased));

        Assert.Null(failures);
        await Task.Delay(ShortDeadline);
        Assert.Empty(Snapshot(journal));

        stillRunning.SetException(new InvalidOperationException("ORA-01013: user requested cancel of current operation"));
        await connectionReleased.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_TransactionReleaseHangs_ReturnsATimeoutAndReleasesTheConnectionAfterIt()
    {
        var journal = new List<string>();
        var transactionReleaseEnds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            ShortDeadline,
            null,
            null,
            new HangingResource("transaction", journal, null, transactionReleaseEnds.Task),
            new RecordingResource("connection", journal, signal: connectionReleased));
        watch.Stop();

        Assert.True(watch.Elapsed < Prompt, $"Returned after {watch.Elapsed}.");
        TimeoutException timeout = Assert.IsType<TimeoutException>(Assert.Single(failures!));
        Assert.Contains("transaction release", timeout.Message, StringComparison.Ordinal);
        Assert.Equal(["transaction"], Snapshot(journal));

        transactionReleaseEnds.SetResult();
        await connectionReleased.Task.WaitAsync(Prompt);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_ConnectionReleaseHangs_ReturnsATimeout()
    {
        var journal = new List<string>();
        var connectionReleaseEnds = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            ShortDeadline,
            null,
            null,
            new RecordingResource("transaction", journal),
            new HangingResource("connection", journal, null, connectionReleaseEnds.Task));
        watch.Stop();

        Assert.True(watch.Elapsed < Prompt, $"Returned after {watch.Elapsed}.");
        TimeoutException timeout = Assert.IsType<TimeoutException>(Assert.Single(failures!));
        Assert.Contains("connection release", timeout.Message, StringComparison.Ordinal);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
        connectionReleaseEnds.SetResult();
    }

    [Fact]
    public async Task ReleaseWithinDeadline_ReleaseFailures_AreReturnedInOrder()
    {
        var journal = new List<string>();
        var transactionFailure = new InvalidOperationException("transaction release failed");
        var connectionFailure = new InvalidOperationException("connection release failed");

        List<Exception>? failures = await OracleSession.ReleaseWithinDeadline(
            LongDeadline,
            null,
            null,
            new RecordingResource("transaction", journal, transactionFailure),
            new RecordingResource("connection", journal, connectionFailure, throwSynchronously: true));

        Assert.NotNull(failures);
        Assert.Equal(2, failures.Count);
        Assert.Same(transactionFailure, failures[0]);
        Assert.Same(connectionFailure, failures[1]);
        Assert.Equal(["transaction", "connection"], Snapshot(journal));
    }

    [Fact]
    public async Task ReleaseWithinDeadline_InvalidArguments_Throw()
    {
        var journal = new List<string>();
        var resource = new RecordingResource("resource", journal);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>("deadline", () => OracleSession.ReleaseWithinDeadline(TimeSpan.Zero, null, null, resource, resource));
        await Assert.ThrowsAsync<ArgumentNullException>("transaction", () => OracleSession.ReleaseWithinDeadline(LongDeadline, null, null, null!, resource));
        await Assert.ThrowsAsync<ArgumentNullException>("connection", () => OracleSession.ReleaseWithinDeadline(LongDeadline, null, null, resource, null!));
        Assert.Empty(journal);
    }

    [Fact]
    public void MaxCommandTimeoutSeconds_IsTheLongestWholeSecondDeadline()
    {
        Assert.True(TimeSpan.FromSeconds(InvoicingDataOptions.MaxCommandTimeoutSeconds) <= OracleSession.MaxDeadline);
        Assert.True(TimeSpan.FromSeconds(InvoicingDataOptions.MaxCommandTimeoutSeconds + 1L) > OracleSession.MaxDeadline);
    }

    [Fact]
    public void FactoryConstructor_CommandTimeoutAtTheMaximum_IsAccepted()
    {
        var options = new InvoicingDataOptions { ConnectionString = "Data Source=HISDB", CommandTimeoutSeconds = InvoicingDataOptions.MaxCommandTimeoutSeconds };

        Assert.IsType<OracleSessionFactory>(new OracleSessionFactory(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InvoicingDataOptions.MaxCommandTimeoutSeconds + 1)]
    [InlineData(int.MaxValue)]
    public void FactoryConstructor_CommandTimeoutOutOfRange_ThrowsNamingTheKey(int commandTimeoutSeconds)
    {
        var options = new InvoicingDataOptions { ConnectionString = "Data Source=HISDB", CommandTimeoutSeconds = commandTimeoutSeconds };

        var exception = Assert.Throws<ArgumentOutOfRangeException>("options", () => new OracleSessionFactory(options));

        Assert.Equal(commandTimeoutSeconds, exception.ActualValue);
        Assert.Contains(CommandTimeoutSecondsKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InvoicingDataOptions.MaxCommandTimeoutSeconds + 1)]
    public async Task OpenConnection_CommandTimeoutOutOfRange_ThrowsNamingTheKeyBeforeOpening(int commandTimeoutSeconds)
    {
        var options = new InvoicingDataOptions { ConnectionString = "Data Source=HISDB", CommandTimeoutSeconds = commandTimeoutSeconds };

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>("options", () => OracleSessionFactory.OpenConnection(options, CancellationToken.None));

        Assert.Equal(commandTimeoutSeconds, exception.ActualValue);
        Assert.Contains(CommandTimeoutSecondsKey, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Returns a call that fails with <paramref name="failure"/> as soon as <paramref name="token"/> is cancelled.</summary>
    /// <param name="token">The call's token.</param>
    /// <param name="failure">The failure the call ends with.</param>
    /// <returns>The call's task.</returns>
    private static Task FailWhenCancelled(CancellationToken token, Exception failure)
    {
        var call = new TaskCompletionSource();
        token.Register(() => call.TrySetException(failure));
        return call.Task;
    }

    /// <summary>Writes an entry to the journal under its lock.</summary>
    /// <param name="journal">The shared journal.</param>
    /// <param name="entry">The entry to write.</param>
    /// <returns>A completed task.</returns>
    private static Task Record(List<string> journal, string entry)
    {
        lock (journal)
        {
            journal.Add(entry);
        }

        return Task.CompletedTask;
    }

    /// <summary>Copies the journal under its lock.</summary>
    /// <param name="journal">The journal a <see cref="RecordingResource"/> writes to.</param>
    /// <returns>The entries recorded so far.</returns>
    private static string[] Snapshot(List<string> journal)
    {
        lock (journal)
        {
            return [.. journal];
        }
    }

    /// <summary>Records its disposal in a journal, then optionally fails and signals.</summary>
    /// <param name="name">Name written to the journal.</param>
    /// <param name="journal">The shared journal.</param>
    /// <param name="failure">Failure raised by the disposal, or null.</param>
    /// <param name="throwSynchronously">Whether <paramref name="failure"/> is thrown rather than returned as a faulted task.</param>
    /// <param name="signal">Completed once the disposal is recorded, or null.</param>
    private sealed class RecordingResource(
        string name,
        List<string> journal,
        Exception? failure = null,
        bool throwSynchronously = false,
        TaskCompletionSource? signal = null) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (journal)
            {
                journal.Add(name);
            }

            signal?.TrySetResult();

            if (failure is null)
            {
                return ValueTask.CompletedTask;
            }

            if (throwSynchronously)
            {
                throw failure;
            }

            return ValueTask.FromException(failure);
        }
    }

    /// <summary>Records the start of its disposal in a journal, then completes only when <paramref name="ends"/> does.</summary>
    /// <param name="name">Name written to the journal.</param>
    /// <param name="journal">The shared journal.</param>
    /// <param name="started">Completed once the disposal is recorded, or null.</param>
    /// <param name="ends">Task whose completion ends the disposal.</param>
    private sealed class HangingResource(
        string name,
        List<string> journal,
        TaskCompletionSource? started,
        Task ends) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (journal)
            {
                journal.Add(name);
            }

            started?.TrySetResult();
            return new ValueTask(ends);
        }
    }
}
