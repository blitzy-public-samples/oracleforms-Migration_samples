using System.Text.RegularExpressions;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Data.Queries;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Statements, bind-width guards and session checks of the <see cref="LookupQueries"/> reads that run inside the create transaction; no connection is opened.</summary>
[Trait("Category", "DataUnit")]
[Trait("Decision", "D-112")]
public sealed class LookupLockQueriesTests
{
    private const string ForUpdateClause = " FOR UPDATE";
    private const string SessionParameter = "session";
    private const string PatientNo = "P0001";
    private const string SubCompCode = "SC1";
    private const string ClassCode = "3";
    private const string ServiceId = "S1";
    private const string PackageServiceId = "PKG1";
    private const string ClaimNo = "O-P0001-5-290926";
    private const decimal ListId = 10m;

    private static readonly Regex BindReference = new(@":(\w+)", RegexOptions.CultureInvariant);

    private static readonly Regex ForUpdateKeyword = new(@"\bFOR\s+UPDATE\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Theory]
    [InlineData(LookupQueries.LockPatientCardIdSql, LookupQueries.GetPatientCardIdSql)]
    [InlineData(LookupQueries.LockClassAdvancedModeSql, LookupQueries.GetClassAdvancedModeSql)]
    [InlineData(LookupQueries.LockServiceProfileSql, LookupQueries.GetServiceProfileSql)]
    [InlineData(LookupQueries.LockPackageComponentFlagsSql, LookupQueries.GetPackageComponentFlagsSql)]
    [InlineData(LookupQueries.LockClaimPreloadSql, InvoiceQueries.GetClaimPreloadSql)]
    public void LockStatement_IsItsUnlockedTwinForUpdate(string lockSql, string unlockedSql)
    {
        Assert.Empty(ForUpdateKeyword.Matches(unlockedSql));
        Assert.Equal(unlockedSql + ForUpdateClause, lockSql);
        Assert.Single(ForUpdateKeyword.Matches(lockSql));
    }

    [Fact]
    public void MaxDeductableStatement_LocksTheViewByPatientOfItsDeductibleColumnOnly()
    {
        string sql = LookupQueries.LockPatientMaxDeductableSql;

        Assert.StartsWith("SELECT MAX_DEDUCTABLE FROM V_PAT_DATA ", sql, StringComparison.Ordinal);
        Assert.Equal(new[] { "patientNo" }, BindReference.Matches(sql).Select(match => match.Groups[1].Value));
        Assert.Single(ForUpdateKeyword.Matches(sql));
        Assert.EndsWith(ForUpdateClause + " OF MAX_DEDUCTABLE", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(LookupQueries.LockPatientCardId))]
    [InlineData(nameof(LookupQueries.LockClassAdvancedMode))]
    [InlineData(nameof(LookupQueries.LockServiceProfiles))]
    [InlineData(nameof(LookupQueries.LockPackageComponentFlags))]
    [InlineData(nameof(LookupQueries.LockPatientMaxDeductable))]
    [InlineData(nameof(LookupQueries.LockClaimPreload))]
    public async Task SessionRead_RejectsASessionThatIsNotAnOracleSession(string member)
    {
        var session = new ForeignSession();

        var failure = await Assert.ThrowsAsync<ArgumentException>(SessionParameter, () => Invoke(member, session));

        Assert.Contains(nameof(OracleSession), failure.Message, StringComparison.Ordinal);
        Assert.Empty(session.Calls);
    }

    [Theory]
    [InlineData(nameof(LookupQueries.LockPatientCardId))]
    [InlineData(nameof(LookupQueries.LockClassAdvancedMode))]
    [InlineData(nameof(LookupQueries.LockServiceProfiles))]
    [InlineData(nameof(LookupQueries.LockPackageComponentFlags))]
    [InlineData(nameof(LookupQueries.LockPatientMaxDeductable))]
    [InlineData(nameof(LookupQueries.LockClaimPreload))]
    public async Task SessionRead_RejectsANullSession(string member) =>
        await Assert.ThrowsAsync<ArgumentNullException>(SessionParameter, () => Invoke(member, null!));

    [Theory]
    [InlineData(null)]
    [InlineData(new object[] { new string[0] })]
    [InlineData(new object[] { new[] { "", " " } })]
    public async Task LockServiceProfiles_NoServiceId_ReturnsEmptyWithoutUsingTheSession(string[]? serviceIds)
    {
        var session = new ForeignSession();

        var profiles = await Queries().LockServiceProfiles(session, serviceIds!, ListId);

        Assert.Empty(profiles);
        Assert.Empty(session.Calls);
    }

    [Theory]
    [Trait("Decision", "D-108")]
    [InlineData("patient-card-id", "patientNo", "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("patient-card-id-bytes", "patientNo", "patientNo has 13 bytes in UTF-8; at most 12 can be bound.")]
    [InlineData("class-advanced-mode", "subCompCode", "subCompCode has 11 characters; at most 10 can be bound.")]
    [InlineData("class-advanced-mode-bytes", "subCompCode", "subCompCode has 11 bytes in UTF-8; at most 10 can be bound.")]
    [InlineData("service-profiles", "serviceIds", "serviceId has 21 characters; at most 20 can be bound.")]
    [InlineData("service-profiles-bytes", "serviceIds", "serviceId has 21 bytes in UTF-8; at most 20 can be bound.")]
    [InlineData("package-component-flags", "packageServiceId", "packageServiceId has 21 characters; at most 20 can be bound.")]
    [InlineData("package-component-flags-bytes", "packageServiceId", "packageServiceId has 21 bytes in UTF-8; at most 20 can be bound.")]
    [InlineData("patient-max-deductable", "patientNo", "patientNo has 13 characters; at most 12 can be bound.")]
    [InlineData("patient-max-deductable-bytes", "patientNo", "patientNo has 13 bytes in UTF-8; at most 12 can be bound.")]
    [InlineData("claim-preload", "claimNo", "claimNo has 41 characters; at most 40 can be bound.")]
    [InlineData("claim-preload-bytes", "claimNo", "claimNo has 41 bytes in UTF-8; at most 40 can be bound.")]
    public async Task SessionRead_OverWidthIsRefusedBeforeTheSessionIsTakenAndAtWidthReachesIt(string readCase, string paramName, string expectedText)
    {
        var session = new ForeignSession();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => InvokeBounded(readCase, session, overWidth: true));

        Assert.Equal(paramName, error.ParamName);
        Assert.Equal(expectedText, error.Data[OracleFailureTranslator.BindingRejectionKey]);
        Assert.Empty(session.Calls);

        DataFailure? failure = new OracleFailureTranslator().Translate(error);
        Assert.NotNull(failure);
        Assert.Equal(422, failure.Status);
        Assert.Equal("field-validation", failure.Type);
        Assert.Equal(expectedText, failure.Message);

        ArgumentException reached = await Assert.ThrowsAsync<ArgumentException>(SessionParameter, () => InvokeBounded(readCase, session, overWidth: false));

        Assert.Contains(nameof(OracleSession), reached.Message, StringComparison.Ordinal);
        Assert.Null(reached.Data[OracleFailureTranslator.BindingRejectionKey]);
        Assert.Empty(session.Calls);
    }

    private static LookupQueries Queries() => new(new InvoicingDataOptions());

    /// <summary>Calls one width-checked session read over <paramref name="session"/> with its checked bind one past or exactly at its width.</summary>
    /// <param name="readCase">Session read and value kind to call.</param>
    /// <param name="session">Session handed to the read.</param>
    /// <param name="overWidth">True for a value one past the width.</param>
    /// <returns>The read's task.</returns>
    private static Task InvokeBounded(string readCase, IOracleSession session, bool overWidth)
    {
        var queries = Queries();
        string Wide(int width) => new('9', overWidth ? width + 1 : width);
        string WideInBytes(int width)
        {
            int bytes = overWidth ? width + 1 : width;
            return new string('\u00E9', bytes / 2) + new string('9', bytes % 2);
        }

        return readCase switch
        {
            "patient-card-id" => queries.LockPatientCardId(session, Wide(BoundedVarchar2.PatientNoBytes)),
            "patient-card-id-bytes" => queries.LockPatientCardId(session, WideInBytes(BoundedVarchar2.PatientNoBytes)),
            "class-advanced-mode" => queries.LockClassAdvancedMode(session, Wide(BoundedVarchar2.SubCompCodeBytes), ClassCode),
            "class-advanced-mode-bytes" => queries.LockClassAdvancedMode(session, WideInBytes(BoundedVarchar2.SubCompCodeBytes), ClassCode),
            "service-profiles" => queries.LockServiceProfiles(session, [ServiceId, Wide(BoundedVarchar2.ServiceIdBytes)], ListId),
            "service-profiles-bytes" => queries.LockServiceProfiles(session, [ServiceId, WideInBytes(BoundedVarchar2.ServiceIdBytes)], ListId),
            "package-component-flags" => queries.LockPackageComponentFlags(session, Wide(BoundedVarchar2.ServiceIdBytes), ListId),
            "package-component-flags-bytes" => queries.LockPackageComponentFlags(session, WideInBytes(BoundedVarchar2.ServiceIdBytes), ListId),
            "patient-max-deductable" => queries.LockPatientMaxDeductable(session, Wide(BoundedVarchar2.PatientNoBytes)),
            "patient-max-deductable-bytes" => queries.LockPatientMaxDeductable(session, WideInBytes(BoundedVarchar2.PatientNoBytes)),
            "claim-preload" => queries.LockClaimPreload(session, Wide(BoundedVarchar2.ClaimNoBytes)),
            "claim-preload-bytes" => queries.LockClaimPreload(session, WideInBytes(BoundedVarchar2.ClaimNoBytes)),
            _ => throw new ArgumentOutOfRangeException(nameof(readCase), readCase, null),
        };
    }

    /// <summary>Calls the named session read with valid arguments over <paramref name="session"/>.</summary>
    private static Task Invoke(string member, IOracleSession session)
    {
        var queries = Queries();
        return member switch
        {
            nameof(LookupQueries.LockPatientCardId) => queries.LockPatientCardId(session, PatientNo),
            nameof(LookupQueries.LockClassAdvancedMode) => queries.LockClassAdvancedMode(session, SubCompCode, ClassCode),
            nameof(LookupQueries.LockServiceProfiles) => queries.LockServiceProfiles(session, [ServiceId], ListId),
            nameof(LookupQueries.LockPackageComponentFlags) => queries.LockPackageComponentFlags(session, PackageServiceId, ListId),
            nameof(LookupQueries.LockPatientMaxDeductable) => queries.LockPatientMaxDeductable(session, PatientNo),
            nameof(LookupQueries.LockClaimPreload) => queries.LockClaimPreload(session, ClaimNo),
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, "Unknown session read."),
        };
    }

    /// <summary>Session port that is not a data-layer <see cref="OracleSession"/>; records every member called on it.</summary>
    private sealed class ForeignSession : IOracleSession
    {
        public List<string> Calls { get; } = [];

        public Task Commit(CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(Commit));
            return Task.CompletedTask;
        }

        public Task Rollback(CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(Rollback));
            return Task.CompletedTask;
        }

        public void Rollback(string savepointName) => Calls.Add(nameof(Rollback));

        public void Save(string savepointName) => Calls.Add(nameof(Save));

        public ValueTask DisposeAsync()
        {
            Calls.Add(nameof(DisposeAsync));
            return ValueTask.CompletedTask;
        }
    }
}
