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
    private const string ProblemJson = "application/problem+json";
    private const string CompCodeItem = "COMP_CODE";
    private const string SubCompCodeItem = "SUB_COMP_CODE";
    private const string DocIdxItem = "DOCIDX";
    private const string PatientNoItem = "PATIENTNO";
    private const string PayTypeItem = "PAYTYPE";
    private const string LovNotFoundText = "List of values not found.";
    private const string OffersLov = "OFFERS";
    private const string OferIdColumn = "OFERID";

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
    /// <returns>200 with the rows, each <c>OFFERS</c> row's <c>OFERID</c> as decimal text; 404 <c>not-found</c> for an unknown LOV; or 422 naming every missing, overlong or refused item, or a missing operator header.</returns>
    [HttpGet("lov/{name}")]
    [ProducesResponseType<LovResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<LovResponse>> Lov(
        string name,
        [FromQuery] string? compCode,
        [FromQuery] string? subCompCode,
        [FromQuery] string? docIdx,
        [FromQuery] string? patientNo,
        [FromQuery] string? payType,
        [ModelBinder(typeof(DraftDto.DraftDateQueryBinder))] DateTime? draftDate)
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
            [PayTypeItem] = payType,
        };

        LovResponse? response = await _workflow.GetLov(name, binds, draftDate, operatorContext, HttpContext.RequestAborted);
        if (response is null)
        {
            await _problems.WriteNotFoundAsync(HttpContext, LovNotFoundText);
            return new EmptyResult();
        }

        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, [], null);
        }

        return Ok(WithOfferIdText(response));
    }

    /// <summary>Returns the invoice-type list items.</summary>
    /// <returns>200 with the invoice types.</returns>
    [HttpGet("lookups/invoice-types")]
    [ProducesResponseType<IReadOnlyList<LookupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<LookupItem>>> InvoiceTypes() =>
        Ok(await _workflow.GetInvoiceTypes(HttpContext.RequestAborted));

    /// <summary>Returns the currency list items.</summary>
    /// <returns>200 with the currencies.</returns>
    [HttpGet("lookups/currencies")]
    [ProducesResponseType<IReadOnlyList<LookupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<LookupItem>>> Currencies() =>
        Ok(await _workflow.GetCurrencies(HttpContext.RequestAborted));

    /// <summary>Returns the operator context the middleware stored for this request.</summary>
    /// <returns>The operator context, or null when none is stored.</returns>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out object? value) ? value as OperatorContext : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming every operator-context header.</summary>
    /// <returns>An empty result, the body having been written.</returns>
    private async Task<ActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns the <c>OFFERS</c> list with each row's <c>OFERID</c> as invariant decimal text; any other list unchanged.</summary>
    /// <param name="response">Rows of the requested list.</param>
    /// <returns>A copy of an <c>OFFERS</c> response with new rows, or <paramref name="response"/> itself.</returns>
    private static LovResponse WithOfferIdText(LovResponse response)
    {
        if (!string.Equals(response.Name, OffersLov, StringComparison.Ordinal))
        {
            return response;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(response.Rows.Count);
        foreach (var row in response.Rows)
        {
            var copy = new Dictionary<string, object?>(row.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (column, value) in row)
            {
                copy[column] = string.Equals(column, OferIdColumn, StringComparison.OrdinalIgnoreCase) && value is not null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : value;
            }

            rows.Add(copy);
        }

        return response with { Rows = rows };
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
    private async Task<ActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
