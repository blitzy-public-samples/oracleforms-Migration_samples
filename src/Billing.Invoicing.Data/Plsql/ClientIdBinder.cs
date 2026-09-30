using System.Data;
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
    /// <exception cref="ArgumentException"><paramref name="lines"/> contains a null line.</exception>
    public static OracleParameter Bind(IReadOnlyList<InvoiceLineDraft> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var size = Math.Max(lines.Count, 1);
        var values = new OracleString[size];
        var bindSizes = new int[size];
        var statuses = new OracleParameterStatus[size];

        for (var i = 0; i < size; i++)
        {
            string? clientId = null;
            if (i < lines.Count)
            {
                var line = lines[i] ?? throw new ArgumentException($"Line at index {i} is null.", nameof(lines));
                clientId = line.ClientId;
            }

            if (clientId is null)
            {
                values[i] = OracleString.Null;
                bindSizes[i] = 1;
                statuses[i] = OracleParameterStatus.NullInsert;
            }
            else
            {
                values[i] = new OracleString(clientId);
                bindSizes[i] = Math.Clamp(clientId.Length, 1, MaxElementLength);
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
