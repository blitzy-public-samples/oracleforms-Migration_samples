using System.Data;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Binds draft line client ids as the <c>l_client_id</c> scalar associative array copied into <c>BIL_INVOICE_API.t_client_id_tab</c>. UNVERIFIED against Oracle.</summary>
public static class ClientIdBinder
{
    private const string ParameterName = "l_client_id";
    private const int MaxElementLength = 4000;

    /// <summary>Builds the <c>l_client_id</c> IN associative array, element <c>i</c> holding <c>lines[i].ClientId</c>.</summary>
    /// <param name="lines">Draft lines in the order they are bound by <c>LineInputBinder</c>.</param>
    /// <returns>A <c>Varchar2</c> associative-array parameter with one element per line, or one null element when there are no lines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="lines"/> contains a null line, or a client id longer than 4000 characters or 4000 UTF-8 bytes.</exception>
    public static OracleParameter Bind(IReadOnlyList<InvoiceLineDraft> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? throw new ArgumentException($"Line at index {i} is null.", nameof(lines));
            if (line.ClientId is not { } clientId)
            {
                continue;
            }

            // A client id over the t_vc element size in characters or UTF-8 bytes is rejected, never truncated to fit.
            string? rejection = null;
            if (clientId.Length > MaxElementLength)
            {
                rejection = $"Invoice line at index {i}: {ParameterName} has {clientId.Length} characters; at most {MaxElementLength} can be bound.";
            }
            else if (Encoding.UTF8.GetByteCount(clientId) is var bytes and > MaxElementLength)
            {
                rejection = $"Invoice line at index {i}: {ParameterName} has {bytes} bytes in UTF-8; at most {MaxElementLength} can be bound.";
            }

            if (rejection is not null)
            {
                var error = new ArgumentException(rejection, nameof(lines));
                error.Data[OracleFailureTranslator.BindingRejectionKey] = rejection;
                throw error;
            }
        }

        var size = Math.Max(lines.Count, 1);
        var values = new OracleString[size];
        var bindSizes = new int[size];
        var statuses = new OracleParameterStatus[size];

        for (var i = 0; i < size; i++)
        {
            var clientId = i < lines.Count ? lines[i].ClientId : null;

            if (clientId is null)
            {
                values[i] = OracleString.Null;
                bindSizes[i] = 1;
                statuses[i] = OracleParameterStatus.NullInsert;
            }
            else
            {
                values[i] = new OracleString(clientId);
                bindSizes[i] = Math.Max(clientId.Length, 1);
                statuses[i] = OracleParameterStatus.Success;
            }
        }

        return new OracleParameter
        {
            ParameterName = ParameterName,
            OracleDbType = OracleDbType.Varchar2,
            Direction = ParameterDirection.Input,
            CollectionType = OracleCollectionType.PLSQLAssociativeArray,
            Size = size,
            Value = values,
            ArrayBindSize = bindSizes,
            ArrayBindStatus = statuses,
        };
    }
}
