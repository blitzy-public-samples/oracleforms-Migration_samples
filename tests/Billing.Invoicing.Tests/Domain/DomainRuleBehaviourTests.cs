using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Domain.Workflow;

namespace Billing.Invoicing.Tests.Domain;

/// <summary>Direct behaviour checks of the Domain rule classes with inline inputs; not parity evidence.</summary>
[Trait("Category", "DomainUnit")]
public sealed class DomainRuleBehaviourTests
{
    private static readonly DateTime DraftDate = new(2026, 9, 28, 10, 15, 0);

    private static ValidationMessage Single(RuleResult result) => Assert.Single(result.Messages);

    private static void AssertMessage(RuleResult result, string? field, string text, string severity, string rule)
    {
        var message = Single(result);
        Assert.Equal(field, message.Field);
        Assert.Equal(text, message.Text);
        Assert.Equal(severity, message.Severity);
        Assert.Equal(rule, message.Rule);
    }

    [Fact]
    public void InvoiceDetails_NoLinesBlocksAndLinesPass()
    {
        AssertMessage(InvoiceDetailRules.RequireDetails(0), null, "Invoice without Details", ValidationMessage.Blocking, "DR-02");
        Assert.True(InvoiceDetailRules.RequireDetails(0).IsBlocking);
        Assert.Same(RuleResult.Empty, InvoiceDetailRules.RequireDetails(2));
    }

    [Fact]
    public void PackageImport_ZeroLinesWarnsWithoutBlocking()
    {
        var result = PackageImportRules.Evaluate(0);
        AssertMessage(result, null, "No Serves Added", ValidationMessage.Warning, "DR-19");
        Assert.False(result.IsBlocking);
        Assert.Same(RuleResult.Empty, PackageImportRules.Evaluate(3));
    }

    [Theory]
    [InlineData(1, 0, "OPD", true)]
    [InlineData(1, 0, null, true)]
    [InlineData(1, 1, "ER", false)]
    [InlineData(0, 1, "OPD", false)]
    [InlineData(null, null, "OPD", false)]
    public void ErClinic_DeptWiseOnlyAtErClinic(int? deptWise, int? call, string? sysCatType, bool blocks)
    {
        var header = new InvoiceHeaderDraft { DeptWise = deptWise, Call = call };
        var clinic = sysCatType is null ? null : new ClinicProfile { SysCatType = sysCatType };

        var result = ErClinicRule.Validate(header, clinic);

        if (blocks)
        {
            AssertMessage(result, "DEPT_WISE", "This open for GP at ER Clinic Only", ValidationMessage.Blocking, "DR-05");
        }
        else
        {
            Assert.Empty(result.Messages);
        }
    }

    [Fact]
    public void ClinicSuitability_SexMismatchWarns()
    {
        var mismatch = new ClinicProfile { Six = 1, PatientSex = 2 };
        AssertMessage(ClinicSuitabilityRules.CheckSex(mismatch), "CLINICID", "Patient sex not suitable for this clinic", ValidationMessage.Warning, "DR-04");
        Assert.Empty(ClinicSuitabilityRules.CheckSex(mismatch with { PatientSex = 1 }).Messages);
        Assert.Empty(ClinicSuitabilityRules.CheckSex(mismatch with { Six = null }).Messages);
        Assert.Empty(ClinicSuitabilityRules.CheckSex(null).Messages);
    }

    [Theory]
    [InlineData(10.0, 18, null, true)]
    [InlineData(30.0, null, 12, true)]
    [InlineData(30.0, 18, 60, false)]
    [InlineData(0.5, 2, null, true)]
    public void ClinicSuitability_AgeOutsideRangeWarns(double age, int? ageMin, int? ageMax, bool warns)
    {
        var clinic = new ClinicProfile { AgeMin = ageMin, AgeMax = ageMax };

        var result = ClinicSuitabilityRules.CheckAge(clinic, (decimal)age);

        if (warns)
        {
            AssertMessage(result, "CLINICID", "Patient age  not suitable for this clinic", ValidationMessage.Warning, "DR-04");
        }
        else
        {
            Assert.Empty(result.Messages);
        }

        Assert.Empty(ClinicSuitabilityRules.CheckAge(null, (decimal)age).Messages);
    }

