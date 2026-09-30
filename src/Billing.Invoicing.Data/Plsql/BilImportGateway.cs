using System.Data;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Calls the BIL_IMPORT request-import and visit-line operations through the <see cref="PlsqlBlocks"/> anonymous blocks. UNVERIFIED against Oracle.</summary>
public sealed class BilImportGateway : IBilImportGateway
{
    private const int BypassApprovalSetting = 2;
    private const int ApprovalCheckSkipped = 0;
    private const int ApprovalCheckEnforced = 1;

    private const string ReviewVisitType = "REVIEW";
    private const string ConsultationVisitType = "CONSULTATION";

    private const string ImportRequestLinesOperation = "ImportRequestLines";
    private const string GetVisitLineOperation = "GetVisitLine";

    private const string RequestRowIdParameterName = "req_row_id";

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Application id, output capacity and command timeout used by every call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public BilImportGateway(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Runs BIL_IMPORT.SET_REQUEST_LINE_SELECTION per row, GET_INVOICE_REQUEST_LINES, CLEAR_REQUEST_INVOICE_SELECTION and TO_ENGINE_LINES in the session.</summary>
    /// <param name="session">Open session whose transaction the calls run in; the caller commits or rolls it back.</param>
    /// <param name="header">Draft header supplying the patient, pay type and draft date.</param>
    /// <param name="operatorContext">Operator supplying the selection session id and user name.</param>
    /// <param name="visitUnique">Visit the request rows belong to.</param>
    /// <param name="patServReqRowIds">PAT_SERV_REQ row ids to select for import.</param>
    /// <param name="approvalMode">Approval check mode: 0 or 1.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    /// <returns>The imported rows as engine lines, and the package's import result.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="approvalMode"/> is neither 0 nor 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an Oracle session of this layer.</exception>
    /// <exception cref="OracleException">The block failed; the exception carries the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> ImportRequestLines(
        IOracleSession session,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        string visitUnique,
        IReadOnlyList<long> patServReqRowIds,
        int approvalMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);
        ArgumentNullException.ThrowIfNull(visitUnique);
        ArgumentNullException.ThrowIfNull(patServReqRowIds);
        if (approvalMode is not (ApprovalCheckSkipped or ApprovalCheckEnforced))
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvalMode),
                approvalMode,
                $"Approval check mode must be {ApprovalCheckSkipped} or {ApprovalCheckEnforced}.");
        }

        return await Execute(
            session,
            PlsqlBlocks.RequestImport,
            ImportRequestLinesOperation,
            parameters =>
            {
                parameters.Add(Input("patientno", OracleDbType.Varchar2, header.PatientNo));
                parameters.Add(Input("visit_unique", OracleDbType.Varchar2, visitUnique));
                parameters.Add(Input("paytype", OracleDbType.Decimal, header.PayType));
                parameters.Add(Input("app_id", OracleDbType.Decimal, _options.ApplicationId));
                parameters.Add(Input("app_session_id", OracleDbType.Varchar2, operatorContext.SessionId));
                parameters.Add(Input("app_user", OracleDbType.Varchar2, operatorContext.UserName));
                parameters.Add(Input("invoice_date", OracleDbType.Date, header.DraftDate));
                parameters.Add(Input("approval_mode", OracleDbType.Decimal, approvalMode));
                parameters.Add(RequestRowIdArray(patServReqRowIds));
                parameters.Add(Input("req_row_count", OracleDbType.Decimal, patServReqRowIds.Count));
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            parameters => (OutputArrayReader.ReadEngineLines(parameters), OutputArrayReader.ReadImportResult(parameters)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps an X422_APPROV_CHECK value to the approval check mode of BIL_IMPORT.GET_INVOICE_REQUEST_LINES.</summary>
    /// <param name="x422ApprovCheck">The X422_APPROV_CHECK entry parameter.</param>
    /// <returns>0 when the value is 2; otherwise 1, including for null.</returns>
    public int ApprovalCheckMode(int? x422ApprovCheck) =>
        x422ApprovCheck == BypassApprovalSetting ? ApprovalCheckSkipped : ApprovalCheckEnforced;

    /// <summary>Runs BIL_IMPORT.GET_VISIT_LINE and TO_ENGINE_LINES in the session for a review or consultation choice.</summary>
    /// <param name="session">Open session whose transaction the calls run in; the caller commits or rolls it back.</param>
    /// <param name="header">Draft header supplying the patient, doctor, pay type, clinic and draft date.</param>
    /// <param name="operatorContext">Operator supplying the information centre.</param>
    /// <param name="choice"><see cref="VisitLineChoice.Review"/> or <see cref="VisitLineChoice.Consultation"/>.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    /// <returns>The first engine line, or null when the package returns none, and the package's import result.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="choice"/> is neither review nor consultation, or <paramref name="session"/> is not an Oracle session of this layer.</exception>
    /// <exception cref="OracleException">The block failed; the exception carries the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(EngineLineInput? Line, ImportResultRow Result)> GetVisitLine(
        IOracleSession session,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        VisitLineChoice choice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var visitType = VisitType(choice);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);

        return await Execute(
            session,
            PlsqlBlocks.VisitLine,
            GetVisitLineOperation,
            parameters =>
            {
                parameters.Add(Input("patientno", OracleDbType.Varchar2, header.PatientNo));
                parameters.Add(Input("docid", OracleDbType.Decimal, header.DocId));
                parameters.Add(Input("new_visit_type", OracleDbType.Varchar2, visitType));
                parameters.Add(Input("paytype", OracleDbType.Decimal, header.PayType));
                parameters.Add(Input("clinicid", OracleDbType.Decimal, header.ClinicId));
                parameters.Add(Input("info_center_id", OracleDbType.Varchar2, operatorContext.InfoCenterId));
                parameters.Add(Input("invoice_date", OracleDbType.Date, header.DraftDate));
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            parameters =>
            {
                var lines = OutputArrayReader.ReadEngineLines(parameters);
                return (lines.Count > 0 ? lines[0] : null, OutputArrayReader.ReadImportResult(parameters));
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the GET_VISIT_LINE visit type of a review or consultation choice.</summary>
    /// <exception cref="ArgumentException">The choice is of any other kind.</exception>
    private static string VisitType(VisitLineChoice choice)
    {
        if (string.Equals(choice.Kind, VisitLineChoice.Review.Kind, StringComparison.Ordinal))
        {
            return ReviewVisitType;
        }

        if (string.Equals(choice.Kind, VisitLineChoice.Consultation.Kind, StringComparison.Ordinal))
        {
            return ConsultationVisitType;
        }

        throw new ArgumentException(
            $"Visit line choice '{choice.Kind}' has no BIL_IMPORT visit line; only '{VisitLineChoice.Review.Kind}' and '{VisitLineChoice.Consultation.Kind}' are imported.",
            nameof(choice));
    }

    /// <summary>Runs one block on the session's connection, tags a driver failure with its operation name, and reads the outputs.</summary>
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an <see cref="OracleSession"/>.</exception>
    private async Task<T> Execute<T>(
        IOracleSession session,
        string block,
        string operation,
        Action<OracleParameterCollection> bind,
        Func<OracleParameterCollection, T> read,
        CancellationToken cancellationToken)
    {
        if (session is not OracleSession oracleSession)
        {
            throw new ArgumentException(
                $"The session must be a {nameof(OracleSession)} opened by the data layer, not {session.GetType().Name}.",
                nameof(session));
        }

        using var command = new OracleCommand(block.Replace("\r\n", "\n", StringComparison.Ordinal), oracleSession.Connection)
        {
            CommandType = CommandType.Text,
            BindByName = true,
            CommandTimeout = _options.CommandTimeoutSeconds,
        };

        try
        {
            bind(command.Parameters);

            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OracleException ex)
            {
                ex.Data[OracleErrorParser.OperationKey] = operation;
                throw;
            }

            return read(command.Parameters);
        }
        finally
        {
            foreach (OracleParameter parameter in command.Parameters)
            {
                parameter.Dispose();
            }
        }
    }

    /// <summary>Builds the <c>req_row_id</c> NUMBER associative array, one element per id, or one null element when there are none.</summary>
    private static OracleParameter RequestRowIdArray(IReadOnlyList<long> ids)
    {
        var size = Math.Max(ids.Count, 1);
        var values = new OracleDecimal[size];
        var statuses = new OracleParameterStatus[size];
        Array.Fill(values, OracleDecimal.Null);
        Array.Fill(statuses, OracleParameterStatus.NullInsert);

        for (var i = 0; i < ids.Count; i++)
        {
            values[i] = new OracleDecimal(ids[i]);
            statuses[i] = OracleParameterStatus.Success;
        }

        return new OracleParameter(RequestRowIdParameterName, OracleDbType.Decimal)
        {
            Direction = ParameterDirection.Input,
            CollectionType = OracleCollectionType.PLSQLAssociativeArray,
            Size = size,
            Value = values,
            ArrayBindStatus = statuses,
        };
    }

    /// <summary>Creates one scalar input parameter of the given type, sending a null value as <see cref="DBNull.Value"/>.</summary>
    private static OracleParameter Input(string name, OracleDbType dbType, object? value) =>
        new(name, dbType)
        {
            Direction = ParameterDirection.Input,
            Value = value ?? DBNull.Value,
        };
}
