using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Calls the BIL_INVOICE_API preview, create, bundled-offer and package-line operations through the <see cref="PlsqlBlocks"/> anonymous blocks inside the caller's session. UNVERIFIED against Oracle.</summary>
public sealed class BilInvoiceApiGateway : IBilInvoiceApiGateway
{
    private const string PrintUrlUnavailableMessage =
        $"{OpenItemIds.OI11}: BIL_REPORTS_PRINT.BUILD_PRINT_URL is not in the ingested sources; the invoice print URL cannot be built.";

    private const string AutoYes = "Y";
    private const string AutoNo = "N";

    private const string MaxOutputLinesKey = "Invoicing:MaxOutputLines";

    private const string PreviewLineCountName = "pl_count";
    private const string EngineLineCountName = "el_count";

    private const int BundledOfferType = 0;

    private const string DraftSealKeyKey = "Invoicing:DraftSealKey";

    /// <summary>Fewest bytes a configured <see cref="InvoicingDataOptions.DraftSealKey"/> decodes to.</summary>
    public const int MinDraftSealKeyBytes = 32;

    private readonly InvoicingDataOptions _options;

    private readonly byte[] _draftSealKey;

    /// <summary>Stores the data-layer settings and the draft-seal key; opens nothing.</summary>
    /// <param name="options">Command timeout, OUT-array capacity and draft-seal key applied to every call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.MaxOutputLines"/> is below 1, or <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is below 1 or above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="InvoicingDataOptions.DraftSealKey"/> is set but is not base64 of at least <see cref="MinDraftSealKeyBytes"/> bytes.</exception>
    public BilInvoiceApiGateway(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxOutputLines < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxOutputLines,
                $"{MaxOutputLinesKey} ({nameof(InvoicingDataOptions)}.{nameof(InvoicingDataOptions.MaxOutputLines)}) must be at least 1.");
        }

        options.EnsureCommandTimeout(nameof(options));

        _options = options;
        _draftSealKey = DraftSealKeyFrom(options.DraftSealKey, nameof(options));
    }

    /// <summary>Most engine lines, rebuilt bundle parents included, that a create accepts and a preview returns: <see cref="InvoicingDataOptions.MaxOutputLines"/>.</summary>
    public int MaxDraftLines => _options.MaxOutputLines;

    /// <summary>Returns the upper-case hexadecimal HMAC-SHA256 of the request id and the draft date's ticks under the draft-seal key.</summary>
    /// <param name="requestId">Request id issued with the draft.</param>
    /// <param name="draftDate">Draft date issued with the draft.</param>
    /// <returns>A 64-character seal, equal on every gateway configured with the same key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestId"/> is null.</exception>
    public string SealDraftDate(string requestId, DateTime draftDate)
    {
        ArgumentNullException.ThrowIfNull(requestId);

        return Convert.ToHexString(HMACSHA256.HashData(
            _draftSealKey,
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{requestId}|{draftDate.Ticks}"))));
    }

    /// <summary>Runs BIL_INVOICE_API.EXPAND_BUNDLED_OFFER_IG_LINES, then CALCULATE_EDITABLE_INVOICE_PREVIEW, for the draft.</summary>
    /// <param name="session">Open session whose transaction the call runs in.</param>
    /// <param name="header">Invoice header draft.</param>
    /// <param name="lines">Visible draft lines in bind order.</param>
    /// <param name="operatorContext">Operator bound into the header.</param>
    /// <param name="amount1Auto">Whether the package fills amount 1 automatically.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The preview lines and totals as returned by the package.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The draft exceeds Invoicing:MaxOutputLines, <paramref name="session"/> is not an <see cref="OracleSession"/>, or a header or line value cannot be bound.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)> CalculatePreview(
        IOracleSession session,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        bool amount1Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(operatorContext);
        if (lines.Count > _options.MaxOutputLines)
        {
            throw CapacityRejection(
                $"The draft has {lines.Count} lines; a preview returns at most {_options.MaxOutputLines} lines ({MaxOutputLinesKey}).",
                nameof(lines));
        }

        var oracleSession = AsOracleSession(session);

        var preview = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.Preview,
            inputs =>
            {
                AddDraftInputs(inputs, header, lines, operatorContext);
                inputs.Add(Number("amount_1", header.Amount1 ?? 0m));
                inputs.Add(Number("amount_2", header.Amount2 ?? 0m));
                inputs.Add(BoundedVarchar2.Input("amount_1_auto", amount1Auto ? AutoYes : AutoNo, BoundedVarchar2.FlagBytes, nameof(amount1Auto)));
                inputs.Add(Number("max_output_lines", _options.MaxOutputLines));
            },
            parameters =>
            {
                OutputArrayReader.AddPreviewLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddPreviewTotalsOutputs(parameters);
            },
            parameters =>
            {
                var count = OutCount(parameters, PreviewLineCountName);
                if (count > _options.MaxOutputLines)
                {
                    throw CapacityRejection(
                        $"The draft expands to {count} preview lines; a preview returns at most {_options.MaxOutputLines} lines ({MaxOutputLinesKey}).",
                        nameof(lines));
                }

                return (OutputArrayReader.ReadPreviewLines(parameters), OutputArrayReader.ReadPreviewTotals(parameters));
            },
            nameof(CalculatePreview),
            cancellationToken).ConfigureAwait(false);

        oracleSession.EndCall();
        return preview;
    }

    /// <summary>Runs BIL_INVOICE_API.EXPAND_BUNDLED_OFFER_IG_LINES, then CREATE_FULL_INVOICE, for the draft and its request id; with no lines CREATE_FULL_INVOICE runs without expansion, so a recorded request id returns its existing invoice.</summary>
    /// <param name="session">Open session whose transaction the call runs in; the caller commits or rolls it back.</param>
    /// <param name="header">Invoice header draft; its amounts and sub pay types are passed to the create call.</param>
    /// <param name="lines">Visible draft lines in bind order; when empty, expansion is skipped.</param>
    /// <param name="operatorContext">Operator bound into the header.</param>
    /// <param name="requestId">Idempotency request id of the draft, passed unchanged as <c>p_request_id</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The invoice result, posting flags and message as returned by the package.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is empty or whitespace, a value exceeds its destination width, the lines with their rebuilt bundle parents exceed Invoicing:MaxOutputLines, <paramref name="session"/> is not an <see cref="OracleSession"/>, or a header or line value cannot be bound.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<FullInvoiceResultRow> CreateFullInvoice(
        IOracleSession session,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(operatorContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        BoundedVarchar2.Validate("request_id", requestId, BoundedVarchar2.RequestIdBytes, nameof(requestId));
        var engineLines = EngineLineCount(lines);
        if (engineLines > _options.MaxOutputLines)
        {
            throw CapacityRejection(
                $"The draft expands to {engineLines} invoice lines; an invoice can be created with at most {_options.MaxOutputLines} lines ({MaxOutputLinesKey}).",
                nameof(lines));
        }

        var oracleSession = AsOracleSession(session);

        var result = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.Create,
            inputs =>
            {
                AddDraftInputs(inputs, header, lines, operatorContext);
                inputs.Add(Number("amount_1", header.Amount1));
                inputs.Add(Number("amount_2", header.Amount2 ?? 0m));
                inputs.Add(Number("sub_paytype", header.SubPayType));
                inputs.Add(Number("sub_paytype2", header.SubPayType2));
                inputs.Add(BoundedVarchar2.Input("request_id", requestId, BoundedVarchar2.RequestIdBytes, nameof(requestId)));
            },
            OutputArrayReader.AddFullInvoiceResultOutputs,
            OutputArrayReader.ReadFullInvoiceResult,
            nameof(CreateFullInvoice),
            cancellationToken).ConfigureAwait(false);

        oracleSession.EndCall();
        return result;
    }

    /// <summary>Runs BIL_INVOICE_API.GET_BUNDLED_OFFER_IG_LINES for the draft's patient, pay type and date.</summary>
    /// <param name="session">Open session whose transaction the call runs in.</param>
    /// <param name="header">Invoice header draft supplying the patient, pay type and draft date.</param>
    /// <param name="operatorContext">Operator supplying the information centre.</param>
    /// <param name="offerId">Bundled offer id chosen in the OFFERS LOV.</param>
    /// <param name="bundleQty">Number of bundles.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The offer's component lines as preview lines.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A value exceeds its destination width, <paramref name="session"/> is not an <see cref="OracleSession"/>, or the package returns more lines than Invoicing:MaxOutputLines.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<IReadOnlyList<EditablePreviewLine>> GetBundledOfferLines(
        IOracleSession session,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        decimal offerId,
        decimal bundleQty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);
        BoundedVarchar2.Validate("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header));
        BoundedVarchar2.Validate("info_center_id", operatorContext.InfoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(operatorContext));
        var oracleSession = AsOracleSession(session);

        var offerLines = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.BundledOffer,
            inputs =>
            {
                inputs.Add(BoundedVarchar2.Input("patientno", header.PatientNo, BoundedVarchar2.PatientNoBytes, nameof(header)));
                inputs.Add(Number("paytype", header.PayType));
                inputs.Add(Date("invoice_date", header.DraftDate));
                inputs.Add(BoundedVarchar2.Input("info_center_id", operatorContext.InfoCenterId, BoundedVarchar2.InfoCenterIdBytes, nameof(operatorContext)));
                inputs.Add(Number("offer_id", offerId));
                inputs.Add(Number("bundle_qty", bundleQty));
                inputs.Add(Number("max_output_lines", _options.MaxOutputLines));
            },
            parameters => OutputArrayReader.AddPreviewLineOutputs(parameters, _options.MaxOutputLines),
            parameters =>
            {
                RejectOverCapacity(parameters, PreviewLineCountName, "The bundled offer expands to", nameof(offerId));
                return OutputArrayReader.ReadPreviewLines(parameters);
            },
            nameof(GetBundledOfferLines),
            cancellationToken).ConfigureAwait(false);

        oracleSession.EndCall();
        return offerLines;
    }

    /// <summary>Runs BIL_INVOICE_API.GET_PACKAGE_LINES, then BIL_IMPORT.TO_ENGINE_LINES, for a package service on a price list.</summary>
    /// <param name="session">Open session whose transaction the call runs in.</param>
    /// <param name="packageServiceId">Package service id.</param>
    /// <param name="listId">Price list the caller resolved for the package.</param>
    /// <param name="parentSourceId">Source id of the parent line, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The package components as engine lines, with the import result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="packageServiceId"/> is null.</exception>
    /// <exception cref="ArgumentException">A value exceeds its destination width, <paramref name="session"/> is not an <see cref="OracleSession"/>, or the package returns more lines than Invoicing:MaxOutputLines.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> GetPackageLines(
        IOracleSession session,
        string packageServiceId,
        decimal listId,
        string? parentSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(packageServiceId);
        BoundedVarchar2.Validate("package_serviceid", packageServiceId, BoundedVarchar2.ServiceIdBytes, nameof(packageServiceId));
        BoundedVarchar2.Validate("parent_source_id", parentSourceId, BoundedVarchar2.SourceIdBytes, nameof(parentSourceId));
        var oracleSession = AsOracleSession(session);

        var package = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.PackageLines,
            inputs =>
            {
                inputs.Add(BoundedVarchar2.Input("package_serviceid", packageServiceId, BoundedVarchar2.ServiceIdBytes, nameof(packageServiceId)));
                inputs.Add(Number("list_id", listId));
                inputs.Add(BoundedVarchar2.Input("parent_source_id", parentSourceId, BoundedVarchar2.SourceIdBytes, nameof(parentSourceId)));
                inputs.Add(Number("max_output_lines", _options.MaxOutputLines));
            },
            parameters =>
            {
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            parameters =>
            {
                RejectOverCapacity(parameters, EngineLineCountName, "The package expands to", nameof(packageServiceId));
                return (OutputArrayReader.ReadEngineLines(parameters), OutputArrayReader.ReadImportResult(parameters));
            },
            nameof(GetPackageLines),
            cancellationToken).ConfigureAwait(false);

        oracleSession.EndCall();
        return package;
    }

    /// <summary>Stands in for BIL_INVOICE_API.BUILD_PRINT_URL; always throws without Oracle access.</summary>
    /// <param name="invNo">Saved invoice number.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotImplementedException">Always, with a message starting with the print open-item id.</exception>
    public string BuildPrintUrl(long invNo) => throw new NotImplementedException(PrintUrlUnavailableMessage);

    /// <summary>Returns the session as an <see cref="OracleSession"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="session"/> is another implementation.</exception>
    private static OracleSession AsOracleSession(IOracleSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session as OracleSession
            ?? throw new ArgumentException($"Expected an {nameof(OracleSession)}, got {session.GetType().Name}.", nameof(session));
    }

    /// <summary>Builds a capacity rejection carrying its text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</summary>
    /// <param name="text">Operator-facing rejection text.</param>
    /// <param name="paramName">Argument the rejection names.</param>
    /// <returns>The rejection to throw.</returns>
    private static ArgumentException CapacityRejection(string text, string paramName)
    {
        var error = new ArgumentException(text, paramName);
        error.Data[OracleFailureTranslator.BindingRejectionKey] = text;
        return error;
    }

    /// <summary>Decodes the configured draft-seal key, or returns a random key of this instance when none is configured.</summary>
    /// <exception cref="ArgumentException">The configured key is not base64 of at least <see cref="MinDraftSealKeyBytes"/> bytes.</exception>
    private static byte[] DraftSealKeyFrom(string? configuredKey, string paramName)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return RandomNumberGenerator.GetBytes(MinDraftSealKeyBytes);
        }

        var key = new byte[configuredKey.Length];
        if (!Convert.TryFromBase64String(configuredKey.Trim(), key, out var length) || length < MinDraftSealKeyBytes)
        {
            throw new ArgumentException(
                $"{DraftSealKeyKey} ({nameof(InvoicingDataOptions)}.{nameof(InvoicingDataOptions.DraftSealKey)}) must be base64 of at least {MinDraftSealKeyBytes} bytes.",
                paramName);
        }

        return key[..length];
    }

    /// <summary>Lines EXPAND_BUNDLED_OFFER_IG_LINES emits: every line plus one parent per distinct space-trimmed offer instance id of the bundled-offer lines.</summary>
    private static int EngineLineCount(IReadOnlyList<InvoiceLineDraft?> lines) =>
        lines.Count
        + lines
            .Select(line => line is { OfferType: BundledOfferType } ? line.OfferInstanceId?.Trim(' ') : null)
            .Where(instanceId => !string.IsNullOrEmpty(instanceId))
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>Reads an OUT line count; null counts as 0.</summary>
    /// <exception cref="InvalidCastException">The value is neither a number nor null.</exception>
    /// <exception cref="OverflowException">The value exceeds the Int32 range.</exception>
    private static int OutCount(OracleParameterCollection parameters, string countName) =>
        parameters[countName].Value switch
        {
            null or DBNull or OracleDecimal { IsNull: true } => 0,
            OracleDecimal count => count.ToInt32(),
            decimal count => decimal.ToInt32(count),
            var other => throw new InvalidCastException($"OUT parameter '{countName}' holds a {other.GetType().Name}, not a number."),
        };

    /// <summary>Throws a capacity rejection when the OUT line count exceeds <see cref="InvoicingDataOptions.MaxOutputLines"/>.</summary>
    /// <param name="parameters">Executed command parameters.</param>
    /// <param name="countName">Name of the OUT count parameter.</param>
    /// <param name="subject">Start of the rejection text, naming what returned the lines.</param>
    /// <param name="paramName">Argument the rejection names.</param>
    /// <exception cref="ArgumentException">The count exceeds the capacity; <see cref="Exception.Data"/> holds the text under <see cref="OracleFailureTranslator.BindingRejectionKey"/>.</exception>
    private void RejectOverCapacity(OracleParameterCollection parameters, string countName, string subject, string paramName)
    {
        var count = OutCount(parameters, countName);
        if (count > _options.MaxOutputLines)
        {
            throw CapacityRejection(
                $"{subject} {count} lines; at most {_options.MaxOutputLines} lines can be returned ({MaxOutputLinesKey}).",
                paramName);
        }
    }

    /// <summary>Adds the header, line and client-id inputs shared by the preview and create blocks to the list.</summary>
    private static void AddDraftInputs(List<OracleParameter> inputs, InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext)
    {
        inputs.AddRange(HeaderInputBinder.Bind(header, operatorContext));
        inputs.AddRange(LineInputBinder.Bind(lines));
        inputs.Add(ClientIdBinder.Bind(lines));
    }

    /// <summary>Builds the inputs, runs a block on the session's connection as a gateway call, tags a driver failure with its operation name, reads the outputs, and disposes every parameter and the command.</summary>
    /// <param name="session">Session whose connection and active transaction the block runs in.</param>
    /// <param name="block">Anonymous PL/SQL block text.</param>
    /// <param name="addInputs">Adds the IN parameters, each bound by name, to the list.</param>
    /// <param name="addOutputs">Adds the OUT parameters to the command's collection.</param>
    /// <param name="read">Reads the result from the executed command's parameters.</param>
    /// <param name="operation">Public method name stored on a failing <see cref="OracleException"/>.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The value <paramref name="read"/> returns.</returns>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="InvalidOperationException">The session is already committed or rolled back.</exception>
    private async Task<T> ExecuteAsync<T>(
        OracleSession session,
        string block,
        Action<List<OracleParameter>> addInputs,
        Action<OracleParameterCollection> addOutputs,
        Func<OracleParameterCollection, T> read,
        string operation,
        CancellationToken cancellationToken)
    {
        var inputs = new List<OracleParameter>();
        OracleCommand? command = null;
        try
        {
            addInputs(inputs);
            session.BeginCall();

            command = new OracleCommand(block.Replace("\r\n", "\n", StringComparison.Ordinal), session.Connection)
            {
                Transaction = session.Transaction,
                CommandType = CommandType.Text,
                BindByName = true,
                CommandTimeout = _options.CommandTimeoutSeconds,
            };

            foreach (var parameter in inputs)
            {
                command.Parameters.Add(parameter);
            }

            inputs.Clear();
            addOutputs(command.Parameters);

            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OracleException exception)
            {
                exception.Data[OracleErrorParser.OperationKey] = operation;
                session.RecordFailure(exception);
                throw;
            }

            return read(command.Parameters);
        }
        finally
        {
            if (command is not null)
            {
                foreach (OracleParameter parameter in command.Parameters)
                {
                    parameter.Dispose();
                }
            }

            foreach (var parameter in inputs)
            {
                if (command is null || !command.Parameters.Contains(parameter))
                {
                    parameter.Dispose();
                }
            }

            command?.Dispose();
        }
    }

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, decimal? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, int? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Date input bound as <see cref="OracleDbType.Date"/>.</summary>
    private static OracleParameter Date(string name, DateTime value) => Input(name, OracleDbType.Date, value);

    /// <summary>Creates one IN parameter of the given type, sending a null value as <see cref="DBNull.Value"/>.</summary>
    private static OracleParameter Input(string name, OracleDbType dbType, object? value) =>
        new(name, dbType)
        {
            Direction = ParameterDirection.Input,
            Value = value ?? DBNull.Value,
        };
}