    [Fact]
    public void DoctorSelection_WarnsAndResetsToLockedDoctor()
    {
        var parameters = new InvoiceEntryParameters { TheDoc = 5 };

        AssertMessage(DoctorSelectionRules.Validate(new InvoiceHeaderDraft(), parameters), "DOCIDX", "You Must Select Doctor", ValidationMessage.Warning, "DR-11");

        var changed = DoctorSelectionRules.Validate(new InvoiceHeaderDraft { DocId = 7 }, parameters);
        AssertMessage(changed, "DOCIDX", "You Cant Change doctor", ValidationMessage.Warning, "DR-11");
        Assert.Equal(5, changed.Adjusted["DOCIDX"]);
        Assert.False(changed.IsBlocking);

        Assert.Empty(DoctorSelectionRules.Validate(new InvoiceHeaderDraft { DocId = 5 }, parameters).Messages);
        Assert.Empty(DoctorSelectionRules.Validate(new InvoiceHeaderDraft { DocId = 7 }, new InvoiceEntryParameters()).Messages);
    }

    [Fact]
    public void InvoiceState_OnlyUnsavedDraftIsEditableAndDeleteIsRefused()
    {
        Assert.True(InvoiceStatePolicy.CanEdit(null));
        Assert.False(InvoiceStatePolicy.CanEdit(1234));
        AssertMessage(InvoiceStatePolicy.CanDelete(null), null, "You Cant Delete Invoice From Here", ValidationMessage.Blocking, "DR-17");
        AssertMessage(InvoiceStatePolicy.CanDelete(1234), null, "You Cant Delete Invoice From Here", ValidationMessage.Blocking, "DR-17");
    }

    [Fact]
    public void PaymentAllocation_SecondAmountDefaultsAndReset()
    {
        Assert.Equal(60.50m, PaymentAllocationRules.AllocateSecondAmount(100.50m, 40m));
        Assert.Equal(-5m, PaymentAllocationRules.AllocateSecondAmount(10m, 15m));
        Assert.Equal(0m, PaymentAllocationRules.AllocateSecondAmount(null, null));

        var reset = PaymentAllocationRules.ResetAfterDiscountChange(99.995m);
        Assert.Empty(reset.Messages);
        Assert.Equal(100.00m, reset.Adjusted["AMOUNT_1"]);
        Assert.Equal(0m, reset.Adjusted["AMOUNT_2"]);
        Assert.Null(PaymentAllocationRules.ResetAfterDiscountChange(null).Adjusted["AMOUNT_1"]);

        Assert.Equal(55.5m, PaymentAllocationRules.DefaultFirstAmount(55.5m));
        Assert.Null(PaymentAllocationRules.DefaultFirstAmount(null));
    }

    [Theory]
    [InlineData(200.0, 150.0, 1, 30.0, 2, 50.0)]
    [InlineData(200.0, 150.0, 1, 30.0, 1, 20.0)]
    [InlineData(0.0, 150.0, 1, 30.0, 1, 0.0)]
    [InlineData(200.0, 150.0, 2, 30.0, 3, 0.0)]
    [InlineData(100.0, 150.0, 1, null, null, -50.0)]
    public void PaymentAllocation_RefundCountsCashMethodsOnly(double cashPayed, double? amount1, int? subPayType, double? amount2, int? subPayType2, double expected)
    {
        var allocation = new PaymentAllocation
        {
            CashPayed = (decimal)cashPayed,
            Amount1 = (decimal?)amount1,
            SubPayType = subPayType,
            Amount2 = (decimal?)amount2,
            SubPayType2 = subPayType2,
        };

        Assert.Equal((decimal)expected, PaymentAllocationRules.Refund(allocation));
    }

    [Fact]
    public void PaymentAllocation_TotalCollectedAddsBothAmounts()
    {
        Assert.Equal(15.00m, PaymentAllocationRules.TotalCollected(10.25m, 4.75m));
        Assert.Equal(10m, PaymentAllocationRules.TotalCollected(10m, null));
        Assert.Equal(0m, PaymentAllocationRules.TotalCollected(null, null));
        Assert.Throws<ArgumentNullException>(() => PaymentAllocationRules.Refund(null!));
    }

    [Fact]
    public void PaymentAllocation_AmountsAtIngressBoundDoNotOverflow()
    {
        const decimal bound = 19807040628566084398385987584m;
        const decimal twiceBound = 39614081257132168796771975168m;
        Assert.Equal(decimal.MaxValue / 4m, bound);

        Assert.Equal(twiceBound, PaymentAllocationRules.AllocateSecondAmount(bound, -bound));
        Assert.Equal(-twiceBound, PaymentAllocationRules.AllocateSecondAmount(-bound, bound));

        var cash = new PaymentAllocation { CashPayed = bound, Amount1 = bound, SubPayType = 1, Amount2 = bound, SubPayType2 = 1 };
        Assert.Equal(-bound, PaymentAllocationRules.Refund(cash));

        Assert.Equal(twiceBound, PaymentAllocationRules.TotalCollected(bound, bound));
        Assert.Equal(-twiceBound, PaymentAllocationRules.TotalCollected(-bound, -bound));
    }

