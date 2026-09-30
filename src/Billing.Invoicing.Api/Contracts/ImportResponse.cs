using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Lines, counts and notices returned by an import.</summary>
public sealed record ImportResponse
{
    /// <summary>Lines to add to the draft.</summary>
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>Package import counts and message (<c>t_import_result</c>); <c>null</c> where the operation has none.</summary>
    public ImportResultRow? Result { get; init; }

    /// <summary>Header values changed by the import, keyed by upper-case legacy item name, such as <c>ADD_TO_LIST</c>.</summary>
    public IReadOnlyDictionary<string, object?> Adjusted { get; init; } = new Dictionary<string, object?>();

    /// <summary>Notices and warnings, with legacy text verbatim.</summary>
    public IReadOnlyList<MessageDto> Messages { get; init; } = [];

    /// <summary>Open-item ids that apply to this import.</summary>
    public IReadOnlyList<string> OpenItems { get; init; } = [];
}
