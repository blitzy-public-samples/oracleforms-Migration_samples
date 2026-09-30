using System.Data;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Plsql;

/// <summary>Calls the BIL_INVOICE_API preview, create, bundled-offer and package-line operations through the <see cref="PlsqlBlocks"/> anonymous blocks inside the caller's session. UNVERIFIED against Oracle.</summary>
public sealed class BilInvoiceApiGateway : IBilInvoiceApiGateway
{
    private const string PrintUrlUnavailableMessage =
        $"{OpenItemIds.OI11}: BIL_REPORTS_PRINT.BUILD_PRINT_URL is not in the ingested sources; the invoice print URL cannot be built.";

    private const string AutoYes = "Y";
    private const string AutoNo = "N";

    private readonly InvoicingDataOptions _options;

    /// <summary>Stores the data-layer settings; opens nothing.</summary>
    /// <param name="options">Command timeout and OUT-array capacity applied to every call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="InvoicingDataOptions.MaxOutputLines"/> is below 1 or <see cref="InvoicingDataOptions.CommandTimeoutSeconds"/> is negative.</exception>
    public BilInvoiceApiGateway(InvoicingDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxOutputLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxOutputLines, $"{nameof(InvoicingDataOptions.MaxOutputLines)} must be at least 1.");
        }

        if (options.CommandTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.CommandTimeoutSeconds, $"{nameof(InvoicingDataOptions.CommandTimeoutSeconds)} must not be negative.");
        }

        _options = options;
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
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an <see cref="OracleSession"/>, or a line cannot be bound.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)> CalculatePreview(
        IOracleSession session,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        bool amount1Auto,
        CancellationToken cancellationToken = default)
    {
        var oracleSession = AsOracleSession(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var inputs = DraftInputs(header, lines, operatorContext);
        inputs.Add(Number("amount_1", header.Amount1 ?? 0m));
        inputs.Add(Number("amount_2", header.Amount2 ?? 0m));
        inputs.Add(Text("amount_1_auto", amount1Auto ? AutoYes : AutoNo));

        using var command = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.Preview,
            inputs,
            parameters =>
            {
                OutputArrayReader.AddPreviewLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddPreviewTotalsOutputs(parameters);
            },
            nameof(CalculatePreview),
            cancellationToken).ConfigureAwait(false);

        return (OutputArrayReader.ReadPreviewLines(command.Parameters), OutputArrayReader.ReadPreviewTotals(command.Parameters));
    }

    /// <summary>Runs BIL_INVOICE_API.EXPAND_BUNDLED_OFFER_IG_LINES, then CREATE_FULL_INVOICE, for the draft and its request id.</summary>
    /// <param name="session">Open session whose transaction the call runs in; the caller commits or rolls it back.</param>
    /// <param name="header">Invoice header draft; its amounts and sub pay types are passed to the create call.</param>
    /// <param name="lines">Visible draft lines in bind order.</param>
    /// <param name="operatorContext">Operator bound into the header.</param>
    /// <param name="requestId">Idempotency request id of the draft, passed unchanged as <c>p_request_id</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The invoice result, posting flags and message as returned by the package.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is empty or whitespace, <paramref name="session"/> is not an <see cref="OracleSession"/>, or a line cannot be bound.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<FullInvoiceResultRow> CreateFullInvoice(
        IOracleSession session,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var oracleSession = AsOracleSession(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(operatorContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var inputs = DraftInputs(header, lines, operatorContext);
        inputs.Add(Number("amount_1", header.Amount1));
        inputs.Add(Number("amount_2", header.Amount2 ?? 0m));
        inputs.Add(Number("sub_paytype", header.SubPayType));
        inputs.Add(Number("sub_paytype2", header.SubPayType2));
        inputs.Add(Text("request_id", requestId));

        using var command = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.Create,
            inputs,
            OutputArrayReader.AddFullInvoiceResultOutputs,
            nameof(CreateFullInvoice),
            cancellationToken).ConfigureAwait(false);

        return OutputArrayReader.ReadFullInvoiceResult(command.Parameters);
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
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an <see cref="OracleSession"/>.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<IReadOnlyList<EditablePreviewLine>> GetBundledOfferLines(
        IOracleSession session,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        decimal offerId,
        decimal bundleQty,
        CancellationToken cancellationToken = default)
    {
        var oracleSession = AsOracleSession(session);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(operatorContext);

        OracleParameter[] inputs =
        [
            Text("patientno", header.PatientNo),
            Number("paytype", header.PayType),
            Date("invoice_date", header.DraftDate),
            Text("info_center_id", operatorContext.InfoCenterId),
            Number("offer_id", offerId),
            Number("bundle_qty", bundleQty),
        ];

        using var command = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.BundledOffer,
            inputs,
            parameters => OutputArrayReader.AddPreviewLineOutputs(parameters, _options.MaxOutputLines),
            nameof(GetBundledOfferLines),
            cancellationToken).ConfigureAwait(false);

        return OutputArrayReader.ReadPreviewLines(command.Parameters);
    }

    /// <summary>Runs BIL_INVOICE_API.GET_PACKAGE_LINES, then BIL_IMPORT.TO_ENGINE_LINES, for a package service on a price list.</summary>
    /// <param name="session">Open session whose transaction the call runs in.</param>
    /// <param name="packageServiceId">Package service id.</param>
    /// <param name="listId">Price list the caller resolved for the package.</param>
    /// <param name="parentSourceId">Source id of the parent line, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The package components as engine lines, with the import result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="packageServiceId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="session"/> is not an <see cref="OracleSession"/>.</exception>
    /// <exception cref="OracleException">The block failed; <see cref="Exception.Data"/> holds the operation name under <see cref="OracleErrorParser.OperationKey"/>.</exception>
    public async Task<(IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result)> GetPackageLines(
        IOracleSession session,
        string packageServiceId,
        decimal listId,
        string? parentSourceId,
        CancellationToken cancellationToken = default)
    {
        var oracleSession = AsOracleSession(session);
        ArgumentNullException.ThrowIfNull(packageServiceId);

        OracleParameter[] inputs =
        [
            Text("package_serviceid", packageServiceId),
            Number("list_id", listId),
            Text("parent_source_id", parentSourceId),
        ];

        using var command = await ExecuteAsync(
            oracleSession,
            PlsqlBlocks.PackageLines,
            inputs,
            parameters =>
            {
                OutputArrayReader.AddEngineLineOutputs(parameters, _options.MaxOutputLines);
                OutputArrayReader.AddImportResultOutputs(parameters);
            },
            nameof(GetPackageLines),
            cancellationToken).ConfigureAwait(false);

        return (OutputArrayReader.ReadEngineLines(command.Parameters), OutputArrayReader.ReadImportResult(command.Parameters));
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

    /// <summary>Header, line and client-id inputs shared by the preview and create blocks.</summary>
    private static List<OracleParameter> DraftInputs(InvoiceHeaderDraft header, IReadOnlyList<InvoiceLineDraft> lines, OperatorContext operatorContext)
    {
        var inputs = new List<OracleParameter>(HeaderInputBinder.Bind(header, operatorContext));
        inputs.AddRange(LineInputBinder.Bind(lines));
        inputs.Add(ClientIdBinder.Bind(lines));
        return inputs;
    }

    /// <summary>Runs a block on the session's connection with the given inputs and outputs; the caller reads the OUT values and disposes the command.</summary>
    /// <param name="session">Session whose connection and active transaction the block runs in.</param>
    /// <param name="block">Anonymous PL/SQL block text.</param>
    /// <param name="inputs">IN parameters, each bound by name.</param>
    /// <param name="addOutputs">Adds the OUT parameters to the command's collection.</param>
    /// <param name="operation">Public method name stored on a failing <see cref="OracleException"/>.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The executed command holding the OUT values.</returns>
    private async Task<OracleCommand> ExecuteAsync(
        OracleSession session,
        string block,
        IEnumerable<OracleParameter> inputs,
        Action<OracleParameterCollection> addOutputs,
        string operation,
        CancellationToken cancellationToken)
    {
        var command = new OracleCommand(block.Replace("\r\n", "\n", StringComparison.Ordinal), session.Connection)
        {
            CommandType = CommandType.Text,
            BindByName = true,
            CommandTimeout = _options.CommandTimeoutSeconds,
        };

        var executed = false;
        try
        {
            foreach (var parameter in inputs)
            {
                command.Parameters.Add(parameter);
            }

            addOutputs(command.Parameters);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            executed = true;
            return command;
        }
        catch (OracleException exception)
        {
            exception.Data[OracleErrorParser.OperationKey] = operation;
            throw;
        }
        finally
        {
            if (!executed)
            {
                command.Dispose();
            }
        }
    }

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, decimal? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Numeric input bound as <see cref="OracleDbType.Decimal"/>.</summary>
    private static OracleParameter Number(string name, int? value) => Input(name, OracleDbType.Decimal, value);

    /// <summary>Text input bound as <see cref="OracleDbType.Varchar2"/>.</summary>
    private static OracleParameter Text(string name, string? value) => Input(name, OracleDbType.Varchar2, value);

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