    [Fact]
    public void RequestImport_DoctorRequired()
    {
        AssertMessage(RequestImportRules.RequireDoctor(null), "DOCIDX", "Select doctor First", ValidationMessage.Blocking, "DR-18");
        Assert.Same(RuleResult.Empty, RequestImportRules.RequireDoctor(3));
    }

    [Fact]
    public void RequestImport_NoticesFollowRowFlags()
    {
        var rows = new (string, int?, int?, string?)[]
        {
            ("1001", 3, 0, null),
            ("1002", 1, 1, null),
            ("1003", 1, 1, "REF-9"),
            ("1004", 1, 0, null),
            ("1005", null, null, null),
        };

        var enforced = RequestImportRules.Notices(rows, 1, 2);
        Assert.Equal(new[] { "1001 Rejected ", "1002 Need Approval" }, enforced.Messages.Select(m => m.Text));
        Assert.All(enforced.Messages, m =>
        {
            Assert.Equal("SERVICEID", m.Field);
            Assert.Equal(ValidationMessage.Warning, m.Severity);
            Assert.Equal("DR-18", m.Rule);
        });

        Assert.Equal(new[] { "1001 Rejected " }, RequestImportRules.Notices(rows, 2, 2).Messages.Select(m => m.Text));
        Assert.Equal(new[] { "1001 Rejected " }, RequestImportRules.Notices(rows, 1, 1).Messages.Select(m => m.Text));
        Assert.Equal(new[] { "1001 Rejected " }, RequestImportRules.Notices(rows, null, 2).Messages.Select(m => m.Text));
        Assert.Same(RuleResult.Empty, RequestImportRules.Notices(Array.Empty<(string, int?, int?, string?)>(), 1, 2));
        Assert.Throws<ArgumentNullException>(() => RequestImportRules.Notices(null!, 1, 2));
    }

    [Theory]
    [InlineData(1, 2.0, "Due to Quality system not allow more than 1 at qty for this service")]
    [InlineData(1, 1.0, null)]
    [InlineData(0, 5.0, null)]
    [InlineData(null, 0.0, "Qty should be >=1")]
    [InlineData(null, -1.0, "Qty should be >=1")]
    [InlineData(null, null, "Qty should be >=1")]
    public void LineEntry_QuantityChecks(int? showQty, double? qty, string? expected)
    {
        var service = showQty is null ? null : new ServiceProfile { ShowQty = showQty };

        var result = LineEntryRules.ValidateQuantity(service, (decimal?)qty);

        if (expected is null)
        {
            Assert.Empty(result.Messages);
        }
        else
        {
            AssertMessage(result, "QTY", expected, ValidationMessage.Blocking, "DR-12");
        }
    }

    [Fact]
    public void LineEntry_ValueDiscountRefusedOnClassInvoice()
    {
        var refused = LineEntryRules.ValidateDiscountType("12", "V");
        AssertMessage(refused, "LDISCT", "You cant use value disocunt for credit invoices", ValidationMessage.Blocking, "DR-13");
        Assert.Equal("R", refused.Adjusted["LDISCT"]);

        Assert.Empty(LineEntryRules.ValidateDiscountType(null, "V").Messages);
        Assert.Empty(LineEntryRules.ValidateDiscountType("12", "R").Messages);
    }

    [Theory]
    [InlineData("S1", 2, 1, 1, null, true)]
    [InlineData("S1", 2, 1, 1, "REF", false)]
    [InlineData("S1", 1, 1, 1, null, false)]
    [InlineData("S1", 2, 2, 1, null, false)]
    [InlineData("S1", 2, 1, 0, null, false)]
    [InlineData("S1", 2, 1, null, null, false)]
    [InlineData("S1", 2, null, 1, null, false)]
    [InlineData(null, 2, 1, 1, null, false)]
    public void LineEntry_CreditLineNeedsApprovalReference(string? serviceId, int? payType, int? x422, int? reqNeedA, string? approvRefNo, bool blocks)
    {
        var result = LineEntryRules.ValidateApproval(serviceId, payType, x422, reqNeedA, approvRefNo);

        if (blocks)
        {
            AssertMessage(result, "APPROV_REF_NO", serviceId + "Need Approval", ValidationMessage.Blocking, "DR-14");
        }
        else
        {
            Assert.Empty(result.Messages);
        }
    }

