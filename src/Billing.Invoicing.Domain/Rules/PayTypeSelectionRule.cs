using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-24: decides the invoice pay type bound as <c>T_HEADER_INPUT.paytype</c> (T015, T023, T026, MAKE_CASH).</summary>
public static class PayTypeSelectionRule
{
    private const int Cash = 1;
    private const int Credit = 2;
    private const string CashCompanyCode = "0";

    /// <summary>Returns the pay type of the draft: 1 cash, 2 credit.</summary>
    /// <param name="compCode">Company code of the patient or draft (COMP_CODE); '0' is the cash company.</param>
    /// <param name="companyType">Company type (<c>COMPANYS.COMP_TYPE</c>) of that company, when known.</param>
    /// <param name="parameters">Entry parameters; <see cref="InvoiceEntryParameters.CashOrCredit"/> is read.</param>
    /// <param name="claimPreload">Header preloaded from the claim's first invoice; when set, it decides the pay type before any company rule. Null when there is no claim preload.</param>
    /// <returns>1 for a cash invoice, 2 for a credit invoice; null only when a claim preload's inherited pay type is null.</returns>
    public static int? Decide(string? compCode, int? companyType, InvoiceEntryParameters parameters, InvoiceHeaderDraft? claimPreload)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (claimPreload is not null)
        {
            return parameters.CashOrCredit switch
            {
                Cash => Cash,
                Credit => Credit,
                _ => claimPreload.PayType,
            };
        }

        if (compCode == CashCompanyCode)
        {
            return Cash;
        }

        if (parameters.CashOrCredit == Cash && companyType is 1 or 2)
        {
            return Cash;
        }

        if (string.IsNullOrEmpty(compCode))
        {
            return Cash;
        }

        return Credit;
    }
}
