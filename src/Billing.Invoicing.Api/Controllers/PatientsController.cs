using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Invoicing.Api.Controllers;

/// <summary>Patient coverage endpoint.</summary>
[ApiController]
[Route("api/patients")]
public sealed class PatientsController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

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
    /// <param name="workflow">Workflow that reads the coverage and runs its rules.</param>
    /// <param name="problems">Writer of the 422 error-contract bodies.</param>
    public PatientsController(InvoiceWorkflowService workflow, ProblemDetailsWriter problems)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(problems);

        _workflow = workflow;
        _problems = problems;
    }

    /// <summary>Returns the patient's coverage snapshot, coverage messages and pay type.</summary>
    /// <param name="patientNo">Patient number whose coverage is read.</param>
    /// <param name="draftDate">Draft date the coverage is checked against; the database time when absent.</param>
    /// <param name="parameters">Entry parameters of the draft.</param>
    /// <returns>200 with the coverage response, or 422 <c>field-validation</c> when a message is blocking.</returns>
    [HttpGet("{patientNo}/coverage")]
    [ProducesResponseType<CoverageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<CoverageResponse>> Coverage(
        string? patientNo,
        [ModelBinder(typeof(DraftDto.DraftDateQueryBinder))] DateTime? draftDate,
        [FromQuery] InvoiceEntryParameters parameters)
    {
        OperatorContext? operatorContext = CurrentOperator();
        if (operatorContext is null)
        {
            return await OperatorMissing();
        }

        // A blank patient number reaches the workflow as empty, which returns the patient-required message (D-100).
        CoverageResponse response = await _workflow.GetCoverage(
            patientNo ?? string.Empty,
            draftDate,
            parameters ?? new InvoiceEntryParameters(),
            operatorContext,
            HttpContext.RequestAborted);

        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, response.OpenItems, null);
        }

        return Ok(response);
    }

    /// <summary>Returns the operator context the middleware stored for this request.</summary>
    /// <returns>The operator context, or null when none was stored.</returns>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out object? value)
            ? value as OperatorContext
            : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming every operator header.</summary>
    /// <returns>An empty result, the body being already written.</returns>
    private async Task<ActionResult> OperatorMissing()
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
    /// <param name="openItems">Advisory open-item ids; none when null.</param>
    /// <param name="adjusted">Adjusted values keyed by legacy item name; omitted when null.</param>
    /// <returns>An empty result, the body being already written.</returns>
    private async Task<ActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