    [Fact]
    public void LineEntry_ServiceRequired()
    {
        AssertMessage(LineEntryRules.RequireService(null), "SERVICEID", "You Must Select Value", ValidationMessage.Blocking, "DR-15");
        AssertMessage(LineEntryRules.RequireService(""), "SERVICEID", "You Must Select Value", ValidationMessage.Blocking, "DR-15");
        Assert.Empty(LineEntryRules.RequireService("1001").Messages);
    }

    [Fact]
    public void LineEntry_NotRequestedServiceWarnsOnInsuredNewInvoice()
    {
        var header = new InvoiceHeaderDraft { SubCompCode = "S1" };
        var service = new ServiceProfile { ServiceId = "1001", BeginOfClaim = 0 };

        AssertMessage(LineEntryRules.WarnNotRequested(header, service, false), "SERVICEID", "This service not requested by doctor at this claim", ValidationMessage.Warning, "DR-16");
        Assert.Empty(LineEntryRules.WarnNotRequested(header, service with { BeginOfClaim = null }, true).Messages);
        Assert.Single(LineEntryRules.WarnNotRequested(header, service with { BeginOfClaim = null }, false).Messages);
        Assert.Empty(LineEntryRules.WarnNotRequested(header, service with { BeginOfClaim = 1 }, false).Messages);
        Assert.Empty(LineEntryRules.WarnNotRequested(header with { SubCompCode = null }, service, false).Messages);
        Assert.Empty(LineEntryRules.WarnNotRequested(header with { InvNo = 10 }, service, false).Messages);
        Assert.Empty(LineEntryRules.WarnNotRequested(header, null, false).Messages);
        Assert.Throws<ArgumentNullException>(() => LineEntryRules.WarnNotRequested(null!, service, false));
    }

    private static PatientCoverageSnapshot InsuredCoverage() => new()
    {
        CompCode = "300",
        CompanyType = 2,
        CompanyIsActive = 1,
        ContractEnd = new DateTime(2027, 1, 1),
        CardEnd = new DateTime(2027, 1, 1),
    };

    private static InvoiceEntryParameters Admin(int? invDateAdmin) => new() { InvDateAdmin = invDateAdmin };

    private static IEnumerable<(string Text, string Severity)> Texts(RuleResult result) =>
        result.Messages.Select(m => (m.Text, m.Severity));

