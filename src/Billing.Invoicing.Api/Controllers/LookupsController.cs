using System.Globalization;
using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Invoicing.Api.Controllers;

/// <summary>LOV and list-item lookup endpoints.</summary>
[ApiController]
[Route("api")]
public sealed class LookupsController : ControllerBase
{
    private const string CompCodeItem = "COMP_CODE";
    private const string SubCompCodeItem = "SUB_COMP_CODE";
    private const string DocIdxItem = "DOCIDX";
    private const string PatientNoItem = "PATIENTNO";
    private const string PayTypeItem = "PAYTYPE";

    /// <summary>Operator-context headers named when the request carries no operator context.</summary>
    private static readonly string[] OperatorHeaders =
    [
        "X-His-User-No",
        "X-His-User-Name",
        "X-His-Info-Center-Id",
        "X-His-Machine",
        "X-His-Session-Id",
    ];

    private readonly InvoiceWorkflowService _workflow;
    private readonly ProblemDetailsWriter _problems;

    /// <summary>Creates the controller over the workflow service and the problem-details writer.</summary>
    /// <param name="workflow">Workflow service answering the lookups.</param>
    /// <param name="problems">Writer of the 422 error-contract bodies.</param>
    public LookupsController(InvoiceWorkflowService workflow, ProblemDetailsWriter problems)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(problems);

        _workflow = workflow;
        _problems = problems;
    }

    /// <summary>Returns the rows of one LOV record group.</summary>
    /// <param name="name">LOV name, such as <c>COMPANY1_2</c>.</param>
    /// <param name="compCode">Draft company code, bound as <c>COMP_CODE</c>.</param>
    /// <param name="subCompCode">Draft sub-company code, bound as <c>SUB_COMP_CODE</c>.</param>
    /// <param name="docIdx">Draft doctor id, bound as <c>DOCIDX</c>.</param>
    /// <param name="patientNo">Draft patient number, bound as <c>PATIENTNO</c>.</param>
    /// <param name="payType">Draft pay type, bound as <c>PAYTYPE</c>.</param>
    /// <param name="draftDate">Draft date, bound as <c>INVDATE</c>.</param>
    /// <returns>200 with the rows, 404 for an unknown LOV, or 422 naming a missing item or operator header.</returns>
    [HttpGet("lov/{name}")]
    public async Task<IActionResult> Lov(
        string name,
        [FromQuery] string? compCode,
        [FromQuery] string? subCompCode,
        [FromQuery] string? docIdx,
        [FromQuery] string? patientNo,
        [FromQuery] int? payType,
        [FromQuery] DateTime? draftDate)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var binds = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [CompCodeItem] = compCode,
            [SubCompCodeItem] = subCompCode,
            [DocIdxItem] = docIdx,
            [PatientNoItem] = patientNo,
            [PayTypeItem] = payType?.ToString(CultureInfo.InvariantCulture),
        };

        LovResponse? response = await _workflow.GetLov(name, binds, draftDate, operatorContext, HttpContext.RequestAborted);
        if (response is null)
        {
            return NotFound();
        }

        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, [], null);
        }

        return Ok(response);
    }

    /// <summary>Returns the invoice-type list items.</summary>
    /// <returns>200 with the invoice types.</returns>
    [HttpGet("lookups/invoice-types")]
    public async Task<IActionResult> InvoiceTypes() =>
        Ok(await _workflow.GetInvoiceTypes(HttpContext.RequestAborted));

    /// <summary>Returns the currency list items.</summary>
    /// <returns>200 with the currencies.</returns>
    [HttpGet("lookups/currencies")]
    public async Task<IActionResult> Currencies() =>
        Ok(await _workflow.GetCurrencies(HttpContext.RequestAborted));

    /// <summary>Returns the operator context the middleware stored for this request.</summary>
    /// <returns>The operator context, or null when none is stored.</returns>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out object? value) ? value as OperatorContext : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming every operator-context header.</summary>
    /// <returns>An empty result, the body having been written.</returns>
    private async Task<IActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns whether any message is blocking.</summary>
    /// <param name="messages">Messages to inspect; null holds none.</param>
    /// <returns><c>true</c> when at least one message has blocking severity.</returns>
    private static bool HasBlocking(IReadOnlyList<MessageDto>? messages) =>
        messages is not null
        && messages.Any(message => string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal));

    /// <summary>Writes the 422 <c>field-validation</c> body with the messages, open-item ids and adjusted values.</summary>
    /// <param name="messages">Blocking and warning messages.</param>
    /// <param name="openItems">Open-item ids; null writes none.</param>
    /// <param name="adjusted">Adjusted values keyed by legacy item name; null omits them.</param>
    /// <returns>An empty result, the body having been written.</returns>
    private async Task<IActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
