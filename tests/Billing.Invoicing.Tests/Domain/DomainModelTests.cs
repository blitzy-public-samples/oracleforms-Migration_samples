using System.Reflection;
using System.Text.Json;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>Defaults, invariants and JSON round-trips of the Domain model types.</summary>
[Trait("Category", "DomainUnit")]
public sealed class DomainModelTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static TheoryData<Type> CarriedModelTypes => new()
    {
        typeof(InvoiceHeaderDraft),
        typeof(InvoiceLineDraft),
        typeof(InvoiceEntryParameters),
        typeof(OperatorContext),
        typeof(PatientCoverageSnapshot),
        typeof(ClinicProfile),
        typeof(ServiceProfile),
        typeof(PreviewTotals),
        typeof(PaymentAllocation),
    };

    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.345", "2.35")]
    [InlineData("12.344", "12.34")]
    [InlineData("0.125", "0.13")]
    [InlineData("100", "100")]
    public void Round2_RoundsMidpointAwayFromZero(string input, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Round2(decimal.Parse(input)));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void ShouldClear_OnlyOnNewCreateWithTransferRecorded(bool isReplay, bool hasNewInvDocId, bool expected)
    {
        Assert.Equal(expected, ReceptionTransferRule.ShouldClear(isReplay, hasNewInvDocId));
    }

    [Fact]
    public void VisitLineChoice_StaticChoicesCarryNoService()
    {
        Assert.Equal(new VisitLineChoice("None", null), VisitLineChoice.None);
        Assert.Equal(new VisitLineChoice("Consultation", null), VisitLineChoice.Consultation);
        Assert.Equal(new VisitLineChoice("Review", null), VisitLineChoice.Review);
    }

    [Fact]
    public void VisitLineChoice_FixedServiceCarriesServiceId()
    {
        var choice = VisitLineChoice.FixedService("2000");

        Assert.Equal("FixedService", choice.Kind);
        Assert.Equal("2000", choice.ServiceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VisitLineChoice_FixedServiceRejectsBlankServiceId(string? serviceId)
    {
        Assert.ThrowsAny<ArgumentException>(() => VisitLineChoice.FixedService(serviceId!));
    }

    [Fact]
    public void OperatorContext_TruncatesMachineNameTo15Characters()
    {
        var context = new OperatorContext { MachineName = "WORKSTATION-0123456789" };

        Assert.Equal("WORKSTATION-012", context.MachineName);
    }

    [Fact]
    public void OperatorContext_KeepsShortMachineName()
    {
        var context = new OperatorContext { MachineName = "clone0" };

        Assert.Equal("clone0", context.MachineName);
    }

    [Fact]
    public void OperatorContext_RejectsNullMachineName()
    {
        Assert.Throws<ArgumentNullException>(() => new OperatorContext { MachineName = null! });
    }

    [Fact]
    public void OperatorContext_DefaultsToEmptyStrings()
    {
        var context = new OperatorContext();

        Assert.Equal(0, context.UserNo);
        Assert.Equal(string.Empty, context.UserName);
        Assert.Equal(string.Empty, context.InfoCenterId);
        Assert.Equal(string.Empty, context.MachineName);
        Assert.Equal(string.Empty, context.SessionId);
    }

    [Fact]
    public void InvoiceEntryParameters_CarryFormDefaults()
    {
        var parameters = new InvoiceEntryParameters();

        Assert.Equal("X", parameters.NewPatInv);
        Assert.Equal("N", parameters.DoReview);
        Assert.Equal("KSA", parameters.TheCountry);
        Assert.Equal("Y", parameters.DirectCall);
        Assert.Equal(505, parameters.LocalDocType);
        Assert.Equal("N", parameters.WillDoImp);
        Assert.Equal("N", parameters.OpenFromAcc);
        Assert.Equal("0", parameters.ClaimNo);
    }

    [Fact]
    public void InvoiceLineDraft_DefaultsToRateDiscountWithoutOverride()
    {
        var line = new InvoiceLineDraft();

        Assert.Equal("R", line.DiscountType);
        Assert.Null(line.PriceOverride);
    }

    [Fact]
    public void InvoiceHeaderDraft_DefaultsToNoPreAuthorization()
    {
        Assert.Null(new InvoiceHeaderDraft().PreAuthorization);
    }

    [Fact]
    public void ServiceProfile_DefaultsToNoComponents()
    {
        Assert.Empty(new ServiceProfile().Components);
    }

    [Fact]
    public void ValidationMessage_SeverityConstants()
    {
        var message = new ValidationMessage("QTY", "Qty should be >=1", ValidationMessage.Blocking, "DR-12");

        Assert.Equal("Blocking", message.Severity);
        Assert.Equal("Warning", ValidationMessage.Warning);
        Assert.Equal("QTY", message.Field);
        Assert.Equal("Qty should be >=1", message.Text);
        Assert.Equal("DR-12", message.Rule);
    }

    [Fact]
    public void DiscountLimitChoice_HasTheTwoAlertButtons()
    {
        Assert.Equal(
            new[] { nameof(DiscountLimitChoice.MaximumDiscount), nameof(DiscountLimitChoice.Cancel) },
            Enum.GetNames<DiscountLimitChoice>());
    }

    [Fact]
    public void OpenItemIds_Declare58DistinctIdsMatchingTheirNames()
    {
        var ids = typeof(OpenItemIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (Name: field.Name, Value: (string)field.GetRawConstantValue()!))
            .ToList();

        Assert.Equal(58, ids.Count);
        Assert.Equal(58, ids.Select(id => id.Value).Distinct().Count());
        Assert.All(ids, id => Assert.Equal(id.Name, id.Value.Replace("-", string.Empty)));
        Assert.Equal(
            Enumerable.Range(1, 58).Select(n => $"OI-{n:00}"),
            ids.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CarriedModelTypes))]
    public void Model_RoundTripsEveryPropertyThroughJson(Type modelType)
    {
        var original = Populate(modelType);

        var json = JsonSerializer.Serialize(original, modelType, WebJson);
        var restored = JsonSerializer.Deserialize(json, modelType, WebJson);

        Assert.NotNull(restored);
        foreach (var property in SettableProperties(modelType))
        {
            Assert.NotNull(property.GetValue(restored));
        }
        Assert.Equal(json, JsonSerializer.Serialize(restored, modelType, WebJson));
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.SetMethod is { IsPublic: true });

    private static object Populate(Type type)
    {
        var instance = Activator.CreateInstance(type)!;
        foreach (var property in SettableProperties(type))
        {
            property.SetValue(instance, SampleValue(property.PropertyType));
        }
        return instance;
    }

    private static object SampleValue(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string)) return "V1";
        if (target == typeof(int)) return 7;
        if (target == typeof(long)) return 7L;
        if (target == typeof(decimal)) return 12.35m;
        if (target == typeof(DateTime)) return new DateTime(2026, 9, 29, 10, 30, 0);
        if (target == typeof(IReadOnlyList<ServiceProfile>))
        {
            return new[] { new ServiceProfile { ServiceId = "C1", AddToQue = 1 } };
        }
        throw new NotSupportedException($"No sample value for {type}.");
    }
}
