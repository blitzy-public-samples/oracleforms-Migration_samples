using System.Data;
using System.Globalization;
using System.Text.Json;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Tests.Data;

/// <summary>Parameter names, types and values produced by <see cref="HeaderInputBinder.Bind"/>.</summary>
[Trait("Category", "DataUnit")]
public sealed class HeaderInputBinderTests
{
    private static readonly DateTime DraftDate = new(2026, 9, 28, 23, 59, 59);

    private static readonly string[] ExpectedNames =
    [
        "h_patientno",
        "h_invdate",
        "h_invtypeid",
        "h_paytype",
        "h_sub_paytype",
        "h_sub_paytype2",
        "h_clinicid",
        "h_docid",
        "h_curr_code",
        "h_pre_authorization",
        "h_claim_no",
        "h_claim_flag",
        "h_note_no",
        "h_finaldisc_perc",
        "h_finaldisc",
        "h_amount_1",
        "h_amount_2",
        "h_add_to_list",
        "h_user_no",
        "h_machine_n",
        "h_info_center_id",
    ];

    public static TheoryData<string, OracleDbType> ExpectedTypes => new()
    {
        { "h_patientno", OracleDbType.Varchar2 },
        { "h_invdate", OracleDbType.Date },
        { "h_invtypeid", OracleDbType.Decimal },
        { "h_paytype", OracleDbType.Decimal },
        { "h_sub_paytype", OracleDbType.Decimal },
        { "h_sub_paytype2", OracleDbType.Decimal },
        { "h_clinicid", OracleDbType.Decimal },
        { "h_docid", OracleDbType.Decimal },
        { "h_curr_code", OracleDbType.Varchar2 },
        { "h_pre_authorization", OracleDbType.Varchar2 },
        { "h_claim_no", OracleDbType.Varchar2 },
        { "h_claim_flag", OracleDbType.Varchar2 },
        { "h_note_no", OracleDbType.Varchar2 },
        { "h_finaldisc_perc", OracleDbType.Decimal },
        { "h_finaldisc", OracleDbType.Decimal },
        { "h_amount_1", OracleDbType.Decimal },
        { "h_amount_2", OracleDbType.Decimal },
        { "h_add_to_list", OracleDbType.Decimal },
        { "h_user_no", OracleDbType.Decimal },
        { "h_machine_n", OracleDbType.Varchar2 },
        { "h_info_center_id", OracleDbType.Varchar2 },
    };

    public static TheoryData<int, decimal?, decimal?> FinalDiscountModes => new()
    {
        { 1, 10m, null },
        { 0, null, 25m },
    };

    [Fact]
    public void Bind_PopulatedHeader_ReturnsTwentyOneInputsInHeaderFieldOrder()
    {
        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(CreateHeader(), CreateOperator());

        Assert.Equal(ExpectedNames, parameters.Select(p => p.ParameterName.TrimStart(':')).ToArray());
        Assert.All(parameters, p => Assert.Equal(ParameterDirection.Input, p.Direction));
    }

    [Theory]
    [MemberData(nameof(ExpectedTypes))]
    public void Bind_Parameter_HasExpectedOracleDbType(string name, OracleDbType expected)
    {
        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(CreateHeader(), CreateOperator());

        Assert.Equal(expected, Find(parameters, name).OracleDbType);
    }

    [Fact]
    public void Bind_PopulatedHeader_BindsHeaderFieldValues()
    {
        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(CreateHeader(), CreateOperator());

        Assert.Equal("1001", Text(parameters, "h_patientno"));
        Assert.Equal(2m, Number(parameters, "h_invtypeid"));
        Assert.Equal(1m, Number(parameters, "h_paytype"));
        Assert.Equal(1m, Number(parameters, "h_sub_paytype"));
        Assert.Equal(3m, Number(parameters, "h_sub_paytype2"));
        Assert.Equal(14m, Number(parameters, "h_clinicid"));
        Assert.Equal(12m, Number(parameters, "h_docid"));
        Assert.Equal("SAR", Text(parameters, "h_curr_code"));
        Assert.Equal("O-1001-14-280926", Text(parameters, "h_claim_no"));
        Assert.Equal("O", Text(parameters, "h_claim_flag"));
        Assert.Equal("N-77", Text(parameters, "h_note_no"));
        Assert.Equal(150.25m, Number(parameters, "h_amount_1"));
        Assert.Equal(49.75m, Number(parameters, "h_amount_2"));
        Assert.Equal(1m, Number(parameters, "h_add_to_list"));
    }

