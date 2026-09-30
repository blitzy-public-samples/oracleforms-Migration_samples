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
    private const string EngineLineCountName = "el_count";

    private const string MaxOutputLinesKey = "Invoicing:MaxOutputLines";
    private const string CommandTimeoutSecondsKey = "Invoicing:CommandTimeoutSeconds";

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Application id, output capacity and command timeout used by every call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.MaxOutputLines"/> or <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1.</exception>
    public BilImportGateway(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxOutputLines < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxOutputLines,
                $"{MaxOutputLinesKey} ({nameof(InvoicingDataOptions)}.{nameof(InvoicingDataOptions.MaxOutputLines)}) must be at least 1.");
        }

        if (options.CommandTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CommandTimeoutSeconds,
                $"{CommandTimeoutSecondsKey} ({nameof(InvoicingDataOptions)}.{nameof(InvoicingDataOptions.CommandTimeoutSeconds)}) must be at least 1.");
        }

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
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an Oracle session of this layer, a value exceeds its destination width, or the import returns more lines than Invoicing:MaxOutputLines.</exception>
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

        BoundedVarchar2.Validate("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header));
        BoundedVarchar2.Validate("visit_unique", visitUnique, BoundedVarchar2.VisitUniqueBytes, nameof(visitUnique));
        BoundedVarchar2.Validate("app_session_id", operatorContext.SessionId, BoundedVarchar2.SqlVarchar2Bytes, nameof(operatorContext));
        BoundedVarchar2.Validate("app_user", operatorContext.UserName, BoundedVarchar2.SqlVarchar2Bytes, nameof(operatorContext));

        return await Execute(
            session,
            PlsqlBlocks.RequestImport,
            ImportRequestLinesOperation,
            parameters =>
            {
                parameters.Add(BoundedVarchar2.Input("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header)));
                parameters.Add(BoundedVarchar2.Input("visit_unique", visitUnique, BoundedVarchar2.VisitUniqueBytes, nameof(visitUnique)));
                parameters.Add(Input("paytype", OracleDbType.Decimal, header.PayType));
                parameters.Add(Input("app_id", OracleDbType.Decimal, _options.ApplicationId));
                parameters.Add(BoundedVarchar2.Input("app_session_id", operatorContext.SessionId, BoundedVarchar2.SqlVarchar2Bytes, nameof(operatorContext)));
                parameters.Add(BoundedVarchar2.Input("app_user", operatorContext.UserName, BoundedVarchar2.SqlVarchar2Bytes, nameof(operatorContext)));
                parameters.Add(Input("invoice_date", OracleDbType.Date, header.DraftDate));
                parameters.Add(Input("approval_mode", OracleDbType.Decimal, approvalMode));
                parameters.Add(RequestRowIdArray(patServReqRowIds));
                parameters.Add(Input("req_row_count", OracleDbType.Decimal, patServReqRowIds.Count));
                parameters.Add(Input("max_output_lines", OracleDbType.Decimal, _options.MaxOutputLines));
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            parameters =>
            {
                RejectOverCapacity(parameters, "The request import returns", nameof(patServReqRowIds));
                return (OutputArrayReader.ReadEngineLines(parameters), OutputArrayReader.ReadImportResult(parameters));
            },
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
    /// <exception cref="ArgumentException"><paramref name="choice"/> is neither review nor consultation, <paramref name="session"/> is not an Oracle session of this layer, a value exceeds its destination width, or the import returns more lines than Invoicing:MaxOutputLines.</exception>
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
        BoundedVarchar2.Validate("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header));
        BoundedVarchar2.Validate("new_visit_type", visitType, BoundedVarchar2.NewVisitTypeBytes, nameof(choice));
        BoundedVarchar2.Validate("info_center_id", operatorContext.InfoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(operatorContext));

        return await Execute(
            session,
            PlsqlBlocks.VisitLine,
            GetVisitLineOperation,
            parameters =>
            {
                parameters.Add(BoundedVarchar2.Input("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header)));
                parameters.Add(Input("docid", OracleDbType.Decimal, header.DocId));
                parameters.Add(BoundedVarchar2.Input("new_visit_type", visitType, BoundedVarchar2.NewVisitTypeBytes, nameof(choice)));
                parameters.Add(Input("paytype", OracleDbType.Decimal, header.PayType));
                parameters.Add(Input("clinicid", OracleDbType.Decimal, header.ClinicId));
                parameters.Add(BoundedVarchar2.Input("info_center_id", operatorContext.InfoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(operatorContext)));
                parameters.Add(Input("invoice_date", OracleDbType.Date, header.DraftDate));
                parameters.Add(Input("max_output_lines", OracleDbType.Decimal, _options.MaxOutputLines));
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            parameters =>
            {
                RejectOverCapacity(parameters, "The visit line import returns", nameof(choice));
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

    /// <summary>Runs one block on the session's connection as a gateway call, tags a driver failure with its operation name, and reads the outputs.</summary>
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an <see cref="OracleSession"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back.</exception>
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

        oracleSession.BeginCall();

        using var command = new OracleCommand(block.Replace("\r\n", "\n", StringComparison.Ordinal), oracleSession.Connection)
        {
            Transaction = oracleSession.Transaction,
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
                oracleSession.RecordFailure(ex);
                throw;
            }

            var result = read(command.Parameters);
            oracleSession.EndCall();
            return result;
        }
        finally
        {
            foreach (OracleParameter parameter in command.Parameters)
            {
                parameter.Dispose();
            }
        }
    }

    /// <summary>Throws a capacity rejection when the <c>el_count</c> OUT value exceeds <see cref="InvoicingDataOptions.MaxOutputLines"/>; null counts as 0.</summary>
    /// <param name="parameters">Executed command parameters.</param>
    /// <param name="subject">Start of the rejection text, naming what returned the lines.</param>
    /// <param name="paramName">Argument the rejection names.</param>
    /// <exception cref="ArgumentException">The count exceeds the capacity; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    /// <exception cref="InvalidCastException">The count is neither a number nor null.</exception>
    private void RejectOverCapacity(OracleParameterCollection parameters, string subject, string paramName)
    {
        var count = parameters[EngineLineCountName].Value switch
        {
            null or DBNull or OracleDecimal { IsNull: true } => 0,
            OracleDecimal value => value.ToInt32(),
            decimal value => decimal.ToInt32(value),
            var other => throw new InvalidCastException($"OUT parameter '{EngineLineCountName}' holds a {other.GetType().Name}, not a number."),
        };

        if (count > _options.MaxOutputLines)
        {
            var text = $"{subject} {count} lines; at most {_options.MaxOutputLines} lines can be returned ({MaxOutputLinesKey}).";
            var error = new ArgumentException(text, paramName);
            error.Data[OracleFailureTranslator.BindingRejectionKey] = text;
            throw error;
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
