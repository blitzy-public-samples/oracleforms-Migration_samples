using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Ingress checks of <see cref="DraftDto"/> as data-annotations validation runs them on a bound request.</summary>
[Trait("Category", "Orchestration")]
public sealed class DraftDtoValidationTests
{
    private const string UnsupportedDiscountMode = "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)";
    private const string UndefinedDiscountLimitChoice = "DiscountLimitChoice must be MaximumDiscount or Cancel";
    private const decimal MaxAmount = 19807040628566084398385987584m;
    private const string AmountRange = " must be between -19807040628566084398385987584 and 19807040628566084398385987584";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static List<ValidationResult> Validate(DraftDto dto)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        Assert.Equal(valid, results.Count == 0);
        return results;
    }

    private static void AssertRejected(DraftDto dto, string member, string text)
    {
        var result = Assert.Single(Validate(dto));
        Assert.Equal(text, result.ErrorMessage);
        Assert.Equal(new[] { member }, result.MemberNames);
    }

    private static DraftDto WithHeader(InvoiceHeaderDraft header) => new() { Header = header };

    private static DraftDto WithAmount(string item, decimal amount) => WithHeader(item switch
    {
        "AMOUNT_1" => new InvoiceHeaderDraft { Amount1 = amount },
        "AMOUNT_2" => new InvoiceHeaderDraft { Amount2 = amount },
        "CASH_PAYED" => new InvoiceHeaderDraft { CashPayed = amount },
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown amount item."),
    });

    [Fact]
    public void DefaultDraft_IsValid()
    {
        Assert.Empty(Validate(new DraftDto()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public void DiscT_ValueOrRateModeIsValid(int? discT)
    {
        Assert.Empty(Validate(WithHeader(new InvoiceHeaderDraft { DiscT = discT })));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void DiscT_OtherModeIsRejected(int discT)
    {
        AssertRejected(WithHeader(new InvoiceHeaderDraft { DiscT = discT }), "DISC_T", UnsupportedDiscountMode);
    }

    [Theory]
    [InlineData(DiscountLimitChoice.MaximumDiscount)]
    [InlineData(DiscountLimitChoice.Cancel)]
    [InlineData(null)]
    public void DiscountLimitChoice_AlertButtonOrNoneIsValid(DiscountLimitChoice? choice)
    {
        Assert.Empty(Validate(new DraftDto { DiscountLimitChoice = choice }));
    }

    [Fact]
    public void DiscountLimitChoice_UndefinedValueIsRejected()
    {
        AssertRejected(new DraftDto { DiscountLimitChoice = (DiscountLimitChoice)99 }, "FINALDISC", UndefinedDiscountLimitChoice);
        AssertRejected(
            new DraftDto { Header = new InvoiceHeaderDraft { DiscT = 1 }, DiscountLimitChoice = (DiscountLimitChoice)(-1) },
            "FINALDISC_PERC",
            UndefinedDiscountLimitChoice);

        var numeric = JsonSerializer.Deserialize<DraftDto>("""{"discountLimitChoice":99}""", Json);
        Assert.NotNull(numeric);
        Assert.Equal((DiscountLimitChoice)99, numeric.DiscountLimitChoice);
        AssertRejected(numeric, "FINALDISC", UndefinedDiscountLimitChoice);

        var noHeader = JsonSerializer.Deserialize<DraftDto>("""{"header":null,"discountLimitChoice":99}""", Json);
        Assert.NotNull(noHeader);
        AssertRejected(noHeader, "FINALDISC", UndefinedDiscountLimitChoice);

        var named = JsonSerializer.Deserialize<DraftDto>("""{"discountLimitChoice":"Cancel"}""", Json);
        Assert.NotNull(named);
        Assert.Equal(DiscountLimitChoice.Cancel, named.DiscountLimitChoice);
        Assert.Empty(Validate(named));
    }

    [Theory]
    [InlineData("AMOUNT_1")]
    [InlineData("AMOUNT_2")]
    [InlineData("CASH_PAYED")]
    public void Amount_WithinBoundIsValid(string item)
    {
        Assert.Equal(decimal.MaxValue / 4m, MaxAmount);

        foreach (var amount in new[] { MaxAmount, -MaxAmount, -10m, 0m, 150.25m })
        {
            Assert.Empty(Validate(WithAmount(item, amount)));
        }
    }

    [Theory]
    [InlineData("AMOUNT_1")]
    [InlineData("AMOUNT_2")]
    [InlineData("CASH_PAYED")]
    public void Amount_BeyondBoundIsRejected(string item)
    {
        foreach (var amount in new[] { MaxAmount + 1m, -MaxAmount - 1m, decimal.MaxValue, decimal.MinValue })
        {
            AssertRejected(WithAmount(item, amount), item, item + AmountRange);
        }
    }

    [Fact]
    public void EveryRejectionIsReportedInOrder()
    {
        var dto = new DraftDto
        {
            Header = new InvoiceHeaderDraft { DiscT = 2, Amount1 = decimal.MaxValue, Amount2 = decimal.MinValue, CashPayed = decimal.MaxValue },
            DiscountLimitChoice = (DiscountLimitChoice)99,
        };

        Assert.Equal(
            new[] { "DISC_T", "FINALDISC", "AMOUNT_1", "AMOUNT_2", "CASH_PAYED" },
            Validate(dto).Select(r => Assert.Single(r.MemberNames)));
    }

    [Fact]
    public void NullHeader_DoesNotThrow()
    {
        Assert.Empty(Validate(new DraftDto { Header = null! }));

        var bound = JsonSerializer.Deserialize<DraftDto>("""{"header":null}""", Json);
        Assert.NotNull(bound);
        Assert.Null(bound.Header);
        Assert.Empty(Validate(bound));
    }
}
