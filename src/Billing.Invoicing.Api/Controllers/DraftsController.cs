using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Invoicing.Api.Controllers;

/// <summary>Draft creation and validation endpoints.</summary>
[ApiController]
[Route("api/drafts")]
public sealed class DraftsController : ControllerBase
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

    /// <summary>Creates the controller over the invoicing workflow and the error-contract writer.</summary>
    /// <param name="workflow">Workflow that runs the draft rules.</param>
    /// <param name="problems">Writer of <c>application/problem+json</c> bodies.</param>
    public DraftsController(InvoiceWorkflowService workflow, ProblemDetailsWriter problems)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(problems);
        _workflow = workflow;
        _problems = problems;
    }

    /// <summary>Creates a new invoice draft with its defaults, preloads and request id.</summary>
    /// <param name="parameters">Entry parameters set by the calling module.</param>
    /// <returns>200 with the new draft; 422 when the defaults raise a blocking message or the operator context is missing.</returns>
    [HttpGet("new")]
    public async Task<IActionResult> New([FromQuery] InvoiceEntryParameters parameters)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        NewDraftResponse response = await _workflow.NewDraft(parameters, operatorContext, HttpContext.RequestAborted);

        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, response.OpenItems, null);
        }

        return Ok(response);
    }

    /// <summary>Validates one draft field, line or the whole record.</summary>
    /// <param name="request">Draft, validated target and, for a line target, the line index.</param>
    /// <returns>200 with warnings, adjusted values and advisory open items; 422 <c>field-validation</c> when any message is blocking.</returns>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateDraftRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        ValidateDraftResponse response = await _workflow.Validate(request, operatorContext, HttpContext.RequestAborted);

        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, response.OpenItems, response.Adjusted);
        }

        return Ok(response);
    }

    /// <summary>Returns the operator context stored by <see cref="OperatorContextMiddleware"/>.</summary>
    /// <returns>The request's operator context; null when absent.</returns>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out object? value)
            ? value as OperatorContext
            : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming the operator-context headers.</summary>
    /// <returns>An empty result over the written response.</returns>
    private async Task<IActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns whether any message is blocking.</summary>
    /// <param name="messages">Messages to inspect.</param>
    /// <returns>True when at least one message has blocking severity.</returns>
    private static bool HasBlocking(IReadOnlyList<MessageDto>? messages) =>
        messages?.Any(message => message is not null
            && string.Equals(message.Severity, ValidationMessage.Blocking, StringComparison.Ordinal)) == true;

    /// <summary>Writes the 422 <c>field-validation</c> body with the messages, open items and adjusted values.</summary>
    /// <param name="messages">Blocking and warning messages.</param>
    /// <param name="openItems">Open-item ids; none when null.</param>
    /// <param name="adjusted">Adjusted item values; omitted when null.</param>
    /// <returns>An empty result over the written response.</returns>
    private async Task<IActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
