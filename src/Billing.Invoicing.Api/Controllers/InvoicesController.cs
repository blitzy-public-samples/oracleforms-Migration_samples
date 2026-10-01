using System.Globalization;
using Billing.Invoicing.Api.Context;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Invoicing.Api.Controllers;

/// <summary>Invoice preview, create, view and saved-invoice action endpoints.</summary>
[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";
    private const string InvNoItem = "INV_NO";
    private const string InvalidInvoiceNumberText = "Invoice number must be a positive whole number.";
    private const string NoLastInvoiceText = "No invoice exists for the operator's information centre.";

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
    public InvoicesController(InvoiceWorkflowService workflow, ProblemDetailsWriter problems)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(problems);

        _workflow = workflow;
        _problems = problems;
    }

    /// <summary>Calculates an unsaved draft through the package preview.</summary>
    /// <param name="draft">Draft to calculate; nothing is saved.</param>
    /// <returns>200 with the preview, or 422 with the blocking messages.</returns>
    [HttpPost("preview")]
    [ProducesResponseType<PreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<PreviewResponse>> Preview([FromBody] DraftDto draft)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var response = await _workflow.Preview(draft, operatorContext, HttpContext.RequestAborted);
        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, response.OpenItems, null);
        }

        return Ok(response);
    }

    /// <summary>Saves the draft as a new invoice.</summary>
    /// <param name="request">Draft to save with its request id and discount-limit choice.</param>
    /// <returns>201 with the saved or replayed invoice, or 422 with the blocking messages.</returns>
    [HttpPost]
    [ProducesResponseType<CreateInvoiceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<CreateInvoiceResponse>> Create([FromBody] CreateInvoiceRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var outcome = await _workflow.Create(request, operatorContext, HttpContext.RequestAborted);
        if (outcome.Invoice is not { } invoice)
        {
            return await Rejected(outcome.Messages, outcome.OpenItems, null);
        }

        return Created($"/api/invoices/{invoice.InvNo}", invoice);
    }

    /// <summary>Returns a saved invoice of the requested document type, read-only.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <param name="parameters">Entry parameters of the query string; LOCAL_DOC_TYPE selects the ROW_TYPE.</param>
    /// <returns>200 with the invoice; 404 <c>not-found</c> when it is not found; 422 for a missing operator header, an invalid invoice number or a LOCAL_DOC_TYPE other than 505, 532 or 783.</returns>
    [HttpGet("{invNo}")]
    [ProducesResponseType<InvoiceViewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<InvoiceViewResponse>> Get(string invNo, [FromQuery] InvoiceEntryParameters parameters)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        var response = await _workflow.GetInvoice(
            number, parameters ?? new InvoiceEntryParameters(), operatorContext, HttpContext.RequestAborted);
        return response is null ? await InvoiceNotFound(number) : Ok(response);
    }

    /// <summary>Returns the last invoice number of the operator's information centre.</summary>
    /// <returns>200 with <c>{ "invNo": n }</c>; 404 <c>not-found</c> when the centre has no invoice; 422 for a missing operator header.</returns>
    [HttpGet("last")]
    [ProducesResponseType<LastInvoiceNoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<LastInvoiceNoResponse>> Last()
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var invNo = await _workflow.GetLastInvoiceNo(operatorContext, HttpContext.RequestAborted);
        return invNo is { } value ? Ok(new LastInvoiceNoResponse { InvNo = value }) : await NotFoundProblem(NoLastInvoiceText);
    }

    /// <summary>Returns the MORE-canvas details of a saved invoice of the requested document type.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <param name="parameters">Entry parameters of the query string; LOCAL_DOC_TYPE selects the ROW_TYPE.</param>
    /// <returns>200 with the details; 404 <c>not-found</c> when the invoice is not found; 422 for a missing operator header, an invalid invoice number or a LOCAL_DOC_TYPE other than 505, 532 or 783.</returns>
    [HttpGet("{invNo}/more")]
    [ProducesResponseType<MoreDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemJson)]
    public async Task<ActionResult<MoreDetailsResponse>> More(string invNo, [FromQuery] InvoiceEntryParameters parameters)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        var response = await _workflow.GetMoreDetails(
            number, parameters ?? new InvoiceEntryParameters(), operatorContext, HttpContext.RequestAborted);
        return response is null ? await InvoiceNotFound(number) : Ok(response);
    }

    /// <summary>Sends the invoice SMS.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <returns>501 <c>open-item</c> OI-12 with OI-45 in this build; 422 for a missing operator header or an invalid invoice number.</returns>
    [HttpPost("{invNo}/sms")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    public async Task<ActionResult> Sms(string invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        await _workflow.SendSms(number, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Builds a legacy invoice document.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <param name="kind">Document kind: invoice, patient-card, barcode-sms or iqama-check.</param>
    /// <returns>501 <c>open-item</c> in this build: OI-11 with OI-45, OI-46 and OI-49 for invoice, OI-47 for patient-card, OI-47 with OI-26 for barcode-sms, OI-48 for iqama-check; 422 for a missing operator header, an invalid invoice number or an unknown kind.</returns>
    [HttpPost("{invNo}/documents/{kind}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    public async Task<ActionResult> Documents(string invNo, string kind)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        await _workflow.BuildDocument(number, kind, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Updates a saved invoice.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <returns>501 <c>open-item</c> OI-56 in this build; 422 for a missing operator header or an invalid invoice number.</returns>
    [HttpPatch("{invNo}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    public async Task<ActionResult> Update(string invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        await _workflow.Update(number, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Runs the store transfer of a saved invoice.</summary>
    /// <param name="invNo">Invoice number, a positive whole number.</param>
    /// <returns>501 <c>open-item</c> OI-10 with OI-44 in this build; 422 for a missing operator header or an invalid invoice number.</returns>
    [HttpPost("{invNo}/stock-transfer")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, ProblemJson)]
    public async Task<ActionResult> StockTransfer(string invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!TryInvoiceNumber(invNo, out var number))
        {
            return await InvalidInvoiceNumber();
        }

        await _workflow.TransferStock(number, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Returns the operator context stored by <see cref="OperatorContextMiddleware"/>, or null when absent.</summary>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out var value) ? value as OperatorContext : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming every operator header.</summary>
    private async Task<ActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns whether any message is blocking.</summary>
    private static bool HasBlocking(IReadOnlyList<MessageDto>? messages) =>
        messages?.Any(message => message.Severity == ValidationMessage.Blocking) == true;

    /// <summary>Parses a route invoice number that is a positive whole number of ASCII digits.</summary>
    /// <param name="value">Route value.</param>
    /// <param name="number">The parsed invoice number; zero when the value is invalid.</param>
    /// <returns>True when the value is a positive whole number.</returns>
    private static bool TryInvoiceNumber(string? value, out long number) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;

    /// <summary>Writes the 404 <c>not-found</c> body naming the invoice number.</summary>
    /// <param name="number">Invoice number that was not found.</param>
    private Task<ActionResult> InvoiceNotFound(long number) =>
        NotFoundProblem(string.Create(CultureInfo.InvariantCulture, $"Invoice {number} was not found."));

    /// <summary>Writes the 404 <c>not-found</c> body with the message.</summary>
    /// <param name="message">Text naming what was not found.</param>
    private async Task<ActionResult> NotFoundProblem(string message)
    {
        await _problems.WriteNotFoundAsync(HttpContext, message);
        return new EmptyResult();
    }

    /// <summary>Writes the 422 <c>field-validation</c> body naming <c>INV_NO</c>.</summary>
    private Task<ActionResult> InvalidInvoiceNumber() =>
        Rejected(
            [new MessageDto { Field = InvNoItem, Text = InvalidInvoiceNumberText, Severity = ValidationMessage.Blocking, Rule = null }],
            [],
            null);

    /// <summary>Writes the 422 <c>field-validation</c> body with the messages, open items and adjusted values.</summary>
    private async Task<ActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
