using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-18: request-import doctor precondition and per-service notices (T085).</summary>
public static class RequestImportRules
{
    private const string RuleId = "DR-18";
    private const string DoctorField = "DOCIDX";
    private const string ServiceField = "SERVICEID";

    private const string SelectDoctorFirst = "Select doctor First";
    private const string RejectedSuffix = " Rejected ";
    private const string NeedApprovalSuffix = " Need Approval";

    private const int RejectedStatus = 3;
    private const int ApprovalCheckBypassed = 2;
    private const int ApprovalCheckEnforced = 1;
    private const int NoApprovalNeeded = 0;
    private const int CreditPayType = 2;

    /// <summary>Checks that a doctor is selected before request lines are imported.</summary>
    /// <param name="docId">The header doctor (<c>DOCIDX</c>).</param>
    /// <returns>A blocking 'Select doctor First' message on <c>DOCIDX</c> when no doctor is selected; otherwise <see cref="RuleResult.Empty"/>.</returns>
    public static RuleResult RequireDoctor(int? docId) =>
        docId is null
            ? new RuleResult
            {
                Messages = [new ValidationMessage(DoctorField, SelectDoctorFirst, ValidationMessage.Blocking, RuleId)],
            }
            : RuleResult.Empty;

    /// <summary>Returns the per-service notices for the selected request rows, in row order.</summary>
    /// <param name="rows">Selected request rows: service id, <c>REQ_A_STATUS</c>, <c>REQ_NEED_A</c> and <c>APPROV_REF_NO</c>.</param>
    /// <param name="x422ApprovCheck">The <c>X422_APPROV_CHECK</c> entry parameter.</param>
    /// <param name="payType">The header pay type (<c>PAYTYPE</c>): 1 cash, 2 credit.</param>
    /// <returns>One warning on <c>SERVICEID</c> per rejected row (service id + ' Rejected ') and per credit row needing
    /// approval without a reference under setting 1 (service id + ' Need Approval'); <see cref="RuleResult.Empty"/> when none applies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    public static RuleResult Notices(
        IEnumerable<(string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)> rows,
        int? x422ApprovCheck,
        int? payType)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var notices = new List<ValidationMessage>();
        foreach (var row in rows)
        {
            var text = NoticeText(row.ServiceId, row.ReqAStatus, row.ReqNeedA, row.ApprovRefNo, x422ApprovCheck, payType);
            if (text is not null)
            {
                notices.Add(new ValidationMessage(ServiceField, text, ValidationMessage.Warning, RuleId));
            }
        }

        return notices.Count == 0
            ? RuleResult.Empty
            : new RuleResult { Messages = notices.AsReadOnly() };
    }

    private static string? NoticeText(
        string serviceId,
        int? reqAStatus,
        int? reqNeedA,
        string? approvRefNo,
        int? x422ApprovCheck,
        int? payType)
    {
        if (reqAStatus == RejectedStatus)
        {
            return serviceId + RejectedSuffix;
        }

        if (x422ApprovCheck == ApprovalCheckBypassed || reqNeedA == NoApprovalNeeded)
        {
            return null;
        }

        if (x422ApprovCheck == ApprovalCheckEnforced
            && reqNeedA is not null
            && reqNeedA != NoApprovalNeeded
            && string.IsNullOrEmpty(approvRefNo)
            && payType == CreditPayType)
        {
            return serviceId + NeedApprovalSuffix;
        }

        return null;
    }
}