    [Fact]
    public void Eligibility_CashIsNotCheckedAndMissingCoverageBlocks()
    {
        AssertMessage(
            PatientEligibilityRules.Evaluate(null, Admin(2), DraftDate),
            "PATIENTNO",
            "FRM-40735: WHEN-VALIDATE-ITEM trigger raised unhandled exception ORA-01403.",
            ValidationMessage.Blocking,
            "DR-03");
        Assert.True(PatientEligibilityRules.Evaluate(null, new InvoiceEntryParameters { CashOrCredit = 1 }, DraftDate).IsBlocking);
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CompCode = "0", CompanyIsActive = 2 }, Admin(2), DraftDate).Messages);
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CompanyIsActive = 2 }, new InvoiceEntryParameters { CashOrCredit = 1 }, DraftDate).Messages);
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage(), Admin(2), DraftDate).Messages);
        Assert.Throws<ArgumentNullException>(() => PatientEligibilityRules.Evaluate(InsuredCoverage(), null!, DraftDate));
    }

    [Fact]
    public void Eligibility_ContractEnded()
    {
        var ended = InsuredCoverage() with { ContractEnd = new DateTime(2026, 9, 1) };

        var blocked = PatientEligibilityRules.Evaluate(ended, Admin(2), DraftDate);
        Assert.Equal(new[] { ("Contract  Ended 01/09/2026", ValidationMessage.Blocking) }, Texts(blocked));
        Assert.All(blocked.Messages, m => Assert.Equal("PATIENTNO", m.Field));

        var admin = PatientEligibilityRules.Evaluate(ended with { CompanyType = 1 }, Admin(1), DraftDate);
        Assert.Equal(
            new[] { ("Contract  Ended 01/09/2026 , But due to that user have date admin privileges system will open claim", ValidationMessage.Warning) },
            Texts(admin));
    }

    [Fact]
    public void Eligibility_CompanyHeld()
    {
        var result = PatientEligibilityRules.Evaluate(InsuredCoverage() with { CompanyIsActive = 2 }, Admin(2), DraftDate);
        Assert.Equal(new[] { ("Company Is Holed", ValidationMessage.Blocking) }, Texts(result));
    }

    [Fact]
    public void Eligibility_CardExpiry()
    {
        var expired = InsuredCoverage() with { CardEnd = new DateTime(2026, 9, 27) };

        Assert.Equal(new[] { ("Card Expired 27/09/2026", ValidationMessage.Blocking) }, Texts(PatientEligibilityRules.Evaluate(expired, Admin(2), DraftDate)));
        Assert.Equal(
            new[] { ("Card Expired 27/09/2026 , But due to that user have date admin privileges system will open claim", ValidationMessage.Warning) },
            Texts(PatientEligibilityRules.Evaluate(expired, Admin(1), DraftDate)));
        Assert.Equal(
            new[] { ("Card Expired 28/09/2026 , Today last Date", ValidationMessage.Warning) },
            Texts(PatientEligibilityRules.Evaluate(expired with { CardEnd = DraftDate.Date }, Admin(2), DraftDate)));
    }

    [Fact]
    public void Eligibility_PolicyChecks()
    {
        var policy = InsuredCoverage() with { SubCompCode = "S1", SubCompanyContractEnd = new DateTime(2026, 8, 31) };

        Assert.Equal(
            new[] { ("Policy  Ended 31/08/2026 Patient well treated as cash patient ", ValidationMessage.Blocking) },
            Texts(PatientEligibilityRules.Evaluate(policy, Admin(2), DraftDate)));
        Assert.Equal(
            new[] { ("Policy  Ended 31/08/2026 ,But due to that user have date admin privileges system will open claim", ValidationMessage.Warning) },
            Texts(PatientEligibilityRules.Evaluate(policy, Admin(1), DraftDate)));
        Assert.Equal(
            new[] { ("Policy Is Holed", ValidationMessage.Blocking) },
            Texts(PatientEligibilityRules.Evaluate(policy with { SubCompanyContractEnd = null, SubCompanyIsActive = 2 }, Admin(2), DraftDate)));
    }

    [Fact]
    public void Eligibility_ClassChecks()
    {
        var classed = InsuredCoverage() with { MyClass = 4 };

        Assert.Equal(new[] { ("Class Is Holed", ValidationMessage.Blocking) }, Texts(PatientEligibilityRules.Evaluate(classed with { ClassIsActive = 2 }, Admin(2), DraftDate)));
        Assert.Equal(new[] { ("Refral Required For This Class", ValidationMessage.Warning) }, Texts(PatientEligibilityRules.Evaluate(classed with { ClassWithRef = 1 }, Admin(2), DraftDate)));
        Assert.Empty(PatientEligibilityRules.Evaluate(classed, Admin(2), DraftDate).Messages);
    }

    [Fact]
    public void Eligibility_ExpiryComparesDraftDateOnly()
    {
        var today = new DateTime(2026, 9, 28);
        var afternoon = new DateTime(2026, 9, 28, 15, 30, 0);
        var evening = new DateTime(2026, 9, 28, 18, 30, 0);
        var lastSecond = new DateTime(2026, 9, 28, 23, 59, 59);

        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { ContractEnd = today }, Admin(2), afternoon).Messages);
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { ContractEnd = afternoon }, Admin(2), afternoon).Messages);
        Assert.Equal(
            new[] { ("Contract  Ended 27/09/2026", ValidationMessage.Blocking) },
            Texts(PatientEligibilityRules.Evaluate(InsuredCoverage() with { ContractEnd = new DateTime(2026, 9, 27) }, Admin(2), new DateTime(2026, 9, 28, 0, 0, 1))));

        Assert.Equal(
            new[] { ("Card Expired 28/09/2026 , Today last Date", ValidationMessage.Warning) },
            Texts(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CardEnd = today }, Admin(2), evening)));
        Assert.Equal(
            new[] { ("Card Expired 28/09/2026 , Today last Date", ValidationMessage.Warning) },
            Texts(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CardEnd = today }, Admin(1), evening)));
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CardEnd = evening }, Admin(2), evening).Messages);
        Assert.Empty(PatientEligibilityRules.Evaluate(InsuredCoverage() with { CardEnd = new DateTime(2026, 9, 28, 18, 0, 0) }, Admin(2), new DateTime(2026, 9, 28, 10, 15, 0)).Messages);

        Assert.Empty(PatientEligibilityRules.Evaluate(
            InsuredCoverage() with { SubCompCode = "S1", SubCompanyContractEnd = today },
            Admin(2),
            lastSecond).Messages);
        Assert.Empty(PatientEligibilityRules.Evaluate(
            InsuredCoverage() with { SubCompCode = "S1", SubCompanyContractEnd = lastSecond },
            Admin(2),
            lastSecond).Messages);
    }

    [Theory]
    [InlineData(10.0, "Maximum discount allawed is10")]
    [InlineData(12.5, "Maximum discount allawed is12.5")]
    [InlineData(0.5, "Maximum discount allawed is.5")]
    public void FinalDiscount_PercentAboveLimitBlocks(double maxDisc, string expected)
    {
        var header = new InvoiceHeaderDraft { DiscT = 1, FinalDiscPerc = 15m };

        AssertMessage(FinalDiscountLimitRule.Evaluate(header, (decimal)maxDisc, 200m), "FINALDISC_PERC", expected, ValidationMessage.Blocking, "DR-06");
    }

    [Fact]
    public void FinalDiscount_PercentModeWithinLimitOrOfferPasses()
    {
        var header = new InvoiceHeaderDraft { DiscT = 1, FinalDiscPerc = 15m };

        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { FinalDiscPerc = 10m }, 10m, 200m).Messages);
        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { OferId = 9 }, 10m, 200m).Messages);
        AssertMessage(FinalDiscountLimitRule.Evaluate(header with { FinalDiscPerc = 5m }, null, 200m), "FINALDISC_PERC", "Maximum discount allawed is0", ValidationMessage.Blocking, "DR-06");
        AssertMessage(FinalDiscountLimitRule.Evaluate(new InvoiceHeaderDraft { DiscT = 2, FinalDiscPerc = 50m }, 10m, 200m), "DISC_T", "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)", ValidationMessage.Blocking, "DR-06");
    }

    [Fact]
    public void FinalDiscount_UnsupportedModeBlocks()
    {
        var header = new InvoiceHeaderDraft { FinalDisc = 30m };

        foreach (var mode in new[] { -1, 2 })
        {
            var result = FinalDiscountLimitRule.Evaluate(header with { DiscT = mode }, 10m, 200m);
            AssertMessage(result, "DISC_T", "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)", ValidationMessage.Blocking, "DR-06");
            Assert.Empty(result.Adjusted);
        }

        AssertMessage(FinalDiscountLimitRule.Evaluate(header, 10m, 200m), "FINALDISC", "Maximum discount allawed is10", ValidationMessage.Blocking, "DR-06");
    }

    [Fact]
    public void FinalDiscount_ValueModeDerivesPercent()
    {
        var header = new InvoiceHeaderDraft { DiscT = 0 };

        var cleared = FinalDiscountLimitRule.Evaluate(header, 10m, 100m);
        Assert.Empty(cleared.Messages);
        Assert.Equal(0m, cleared.Adjusted["FINALDISC_PERC"]);

        AssertMessage(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = 20m }, 10m, 100m), "FINALDISC", "Maximum discount allawed is10", ValidationMessage.Blocking, "DR-06");

        var within = FinalDiscountLimitRule.Evaluate(header with { FinalDisc = 1m }, 50m, 3m);
        Assert.Empty(within.Messages);
        Assert.Equal(33.33m, within.Adjusted["FINALDISC_PERC"]);

        var offer = FinalDiscountLimitRule.Evaluate(header with { FinalDisc = 20m, OferId = 9 }, 10m, 100m);
        Assert.Empty(offer.Messages);
        Assert.Equal(20.00m, offer.Adjusted["FINALDISC_PERC"]);

        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = 20m }, 10m, null).Messages);
        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = 20m }, 10m, 0m).Messages);
        Assert.True(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = decimal.MaxValue }, 10m, 0.0000001m).IsBlocking);
        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = decimal.MaxValue, OferId = 9 }, 10m, 0.0000001m).Messages);

        var negativeOverflow = FinalDiscountLimitRule.Evaluate(header with { FinalDisc = decimal.MinValue }, 0m, 1m);
        Assert.Empty(negativeOverflow.Messages);
        Assert.Empty(negativeOverflow.Adjusted);
        Assert.Empty(FinalDiscountLimitRule.Evaluate(header with { FinalDisc = decimal.MinValue }, 0m, 0.0000001m).Messages);
    }

    [Fact]
    public void FinalDiscount_AlertChoices()
    {
        var percent = new InvoiceHeaderDraft { DiscT = 1, FinalDiscPerc = 15m };

        var cancel = FinalDiscountLimitRule.ApplyChoice(percent, DiscountLimitChoice.Cancel, 10m);
        AssertMessage(cancel, "FINALDISC_PERC", "Maximum discount allawed is10", ValidationMessage.Blocking, "DR-06");
        Assert.Equal(0m, cancel.Adjusted["FINALDISC"]);
        Assert.Equal(0m, cancel.Adjusted["FINALDISC_PERC"]);

        var cancelValue = FinalDiscountLimitRule.ApplyChoice(percent with { DiscT = 0 }, DiscountLimitChoice.Cancel, -0.5m);
        AssertMessage(cancelValue, "FINALDISC", "Maximum discount allawed is-.5", ValidationMessage.Blocking, "DR-06");

        var maximum = FinalDiscountLimitRule.ApplyChoice(percent, DiscountLimitChoice.MaximumDiscount, 10m);
        Assert.Empty(maximum.Messages);
        Assert.Equal(10m, maximum.Adjusted["FINALDISC_PERC"]);
        Assert.Null(maximum.Adjusted["FINALDISC"]);
        Assert.Equal(1, maximum.Adjusted["DISC_T"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => FinalDiscountLimitRule.ApplyChoice(percent, (DiscountLimitChoice)99, 10m));
    }

    private static InvoiceHeaderDraft ClaimPreload() => new()
    {
        ClaimNo = "O-1001-14-010926",
        PatientNo = "1001",
        ClinicId = 14,
        CompCode = "300",
        SubCompCode = "S1",
        ClassCode = 4,
        PayType = 2,
    };

    [Fact]
    public void InvoiceDefaults_NewDraftDefaults()
    {
        var draft = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { IsHomeCare = "Y", ClaimFlag = "R" }, DraftDate, null, null);

        Assert.Equal(DraftDate, draft.DraftDate);
        Assert.Equal(DraftDate, draft.InvDate);
        Assert.Equal(7, draft.InvTypeId);
        Assert.Equal(1, draft.SubPayType);
        Assert.Equal("R", draft.ClaimFlag);
        Assert.Null(draft.DocId);

        Assert.Equal(0, InvoiceDefaultsRule.Apply(new InvoiceEntryParameters(), DraftDate, null, null).InvTypeId);
        Assert.Throws<ArgumentNullException>(() => InvoiceDefaultsRule.Apply(null!, DraftDate, null, null));
    }

    [Fact]
    public void InvoiceDefaults_DoctorFromNewConsultationAndVisit()
    {
        Assert.Equal(55, InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "1", NewDoc = 55 }, DraftDate, null, null).DocId);
        Assert.Equal(77, InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { VisitUnique = "V1" }, DraftDate, null, 77).DocId);
        Assert.Null(InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { VisitUnique = "V1" }, DraftDate, null, null).DocId);
    }

    [Fact]
    public void InvoiceDefaults_ClaimPreloadCopiesCoverage()
    {
        var credit = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "C-9", CashOrCredit = 2 }, DraftDate, ClaimPreload(), null);
        Assert.Equal("O-1001-14-010926", credit.ClaimNo);
        Assert.Equal("1001", credit.PatientNo);
        Assert.Equal(14, credit.ClinicId);
        Assert.Equal("300", credit.CompCode);
        Assert.Equal("S1", credit.SubCompCode);
        Assert.Equal(4, credit.ClassCode);
        Assert.Equal(2, credit.PayType);

        var cash = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "C-9", CashOrCredit = 1 }, DraftDate, ClaimPreload(), null);
        Assert.Equal("0", cash.CompCode);
        Assert.Null(cash.SubCompCode);
        Assert.Null(cash.ClassCode);
        Assert.Equal(1, cash.PayType);

        var cashNoSubCompany = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "C-9", CashOrCredit = 1 }, DraftDate, ClaimPreload() with { SubCompCode = null }, null);
        Assert.Equal("300", cashNoSubCompany.CompCode);
        Assert.Equal(1, cashNoSubCompany.PayType);

        var ignored = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "0" }, DraftDate, ClaimPreload(), null);
        Assert.Null(ignored.PatientNo);
        Assert.Null(ignored.ClaimNo);
    }

    [Fact]
    public void AddToList_DirectOrLocation14ComponentQueued()
    {
        var queuedComponent = new ServiceProfile { ServiceId = "2001", AddToQue = 1 };

        Assert.Equal(1, AddToListRule.Derive(new[] { new ServiceProfile { AddToQue = 1 } }));
        Assert.Equal(1, AddToListRule.Derive(new[] { null!, new ServiceProfile { ServLocId = 14, Components = new[] { queuedComponent } } }));
        Assert.Equal(0, AddToListRule.Derive(new[] { new ServiceProfile { ServLocId = 10, Components = new[] { queuedComponent } } }));
        Assert.Equal(0, AddToListRule.Derive(new[] { new ServiceProfile { ServLocId = 14, Components = new[] { queuedComponent with { AddToQue = 0 } } } }));
        Assert.Equal(0, AddToListRule.Derive(Array.Empty<ServiceProfile>()));
        Assert.Throws<ArgumentNullException>(() => AddToListRule.Derive(null!));
    }

    [Fact]
    public void ClaimNumber_ParameterOrBuiltNumber()
    {
        var header = new InvoiceHeaderDraft { PatientNo = "1001", ClinicId = 14, DraftDate = DraftDate };

        Assert.Equal("C-1", ClaimNumberRule.Build(header, new InvoiceEntryParameters { ClaimFlag = "R", ClaimNo = "C-1" }));
        Assert.Null(ClaimNumberRule.Build(header, new InvoiceEntryParameters { ClaimFlag = "R", ClaimNo = "" }));
        Assert.Equal("C-1", ClaimNumberRule.Build(header, new InvoiceEntryParameters { ClaimNo = "C-1" }));
        Assert.Equal("O-1001-14-280926", ClaimNumberRule.Build(header, new InvoiceEntryParameters { ClaimNo = "1" }));
        Assert.Equal("O---280926", ClaimNumberRule.Build(new InvoiceHeaderDraft { DraftDate = DraftDate }, new InvoiceEntryParameters { ClaimNo = "2" }));
        Assert.Throws<ArgumentNullException>(() => ClaimNumberRule.Build(null!, new InvoiceEntryParameters()));
        Assert.Throws<ArgumentNullException>(() => ClaimNumberRule.Build(header, null!));
    }

    [Fact]
    public void PayType_EntryDecision()
    {
        var parameters = new InvoiceEntryParameters();

        Assert.Equal(1, PayTypeSelectionRule.Decide("0", null, parameters, null));
        Assert.Equal(1, PayTypeSelectionRule.Decide("300", 1, new InvoiceEntryParameters { CashOrCredit = 1 }, null));
        Assert.Equal(1, PayTypeSelectionRule.Decide("300", 2, new InvoiceEntryParameters { CashOrCredit = 1 }, null));
        Assert.Equal(2, PayTypeSelectionRule.Decide("300", 3, new InvoiceEntryParameters { CashOrCredit = 1 }, null));
        Assert.Equal(2, PayTypeSelectionRule.Decide("300", null, parameters, null));
        Assert.Equal(1, PayTypeSelectionRule.Decide(null, null, parameters, null));
        Assert.Equal(1, PayTypeSelectionRule.Decide("300", null, new InvoiceEntryParameters { CashOrCredit = 1 }, ClaimPreload()));
        Assert.Equal(2, PayTypeSelectionRule.Decide("300", null, new InvoiceEntryParameters { CashOrCredit = 2 }, ClaimPreload()));
        Assert.Equal(1, PayTypeSelectionRule.Decide("300", null, parameters, ClaimPreload() with { PayType = 1 }));
        Assert.Null(PayTypeSelectionRule.Decide("300", null, parameters, ClaimPreload() with { PayType = null }));
        Assert.Throws<ArgumentNullException>(() => PayTypeSelectionRule.Decide("0", null, null!, null));
    }

    [Fact]
    public void PayType_ClaimPreloadDecidesBeforeCashCompany()
    {
        var cashCompanyPreload = ClaimPreload() with { CompCode = "0" };

        Assert.Equal(2, PayTypeSelectionRule.Decide("0", null, new InvoiceEntryParameters { CashOrCredit = 2 }, cashCompanyPreload with { PayType = 1 }));
        Assert.Equal(2, PayTypeSelectionRule.Decide("0", null, new InvoiceEntryParameters(), cashCompanyPreload with { PayType = 2 }));
        Assert.Equal(1, PayTypeSelectionRule.Decide("0", null, new InvoiceEntryParameters(), cashCompanyPreload with { PayType = 1 }));
        Assert.Equal(1, PayTypeSelectionRule.Decide("0", null, new InvoiceEntryParameters { CashOrCredit = 1 }, cashCompanyPreload));

        var draft = InvoiceDefaultsRule.Apply(new InvoiceEntryParameters { ClaimNo = "C-9", CashOrCredit = 2 }, DraftDate, cashCompanyPreload with { PayType = 1 }, null);
        Assert.Equal("0", draft.CompCode);
        Assert.Equal(2, draft.PayType);
    }

    [Theory]
    [InlineData("Y", "1", "1059", 14, "Review", null)]
    [InlineData("N", "1", "205", 3, "Consultation", null)]
    [InlineData("N", "2", "1059", 14, "FixedService", "2000")]
    [InlineData("N", "2", "1059", 3, "Consultation", null)]
    [InlineData("N", "0", "205", 3, "None", null)]
    [InlineData(null, null, null, null, "None", null)]
    public void VisitLine_Choice(string? doReview, string? claimParam, string? compCode, int? clinicId, string kind, string? serviceId)
    {
        Assert.Equal(new VisitLineChoice(kind, serviceId), VisitLineRule.Choose(doReview, claimParam, compCode, clinicId));
    }
}
