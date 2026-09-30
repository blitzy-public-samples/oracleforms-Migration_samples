using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Invoicing.Api.Controllers;

/// <summary>Request, visit-line, package and bundled-offer import endpoints.</summary>
[ApiController]
[Route("api/imports")]
public sealed class ImportsController : ControllerBase
{
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

    /// <summary>Creates the controller over the invoice workflow and the error-contract writer.</summary>
    /// <param name="workflow">Workflow service that performs the imports.</param>
    /// <param name="problems">Writer of the 422 error-contract bodies.</param>
    public ImportsController(InvoiceWorkflowService workflow, ProblemDetailsWriter problems)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(problems);
        _workflow = workflow;
        _problems = problems;
    }

    /// <summary>Imports the patient's selected service requests into the draft.</summary>
    /// <param name="request">Draft supplying the patient, doctor, pay type and visit.</param>
    /// <returns>200 with the imported lines, counts and notices; 422 with the blocking messages.</returns>
    [HttpPost("requests")]
    public async Task<IActionResult> Requests([FromBody] ImportRequestsRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        ImportResponse response = await _workflow.ImportRequests(request, operatorContext, HttpContext.RequestAborted);
        return await Respond(response);
    }

    /// <summary>Adds the automatic consultation, review or fixed-service visit line.</summary>
    /// <param name="request">Draft from which the visit line is chosen.</param>
    /// <returns>200 with zero or one visit line and, for a consultation or review line, its import result; 422 with the blocking messages.</returns>
    [HttpPost("visit-line")]
    public async Task<IActionResult> VisitLine([FromBody] VisitLineRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        ImportResponse response = await _workflow.ImportVisitLine(request, operatorContext, HttpContext.RequestAborted);
        return await Respond(response);
    }

    /// <summary>Expands a package service into its component lines.</summary>
    /// <param name="request">Draft, package service id and optional parent source id.</param>
    /// <returns>200 with the component lines, import result, warnings and adjusted values; 422 with the blocking messages.</returns>
    [HttpPost("package")]
    public async Task<IActionResult> Package([FromBody] PackageImportRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        ImportResponse response = await _workflow.ImportPackage(request, operatorContext, HttpContext.RequestAborted);
        return await Respond(response);
    }

    /// <summary>Loads the lines of a bundled offer.</summary>
    /// <param name="request">Draft, offer id and bundle quantity.</param>
    /// <returns>200 with the offer lines; 422 with the blocking messages.</returns>
    [HttpPost("bundled-offer")]
    public async Task<IActionResult> BundledOffer([FromBody] BundledOfferRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        ImportResponse response = await _workflow.ImportBundledOffer(request, operatorContext, HttpContext.RequestAborted);
        return await Respond(response);
    }

    /// <summary>Returns the operator context the middleware stored for this request.</summary>
    /// <returns>The operator context, or null when none is stored.</returns>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out object? value)
            ? value as OperatorContext
            : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming the operator-context headers.</summary>
    /// <returns>An empty result, the body being already written.</returns>
    private async Task<IActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns 422 when the import carries a blocking message, else 200 with the import.</summary>
    /// <param name="response">The workflow's import response.</param>
    /// <returns>The action result.</returns>
    private async Task<IActionResult> Respond(ImportResponse response) =>
        HasBlocking(response.Messages)
            ? await Rejected(response.Messages, response.OpenItems, response.Adjusted)
            : Ok(response);

    /// <summary>Returns whether any message is blocking.</summary>
    /// <param name="messages">Messages to inspect; null counts as none.</param>
    /// <returns>True when at least one message has severity <see cref="ValidationMessage.Blocking"/>.</returns>
    private static bool HasBlocking(IReadOnlyList<MessageDto>? messages) =>
        messages?.Any(message => message is not null
            && string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal)) == true;

    /// <summary>Writes the 422 <c>field-validation</c> body with the messages, open-item ids and adjusted values.</summary>
    /// <param name="messages">Blocking and warning messages.</param>
    /// <param name="openItems">Open-item ids; null writes none.</param>
    /// <param name="adjusted">Adjusted values keyed by legacy item name; null omits them.</param>
    /// <returns>An empty result, the body being already written.</returns>
    private async Task<IActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
