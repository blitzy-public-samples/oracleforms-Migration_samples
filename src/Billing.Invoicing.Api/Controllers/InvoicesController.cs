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
    private static readonly string[] OperatorHeaders =
    [
        "X-His-User-No",
        "X-His-User-Name",
        "X-His-Info-Center-Id",
        "X-His-Machine",
        "X-His-Session-Id",
    ];

    private static readonly HashSet<string> DocumentKinds = new(StringComparer.Ordinal)
    {
        "invoice",
        "patient-card",
        "barcode-sms",
        "iqama-check",
    };

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
    public async Task<IActionResult> Preview([FromBody] DraftDto draft)
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
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var response = await _workflow.Create(request, operatorContext, HttpContext.RequestAborted);
        if (HasBlocking(response.Messages))
        {
            return await Rejected(response.Messages, response.OpenItems, null);
        }

        return Created($"/api/invoices/{response.InvNo}", response);
    }

    /// <summary>Returns a saved invoice, read-only.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="parameters">Entry parameters supplying the local document type filter.</param>
    /// <returns>200 with the invoice, or 404 when it is not found.</returns>
    [HttpGet("{invNo:long}")]
    public async Task<IActionResult> Get(long invNo, [FromQuery] InvoiceEntryParameters parameters)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var response = await _workflow.GetInvoice(invNo, parameters, operatorContext, HttpContext.RequestAborted);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>Returns the last invoice number of the operator's information centre.</summary>
    /// <returns>200 with <c>{ "invNo": n }</c>, or 404 when the centre has no invoice.</returns>
    [HttpGet("last")]
    public async Task<IActionResult> Last()
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var invNo = await _workflow.GetLastInvoiceNo(operatorContext, HttpContext.RequestAborted);
        return invNo is { } value ? Ok(new { invNo = value }) : NotFound();
    }

    /// <summary>Returns the MORE-canvas details of a saved invoice.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <returns>200 with the details, or 404 when the invoice is not found.</returns>
    [HttpGet("{invNo:long}/more")]
    public async Task<IActionResult> More(long invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        var response = await _workflow.GetMoreDetails(invNo, operatorContext, HttpContext.RequestAborted);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>Sends the invoice SMS.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <returns>204 when the SMS is sent.</returns>
    [HttpPost("{invNo:long}/sms")]
    public async Task<IActionResult> Sms(long invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        await _workflow.SendSms(invNo, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Builds a legacy invoice document.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="kind">Document kind: invoice, patient-card, barcode-sms or iqama-check.</param>
    /// <returns>204 when the document is built, or 404 for an unknown kind.</returns>
    [HttpPost("{invNo:long}/documents/{kind}")]
    public async Task<IActionResult> Documents(long invNo, string kind)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        if (!DocumentKinds.Contains(kind))
        {
            return NotFound();
        }

        await _workflow.BuildDocument(invNo, kind, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Updates a saved invoice.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <returns>204 when the invoice is updated.</returns>
    [HttpPatch("{invNo:long}")]
    public async Task<IActionResult> Update(long invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        await _workflow.Update(invNo, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Runs the store transfer of a saved invoice.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <returns>204 when the transfer is done.</returns>
    [HttpPost("{invNo:long}/stock-transfer")]
    public async Task<IActionResult> StockTransfer(long invNo)
    {
        if (CurrentOperator() is not { } operatorContext)
        {
            return await OperatorMissing();
        }

        await _workflow.TransferStock(invNo, operatorContext, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Returns the operator context stored by <see cref="OperatorContextMiddleware"/>, or null when absent.</summary>
    private OperatorContext? CurrentOperator() =>
        HttpContext.Items.TryGetValue(OperatorContextMiddleware.ItemKey, out var value) ? value as OperatorContext : null;

    /// <summary>Writes the 422 <c>operator-context-missing</c> body naming every operator header.</summary>
    private async Task<IActionResult> OperatorMissing()
    {
        await _problems.WriteAsync(HttpContext, OperatorHeaders);
        return new EmptyResult();
    }

    /// <summary>Returns whether any message is blocking.</summary>
    private static bool HasBlocking(IReadOnlyList<MessageDto>? messages) =>
        messages?.Any(message => message.Severity == ValidationMessage.Blocking) == true;

    /// <summary>Writes the 422 <c>field-validation</c> body with the messages, open items and adjusted values.</summary>
    private async Task<IActionResult> Rejected(
        IReadOnlyList<MessageDto> messages,
        IReadOnlyList<string>? openItems,
        IReadOnlyDictionary<string, object?>? adjusted)
    {
        await _problems.WriteAsync(HttpContext, messages, openItems ?? [], adjusted);
        return new EmptyResult();
    }
}
