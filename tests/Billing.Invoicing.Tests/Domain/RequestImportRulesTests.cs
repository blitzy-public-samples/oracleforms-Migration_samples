using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Tests.Parity;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>DR-18 unit parity tests of <see cref="RequestImportRules"/> against the T085 fixture.</summary>
[Trait("Category", "DomainParity")]
public sealed class RequestImportRulesTests
{
    private const string RuleId = "DR-18";

    private const string T085Locator = "05_Complex/Inv_Small_Cash.xml:755";

    private const string MethodKey = "method";

    private const string RequireDoctorMethod = "RequireDoctor";

    private const string NoticesMethod = "Notices";

    private const string BlockingOutcome = "Blocking";

    private const string WarningOutcome = "Warning";

    private const string PassOutcome = "Pass";

    /// <summary>Runs the DR-18 method a fixture case names and compares its outcome and messages.</summary>
    /// <param name="caseName">Fixture case name.</param>
    [Theory]
    [Trait("Rule", "DR-18")]
    [MemberData(nameof(ParityFixture.CaseNames), RuleId, MemberType = typeof(ParityFixture))]
    public void Request_import_matches_DR_18(string caseName)
    {
        var fixtureCase = ParityFixture.Case(RuleId, caseName);
        Assert.True(ParityFixture.IsDerivable(fixtureCase), $"Fixture '{RuleId}', case '{caseName}' is not derivable.");

        var expectedMessages = fixtureCase.Expected.Messages
            ?? throw new InvalidDataException($"Fixture '{RuleId}', case '{caseName}': expected.messages is missing.");
        var input = fixtureCase.Input;

        RuleResult result;
        var method = ReadMethod(input);
        switch (method)
        {
            case RequireDoctorMethod:
                result = RequestImportRules.RequireDoctor(ReadInt(input, "docId"));
                break;
            case NoticesMethod:
                result = RequestImportRules.Notices(
                    ReadRows(input),
                    ReadInt(input, "x422ApprovCheck"),
                    ReadInt(input, "payType"));
                break;
            default:
                Assert.Fail($"Fixture '{RuleId}', case '{caseName}': unknown input.{MethodKey} '{method}'.");
                return;
        }

        ParityFixture.AssertExact(fixtureCase.Expected.Outcome, Outcome(result));
        ParityFixture.AssertMessages(expectedMessages, result.Messages.Select(m => (m.Field, m.Text, m.Severity)));
        Assert.All(result.Messages, message => ParityFixture.AssertExact(RuleId, message.Rule));
    }

    /// <summary>Checks that the DR-18 fixture is a derivable domain fixture traced to T085 exercising both methods.</summary>
    [Fact]
    [Trait("Rule", "DR-18")]
    public void Fixture_DR_18_is_a_domain_fixture_covering_both_methods()
    {
        var document = ParityFixture.Load(RuleId);

        Assert.Equal(RuleId, document.Id);
        Assert.Equal(ParityFixture.DomainClass, document.Class);
        Assert.NotEmpty(document.Cases);
        Assert.All(document.Cases, fixtureCase => Assert.True(ParityFixture.IsDerivable(fixtureCase), fixtureCase.Name));
        Assert.Contains(document.Source, source => string.Equals(source.Locator, T085Locator, StringComparison.Ordinal));

        var methods = document.Cases.Select(fixtureCase => ReadMethod(fixtureCase.Input)).ToList();
        Assert.All(methods, method => Assert.True(
            method is RequireDoctorMethod or NoticesMethod,
            $"Unknown {MethodKey} '{method}'."));
        Assert.Contains(RequireDoctorMethod, methods);
        Assert.Contains(NoticesMethod, methods);
    }

    private static string Outcome(RuleResult r) =>
        r.IsBlocking ? BlockingOutcome : r.Messages.Count > 0 ? WarningOutcome : PassOutcome;

    private static string ReadMethod(JsonElement input) =>
        ReadString(input, MethodKey)
        ?? throw new InvalidDataException($"Fixture '{RuleId}': input.{MethodKey} is null.");

    private static List<(string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)> ReadRows(JsonElement input)
    {
        var rows = Property(input, "rows");
        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Fixture '{RuleId}': input.rows must be an array, not {rows.ValueKind}.");
        }

        return rows.EnumerateArray().Select(ReadRow).ToList();
    }

    private static (string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo) ReadRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Fixture '{RuleId}': a request row must be an object, not {row.ValueKind}.");
        }

        var serviceId = ReadString(row, "serviceId")
            ?? throw new InvalidDataException($"Fixture '{RuleId}': a request row has a null serviceId.");

        return (serviceId, ReadInt(row, "reqAStatus"), ReadInt(row, "reqNeedA"), ReadString(row, "approvRefNo"));
    }

    private static JsonElement Property(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"Fixture '{RuleId}': property '{name}' is missing.");

    private static int? ReadInt(JsonElement owner, string name)
    {
        var value = Property(owner, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            _ => throw new InvalidDataException($"Fixture '{RuleId}': '{name}' must be a whole number or null, not {value.GetRawText()}."),
        };
    }

    private static string? ReadString(JsonElement owner, string name)
    {
        var value = Property(owner, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidDataException($"Fixture '{RuleId}': '{name}' must be a string or null, not {value.GetRawText()}."),
        };
    }
}
