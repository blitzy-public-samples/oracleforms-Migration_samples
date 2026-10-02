using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Deadline, abandonment and background-release behaviour of <see cref="OracleSession"/> calls, the <see cref="OracleSessionFactory"/> timeout check and its per-connection-string open gate.</summary>
[Trait("Category", "DataUnit")]
public sealed class OracleSessionDeadlineTests
{
    private const string CommandTimeoutSecondsKey = "Invoicing:CommandTimeoutSeconds";
    private const string Operation = "connection open";
    private const string ConnectFailureText = "ORA-50201: Oracle Communication: Failed to connect to server or failed to parse connect string";

    private static readonly TimeSpan ShortDeadline = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

    private static readonly ConstructorInfo? OracleExceptionConstructor = typeof(OracleException).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(Exception) },
        modifiers: null);

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
    [Trait("Decision", "D-89")]
    public async Task RunWithinDeadline_CallerCancelsACallFailingWhenItsTokenFires_PropagatesThatFailureWithoutAbandoning()
    {
        var abandoned = new List<Task>();
        var driverCancel = new InvalidOperationException("driver cancelled the call");
        using var caller = new CancellationTokenSource();

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, token => FailWhenCancelled(token, driverCancel), abandoned.Add, caller.Token);
        await caller.CancelAsync();
        Exception thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Prompt));

        Assert.Same(driverCancel, thrown);
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

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task RunWithinDeadline_ReleasedWhileTheCallRuns_ThrowsTheReleaseAtOnceAndHandsOverTheUncancelledCall()
    {
        var abandoned = new List<Task>();
        var call = new FakeOpen();
        var released = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = new SocketException((int)SocketError.ConnectionRefused);

        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, call.Open, abandoned.Add, CancellationToken.None, released.Task);
        var stopwatch = Stopwatch.StartNew();
        released.SetResult(peer);
        OracleOpenReleasedException thrown = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => run.WaitAsync(Prompt));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < Prompt, $"Released after {stopwatch.Elapsed}.");
        Assert.Same(peer, thrown.InnerException);
        Assert.Same(call.Task, Assert.Single(abandoned));
        Assert.False(call.Token.IsCancellationRequested);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task RunWithinDeadline_ReleaseFaulted_ThrowsTheReleaseWithThatFault()
    {
        var abandoned = new List<Task>();
        var call = new FakeOpen();
        var fault = new InvalidOperationException("release signal faulted");

        OracleOpenReleasedException thrown = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => OracleSession.RunWithinDeadline(
            Operation, LongDeadline, call.Open, abandoned.Add, CancellationToken.None, Task.FromException<Exception>(fault)));

        Assert.Same(fault, thrown.InnerException);
        Assert.Same(call.Task, Assert.Single(abandoned));
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task RunWithinDeadline_CallAlreadyCompletedWhenReleased_KeepsTheCallsOwnOutcome()
    {
        var abandoned = new List<Task>();
        var own = new InvalidOperationException("driver failed at once");
        Task<Exception> released = Task.FromResult<Exception>(new SocketException((int)SocketError.ConnectionRefused));

        await OracleSession.RunWithinDeadline(Operation, LongDeadline, _ => Task.CompletedTask, abandoned.Add, CancellationToken.None, released);
        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => OracleSession.RunWithinDeadline(
            Operation, LongDeadline, _ => Task.FromException(own), abandoned.Add, CancellationToken.None, released));

        Assert.Same(own, thrown);
        Assert.Empty(abandoned);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task RunWithinDeadline_ReleaseNotCompleted_KeepsTheDeadlineAndCallerCancellation()
    {
        var abandoned = new List<Task>();
        var never = new TaskCompletionSource<Exception>();
        var timedOut = new FakeOpen();
        var cancelled = new FakeOpen();
        using var caller = new CancellationTokenSource();

        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => OracleSession.RunWithinDeadline(
            Operation, ShortDeadline, timedOut.Open, abandoned.Add, CancellationToken.None, never.Task));
        Task run = OracleSession.RunWithinDeadline(Operation, LongDeadline, cancelled.Open, abandoned.Add, caller.Token, never.Task);
        await caller.CancelAsync();
        Exception thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Prompt));

        Assert.Equal("The Oracle connection open did not complete within 0.1 seconds.", timeout.Message);
        Assert.True(timedOut.Token.IsCancellationRequested);
        Assert.IsNotType<TimeoutException>(thrown);
        Assert.Equal([timedOut.Task, cancelled.Task], abandoned);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ConcurrentOpenFailsWithAnOutage_ReleasesTheWaitingOpenOnceItsResourcesAreReleased()
    {
        string key = GateKey();
        await PrimeHealthy(key);
        var failing = new FakeOpen();
        var waiting = new FakeOpen();
        var failingReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingRelease = new ReleaseRecorder();
        var outage = new SocketException((int)SocketError.ConnectionRefused);

        Task first = Gate(key, failing.Open, (_, _) => new ValueTask(failingReleased.Task));
        Task second = Gate(key, waiting.Open, waitingRelease.Release);
        failing.Fail(outage);
        await Task.Delay(ShortDeadline);

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        failingReleased.SetResult();
        SocketException thrown = await Assert.ThrowsAsync<SocketException>(() => first.WaitAsync(Prompt));
        OracleOpenReleasedException released = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => second.WaitAsync(Prompt));

        Assert.Same(outage, thrown);
        Assert.Same(outage, released.InnerException);
        Assert.True(outage.Data[OracleErrorParser.DuringOpenKey] is true);
        Assert.True(released.Data[OracleErrorParser.DuringOpenKey] is true);
        Assert.Same(waiting.Task, waitingRelease.Abandoned);
        Assert.Same(released, waitingRelease.Failure);
        Assert.False(waiting.Token.IsCancellationRequested);
        Assert.Equal(1, waiting.Calls);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ReleaseOfAFailedOpenThrows_AttachesThatFailureAndStillReleasesTheWaitingOpen()
    {
        string key = GateKey();
        await PrimeHealthy(key);
        var failing = new FakeOpen();
        var waiting = new FakeOpen();
        var releaseFailure = new InvalidOperationException("dispose failed");
        var outage = new TimeoutException("driver connect timeout");

        Task first = Gate(key, failing.Open, (_, _) => throw releaseFailure);
        Task second = Gate(key, waiting.Open);
        failing.Fail(outage);

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(() => first.WaitAsync(Prompt));
        OracleOpenReleasedException released = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => second.WaitAsync(Prompt));

        Assert.Same(outage, thrown);
        Assert.Same(releaseFailure, Assert.Single(Assert.IsType<List<Exception>>(outage.Data[OracleSession.SecondaryFailuresKey])));
        Assert.Same(outage, released.InnerException);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ColdGateBurstProbeFailsWithAnOutage_ReleasesTheJoinedOpensWithoutTheirOwnOpen()
    {
        string key = GateKey();
        var probe = new FakeOpen();
        FakeOpen[] joiners = [.. Enumerable.Range(0, 19).Select(_ => new FakeOpen())];
        ReleaseRecorder[] releases = [.. joiners.Select(_ => new ReleaseRecorder())];
        OracleException probeFailure = ConnectionRequestTimeout();

        Task probing = Gate(key, probe.Open);
        Task[] joined = [.. joiners.Select((joiner, index) => Gate(key, joiner.Open, releases[index].Release))];
        await Task.Delay(ShortDeadline);

        Assert.Equal(1, probe.Calls);
        Assert.All(joiners, joiner => Assert.Equal(0, joiner.Calls));
        Assert.All(joined, task => Assert.False(task.IsCompleted));

        probe.Fail(probeFailure);
        Assert.Same(probeFailure, await Assert.ThrowsAsync<OracleException>(() => probing.WaitAsync(Prompt)));
        Assert.True(probeFailure.Data[OracleErrorParser.DuringOpenKey] is true);
        for (int index = 0; index < joined.Length; index++)
        {
            Task task = joined[index];
            OracleOpenReleasedException released = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => task.WaitAsync(Prompt));
            Assert.Same(probeFailure, released.InnerException);
            Assert.True(released.Data[OracleErrorParser.DuringOpenKey] is true);
            Assert.Null(releases[index].Abandoned);
            Assert.Same(released, releases[index].Failure);
        }

        Assert.All(joiners, joiner => Assert.Equal(0, joiner.Calls));
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ColdGateProbeSucceeds_JoinedOpensOpenThemselvesAndLaterOpensOpenAtOnce()
    {
        string key = GateKey();
        var probe = new FakeOpen();
        var joiners = new[] { new FakeOpen(), new FakeOpen() };

        Task probing = Gate(key, probe.Open);
        Task[] joined = [.. joiners.Select(joiner => Gate(key, joiner.Open))];
        Assert.Equal(1, probe.Calls);
        Assert.All(joiners, joiner => Assert.Equal(0, joiner.Calls));

        probe.Succeed();
        await probing.WaitAsync(Prompt);
        await Task.WhenAll(joiners.Select(joiner => joiner.Called)).WaitAsync(Prompt);

        var next = new[] { new FakeOpen(), new FakeOpen() };
        Task[] opens = [.. next.Select(open => Gate(key, open.Open))];

        Assert.All(next, open => Assert.Equal(1, open.Calls));
        Assert.All(joined, task => Assert.False(task.IsCompleted));
        Assert.All(joiners, joiner => joiner.Succeed());
        Assert.All(next, open => open.Succeed());
        await Task.WhenAll(joined.Concat(opens)).WaitAsync(Prompt);
        Assert.All(joiners, joiner => Assert.Equal(1, joiner.Calls));
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ProbeFailsAfterAnOutage_ReleasesTheJoinedOpensWithoutTheirOwnOpen()
    {
        string key = GateKey();
        await PrimeFailing(key);
        var probe = new FakeOpen();
        var joiners = new[] { new FakeOpen(), new FakeOpen() };
        var releases = new[] { new ReleaseRecorder(), new ReleaseRecorder() };
        var probeFailure = new TimeoutException("driver connect timeout");

        Task probing = Gate(key, probe.Open);
        Task[] joined = [.. joiners.Select((joiner, index) => Gate(key, joiner.Open, releases[index].Release))];
        await Task.Delay(ShortDeadline);

        Assert.Equal(1, probe.Calls);
        Assert.All(joined, task => Assert.False(task.IsCompleted));

        probe.Fail(probeFailure);
        Assert.Same(probeFailure, await Assert.ThrowsAsync<TimeoutException>(() => probing.WaitAsync(Prompt)));
        for (int index = 0; index < joined.Length; index++)
        {
            Task task = joined[index];
            OracleOpenReleasedException released = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => task.WaitAsync(Prompt));
            Assert.Same(probeFailure, released.InnerException);
            Assert.True(released.Data[OracleErrorParser.DuringOpenKey] is true);
            Assert.Null(releases[index].Abandoned);
            Assert.Same(released, releases[index].Failure);
        }

        Assert.All(joiners, joiner => Assert.Equal(0, joiner.Calls));
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ProbeSucceedsAfterAnOutage_JoinedOpensOpenThemselvesAndTheGateStopsFailing()
    {
        string key = GateKey();
        await PrimeFailing(key);
        var probe = new FakeOpen();
        var joiners = new[] { new FakeOpen(), new FakeOpen() };

        Task probing = Gate(key, probe.Open);
        Task[] joined = [.. joiners.Select(joiner => Gate(key, joiner.Open))];
        Assert.All(joiners, joiner => Assert.Equal(0, joiner.Calls));

        probe.Succeed();
        await probing.WaitAsync(Prompt);
        await Task.WhenAll(joiners.Select(joiner => joiner.Called)).WaitAsync(Prompt);
        Assert.All(joiners, joiner => joiner.Succeed());
        await Task.WhenAll(joined).WaitAsync(Prompt);

        var next = new[] { new FakeOpen(), new FakeOpen() };
        Task[] opens = [.. next.Select(open => Gate(key, open.Open))];

        Assert.All(next, open => Assert.Equal(1, open.Calls));
        Assert.All(next, open => open.Succeed());
        await Task.WhenAll(opens).WaitAsync(Prompt);
        Assert.All(joiners, joiner => Assert.Equal(1, joiner.Calls));
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_CallerCancelledOpen_IsNotPublished()
    {
        string key = GateKey();
        await PrimeHealthy(key);
        var cancelled = new FakeOpen();
        var driverCancelled = new FakeOpen(failWhenCancelled: new SocketException((int)SocketError.OperationAborted));
        var waiting = new FakeOpen();
        using var caller = new CancellationTokenSource();
        using var driverCaller = new CancellationTokenSource();

        Task first = Gate(key, cancelled.Open, cancellationToken: caller.Token);
        Task second = Gate(key, driverCancelled.Open, cancellationToken: driverCaller.Token);
        Task third = Gate(key, waiting.Open);
        await caller.CancelAsync();
        await driverCaller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Prompt));
        await Assert.ThrowsAsync<SocketException>(() => second.WaitAsync(Prompt));
        await Task.Delay(ShortDeadline);
        Assert.False(third.IsCompleted);

        var next = new FakeOpen();
        Task fourth = Gate(key, next.Open);
        Assert.Equal(1, next.Calls);

        next.Succeed();
        waiting.Succeed();
        await Task.WhenAll(third, fourth).WaitAsync(Prompt);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_CallerCancelledProbe_LetsAJoinedOpenProbeNext()
    {
        string key = GateKey();
        await PrimeFailing(key);
        var probe = new FakeOpen();
        var joiner = new FakeOpen();
        using var caller = new CancellationTokenSource();

        Task probing = Gate(key, probe.Open, cancellationToken: caller.Token);
        Task joined = Gate(key, joiner.Open);
        Assert.Equal(0, joiner.Calls);

        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing.WaitAsync(Prompt));
        await joiner.Called.WaitAsync(Prompt);

        var later = new FakeOpen();
        Task laterJoined = Gate(key, later.Open);
        Assert.Equal(0, later.Calls);

        joiner.Succeed();
        await joined.WaitAsync(Prompt);
        await later.Called.WaitAsync(Prompt);
        later.Succeed();
        await laterJoined.WaitAsync(Prompt);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_NonOutageFailure_IsNotPublished()
    {
        string key = GateKey();
        await PrimeHealthy(key);
        var failing = new FakeOpen();
        var waiting = new FakeOpen();
        var notAnOutage = new InvalidOperationException("connection already open");

        Task first = Gate(key, failing.Open);
        Task second = Gate(key, waiting.Open);
        failing.Fail(notAnOutage);

        Assert.Same(notAnOutage, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Prompt)));
        Assert.Null(notAnOutage.Data[OracleErrorParser.DuringOpenKey]);
        await Task.Delay(ShortDeadline);
        Assert.False(second.IsCompleted);

        var next = new FakeOpen();
        Task third = Gate(key, next.Open);
        Assert.Equal(1, next.Calls);

        next.Succeed();
        waiting.Succeed();
        await Task.WhenAll(second, third).WaitAsync(Prompt);

        await PrimeFailing(key);
        var probe = new FakeOpen();
        var joiner = new FakeOpen();
        Task probing = Gate(key, probe.Open);
        Task joined = Gate(key, joiner.Open);
        Assert.Equal(0, joiner.Calls);

        probe.Fail(new InvalidOperationException("connection already open"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => probing.WaitAsync(Prompt));
        await joiner.Called.WaitAsync(Prompt);

        joiner.Succeed();
        await joined.WaitAsync(Prompt);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ReleasedOpenFaultingLaterWithAnOutage_ReleasesTheOpensWaitingThen()
    {
        string key = GateKey();
        await PrimeHealthy(key);
        var failing = new FakeOpen();
        var abandoned = new FakeOpen();
        var probe = new FakeOpen();
        var joiner = new FakeOpen();
        OracleException lateFailure = Driver(50000, "ORA-50000: Connection request timed out");

        Task first = Gate(key, failing.Open);
        Task second = Gate(key, abandoned.Open);
        failing.Fail(new SocketException((int)SocketError.ConnectionReset));
        await Assert.ThrowsAsync<SocketException>(() => first.WaitAsync(Prompt));
        await Assert.ThrowsAsync<OracleOpenReleasedException>(() => second.WaitAsync(Prompt));

        Task probing = Gate(key, probe.Open);
        Task joined = Gate(key, joiner.Open);
        await Task.Delay(ShortDeadline);
        Assert.False(probing.IsCompleted);

        abandoned.Fail(lateFailure);
        OracleOpenReleasedException probeReleased = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => probing.WaitAsync(Prompt));
        OracleOpenReleasedException joinerReleased = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => joined.WaitAsync(Prompt));

        Assert.Same(lateFailure, probeReleased.InnerException);
        Assert.Same(lateFailure, joinerReleased.InnerException);
        Assert.True(lateFailure.Data[OracleErrorParser.DuringOpenKey] is true);
        Assert.Equal(0, joiner.Calls);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenThroughGate_ProbeHangs_JoinedOpenTimesOutWithinItsDeadlineAndOtherGatesOpen()
    {
        string key = GateKey();
        await PrimeFailing(key);
        var probe = new FakeOpen();
        var joiner = new FakeOpen();
        var joinerRelease = new ReleaseRecorder();
        var elsewhere = new FakeOpen();

        Task probing = Gate(key, probe.Open);
        var stopwatch = Stopwatch.StartNew();
        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => Gate(key, joiner.Open, joinerRelease.Release, ShortDeadline));
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, ShortDeadline / 2, Prompt);
        Assert.Equal("The Oracle connection open did not complete within 0.1 seconds.", timeout.Message);
        Assert.True(timeout.Data[OracleErrorParser.DuringOpenKey] is true);
        Assert.Equal(0, joiner.Calls);
        Assert.Null(joinerRelease.Abandoned);
        Assert.Same(timeout, joinerRelease.Failure);

        Task other = Gate(GateKey(), elsewhere.Open);
        Assert.Equal(1, elsewhere.Calls);

        elsewhere.Succeed();
        probe.Succeed();
        await Task.WhenAll(other, probing).WaitAsync(Prompt);
    }

    [Fact]
    public async Task OpenThroughGate_InvalidArguments_ThrowWithoutOpening()
    {
        var open = new FakeOpen();

        await Assert.ThrowsAsync<ArgumentNullException>("key", () => OracleSessionFactory.OpenThroughGate(null!, LongDeadline, open.Open, NoRelease, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>("deadline", () => OracleSessionFactory.OpenThroughGate(GateKey(), TimeSpan.Zero, open.Open, NoRelease, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("open", () => OracleSessionFactory.OpenThroughGate(GateKey(), LongDeadline, null!, NoRelease, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("releaseResources", () => OracleSessionFactory.OpenThroughGate(GateKey(), LongDeadline, open.Open, null!, CancellationToken.None));
        Assert.Equal(0, open.Calls);
    }

    [Fact]
    [Trait("Decision", "D-161")]
    public async Task OpenReleased_TranslatesLikeTheFailureThatReleasedIt()
    {
        var translator = new OracleFailureTranslator();
        OracleException credentials = Driver(1017, "ORA-01017: invalid username/password; logon denied");

        foreach (Exception transport in new Exception[] { new SocketException((int)SocketError.ConnectionRefused), new TimeoutException("driver connect timeout") })
        {
            DataFailure? unavailable = translator.Translate(await Released(transport));

            Assert.NotNull(unavailable);
            Assert.Equal(503, unavailable.Status);
            Assert.Equal(DataFailure.OracleUnavailableType, unavailable.Type);
        }

        DataFailure? bare = translator.Translate(credentials);
        DataFailure? wrapped = translator.Translate(await Released(credentials));

        Assert.NotNull(bare);
        Assert.NotNull(wrapped);
        Assert.Equal(500, wrapped.Status);
        Assert.Equal(bare.Status, wrapped.Status);
        Assert.Equal(bare.Type, wrapped.Type);
        Assert.Equal(bare.Number, wrapped.Number);
        Assert.Equal(bare.Message, wrapped.Message);
    }

    [Fact]
    [Trait("Decision", "D-163")]
    public async Task OpenThroughGate_DriverTransportFailure_TranslatesTo503ForTheFailedAndTheReleasedOpen()
    {
        var translator = new OracleFailureTranslator();

        foreach (Func<OracleException> outage in new Func<OracleException>[] { RefusedConnect, ConnectionRequestTimeout })
        {
            string key = GateKey();
            await PrimeHealthy(key);
            var failing = new FakeOpen();
            var waiting = new FakeOpen();
            OracleException failure = outage();

            Task first = Gate(key, failing.Open);
            Task second = Gate(key, waiting.Open);
            failing.Fail(failure);
            OracleException own = await Assert.ThrowsAsync<OracleException>(() => first.WaitAsync(Prompt));
            OracleOpenReleasedException released = await Assert.ThrowsAsync<OracleOpenReleasedException>(() => second.WaitAsync(Prompt));

            foreach (Exception exception in new Exception[] { own, released })
            {
                DataFailure? translated = translator.Translate(exception);

                Assert.NotNull(translated);
                Assert.Equal(503, translated.Status);
                Assert.Equal(DataFailure.OracleUnavailableType, translated.Type);
                Assert.Equal(failure.Number, translated.Number);
            }

            waiting.Succeed();
        }
    }

    [Fact]
    [Trait("Decision", "D-163")]
    public void Translate_OtherDriverFailuresWhileOpening_Stay500()
    {
        var translator = new OracleFailureTranslator();
        OracleException[] failures =
        {
            Driver(50201, ConnectFailureText, new InvalidOperationException(ConnectFailureText)),
            Driver(303, "ORA-00303: syntax error in NV string"),
            Driver(1017, "ORA-01017: invalid username/password; logon denied"),
        };

        foreach (OracleException failure in failures)
        {
            failure.Data[OracleErrorParser.DuringOpenKey] = true;
            DataFailure? translated = translator.Translate(failure);

            Assert.NotNull(translated);
            Assert.Equal(500, translated.Status);
            Assert.Equal(DataFailure.OracleErrorType, translated.Type);
            Assert.Equal(failure.Number, translated.Number);
        }

        DataFailure? afterOpen = translator.Translate(ConnectionRequestTimeout());

        Assert.NotNull(afterOpen);
        Assert.Equal(500, afterOpen.Status);
        Assert.Equal(50000, afterOpen.Number);
    }

    /// <summary>Returns a gate key no other test uses.</summary>
    private static string GateKey() => $"gate-{Guid.NewGuid():N}";

    /// <summary>Opens through the gate of <paramref name="key"/> with test defaults.</summary>
    /// <param name="key">The gate key.</param>
    /// <param name="open">The open to run.</param>
    /// <param name="release">Releases a failed open's resources, or null for none.</param>
    /// <param name="deadline">The open deadline, or null for <see cref="LongDeadline"/>.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The gated open.</returns>
    private static Task Gate(
        string key,
        Func<CancellationToken, Task> open,
        Func<Task?, Exception, ValueTask>? release = null,
        TimeSpan? deadline = null,
        CancellationToken cancellationToken = default) =>
        OracleSessionFactory.OpenThroughGate(key, deadline ?? LongDeadline, open, release ?? NoRelease, cancellationToken);

    /// <summary>Makes the gate of <paramref name="key"/> unhealthy with one open that fails with a socket failure.</summary>
    /// <param name="key">The gate key.</param>
    private static async Task PrimeFailing(string key) =>
        await Assert.ThrowsAsync<SocketException>(() => Gate(key, _ => Task.FromException(new SocketException((int)SocketError.ConnectionRefused))));

    /// <summary>Makes the gate of <paramref name="key"/> healthy with one open that succeeds.</summary>
    /// <param name="key">The gate key.</param>
    private static Task PrimeHealthy(string key) => Gate(key, _ => Task.CompletedTask);

    /// <summary>Releases nothing.</summary>
    private static ValueTask NoRelease(Task? abandoned, Exception failure) => ValueTask.CompletedTask;

    /// <summary>Returns the release raised by an open that another open's failure released.</summary>
    /// <param name="peer">The failure of the other open.</param>
    /// <returns>The release exception.</returns>
    private static Task<OracleOpenReleasedException> Released(Exception peer) =>
        Assert.ThrowsAsync<OracleOpenReleasedException>(() => OracleSession.RunWithinDeadline(
            Operation, LongDeadline, new FakeOpen().Open, _ => { }, CancellationToken.None, Task.FromResult(peer)));

    /// <summary>Builds an ODP.NET exception through its internal constructor.</summary>
    /// <param name="number">ORA code as the driver reports it.</param>
    /// <param name="message">Driver error message.</param>
    /// <param name="inner">Inner exception, or null for a plain driver detail.</param>
    /// <returns>The driver exception.</returns>
    private static OracleException Driver(int number, string message, Exception? inner = null)
    {
        Assert.NotNull(OracleExceptionConstructor);
        return (OracleException)OracleExceptionConstructor.Invoke(new object[]
        {
            number,
            "HISDB",
            "connection open",
            message,
            inner ?? new InvalidOperationException("driver detail"),
        });
    }

    /// <summary>Builds the driver failure of a refused TCP connect: ORA-50201 over the network layer's ORA-50232.</summary>
    /// <returns>The driver exception.</returns>
    private static OracleException RefusedConnect() => Driver(
        50201,
        ConnectFailureText,
        new InvalidOperationException(
            ConnectFailureText,
            new InvalidOperationException("ORA-50232: Network Transport: TCP transport address connect failure for  host 127.0.0.1 port 20374.")));

    /// <summary>Builds the driver's connection-request timeout, ORA-50000.</summary>
    /// <returns>The driver exception.</returns>
    private static OracleException ConnectionRequestTimeout() => Driver(50000, "ORA-50000: Connection request timed out");

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

    /// <summary>A connection open whose outcome the test sets; it records each call and its token.</summary>
    /// <param name="failWhenCancelled">Failure the open ends with as soon as its token is cancelled, or null to ignore the token.</param>
    private sealed class FakeOpen(Exception? failWhenCancelled = null)
    {
        private readonly TaskCompletionSource _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        /// <summary>The task every call returns.</summary>
        public Task Task => _outcome.Task;

        /// <summary>Completes once the open is first called.</summary>
        public Task Called => _called.Task;

        /// <summary>Number of calls made.</summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Token passed to the last call.</summary>
        public CancellationToken Token { get; private set; }

        /// <summary>Starts the open.</summary>
        /// <param name="token">The open's token.</param>
        /// <returns>The open's task.</returns>
        public Task Open(CancellationToken token)
        {
            Token = token;
            Interlocked.Increment(ref _calls);
            if (failWhenCancelled is not null)
            {
                token.Register(() => _outcome.TrySetException(failWhenCancelled));
            }

            _called.TrySetResult();
            return _outcome.Task;
        }

        /// <summary>Completes the open successfully.</summary>
        public void Succeed() => _outcome.SetResult();

        /// <summary>Fails the open.</summary>
        /// <param name="failure">The open failure.</param>
        public void Fail(Exception failure) => _outcome.SetException(failure);
    }

    /// <summary>Records the arguments a gated open releases its resources with.</summary>
    private sealed class ReleaseRecorder
    {
        /// <summary>The open still running when released, or null.</summary>
        public Task? Abandoned { get; private set; }

        /// <summary>The failure the resources were released after.</summary>
        public Exception? Failure { get; private set; }

        /// <summary>Records the release.</summary>
        /// <param name="abandoned">The open still running, or null.</param>
        /// <param name="failure">The open failure.</param>
        /// <returns>A completed release.</returns>
        public ValueTask Release(Task? abandoned, Exception failure)
        {
            Abandoned = abandoned;
            Failure = failure;
            return ValueTask.CompletedTask;
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