    [Fact]
    public void Bind_HeaderWithOnlyDraftDate_SendsUnsetFieldsAsNull()
    {
        string[] operatorFields = ["h_invdate", "h_user_no", "h_machine_n", "h_info_center_id"];

        IReadOnlyList<OracleParameter> parameters =
            HeaderInputBinder.Bind(new InvoiceHeaderDraft { DraftDate = DraftDate }, CreateOperator());

        Assert.All(
            parameters.Where(p => !operatorFields.Contains(p.ParameterName.TrimStart(':'))),
            p => Assert.True(IsNull(p), p.ParameterName));
        Assert.All(
            parameters.Where(p => operatorFields.Contains(p.ParameterName.TrimStart(':'))),
            p => Assert.False(IsNull(p), p.ParameterName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bind_InvDateDiffersOrIsNull_BindsDraftDate(bool invDateIsNull)
    {
        InvoiceHeaderDraft header = CreateHeader() with
        {
            InvDate = invDateIsNull ? null : new DateTime(2026, 9, 29, 8, 30, 0),
        };

        OracleParameter invDate = Find(HeaderInputBinder.Bind(header, CreateOperator()), "h_invdate");

        Assert.False(IsNull(invDate));
        Assert.Equal(DraftDate, Assert.IsType<DateTime>(invDate.Value));
    }

    [Fact]
    public void Bind_HeaderOperatorFieldsDiffer_BindsOperatorContextValues()
    {
        InvoiceHeaderDraft header = CreateHeader() with { UserNo = 999, InfoCenterId = "99", MachineN = "DRAFT-HOST" };

        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(header, CreateOperator());

        Assert.Equal(501m, Number(parameters, "h_user_no"));
        Assert.Equal("7", Text(parameters, "h_info_center_id"));
        Assert.Equal("WS-FRONT-01", Text(parameters, "h_machine_n"));
    }

    [Fact]
    public void Bind_HeaderOperatorFieldsNull_BindsOperatorContextValues()
    {
        InvoiceHeaderDraft header = CreateHeader() with { UserNo = null, InfoCenterId = null, MachineN = null };

        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(header, CreateOperator());

        Assert.Equal(501m, Number(parameters, "h_user_no"));
        Assert.Equal("7", Text(parameters, "h_info_center_id"));
        Assert.Equal("WS-FRONT-01", Text(parameters, "h_machine_n"));
    }

    [Fact]
    public void Bind_TwentyCharacterMachineName_BindsFirstFifteenCharacters()
    {
        const string machineName = "WS-FRONT-DESK-000001";

        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(CreateHeader(), CreateOperator(machineName));

        Assert.Equal(20, machineName.Length);
        Assert.Equal("WS-FRONT-DESK-0", Text(parameters, "h_machine_n"));
    }

    [Theory]
    [MemberData(nameof(FinalDiscountModes))]
    public void Bind_BothFinalDiscountFieldsSet_BindsOnlyTheEntryModeField(
        int discT, decimal? expectedPercent, decimal? expectedAmount)
    {
        InvoiceHeaderDraft header = CreateHeader() with { DiscT = discT, FinalDiscPerc = 10m, FinalDisc = 25m };

        IReadOnlyList<OracleParameter> parameters = HeaderInputBinder.Bind(header, CreateOperator());

        AssertNumberOrNull(expectedPercent, Find(parameters, "h_finaldisc_perc"));
        AssertNumberOrNull(expectedAmount, Find(parameters, "h_finaldisc"));
    }

    [Fact]
    public void Bind_PreAuthorizationInRequestJson_BindsNull()
    {
        const string json = """{"patientNo":"1001","draftDate":"2026-09-28T23:59:59","preAuthorization":"PA-123"}""";

        InvoiceHeaderDraft? header =
            JsonSerializer.Deserialize<InvoiceHeaderDraft>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(header);
        Assert.Equal("PA-123", header.PreAuthorization);
        Assert.True(IsNull(Find(HeaderInputBinder.Bind(header, CreateOperator()), "h_pre_authorization")));
    }

    [Fact]
    public void Bind_PreAuthorizationAssignedInCode_BindsNull()
    {
        InvoiceHeaderDraft header = CreateHeader() with { PreAuthorization = "PA-456" };

        OracleParameter preAuthorization =
            Find(HeaderInputBinder.Bind(header, CreateOperator()), "h_pre_authorization");

        Assert.True(IsNull(preAuthorization));
    }

    [Fact]
    public void Bind_NullHeader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeaderInputBinder.Bind(null!, CreateOperator()));
    }

    [Fact]
    public void Bind_NullOperatorContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeaderInputBinder.Bind(CreateHeader(), null!));
    }

    private static InvoiceHeaderDraft CreateHeader() => new()
    {
        PatientNo = "1001",
        InvDate = DraftDate,
        InvTypeId = 2,
        PayType = 1,
        SubPayType = 1,
        SubPayType2 = 3,
        ClinicId = 14,
        DocId = 12,
        CurrCode = "SAR",
        ClaimNo = "O-1001-14-280926",
        ClaimFlag = "O",
        NoteNo = "N-77",
        FinalDiscPerc = 5m,
        FinalDisc = 12.5m,
        Amount1 = 150.25m,
        Amount2 = 49.75m,
        AddToList = 1,
        UserNo = 501,
        MachineN = "WS-FRONT-01",
        InfoCenterId = "7",
        DraftDate = DraftDate,
        DiscT = 1,
        CompCode = "0",
    };

    private static OperatorContext CreateOperator(string machineName = "WS-FRONT-01") => new()
    {
        UserNo = 501,
        UserName = "cashier1",
        InfoCenterId = "7",
        MachineName = machineName,
        SessionId = Guid.NewGuid().ToString("N"),
    };

    private static OracleParameter Find(IReadOnlyList<OracleParameter> parameters, string name) =>
        Assert.Single(parameters, p => p.ParameterName.TrimStart(':') == name);

    private static string Text(IReadOnlyList<OracleParameter> parameters, string name) =>
        Assert.IsType<string>(Find(parameters, name).Value);

    private static decimal Number(IReadOnlyList<OracleParameter> parameters, string name)
    {
        OracleParameter parameter = Find(parameters, name);
        Assert.False(IsNull(parameter), name);
        return Convert.ToDecimal(parameter.Value, CultureInfo.InvariantCulture);
    }

    private static void AssertNumberOrNull(decimal? expected, OracleParameter parameter)
    {
        if (expected is null)
        {
            Assert.True(IsNull(parameter), parameter.ParameterName);
            return;
        }

        Assert.False(IsNull(parameter), parameter.ParameterName);
        Assert.Equal(expected.Value, Convert.ToDecimal(parameter.Value, CultureInfo.InvariantCulture));
    }

    private static bool IsNull(OracleParameter p) => p.Value is null || p.Value == DBNull.Value;
}
