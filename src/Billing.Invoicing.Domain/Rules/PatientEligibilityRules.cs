using System.Globalization;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-03: checks the patient's coverage row against the draft date (T023).</summary>
public static class PatientEligibilityRules
{
    private const string Field = "PATIENTNO";
    private const string RuleId = "DR-03";
    private const string DateFormat = "dd/MM/yyyy";

    private const string CashCompanyCode = "0";
    private const int CashOrCreditCash = 1;
    private const int CardCompanyType = 2;
    private const int OnHold = 2;
    private const int DateAdminUser = 1;
    private const int NormalUser = 2;
    private const int ReferralRequiredFlag = 1;

    private const string ContractEnded = "Contract  Ended ";
    private const string DateAdminSuffix = " , But due to that user have date admin privileges system will open claim";
    private const string CompanyIsHoled = "Company Is Holed";
    private const string CardExpired = "Card Expired ";
    private const string TodayLastDate = " , Today last Date";
    private const string PolicyEnded = "Policy  Ended ";
    private const string PolicyDateAdminSuffix = " ,But due to that user have date admin privileges system will open claim";
    private const string PolicyCashSuffix = " Patient well treated as cash patient ";
    private const string PolicyIsHoled = "Policy Is Holed";
    private const string ClassIsHoled = "Class Is Holed";
    private const string ReferralRequired = "Refral Required For This Class";

    /// <summary>Evaluates the contract, company, card, policy and class checks of the patient's coverage.</summary>
    /// <param name="coverage">Coverage snapshot read from <c>V_PAT_DATA</c>; null when the patient has none.</param>
    /// <param name="parameters">Entry parameters; <c>INV_DATE_ADMIN</c> and <c>CASH_OR_CREDIT</c> are read.</param>
    /// <param name="draftDate">The draft's invoice date (<c>INVDATE</c>).</param>
    /// <returns>Messages on <c>PATIENTNO</c> in legacy order, ending at the first blocking message; no adjusted values.</returns>
    public static RuleResult Evaluate(PatientCoverageSnapshot? coverage, InvoiceEntryParameters parameters, DateTime draftDate)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (coverage is null || IsCashCompany(coverage, parameters))
        {
            return RuleResult.Empty;
        }

        var date = draftDate.Date;
        var messages = new List<ValidationMessage>();

        if (coverage.ContractEnd is { } contractEnd && contractEnd < date)
        {
            var text = ContractEnded + Format(contractEnd);
            if (parameters.InvDateAdmin == NormalUser)
            {
                return Stop(messages, text);
            }

            messages.Add(Warning(text + DateAdminSuffix));
        }

        if (coverage.CompanyIsActive == OnHold)
        {
            return Stop(messages, CompanyIsHoled);
        }

        if (coverage.CompanyType != CardCompanyType)
        {
            return Result(messages);
        }

        if (coverage.CardEnd is { } cardEnd)
        {
            if (cardEnd < date)
            {
                var text = CardExpired + Format(cardEnd);
                if (parameters.InvDateAdmin == NormalUser)
                {
                    return Stop(messages, text);
                }

                messages.Add(Warning(text + DateAdminSuffix));
            }
            else if (cardEnd == date)
            {
                messages.Add(Warning(CardExpired + Format(cardEnd) + TodayLastDate));
            }
        }

        if (!string.IsNullOrEmpty(coverage.SubCompCode))
        {
            if (coverage.SubCompanyContractEnd is { } policyEnd && policyEnd < date)
            {
                var text = PolicyEnded + Format(policyEnd);
                if (parameters.InvDateAdmin != DateAdminUser)
                {
                    return Stop(messages, text + PolicyCashSuffix);
                }

                messages.Add(Warning(text + PolicyDateAdminSuffix));
            }

            if (coverage.SubCompanyIsActive == OnHold)
            {
                return Stop(messages, PolicyIsHoled);
            }
        }

        if (coverage.MyClass.HasValue)
        {
            if (coverage.ClassIsActive == OnHold)
            {
                return Stop(messages, ClassIsHoled);
            }

            if ((coverage.ClassWithRef ?? 0) == ReferralRequiredFlag)
            {
                messages.Add(Warning(ReferralRequired));
            }
        }

        return Result(messages);
    }

    private static bool IsCashCompany(PatientCoverageSnapshot coverage, InvoiceEntryParameters parameters) =>
        string.Equals(coverage.CompCode, CashCompanyCode, StringComparison.Ordinal)
        || (parameters.CashOrCredit == CashOrCreditCash && coverage.CompanyType == CardCompanyType);

    private static string Format(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static ValidationMessage Warning(string text) => new(Field, text, ValidationMessage.Warning, RuleId);

    private static RuleResult Stop(List<ValidationMessage> messages, string text)
    {
        messages.Add(new ValidationMessage(Field, text, ValidationMessage.Blocking, RuleId));
        return Result(messages);
    }

    private static RuleResult Result(List<ValidationMessage> messages) =>
        messages.Count == 0 ? RuleResult.Empty : new RuleResult { Messages = messages.ToArray() };
}
