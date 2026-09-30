using System.Globalization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;
using Billing.Invoicing.Domain.Rules;
using Billing.Invoicing.Domain.Workflow;

namespace Billing.Invoicing.Api.Services;

/// <summary>Sequences the Domain rules and the Data ports behind each invoicing endpoint.</summary>
public sealed class InvoiceWorkflowService
{
    private const int CashPayType = 1;
    private const int CreditPayType = 2;
    private const int ApprovalPreference = 422;
    private const int EnforcedApprovalCheck = 1;
    private const int DirectCompany = 1;
    private const int BundledOfferType = 0;
    private const string PriceNotFixed = "N";
    private const string CashCompanyCode = "0";
    private const string ReviewedClaimFlag = "R";
    private const string NoClaimParameter = "0";
    private const string NewConsultationClaimParameter = "1";
    private const string IndependentServiceClaimParameter = "2";
    private const string ParentRole = "PARENT";
    private const string ComponentRole = "COMPONENT";
    private const string ReceptionTransferSavepoint = "dr21";
    private const string ReceptionTransferRuleId = "DR-21";
    private const string ReceptionTransferWarning = "Reception transfer fields were not cleared.";
    private const string FixedPriceFormat = "Price cannot be changed for service {0}.";

    private const string PayTypeItem = "PAYTYPE";
    private const string SubPayTypeItem = "SUB_PAYTYPE";
    private const string DocIdItem = "DOCIDX";
    private const string ClaimNoItem = "CLAIM_NO";
    private const string AddToListItem = "ADD_TO_LIST";
    private const string Amount2Item = "AMOUNT_2";
    private const string RefundItem = "REUND";
    private const string FinalDiscPercItem = "FINALDISC_PERC";
    private const string FinalDiscItem = "FINALDISC";
    private const string DiscTItem = "DISC_T";
    private const string CompCodeItem = "COMP_CODE";
    private const string SubCompCodeItem = "SUB_COMP_CODE";
    private const string PatientNoItem = "PATIENTNO";
    private const string InvDateItem = "INVDATE";
    private const string ServiceIdItem = "SERVICEID";
    private const string VisitUniqueItem = "VISIT_UNIQUE";
    private const string TargetField = "TARGET";
    private const string LineField = "LINE";
    private const string KindField = "KIND";

    private const string LineIndexText = "LineIndex must address a line of the draft.";
    private const string NullLineText = "Draft lines must not contain null entries.";
    private const string PackageServiceRequiredText = "Package service id is required.";
    private const string DocumentKindText = "Document kind must be invoice, patient-card, barcode-sms or iqama-check.";
    private const string VisitUniqueRequiredText = "Request import failed: visit unique is required.";

    private const string RequestSourceType = "REQUEST";
    private const string NoFlag = "N";
    private const string EmptyRequestImportMessage = "Invoice request import completed. Expanded 0 request rows into 0 invoice lines.";
    private const string Amount1Item = "AMOUNT_1";
    private const string ClientIdItem = "CLIENT_ID";
    private const string RequestIdItem = "REQUEST_ID";
    private const int RequestIdLength = 32;
    private const string RequestIdText = "Request id must be 32 upper-case hexadecimal characters.";
    private const string PriceItem = "PRICE";

    private const string InvoiceDocument = "invoice";
    private const string PatientCardDocument = "patient-card";
    private const string BarcodeSmsDocument = "barcode-sms";
    private const string IqamaCheckDocument = "iqama-check";

    private const string PackageConsumptionTitle = "Package consumption mode is not available in this build.";
    private const string UntracedRuleTitle = "This service needs a rule that is not available in this build.";
    private const string UncarriedValueTitle = "This value cannot be saved in this build.";
    private const string SavedInvoiceEditTitle = "Editing a saved invoice is not available in this build.";
    private const string PricePlanTitle = "Price plan resolution is not available in this build.";
    private const string PaidBeforeTitle = "Prior claim payments are not available in this build.";
    private const string UnavailableTitle = "This operation is not available in this build.";

    private static readonly IReadOnlySet<int> NoExcludedLines = new HashSet<int>();
    private static readonly IReadOnlyDictionary<decimal, IReadOnlyList<string>> NoRequestedServices =
        new Dictionary<decimal, IReadOnlyList<string>>();

    private readonly IOracleSessionFactory _sessionFactory;
    private readonly ILookupQueries _lookups;
    private readonly IInvoiceQueries _invoices;
    private readonly ILovQueries _lovs;
    private readonly IBilInvoiceApiGateway _invoiceApi;
    private readonly IBilImportGateway _import;
    private readonly IPatientTransferCommand _patientTransfer;
    private readonly ILegacyExternalCalls _legacy;
    private readonly IPackageConsumptionGateway _packageConsumption;

    /// <summary>Creates the service over the Data ports it sequences.</summary>
    public InvoiceWorkflowService(
        IOracleSessionFactory sessionFactory,
        ILookupQueries lookups,
        IInvoiceQueries invoices,
        ILovQueries lovs,
        IBilInvoiceApiGateway invoiceApi,
        IBilImportGateway import,
        IPatientTransferCommand patientTransfer,
        ILegacyExternalCalls legacy,
        IPackageConsumptionGateway packageConsumption)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(lovs);
        ArgumentNullException.ThrowIfNull(invoiceApi);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(patientTransfer);
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(packageConsumption);

        _sessionFactory = sessionFactory;
        _lookups = lookups;
        _invoices = invoices;
        _lovs = lovs;
        _invoiceApi = invoiceApi;
        _import = import;
        _patientTransfer = patientTransfer;
        _legacy = legacy;
        _packageConsumption = packageConsumption;
    }

    /// <summary>Returns a new draft carrying the database date, the entry defaults and a new request id.</summary>
    /// <param name="parameters">Entry parameters set by the calling module.</param>
    /// <param name="operatorContext">Operator identity written into the draft header.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The new draft with no lines.</returns>
    public async Task<NewDraftResponse> NewDraft(
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (parameters.PkgInv is not null)
        {
            await _packageConsumption.Begin(parameters, cancellationToken);
        }

        var databaseTime = await _lookups.GetDatabaseTime(cancellationToken);
        var preload = await ReadClaimPreload(parameters, cancellationToken);
        int? visitDoctor = IsBlank(parameters.VisitUnique)
            ? null
            : await _lookups.GetVisitDoctor(parameters.VisitUnique!, cancellationToken);

        var header = WithOperator(
            InvoiceDefaultsRule.Apply(parameters, databaseTime, preload?.Header, visitDoctor),
            operatorContext);
        header = header with { PayType = await DecidePayType(header.CompCode, null, parameters, preload, cancellationToken) };

        if (ClaimNumberFromParameter(parameters))
        {
            header = header with { ClaimNo = ClaimNumberRule.Build(header, parameters) };
        }

        return new NewDraftResponse
        {
            Draft = new DraftDto
            {
                RequestId = NewId(),
                DraftDate = databaseTime,
                Header = header,
                Lines = Array.Empty<InvoiceLineDraft>(),
                Parameters = parameters,
                DiscountLimitChoice = null,
            },
        };
    }

    /// <summary>Runs the item, record or line checks of the validated target and returns their messages and adjusted values.</summary>
    /// <param name="request">Draft, target item and, for line targets, the line index.</param>
    /// <param name="operatorContext">Operator identity used for the draft header and the discount limit.</param>
    /// <param name="cancellationToken">Cancels the reads and previews.</param>
    /// <returns>Blocking and warning messages, adjusted values, advisory open items and, for DOCIDX, the visit-line choice.</returns>
    public async Task<ValidateDraftResponse> Validate(
        ValidateDraftRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var draft = Sanitize(request.Draft, operatorContext);
        var target = (request.Target ?? string.Empty).Trim().ToUpperInvariant();

        return target switch
        {
            "PATIENTNO" => await ValidatePatient(draft, cancellationToken),
            "COMP_CODE" => await ValidateCompany(draft, cancellationToken),
            "DOCIDX" => await ValidateDoctor(draft, operatorContext, cancellationToken),
            "CLINICID" => await ValidateClinic(draft, cancellationToken),
            "DEPT_WISE" or "CALL" => await ValidateErFlags(draft, cancellationToken),
            "FINALDISC_PERC" or "FINALDISC" => await ValidateFinalDiscount(draft, operatorContext, cancellationToken),
            "AMOUNT_1" => await ValidateFirstAmount(draft, operatorContext, cancellationToken),
            "AMOUNT_2" => ValidateSecondAmount(draft),
            "SUB_PAYTYPE" or "RECORD" => await ValidateRecord(draft, cancellationToken),
            "LINE" or "SERVICEID" or "QTY" or "LDISCT" or "APPROV_REF_NO" or "PRICE" or "DISC" or "MY_DISC" =>
                await ValidateLine(draft, request.LineIndex, operatorContext, cancellationToken),
            _ => RejectedInput(TargetField, $"Unknown validation target '{request.Target}'."),
        };
    }

    /// <summary>Returns the patient's coverage snapshot with its eligibility messages, pay type and advisory open items.</summary>
    /// <param name="patientNo">Patient number to read.</param>
    /// <param name="draftDate">Draft date the coverage is checked against; the database time when null.</param>
    /// <param name="parameters">Entry parameters of the draft.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The coverage response, or the blocking DR-01 patient message when the patient number is blank.</returns>
    public async Task<CoverageResponse> GetCoverage(
        string patientNo,
        DateTime? draftDate,
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (IsBlank(patientNo))
        {
            return new CoverageResponse { Messages = PatientRequired() };
        }

        var coverage = await _lookups.GetPatientCoverage(patientNo, cancellationToken);
        var asOf = draftDate ?? await _lookups.GetDatabaseTime(cancellationToken);
        var findings = new Findings();
        findings.Add(PatientEligibilityRules.Evaluate(coverage, parameters, asOf));

        var preload = await ReadPatientClaimPreload(parameters, patientNo, cancellationToken);
        var payType = await DecidePayType(coverage?.CompCode, coverage, parameters, preload, cancellationToken);
        var (subCompCode, classCode) = ServerSubCompanyAndClass(payType, coverage, preload);

        findings.AddOpenItem(OpenItemIds.OI24);
        if (!IsBlank(subCompCode))
        {
            findings.AddOpenItem(OpenItemIds.OI21);
        }

        var header = new InvoiceHeaderDraft
        {
            PatientNo = patientNo,
            PayType = payType,
            CompCode = coverage?.CompCode,
            SubCompCode = subCompCode,
            ClassCode = classCode,
            DraftDate = asOf,
        };
        var gate = await ReadGateInputs(header, coverage, preload, readCard: false, cancellationToken);
        if (OpenItemGate.Evaluate(header, Array.Empty<ServiceProfile>(), null, gate.MaxDeductable, gate.UseAdvanced, parameters)
            .Contains(OpenItemIds.OI23, StringComparer.Ordinal))
        {
            findings.AddOpenItem(OpenItemIds.OI23);
        }

        return new CoverageResponse
        {
            Coverage = coverage,
            PayType = payType,
            Messages = findings.Messages,
            OpenItems = findings.OpenItems,
        };
    }

    /// <summary>Returns the package preview of the draft with the refund and the total collected.</summary>
    /// <param name="draft">Draft to preview; nothing is saved.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads and the preview.</param>
    /// <returns>Preview lines and totals as the package returned them, plus advisory open items.</returns>
    public async Task<PreviewResponse> Preview(
        DraftDto draft,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var sanitized = Sanitize(draft, operatorContext);
        var clientIdMessages = ClientIdMessages(sanitized.Lines);
        if (clientIdMessages.Count > 0)
        {
            return new PreviewResponse { Messages = clientIdMessages };
        }

        var parameters = sanitized.Parameters;
        var context = await ReadServerContext(sanitized, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        var gate = await ReadGateInputs(header, context.Coverage, context.Preload, readCard: true, cancellationToken);

        var headerIds = OpenItemGate.Evaluate(
            header, Array.Empty<ServiceProfile>(), gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters);
        if (headerIds.Contains(OpenItemIds.OI23, StringComparer.Ordinal))
        {
            await ThrowGate(headerIds, Array.Empty<MessageDto>(), header, cancellationToken);
        }

        var preview = await CalculateVettedPreview(header, sanitized.Lines, operatorContext, cancellationToken);

        var lists = await RequireLists(
            sanitized.Lines, LineLists(sanitized.Lines, preview, context.Preload), header, operatorContext, cancellationToken);
        var profiles = await BuildProfiles(sanitized.Lines, lists, cancellationToken);
        var findings = new Findings();
        await AddPriceOverrideRules(findings, header, sanitized.Lines, profiles.ByLine, preview, cancellationToken);
        var openItems = OpenItemGate.Evaluate(
            header,
            profiles.TopLevel,
            gate.CardId,
            gate.MaxDeductable,
            gate.UseAdvanced,
            parameters);

        var refund = PaymentAllocationRules.Refund(new PaymentAllocation
        {
            Amount1 = preview.Totals.Amount1 ?? header.Amount1,
            Amount2 = preview.Totals.Amount2 ?? header.Amount2,
            CashPayed = header.CashPayed,
            SubPayType = header.SubPayType,
            SubPayType2 = header.SubPayType2,
        });

        return new PreviewResponse
        {
            Lines = preview.Lines,
            Totals = ToTotals(preview.Totals),
            Refund = refund,
            TotalCollected = PaymentAllocationRules.TotalCollected(preview.Totals.Amount1, preview.Totals.Amount2),
            Messages = findings.Messages,
            OpenItems = openItems,
        };
    }

    /// <summary>Saves the draft as an invoice after re-running every check server-side, or replays an earlier create of the same request id.</summary>
    /// <param name="request">Draft to save, carrying its request id and discount-limit choice.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads and the save.</param>
    /// <returns>The saved invoice with its posting flags, warnings and open items, or the blocking messages when the draft cannot be saved.</returns>
    public async Task<CreateInvoiceResponse> Create(
        CreateInvoiceRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (!HasWellFormedRequestId(request.Draft))
        {
            return RequestIdRejected();
        }

        if (await _invoices.GetCreateRequest(request.Draft.RequestId, cancellationToken) is not null)
        {
            return await Replay(
                request.Draft.RequestId, request.Draft.Header?.PatientNo, request.Draft.DraftDate, operatorContext, cancellationToken);
        }

        var draft = Sanitize(request.Draft, operatorContext);

        if (!InvoiceStatePolicy.CanEdit(draft.Header.InvNo))
        {
            throw OpenItem(OpenItemIds.OI56, SavedInvoiceEditTitle);
        }

        var parameters = draft.Parameters;
        var lines = draft.Lines;
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var gate = await ReadGateInputs(context.Header, context.Coverage, context.Preload, readCard: true, cancellationToken);
        var clinic = await ReadClinic(context.Header, cancellationToken);
        var maxDisc = await _lookups.GetUserMaxDiscount(operatorContext.UserNo, cancellationToken);
        var x422 = await ReadX422(cancellationToken);

        var findings = new Findings();
        var header = await ApplyVisitDoctor(context.Header, parameters, cancellationToken);
        header = header with { ClaimNo = ClaimNumberRule.Build(header, parameters) };
        header = ApplySubPayType(header, findings.Add(HeaderRecordRules.ApplyPaymentTypeDefault(header)));
        header = ApplyDoctor(header, findings.Add(DoctorSelectionRules.Validate(header, parameters)));

        findings.Add(HeaderRecordRules.ValidateRecord(header));
        findings.Add(InvoiceDetailRules.RequireDetails(lines.Count));
        findings.Add(PatientEligibilityRules.Evaluate(context.Coverage, parameters, draft.DraftDate));
        findings.Add(ClinicSuitabilityRules.CheckSex(clinic));
        await CheckClinicAge(clinic, header, draft.DraftDate, findings, cancellationToken);
        findings.Add(ErClinicRule.Validate(header, clinic));

        var clientIdMessages = ClientIdMessages(lines);
        foreach (var message in clientIdMessages)
        {
            findings.Add(message);
        }

        var headerGate = OpenItemGate.Evaluate(
            header, Array.Empty<ServiceProfile>(), gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters);
        var previewAllowed = !headerGate.Contains(OpenItemIds.OI23, StringComparer.Ordinal) && clientIdMessages.Count == 0;

        var succeeded = new List<PreviewResult>();
        LineContext lineContext;
        IReadOnlyDictionary<decimal, IReadOnlyList<string>> requested;
        try
        {
            lineContext = await ReadLineContext(
                lines, header, context.Preload, Array.Empty<string>(), previewAllowed, succeeded, operatorContext, cancellationToken);
            requested = await ReadRequestedServices(header.ClaimNo, lineContext.Profiles.Lists, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            (lineContext, requested) = await FallbackLineContext(
                lines, succeeded, context.Preload, header.ClaimNo, cancellationToken);
            if (!findings.IsBlocking
                && !LineRulesBlock(header, lines, Enumerable.Range(0, lines.Count), lineContext.Preview, x422, lineContext.Profiles, requested)
                && !await PriceOverridesBlock(header, lines, lineContext.Profiles.ByLine, cancellationToken)
                && !DiscountBlocks(header, maxDisc, draft.DiscountLimitChoice))
            {
                throw;
            }
        }

        var profiles = lineContext.Profiles;
        header = header with { AddToList = AddToListRule.Derive(profiles.TopLevel) };

        var discountChanged = false;
        var offerHeader = header with { OferId = BundledOfferId(lineContext.Preview) };
        var discount = FinalDiscountLimitRule.Evaluate(
            offerHeader, maxDisc, lineContext.CoversDraft ? lineContext.Preview?.Totals.PatPay : null);
        if (discount.IsBlocking && draft.DiscountLimitChoice is { } choice)
        {
            var chosen = findings.Add(FinalDiscountLimitRule.ApplyChoice(offerHeader, choice, maxDisc));
            if (!chosen.IsBlocking)
            {
                header = ApplyDiscount(header, chosen.Adjusted);
                discountChanged = true;
            }
        }
        else
        {
            findings.Add(discount);
        }

        for (var index = 0; index < lines.Count; index++)
        {
            AddLineRules(
                findings, header, lines[index], profiles.ByLine[index], lineContext.Preview, x422, RequestedFor(requested, profiles.Lists, index));
        }

        await AddPriceOverrideRules(findings, header, lines, profiles.ByLine, lineContext.Preview, cancellationToken);

        var gateIds = new SortedSet<string>(
            OpenItemGate.Evaluate(header, profiles.TopLevel, gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters),
            StringComparer.Ordinal);
        if (header.DeptWise == 1 || header.Call == 1)
        {
            gateIds.Add(OpenItemIds.OI33);
        }

        if (findings.IsBlocking)
        {
            return new CreateInvoiceResponse
            {
                InvNo = null,
                Messages = findings.Messages,
                OpenItems = gateIds.ToArray(),
            };
        }

        if (gateIds.Count > 0)
        {
            await ThrowGate(gateIds.ToArray(), findings.Warnings, header, cancellationToken);
        }

        if (discountChanged)
        {
            var discounted = await RulesPreview(header, lines, NoExcludedLines, operatorContext, cancellationToken);
            var reset = PaymentAllocationRules.ResetAfterDiscountChange(discounted?.Totals.CashCollected);
            header = header with
            {
                Amount1 = ToDecimal(reset.Adjusted[Amount1Item]),
                Amount2 = ToDecimal(reset.Adjusted[Amount2Item]),
            };
            header = ApplySubPayType(header, findings.Add(HeaderRecordRules.ApplyPaymentTypeDefault(header)));
        }

        return await CreateNew(draft.RequestId, header, lines, clinic, findings, operatorContext, cancellationToken);
    }

    /// <summary>Returns a saved invoice as a read-only view, or null when it is not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="parameters">Entry parameters supplying the local document type filter.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The invoice header, lines and display values, or null.</returns>
    public async Task<InvoiceViewResponse?> GetInvoice(
        long invNo,
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (await _invoices.GetInvoice(invNo, parameters.LocalDocType, cancellationToken) is not { } invoice)
        {
            return null;
        }

        return new InvoiceViewResponse
        {
            Header = invoice.Header,
            Lines = invoice.Lines,
            Display = invoice.Display,
            ReadOnly = !InvoiceStatePolicy.CanEdit(invNo),
            OpenItems = new[] { OpenItemIds.OI56 },
        };
    }

    /// <summary>Refuses every change to a saved invoice with open item OI-56.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Unused; the operation is refused.</param>
    /// <returns>Never returns; always throws <see cref="NotImplementedException"/>.</returns>
    public Task Update(long invNo, OperatorContext operatorContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorContext);

        throw OpenItem(OpenItemIds.OI56, SavedInvoiceEditTitle);
    }

    /// <summary>Returns the highest invoice number of the operator's information centre, or null when there is none.</summary>
    /// <param name="operatorContext">Operator identity supplying the information centre.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The last invoice number, or null.</returns>
    public async Task<long?> GetLastInvoiceNo(OperatorContext operatorContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorContext);

        return await _invoices.GetLastInvoiceNo(operatorContext.InfoCenterId, cancellationToken);
    }

    /// <summary>Returns the persisted insurance, line and transfer details of a saved invoice, or null when it is not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The details, or null.</returns>
    public async Task<MoreDetailsResponse?> GetMoreDetails(
        long invNo,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (await _invoices.GetMoreDetails(invNo, cancellationToken) is not { } details)
        {
            return null;
        }

        return new MoreDetailsResponse
        {
            InvNo = invNo,
            InsNumber = TextValue(details.Header, "INS_NUMBER"),
            CardEnd = DateValue(details.Header, "CARD_END"),
            PatPolicyNo = TextValue(details.Header, "PAT_POLICY_NO"),
            Lines = details.Lines,
            TransMRowIds = details.Transfers
                .Select(transfer => TextValue(transfer, "TRANS_M_ROW_ID"))
                .OfType<string>()
                .ToArray(),
        };
    }

    /// <summary>Sends the invoice SMS; blocked by open items OI-12 and OI-45 in this build.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that faults with <see cref="NotImplementedException"/> in this build.</returns>
    public async Task SendSms(long invNo, OperatorContext operatorContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorContext);

        try
        {
            await _legacy.SendInvoiceSms(invNo, cancellationToken);
        }
        catch (NotImplementedException failure)
        {
            failure.Data[ProblemDetailsWriter.OpenItemsDataKey] = new[] { OpenItemIds.OI12, OpenItemIds.OI45 };
            throw;
        }
    }

    /// <summary>Builds an invoice document of the given kind; blocked by its open item in this build.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="kind">Document kind: invoice, patient-card, barcode-sms or iqama-check.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that faults with <see cref="NotImplementedException"/> in this build, or with <see cref="ArgumentException"/> carrying a blocking KIND message for a blank or unknown kind.</returns>
    public async Task BuildDocument(
        long invNo,
        string kind,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        if (IsBlank(kind))
        {
            throw InvalidDocumentKind(kind);
        }

        ArgumentNullException.ThrowIfNull(operatorContext);

        var normalized = kind.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case InvoiceDocument:
                try
                {
                    _ = _invoiceApi.BuildPrintUrl(invNo);
                }
                catch (NotImplementedException failure)
                {
                    failure.Data[ProblemDetailsWriter.OpenItemsDataKey] = new[]
                    {
                        OpenItemIds.OI11, OpenItemIds.OI45, OpenItemIds.OI46, OpenItemIds.OI49,
                    };
                    throw;
                }

                return;
            case PatientCardDocument or BarcodeSmsDocument or IqamaCheckDocument:
                try
                {
                    _ = await _legacy.BuildLegacyDocument(invNo, normalized, cancellationToken);
                }
                catch (NotImplementedException failure)
                {
                    failure.Data[ProblemDetailsWriter.OpenItemsDataKey] = normalized switch
                    {
                        PatientCardDocument => new[] { OpenItemIds.OI47 },
                        BarcodeSmsDocument => new[] { OpenItemIds.OI47, OpenItemIds.OI26 },
                        _ => new[] { OpenItemIds.OI48 },
                    };
                    throw;
                }

                return;
            default:
                throw InvalidDocumentKind(kind);
        }
    }

    /// <summary>Transfers the invoice's stock to the store; blocked by open items OI-10 and OI-44 in this build.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that faults with <see cref="NotImplementedException"/> in this build.</returns>
    public async Task TransferStock(long invNo, OperatorContext operatorContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorContext);

        try
        {
            await _legacy.TransferStock(invNo, cancellationToken);
        }
        catch (NotImplementedException failure)
        {
            failure.Data[ProblemDetailsWriter.OpenItemsDataKey] = new[] { OpenItemIds.OI10, OpenItemIds.OI44 };
            throw;
        }
    }

    /// <summary>Imports the patient's selected request lines for the visit, with the per-service notices.</summary>
    /// <param name="request">Draft supplying the patient, doctor, pay type and visit.</param>
    /// <param name="operatorContext">Operator identity keying the request selection.</param>
    /// <param name="cancellationToken">Cancels the reads and the import.</param>
    /// <returns>Imported lines, the import result (zero counts when no request row is selected) and the notices, or the blocking doctor, patient or visit message.</returns>
    public async Task<ImportResponse> ImportRequests(
        ImportRequestsRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var draft = Sanitize(request.Draft, operatorContext);
        var parameters = draft.Parameters;

        var doctor = RequestImportRules.RequireDoctor(draft.Header.DocId);
        if (doctor.IsBlocking)
        {
            return new ImportResponse { Messages = ToDtos(doctor.Messages) };
        }

        if (IsBlank(draft.Header.PatientNo))
        {
            return new ImportResponse { Messages = PatientRequired() };
        }

        if (IsBlank(parameters.VisitUnique))
        {
            return new ImportResponse { Messages = new[] { Blocking(VisitUniqueItem, VisitUniqueRequiredText) } };
        }

        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        var payType = header.PayType.GetValueOrDefault();
        var x422 = await ReadX422(cancellationToken);

        var rows = await _invoices.GetSelectedRequestRows(
            header.PatientNo!, parameters.VisitUnique!, payType, cancellationToken);
        var notices = RequestImportRules.Notices(
            rows.Select(row => (row.ServiceId, row.ReqAStatus, row.ReqNeedA, row.ApprovRefNo)),
            x422,
            payType);

        if (rows.Count == 0)
        {
            return new ImportResponse { Result = EmptyRequestImport(), Messages = ToDtos(notices.Messages) };
        }

        var approvalMode = _import.ApprovalCheckMode(x422);
        (IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result) imported;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            imported = await _import.ImportRequestLines(
                session,
                header,
                operatorContext,
                parameters.VisitUnique!,
                rows.Select(row => row.PatServReqRowId).ToArray(),
                approvalMode,
                cancellationToken);
        }

        return new ImportResponse
        {
            Lines = imported.Lines.Select(ToLine).ToArray(),
            Result = imported.Result,
            Messages = ToDtos(notices.Messages),
        };
    }

    /// <summary>Returns the automatic visit line the entry parameters, company and clinic call for, if any.</summary>
    /// <param name="request">Draft supplying the entry parameters, company, clinic and doctor.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads and the import.</param>
    /// <returns>Zero or one visit line and, for a consultation or review line, the import result.</returns>
    public async Task<ImportResponse> ImportVisitLine(
        VisitLineRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var draft = Sanitize(request.Draft, operatorContext);
        var parameters = draft.Parameters;
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        var choice = VisitLineRule.Choose(parameters.DoReview, parameters.ClaimNo, header.CompCode, header.ClinicId);

        if (choice == VisitLineChoice.None)
        {
            return new ImportResponse();
        }

        if (choice.ServiceId is { } fixedServiceId)
        {
            return new ImportResponse
            {
                Lines = new[] { new InvoiceLineDraft { ServiceId = fixedServiceId, Qty = 1m, ClientId = NewId() } },
                Result = null,
            };
        }

        (EngineLineInput? Line, ImportResultRow Result) visit;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            visit = await _import.GetVisitLine(session, header, operatorContext, choice, cancellationToken);
        }

        return new ImportResponse
        {
            Lines = visit.Line is null ? Array.Empty<InvoiceLineDraft>() : new[] { ToLine(visit.Line) },
            Result = visit.Result,
        };
    }

    /// <summary>Imports the components of a package on the draft's price list, with the no-lines warning and the derived ADD_TO_LIST.</summary>
    /// <param name="request">Draft, package service id and optional parent source id.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads, the preview and the import.</param>
    /// <returns>Imported lines, the import result, the warning and the adjusted ADD_TO_LIST, or the blocking SERVICEID message when the package service id is blank.</returns>
    public async Task<ImportResponse> ImportPackage(
        PackageImportRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (IsBlank(request.PackageServiceId))
        {
            return new ImportResponse { Messages = new[] { Blocking(ServiceIdItem, PackageServiceRequiredText) } };
        }

        var draft = Sanitize(request.Draft, operatorContext);
        var packageServiceId = request.PackageServiceId.Trim();
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var preload = context.Preload;
        var header = context.Header;

        var clientIdMessages = ClientIdMessages(draft.Lines);
        if (clientIdMessages.Count > 0)
        {
            return new ImportResponse { Messages = clientIdMessages };
        }

        var (_, preview) = await ContextPreview(
            draft.Lines,
            LocallyRejectedLines(draft.Lines),
            new[] { packageServiceId },
            preload,
            header,
            new List<PreviewResult>(),
            operatorContext,
            cancellationToken);
        var listId = await ResolveListId(preview, preload, header, operatorContext, cancellationToken);

        (IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result) package;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            package = await _invoiceApi.GetPackageLines(
                session, packageServiceId, listId, request.ParentSourceId, cancellationToken);
        }

        var imported = package.Lines.Select(ToLine).ToArray();
        var findings = new Findings();
        findings.Add(PackageImportRules.Evaluate(imported.Length));

        var lists = LineLists(draft.Lines, preview, preload)
            .Select(list => (decimal?)(list ?? listId))
            .Concat(imported.Select(_ => (decimal?)listId))
            .ToArray();
        var profiles = await BuildProfiles(draft.Lines.Concat(imported).ToArray(), lists, cancellationToken);
        findings.Adjust(AddToListItem, AddToListRule.Derive(profiles.TopLevel));

        return new ImportResponse
        {
            Lines = imported,
            Result = package.Result,
            Adjusted = findings.Adjusted,
            Messages = findings.Messages,
        };
    }

    /// <summary>Returns the lines of a bundled offer for the draft's patient, pay type and date.</summary>
    /// <param name="request">Draft, offer id and bundle quantity.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads and the call.</param>
    /// <returns>The offer lines as draft lines.</returns>
    public async Task<ImportResponse> ImportBundledOffer(
        BundledOfferRequest request,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var draft = Sanitize(request.Draft, operatorContext);
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;

        IReadOnlyList<EditablePreviewLine> lines;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            lines = await _invoiceApi.GetBundledOfferLines(
                session, header, operatorContext, request.OfferId, request.BundleQty, cancellationToken);
        }

        return new ImportResponse
        {
            Lines = lines.Select(ToLine).ToArray(),
            Result = null,
        };
    }

    /// <summary>Returns the rows of a list of values, or null for an unknown list.</summary>
    /// <param name="name">List name, compared ignoring case.</param>
    /// <param name="binds">Item values the list depends on, keyed by legacy item name ignoring case.</param>
    /// <param name="draftDate">Draft date bound as INVDATE by the date-filtered lists RESERV_NO and OFFERS; null returns their missing INVDATE message.</param>
    /// <param name="operatorContext">Operator identity supplying the information centre.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows, a blocking message naming a missing item, or null.</returns>
    public async Task<LovResponse?> GetLov(
        string name,
        IReadOnlyDictionary<string, string?> binds,
        DateTime? draftDate,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(binds);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var lov = name.Trim().ToUpperInvariant();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, value) in binds)
        {
            values[item] = value;
        }

        switch (lov)
        {
            case "COMPANY1_2":
                return Rows(lov, await _lovs.Company(operatorContext.InfoCenterId, cancellationToken));
            case "SUB_COMPANY":
                return BindText(values, CompCodeItem) is { } compCode
                    ? Rows(lov, await _lovs.SubCompany(compCode, cancellationToken))
                    : MissingBind(lov, CompCodeItem);
            case "THE_CLASS":
                return BindText(values, SubCompCodeItem) is { } subCompCode
                    ? Rows(lov, await _lovs.TheClass(subCompCode, cancellationToken))
                    : MissingBind(lov, SubCompCodeItem);
            case "PAY_TYPE1" or "PAY_TYPE2":
                return Rows(lov, await _lovs.PayTypes(cancellationToken));
            case "DOC":
                return Rows(lov, await _lovs.Doc(operatorContext.InfoCenterId, cancellationToken));
            case "RESERV_NO":
                if (BindInt(values, DocIdItem) is not { } docId)
                {
                    return MissingBind(lov, DocIdItem);
                }

                if (BindText(values, PatientNoItem) is not { } patientNo)
                {
                    return MissingBind(lov, PatientNoItem);
                }

                if (draftDate is not { } reservationDate)
                {
                    return MissingBind(lov, InvDateItem);
                }

                return Rows(lov, await _lovs.ReservNo(reservationDate, docId, patientNo, cancellationToken)) with
                {
                    ViewOnly = true,
                };
            case "OFFERS":
                if (BindInt(values, PayTypeItem) is not { } payType)
                {
                    return MissingBind(lov, PayTypeItem);
                }

                if (draftDate is not { } offerDate)
                {
                    return MissingBind(lov, InvDateItem);
                }

                return Rows(lov, await _lovs.Offers(payType, offerDate, operatorContext.InfoCenterId, cancellationToken));
            case "CAT":
                return Rows(lov, await _lovs.Cat(cancellationToken));
            case "SERVICES":
                _ = await _legacy.ResolvePricePlan(
                    operatorContext.InfoCenterId,
                    BindText(values, CompCodeItem),
                    BindText(values, SubCompCodeItem),
                    cancellationToken);
                throw OpenItem(OpenItemIds.OI24, PricePlanTitle);
            case "DOC1":
                throw OpenItem(OpenItemIds.OI33, UncarriedValueTitle);
            default:
                return null;
        }
    }

    /// <summary>Returns the invoice types as lookup items.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The invoice types in query order.</returns>
    public async Task<IReadOnlyList<LookupItem>> GetInvoiceTypes(CancellationToken cancellationToken = default)
    {
        var types = await _lookups.GetInvoiceTypes(cancellationToken);
        return types.Select(type => new LookupItem { Code = type.Id, Name = type.Description }).ToArray();
    }

    /// <summary>Returns the currencies as lookup items.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The currencies in query order.</returns>
    public async Task<IReadOnlyList<LookupItem>> GetCurrencies(CancellationToken cancellationToken = default)
    {
        var currencies = await _lookups.GetCurrencies(cancellationToken);
        return currencies.Select(currency => new LookupItem { Code = currency.Code, Name = currency.Name }).ToArray();
    }

    private async Task<CreateInvoiceResponse> Replay(
        string requestId,
        string? patientNo,
        DateTime draftDate,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var header = WithOperator(
            new InvoiceHeaderDraft { PatientNo = patientNo, DraftDate = draftDate, InvDate = draftDate },
            operatorContext);

        FullInvoiceResultRow result;
        long invNo;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            try
            {
                result = await _invoiceApi.CreateFullInvoice(
                    session, header, Array.Empty<InvoiceLineDraft>(), operatorContext, requestId, cancellationToken);
                invNo = RequireInvoiceNumber(result);
            }
            catch (Exception)
            {
                await TryRollback(session);
                throw;
            }

            await session.Commit(cancellationToken);
        }

        var openItems = new SortedSet<string>(StringComparer.Ordinal) { OpenItemIds.OI20 };
        if (await _invoices.GetInvoice(invNo, null, cancellationToken) is { } saved)
        {
            AddHeaderOpenItems(openItems, saved.Header);
            if (HasAgeLimit(await ReadClinic(saved.Header, cancellationToken)))
            {
                openItems.Add(OpenItemIds.OI22);
            }
        }

        return new CreateInvoiceResponse
        {
            InvNo = result.InvNo,
            Message = result.Message,
            PostingFlags = PostingFlags(result),
            Messages = Array.Empty<MessageDto>(),
            OpenItems = openItems.ToArray(),
        };
    }

    private async Task<CreateInvoiceResponse> CreateNew(
        string requestId,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        ClinicProfile? clinic,
        Findings findings,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var openItems = new SortedSet<string>(findings.OpenItems, StringComparer.Ordinal);
        FullInvoiceResultRow result;

        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            try
            {
                result = await _invoiceApi.CreateFullInvoice(
                    session, header, lines, operatorContext, requestId, cancellationToken);

                if (ReceptionTransferRule.ShouldClear(isReplay: false, hasNewInvDocId: true)
                    && !await ClearReceptionTransfer(session, header.PatientNo, cancellationToken))
                {
                    findings.Add(new MessageDto
                    {
                        Field = null,
                        Text = ReceptionTransferWarning,
                        Severity = ValidationMessage.Warning,
                        Rule = ReceptionTransferRuleId,
                    });
                }

                try
                {
                    await _legacy.ValidateTotalInvoice(session, RequireInvoiceNumber(result), cancellationToken);
                }
                catch (NotImplementedException failure) when (IsOpenItem(failure, OpenItemIds.OI20))
                {
                    openItems.Add(OpenItemIds.OI20);
                }
            }
            catch (Exception)
            {
                await TryRollback(session);
                throw;
            }

            await session.Commit(cancellationToken);
        }

        AddHeaderOpenItems(openItems, header);
        if (HasAgeLimit(clinic))
        {
            openItems.Add(OpenItemIds.OI22);
        }

        return new CreateInvoiceResponse
        {
            InvNo = result.InvNo,
            Message = result.Message,
            PostingFlags = PostingFlags(result),
            Messages = findings.Warnings,
            OpenItems = openItems.ToArray(),
        };
    }

    private async Task<bool> ClearReceptionTransfer(IOracleSession session, string? patientNo, CancellationToken cancellationToken)
    {
        if (IsBlank(patientNo))
        {
            return true;
        }

        session.Save(ReceptionTransferSavepoint);
        try
        {
            await _patientTransfer.ClearReceptionTransfer(session, patientNo!, cancellationToken);
            return true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            session.Rollback(ReceptionTransferSavepoint);
            return false;
        }
    }

    private async Task<ValidateDraftResponse> ValidatePatient(DraftDto draft, CancellationToken cancellationToken)
    {
        var context = await ReadServerContext(draft, patientCompanyFirst: true, cancellationToken);
        var findings = new Findings();
        findings.Add(PatientEligibilityRules.Evaluate(context.Coverage, draft.Parameters, draft.DraftDate));
        findings.Adjust(PayTypeItem, context.Header.PayType);
        if (!IsBlank(context.Header.SubCompCode))
        {
            findings.AddOpenItem(OpenItemIds.OI21);
        }

        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateCompany(DraftDto draft, CancellationToken cancellationToken)
    {
        var preload = await ReadPatientClaimPreload(draft.Parameters, draft.Header.PatientNo, cancellationToken);
        var findings = new Findings();
        findings.Adjust(
            PayTypeItem,
            await DecidePayType(draft.Header.CompCode, null, draft.Parameters, preload, cancellationToken));
        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateDoctor(
        DraftDto draft,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var parameters = draft.Parameters;
        var findings = new Findings();
        var header = ApplyDoctor(draft.Header, findings.Add(DoctorSelectionRules.Validate(draft.Header, parameters)));
        findings.Adjust(ClaimNoItem, ClaimNumberRule.Build(header, parameters));
        var visitLine = VisitLineRule.Choose(parameters.DoReview, parameters.ClaimNo, header.CompCode, header.ClinicId);

        var profiles = ProfileSet.Empty(draft.Lines.Count);
        if (ServiceIds(draft.Lines).Length > 0)
        {
            var clientIdMessages = ClientIdMessages(draft.Lines);
            if (clientIdMessages.Count > 0)
            {
                foreach (var message in clientIdMessages)
                {
                    findings.Add(message);
                }

                return findings.ToValidateResponse(visitLine);
            }

            var context = await ReadServerContext(draft with { Header = header }, patientCompanyFirst: false, cancellationToken);
            var lineContext = await ReadLineContext(
                draft.Lines,
                context.Header,
                context.Preload,
                Array.Empty<string>(),
                previewAllowed: true,
                new List<PreviewResult>(),
                operatorContext,
                cancellationToken);
            profiles = lineContext.Profiles;
        }

        findings.Adjust(AddToListItem, AddToListRule.Derive(profiles.TopLevel));
        return findings.ToValidateResponse(visitLine);
    }

    private async Task<ValidateDraftResponse> ValidateClinic(DraftDto draft, CancellationToken cancellationToken)
    {
        var clinic = await ReadClinic(draft.Header, cancellationToken);
        var findings = new Findings();
        findings.Add(ClinicSuitabilityRules.CheckSex(clinic));
        await CheckClinicAge(clinic, draft.Header, draft.DraftDate, findings, cancellationToken);
        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateErFlags(DraftDto draft, CancellationToken cancellationToken)
    {
        var clinic = await ReadClinic(draft.Header, cancellationToken);
        var findings = new Findings();
        findings.Add(ErClinicRule.Validate(draft.Header, clinic));
        if (draft.Header.DeptWise == 1 || draft.Header.Call == 1)
        {
            findings.AddOpenItem(OpenItemIds.OI33);
        }

        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateFinalDiscount(
        DraftDto draft,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        await GuardDeductible(header, draft.Parameters, context, cancellationToken);

        var maxDisc = await _lookups.GetUserMaxDiscount(operatorContext.UserNo, cancellationToken);
        var preview = await RulesPreview(header, draft.Lines, NoExcludedLines, operatorContext, cancellationToken);

        var offerHeader = header with { OferId = BundledOfferId(preview) };
        var evaluated = FinalDiscountLimitRule.Evaluate(offerHeader, maxDisc, preview?.Totals.PatPay);
        var outcome = evaluated.IsBlocking && draft.DiscountLimitChoice is { } choice
            ? FinalDiscountLimitRule.ApplyChoice(offerHeader, choice, maxDisc)
            : evaluated;

        var findings = new Findings();
        findings.Add(outcome);
        if (!outcome.IsBlocking)
        {
            var adjusted = ApplyDiscount(header, outcome.Adjusted);
            if (!ReferenceEquals(outcome, evaluated) && adjusted != header)
            {
                preview = await RulesPreview(adjusted, draft.Lines, NoExcludedLines, operatorContext, cancellationToken);
            }

            findings.Add(PaymentAllocationRules.ResetAfterDiscountChange(preview?.Totals.CashCollected));
        }

        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateFirstAmount(
        DraftDto draft,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        await GuardDeductible(header, draft.Parameters, context, cancellationToken);

        var preview = await RulesPreview(header, draft.Lines, NoExcludedLines, operatorContext, cancellationToken);
        var findings = new Findings();
        findings.Adjust(
            Amount2Item,
            PaymentAllocationRules.AllocateSecondAmount(preview?.Totals.CashCollected, header.Amount1));
        return findings.ToValidateResponse();
    }

    private static ValidateDraftResponse ValidateSecondAmount(DraftDto draft)
    {
        var header = draft.Header;
        var findings = new Findings();
        findings.Adjust(
            RefundItem,
            PaymentAllocationRules.Refund(new PaymentAllocation
            {
                Amount1 = header.Amount1,
                Amount2 = header.Amount2,
                CashPayed = header.CashPayed,
                SubPayType = header.SubPayType,
                SubPayType2 = header.SubPayType2,
            }));
        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateRecord(DraftDto draft, CancellationToken cancellationToken)
    {
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var findings = new Findings();
        var header = ApplySubPayType(context.Header, findings.Add(HeaderRecordRules.ApplyPaymentTypeDefault(context.Header)));
        findings.Add(HeaderRecordRules.ValidateRecord(header));
        return findings.ToValidateResponse();
    }

    private async Task<ValidateDraftResponse> ValidateLine(
        DraftDto draft,
        int? lineIndex,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        if (lineIndex is not int index || index < 0 || index >= draft.Lines.Count)
        {
            return RejectedInput(LineField, LineIndexText);
        }

        var parameters = draft.Parameters;
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header with { ClaimNo = ClaimNumberRule.Build(context.Header, parameters) };
        var line = draft.Lines[index];
        var x422 = await ReadX422(cancellationToken);
        var gate = await ReadGateInputs(header, context.Coverage, context.Preload, readCard: true, cancellationToken);

        var findings = new Findings();
        var clientIdMessages = ClientIdMessages(draft.Lines);
        foreach (var message in clientIdMessages)
        {
            findings.Add(message);
        }

        var headerGate = OpenItemGate.Evaluate(
            header, Array.Empty<ServiceProfile>(), gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters);
        var previewAllowed = !headerGate.Contains(OpenItemIds.OI23, StringComparer.Ordinal) && clientIdMessages.Count == 0;

        var succeeded = new List<PreviewResult>();
        LineContext lineContext;
        IReadOnlyDictionary<decimal, IReadOnlyList<string>> requested;
        try
        {
            lineContext = await ReadLineContext(
                draft.Lines, header, context.Preload, Array.Empty<string>(), previewAllowed, succeeded, operatorContext, cancellationToken);
            requested = await ReadRequestedServices(header.ClaimNo, lineContext.Profiles.Lists, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            (lineContext, requested) = await FallbackLineContext(
                draft.Lines, succeeded, context.Preload, header.ClaimNo, cancellationToken);
            if (!findings.IsBlocking
                && !LineRulesBlock(header, draft.Lines, new[] { index }, lineContext.Preview, x422, lineContext.Profiles, requested)
                && !await PriceOverridesBlock(
                    header, new[] { draft.Lines[index] }, new[] { lineContext.Profiles.ByLine[index] }, cancellationToken))
            {
                throw;
            }
        }

        var profiles = lineContext.Profiles;
        var profile = profiles.ByLine[index];
        AddLineRules(findings, header, line, profile, lineContext.Preview, x422, RequestedFor(requested, profiles.Lists, index));
        await AddPriceOverrideRules(findings, header, new[] { line }, new[] { profile }, lineContext.Preview, cancellationToken);
        if (lineContext.Resolved)
        {
            findings.Adjust(AddToListItem, AddToListRule.Derive(profiles.TopLevel));
        }

        var gateIds = OpenItemGate.Evaluate(
            header,
            profile is null ? Array.Empty<ServiceProfile>() : new[] { profile },
            gate.CardId,
            gate.MaxDeductable,
            gate.UseAdvanced,
            parameters);

        if (findings.IsBlocking)
        {
            findings.AddOpenItems(gateIds);
            return findings.ToValidateResponse();
        }

        if (gateIds.Count > 0)
        {
            await ThrowGate(gateIds, findings.Warnings, header, cancellationToken);
        }

        return findings.ToValidateResponse();
    }

    private static void AddLineRules(
        Findings findings,
        InvoiceHeaderDraft header,
        InvoiceLineDraft line,
        ServiceProfile? profile,
        PreviewResult? preview,
        int? x422ApprovCheck,
        IReadOnlyList<string> requestedServices)
    {
        findings.Add(LineEntryRules.RequireService(line.ServiceId));
        findings.Add(LineEntryRules.ValidateQuantity(profile, line.Qty));
        findings.Add(LineEntryRules.ValidateDiscountType(ClassText(header.ClassCode), line.DiscountType));
        findings.Add(LineEntryRules.ValidateApproval(
            line.ServiceId, header.PayType, x422ApprovCheck, ServerReqNeedA(line, profile, preview), line.ApprovRefNo));
        findings.Add(LineEntryRules.WarnNotRequested(header, profile, IsRequested(line.ServiceId, requestedServices)));
    }

    /// <summary>Returns true when the line rules block any of the indexed lines on the profiles and requested services of the line context.</summary>
    private static bool LineRulesBlock(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IEnumerable<int> indexes,
        PreviewResult? preview,
        int? x422ApprovCheck,
        ProfileSet profiles,
        IReadOnlyDictionary<decimal, IReadOnlyList<string>> requested)
    {
        var scratch = new Findings();
        foreach (var index in indexes)
        {
            AddLineRules(
                scratch, header, lines[index], profiles.ByLine[index], preview, x422ApprovCheck, RequestedFor(requested, profiles.Lists, index));
        }

        return scratch.IsBlocking;
    }

    /// <summary>Returns true when a manual price override is refused on a line whose service profile was read.</summary>
    private async Task<bool> PriceOverridesBlock(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<ServiceProfile?> profiles,
        CancellationToken cancellationToken)
    {
        var known = Enumerable.Range(0, lines.Count).Where(index => profiles[index] is not null).ToArray();
        if (known.Length == 0)
        {
            return false;
        }

        var scratch = new Findings();
        await AddPriceOverrideRules(
            scratch,
            header,
            known.Select(index => lines[index]).ToArray(),
            known.Select(index => profiles[index]).ToArray(),
            null,
            cancellationToken);
        return scratch.IsBlocking;
    }

    /// <summary>Returns true when the final-discount limit blocks without a patient share and the draft's choice does not clear it.</summary>
    private static bool DiscountBlocks(InvoiceHeaderDraft header, decimal? maxDisc, DiscountLimitChoice? choice)
    {
        var discount = FinalDiscountLimitRule.Evaluate(header, maxDisc, null);
        return discount.IsBlocking
            && (choice is not { } chosen || FinalDiscountLimitRule.ApplyChoice(header, chosen, maxDisc).IsBlocking);
    }

    private static int? ServerReqNeedA(InvoiceLineDraft line, ServiceProfile? profile, PreviewResult? preview)
    {
        var previewLines = preview?.Lines ?? Array.Empty<EditablePreviewLine>();
        var candidates = IsBlank(line.ClientId)
            ? Array.Empty<EditablePreviewLine>()
            : previewLines.Where(previewLine => string.Equals(previewLine.ClientId, line.ClientId, StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0 && Trimmed(line.ServiceId) is { } serviceId)
        {
            candidates = previewLines
                .Where(previewLine => string.Equals(Trimmed(previewLine.ServiceId), serviceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var values = candidates.Select(candidate => candidate.ReqNeedA).Where(value => value is not null).ToArray();
        if (values.Length == 0)
        {
            return profile?.ReqNeedA;
        }

        return values.FirstOrDefault(value => value is not 0) ?? 0;
    }

    private async Task AddPriceOverrideRules(
        Findings findings,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<ServiceProfile?> profiles,
        PreviewResult? preview,
        CancellationToken cancellationToken)
    {
        var isDirect = CompanyIsDirect(header, cancellationToken);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!IsManualPriceOverride(line))
            {
                continue;
            }

            var refused = preview is not null && preview.CheckedOverrides.Contains(line)
                ? preview.RefusedOverrides.Contains(line)
                : !await PriceOverrideAllowed(header, profiles[index], isDirect);
            if (refused)
            {
                findings.Add(new MessageDto
                {
                    Field = PriceItem,
                    Text = string.Format(CultureInfo.InvariantCulture, FixedPriceFormat, Trimmed(line.ServiceId) ?? string.Empty),
                    Severity = ValidationMessage.Blocking,
                    Rule = null,
                });
            }
        }
    }

    private static bool IsManualPriceOverride(InvoiceLineDraft line) =>
        line.PriceOverride is not null
        && line.PatServReqRowId is null
        && !HasRole(line, ComponentRole)
        && line.OfferId is null
        && IsBlank(line.OfferLineRole);

    private Lazy<Task<int?>> CompanyIsDirect(InvoiceHeaderDraft header, CancellationToken cancellationToken) =>
        new(() => IsBlank(header.CompCode)
            ? Task.FromResult<int?>(null)
            : _lookups.GetCompanyIsDirect(header.CompCode!, cancellationToken));

    private static async Task<bool> PriceOverrideAllowed(
        InvoiceHeaderDraft header,
        ServiceProfile? profile,
        Lazy<Task<int?>> isDirect) =>
        (header.PayType == CashPayType
            && string.Equals(Trimmed(profile?.PriceIsFixed), PriceNotFixed, StringComparison.OrdinalIgnoreCase))
        || await isDirect.Value == DirectCompany;

    private async Task CheckClinicAge(
        ClinicProfile? clinic,
        InvoiceHeaderDraft header,
        DateTime asOf,
        Findings findings,
        CancellationToken cancellationToken)
    {
        if (clinic is null || (clinic.AgeMin is null && clinic.AgeMax is null) || IsBlank(header.PatientNo))
        {
            return;
        }

        try
        {
            var ageYears = await _legacy.ComputePatientAgeYears(header.PatientNo!, asOf, cancellationToken);
            findings.Add(ClinicSuitabilityRules.CheckAge(clinic, ageYears));
        }
        catch (NotImplementedException failure) when (IsOpenItem(failure, OpenItemIds.OI22))
        {
            findings.AddOpenItem(OpenItemIds.OI22);
        }
    }

    private async Task GuardDeductible(
        InvoiceHeaderDraft header,
        InvoiceEntryParameters parameters,
        ServerContext context,
        CancellationToken cancellationToken)
    {
        var gate = await ReadGateInputs(header, context.Coverage, context.Preload, readCard: true, cancellationToken);
        var ids = OpenItemGate.Evaluate(
            header, Array.Empty<ServiceProfile>(), gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters);
        if (ids.Contains(OpenItemIds.OI23, StringComparer.Ordinal))
        {
            await ThrowGate(ids, Array.Empty<MessageDto>(), header, cancellationToken);
        }
    }

    private async Task ThrowGate(
        IReadOnlyList<string> ids,
        IReadOnlyList<MessageDto> warnings,
        InvoiceHeaderDraft header,
        CancellationToken cancellationToken)
    {
        var primary = ids[0];
        if (primary == OpenItemIds.OI23)
        {
            try
            {
                _ = await _legacy.GetPaidBefore(header.CompCode, header.SubCompCode, header.ClaimNo, cancellationToken);
            }
            catch (NotImplementedException failure)
            {
                AttachGateData(failure, ids, warnings);
                throw;
            }
        }

        var gateFailure = OpenItem(primary, GateTitle(primary));
        AttachGateData(gateFailure, ids, warnings);
        throw gateFailure;
    }

    private static void AttachGateData(Exception failure, IReadOnlyList<string> ids, IReadOnlyList<MessageDto> warnings)
    {
        failure.Data[ProblemDetailsWriter.MessagesDataKey] = warnings.ToArray();
        failure.Data[ProblemDetailsWriter.OpenItemsDataKey] = ids.ToArray();
    }

    private static string GateTitle(string openItemId) => openItemId switch
    {
        OpenItemIds.OI23 => PaidBeforeTitle,
        OpenItemIds.OI24 => PricePlanTitle,
        OpenItemIds.OI31 => PackageConsumptionTitle,
        OpenItemIds.OI32 => UntracedRuleTitle,
        OpenItemIds.OI33 => UncarriedValueTitle,
        OpenItemIds.OI56 => SavedInvoiceEditTitle,
        _ => UnavailableTitle,
    };

    private static NotImplementedException OpenItem(string openItemId, string title) => new($"{openItemId}: {title}");

    private static bool IsOpenItem(NotImplementedException failure, string openItemId) =>
        failure.Message.StartsWith($"{openItemId}: ", StringComparison.Ordinal);

    private static async Task<bool> TryRollback(IOracleSession session)
    {
        try
        {
            await session.Rollback(CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<ServerContext> ReadServerContext(
        DraftDto draft,
        bool patientCompanyFirst,
        CancellationToken cancellationToken)
    {
        var header = draft.Header;
        var preload = await ReadPatientClaimPreload(draft.Parameters, header.PatientNo, cancellationToken);
        var coverage = await ReadCoverage(header.PatientNo, cancellationToken);
        var compCode = patientCompanyFirst
            ? (IsBlank(coverage?.CompCode) ? header.CompCode : coverage!.CompCode)
            : (IsBlank(header.CompCode) ? coverage?.CompCode : header.CompCode);
        var payType = await DecidePayType(compCode, coverage, draft.Parameters, preload, cancellationToken);
        var (subCompCode, classCode) = ServerSubCompanyAndClass(payType, coverage, preload);

        return new ServerContext(
            header with { CompCode = compCode, PayType = payType, SubCompCode = subCompCode, ClassCode = classCode },
            coverage,
            preload);
    }

    private static (string? SubCompCode, int? ClassCode) ServerSubCompanyAndClass(
        int payType,
        PatientCoverageSnapshot? coverage,
        ClaimPreloadData? preload)
    {
        if (payType != CreditPayType)
        {
            return (null, null);
        }

        if (preload?.Header is { } preloadHeader && !IsBlank(preloadHeader.SubCompCode))
        {
            return (preloadHeader.SubCompCode, preloadHeader.ClassCode);
        }

        return (coverage?.SubCompCode, coverage?.ClassCode);
    }

    private async Task<int> DecidePayType(
        string? compCode,
        PatientCoverageSnapshot? coverage,
        InvoiceEntryParameters parameters,
        ClaimPreloadData? preload,
        CancellationToken cancellationToken)
    {
        int? companyType = null;
        if (!IsBlank(compCode))
        {
            companyType = coverage is { CompanyType: not null } && string.Equals(coverage.CompCode, compCode, StringComparison.Ordinal)
                ? coverage.CompanyType
                : await _lookups.GetCompanyType(compCode!, cancellationToken);
        }

        return PayTypeSelectionRule.Decide(compCode, companyType, parameters, preload?.Header);
    }

    private async Task<InvoiceHeaderDraft> ApplyVisitDoctor(
        InvoiceHeaderDraft header,
        InvoiceEntryParameters parameters,
        CancellationToken cancellationToken)
    {
        if (IsBlank(parameters.VisitUnique))
        {
            return header;
        }

        return await _lookups.GetVisitDoctor(parameters.VisitUnique!, cancellationToken) is { } visitDoctor
            ? header with { DocId = visitDoctor }
            : header;
    }

    private async Task<ClaimPreloadData?> ReadClaimPreload(InvoiceEntryParameters parameters, CancellationToken cancellationToken)
    {
        var claimNo = Trimmed(parameters.ClaimNo);
        if (claimNo is null or NoClaimParameter or NewConsultationClaimParameter or IndependentServiceClaimParameter)
        {
            return null;
        }

        return await _invoices.GetClaimPreload(claimNo, cancellationToken) is { } preload
            ? new ClaimPreloadData(preload.Header, preload.ListId, preload.MaxDeductable, preload.CardId)
            : null;
    }

    private async Task<ClaimPreloadData?> ReadPatientClaimPreload(
        InvoiceEntryParameters parameters,
        string? patientNo,
        CancellationToken cancellationToken) =>
        await ReadClaimPreload(parameters, cancellationToken) is { } preload
        && Trimmed(patientNo) is { } patient
        && string.Equals(Trimmed(preload.Header.PatientNo), patient, StringComparison.Ordinal)
            ? preload
            : null;

    private async Task<PatientCoverageSnapshot?> ReadCoverage(string? patientNo, CancellationToken cancellationToken) =>
        IsBlank(patientNo) ? null : await _lookups.GetPatientCoverage(patientNo!, cancellationToken);

    private async Task<ClinicProfile?> ReadClinic(InvoiceHeaderDraft header, CancellationToken cancellationToken) =>
        header.ClinicId is int clinicId
            ? await _lookups.GetClinicProfile(clinicId, header.PatientNo, cancellationToken)
            : null;

    private async Task<GateInputs> ReadGateInputs(
        InvoiceHeaderDraft header,
        PatientCoverageSnapshot? coverage,
        ClaimPreloadData? preload,
        bool readCard,
        CancellationToken cancellationToken)
    {
        var maxDeductable = Greater(coverage?.MaxDeductable, preload?.MaxDeductable);

        int? useAdvanced = !IsBlank(header.SubCompCode) && header.ClassCode is int classCode
            ? await _lookups.GetClassAdvancedMode(header.SubCompCode!, ClassText(classCode)!, cancellationToken)
            : null;

        int? cardId = null;
        if (readCard)
        {
            cardId = IsBlank(header.PatientNo)
                ? null
                : await _lookups.GetPatientCardId(header.PatientNo!, cancellationToken);
            cardId ??= preload?.CardId;
        }

        return new GateInputs(maxDeductable, useAdvanced, cardId);
    }

    private async Task<int> ReadX422(CancellationToken cancellationToken)
    {
        var preferences = await _lookups.GetPreferences(cancellationToken);
        return preferences.TryGetValue(ApprovalPreference, out var value)
            && int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode)
                ? mode
                : EnforcedApprovalCheck;
    }

    /// <summary>Returns the rolled-back package preview of the service lines not in the excluded indexes, or null when none remains.</summary>
    private async Task<PreviewResult?> RulesPreview(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> excludedLines,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var eligible = lines
            .Where((line, index) => !IsBlank(line.ServiceId) && !excludedLines.Contains(index))
            .ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        return await CalculateVettedPreview(header, eligible, operatorContext, cancellationToken);
    }

    private async Task<PreviewResult> CalculateVettedPreview(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var manual = lines.Where(IsManualPriceOverride).ToHashSet();
        var probe = await CalculatePreviewLines(header, WithoutPriceOverrides(lines, manual), operatorContext, cancellationToken);
        if (manual.Count == 0)
        {
            return probe;
        }

        var isDirect = CompanyIsDirect(header, cancellationToken);
        var refused = new HashSet<InvoiceLineDraft>();
        foreach (var line in manual)
        {
            var profile = OwnOrFirstListId(line.ClientId, probe) is { } listId && Trimmed(line.ServiceId) is { } serviceId
                ? await _lookups.GetServiceProfile(serviceId, listId, cancellationToken)
                : null;
            if (!await PriceOverrideAllowed(header, profile, isDirect))
            {
                refused.Add(line);
            }
        }

        var vetted = refused.Count == manual.Count
            ? probe
            : await CalculatePreviewLines(header, WithoutPriceOverrides(lines, refused), operatorContext, cancellationToken);
        return vetted with { CheckedOverrides = manual, RefusedOverrides = refused };
    }

    private async Task<PreviewResult> CalculatePreviewLines(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        await using var session = await _sessionFactory.Open(cancellationToken);
        var (previewLines, totals) = await _invoiceApi.CalculatePreview(
            session, header, lines, operatorContext, header.Amount1 is null, cancellationToken);
        return new PreviewResult(previewLines, totals);
    }

    private static IReadOnlyList<InvoiceLineDraft> WithoutPriceOverrides(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<InvoiceLineDraft> stripped) =>
        stripped.Count == 0
            ? lines
            : lines.Select(line => stripped.Contains(line) ? line with { PriceOverride = null } : line).ToArray();

    private static decimal? OwnOrFirstListId(string? clientId, PreviewResult preview) =>
        (IsBlank(clientId)
            ? null
            : preview.Lines.FirstOrDefault(line =>
                line.ListId is not null && string.Equals(line.ClientId, clientId, StringComparison.Ordinal))?.ListId)
        ?? preview.Lines.FirstOrDefault(line => line.ListId is not null)?.ListId;

    private static int? BundledOfferId(PreviewResult? preview) =>
        preview?.Lines.FirstOrDefault(line => line.OfferType == BundledOfferType && line.OfferId is not null)?.OfferId;

    /// <summary>Returns the header without its final discount, for the previews that only resolve line lists and the patient share.</summary>
    private static InvoiceHeaderDraft ContextHeader(InvoiceHeaderDraft header) =>
        header with { FinalDisc = null, FinalDiscPerc = null };

    /// <summary>Returns the indexes of the service lines whose quantity the DR-12 minimum already rejects.</summary>
    private static HashSet<int> LocallyRejectedLines(IReadOnlyList<InvoiceLineDraft> lines)
    {
        var rejected = new HashSet<int>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (!IsBlank(lines[index].ServiceId) && LineEntryRules.ValidateQuantity(null, lines[index].Qty).IsBlocking)
            {
                rejected.Add(index);
            }
        }

        return rejected;
    }

    /// <summary>Returns the indexes of the plain lines that are package services not yet expanded (known package ids, or IS_PACKAGE = 1 on the classification list) and the plain lines no list could classify.</summary>
    private async Task<ParentScan> UnexpandedParents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> rejected,
        IReadOnlyCollection<string> knownPackageIds,
        ClaimPreloadData? preload,
        InvoiceHeaderDraft header,
        bool previewAllowed,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var parents = new HashSet<int>();
        var discoveryExcluded = new HashSet<int>(rejected);
        var unclassified = new List<int>();
        var hasRoleLine = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (rejected.Contains(index) || Trimmed(line.ServiceId) is not { } serviceId)
            {
                continue;
            }

            if (!IsBlank(line.PackageLineRole) || !IsBlank(line.OfferLineRole))
            {
                hasRoleLine = true;
                continue;
            }

            discoveryExcluded.Add(index);
            if (knownPackageIds.Contains(serviceId, StringComparer.Ordinal))
            {
                parents.Add(index);
            }
            else
            {
                unclassified.Add(index);
            }
        }

        if (unclassified.Count == 0)
        {
            return new ParentScan(parents, unclassified);
        }

        var classificationList = preload?.ListId;
        if (classificationList is null && previewAllowed && hasRoleLine)
        {
            var discovery = await RulesPreview(
                ContextHeader(header), lines, discoveryExcluded, operatorContext, cancellationToken);
            if (discovery is not null)
            {
                succeeded.Add(discovery);
            }

            classificationList = FirstPreviewList(discovery);
        }

        if (classificationList is not { } listId)
        {
            return new ParentScan(parents, unclassified);
        }

        parents.UnionWith(await PackageLines(lines, unclassified, listId, cancellationToken));
        return new ParentScan(parents, Array.Empty<int>());
    }

    /// <summary>Returns the candidate lines whose service is a package (IS_PACKAGE = 1) on the given list.</summary>
    private async Task<HashSet<int>> PackageLines(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<int> candidates,
        decimal listId,
        CancellationToken cancellationToken)
    {
        var packages = new HashSet<int>();
        var isPackage = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var index in candidates)
        {
            var serviceId = Trimmed(lines[index].ServiceId)!;
            if (!isPackage.TryGetValue(serviceId, out var package))
            {
                package = (await _lookups.GetServiceProfile(serviceId, listId, cancellationToken))?.IsPackage == 1;
                isPackage[serviceId] = package;
            }

            if (package)
            {
                packages.Add(index);
            }
        }

        return packages;
    }

    /// <summary>Returns the lines excluded from the context preview and that preview, adding every other preview that succeeds to <paramref name="succeeded"/>. When the preview fails while plain lines are unclassified, the list of the first of them that previews alone classifies them, and the preview is re-run without the package parents found; the failure stands when none is found.</summary>
    private async Task<(HashSet<int> Excluded, PreviewResult? Preview)> ContextPreview(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> rejected,
        IReadOnlyCollection<string> knownPackageIds,
        ClaimPreloadData? preload,
        InvoiceHeaderDraft header,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var scan = await UnexpandedParents(
            lines, rejected, knownPackageIds, preload, header, previewAllowed: true, succeeded, operatorContext, cancellationToken);
        var excluded = new HashSet<int>(scan.Parents);
        excluded.UnionWith(rejected);
        var contextHeader = ContextHeader(header);
        try
        {
            return (excluded, await RulesPreview(contextHeader, lines, excluded, operatorContext, cancellationToken));
        }
        catch (Exception failure) when (failure is not OperationCanceledException && scan.Unclassified.Count > 0)
        {
            var probed = await ProbeParents(
                lines, scan.Unclassified, excluded, contextHeader, succeeded, operatorContext, cancellationToken);
            if (probed.Count == 0)
            {
                throw;
            }

            excluded.UnionWith(probed);
        }

        return (excluded, await RulesPreview(contextHeader, lines, excluded, operatorContext, cancellationToken));
    }

    /// <summary>Returns the unclassified plain lines that are package services on the list of the first of them whose service previews alone, or none when no such service previews or the failed preview held a single line.</summary>
    private async Task<HashSet<int>> ProbeParents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<int> unclassified,
        IReadOnlySet<int> failedExcluded,
        InvoiceHeaderDraft contextHeader,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var failedLineCount = Enumerable.Range(0, lines.Count)
            .Count(index => !failedExcluded.Contains(index) && !IsBlank(lines[index].ServiceId));
        if (failedLineCount < 2)
        {
            return new HashSet<int>();
        }

        var probedServices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in unclassified)
        {
            if (!probedServices.Add(Trimmed(lines[index].ServiceId)!))
            {
                continue;
            }

            var others = Enumerable.Range(0, lines.Count).Where(other => other != index).ToHashSet();
            if (await PreviewOrNull(lines, others, contextHeader, operatorContext, cancellationToken) is { } alone)
            {
                succeeded.Add(alone);
                if (FirstPreviewList(alone) is { } listId)
                {
                    return await PackageLines(lines, unclassified, listId, cancellationToken);
                }
            }
        }

        return new HashSet<int>();
    }

    /// <summary>Returns the preview of the lines outside the excluded indexes, or null when no line remains or the package refuses that preview.</summary>
    private async Task<PreviewResult?> PreviewOrNull(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> excludedLines,
        InvoiceHeaderDraft contextHeader,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RulesPreview(contextHeader, lines, excludedLines, operatorContext, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Returns the context preview of the lines that are neither rejected nor unexpanded parents, with every line's list and profile; the other previews that succeed are added to <paramref name="succeeded"/>.</summary>
    private async Task<LineContext> ReadLineContext(
        IReadOnlyList<InvoiceLineDraft> lines,
        InvoiceHeaderDraft header,
        ClaimPreloadData? preload,
        IReadOnlyCollection<string> knownPackageIds,
        bool previewAllowed,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var rejected = LocallyRejectedLines(lines);
        var (excluded, preview) = previewAllowed
            ? await ContextPreview(lines, rejected, knownPackageIds, preload, header, succeeded, operatorContext, cancellationToken)
            : (rejected, (PreviewResult?)null);

        IReadOnlyList<decimal?> lists = LineLists(lines, preview, preload);
        var resolved = true;
        if (LacksList(lines, lists))
        {
            if (previewAllowed)
            {
                lists = await RequireLists(lines, lists, header, operatorContext, cancellationToken);
            }
            else
            {
                resolved = false;
            }
        }

        var profiles = await BuildProfiles(lines, lists, cancellationToken);
        return new LineContext(preview, profiles, preview is not null && excluded.Count == 0, resolved);
    }

    /// <summary>Returns the line context and requested services read on the lists of the previews that succeeded, else the claim preload's list, for use when the context preview failed; an unavailable context when those reads fail.</summary>
    private async Task<(LineContext Context, IReadOnlyDictionary<decimal, IReadOnlyList<string>> Requested)> FallbackLineContext(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<PreviewResult> succeeded,
        ClaimPreloadData? preload,
        string? claimNo,
        CancellationToken cancellationToken)
    {
        var lists = LineLists(lines, succeeded.SelectMany(preview => preview.Lines).ToArray(), preload);
        try
        {
            var profiles = await BuildProfiles(lines, lists, cancellationToken);
            var requested = await ReadRequestedServices(claimNo, profiles.Lists, cancellationToken);
            return (new LineContext(null, profiles, false, false), requested);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return (LineContext.Unavailable(lines.Count), NoRequestedServices);
        }
    }

    /// <summary>Returns the draft-level price list: the first preview list, else the claim preload's list, else the price plan's list or OI-24.</summary>
    private async Task<decimal> ResolveListId(
        PreviewResult? preview,
        ClaimPreloadData? preload,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        if ((FirstPreviewList(preview) ?? preload?.ListId) is { } listId)
        {
            return listId;
        }

        var plan = await _legacy.ResolvePricePlan(
            operatorContext.InfoCenterId, header.CompCode, header.SubCompCode, cancellationToken);
        return plan.ListId ?? throw OpenItem(OpenItemIds.OI24, PricePlanTitle);
    }

    /// <summary>Returns each service line's price list: its own preview list by client id, else the first preview list, else the claim preload's list; null for lines without a service.</summary>
    private static decimal?[] LineLists(
        IReadOnlyList<InvoiceLineDraft> lines,
        PreviewResult? preview,
        ClaimPreloadData? preload) =>
        LineLists(lines, preview?.Lines ?? Array.Empty<EditablePreviewLine>(), preload);

    /// <summary>Returns each service line's price list: its own list among the preview lines by client id, else their first list, else the claim preload's list; null for lines without a service.</summary>
    private static decimal?[] LineLists(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<EditablePreviewLine> previewLines,
        ClaimPreloadData? preload)
    {
        var firstListId = previewLines.FirstOrDefault(line => line.ListId is not null)?.ListId;
        var lists = new decimal?[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            if (IsBlank(lines[index].ServiceId))
            {
                continue;
            }

            var clientId = lines[index].ClientId;
            var ownListId = IsBlank(clientId)
                ? null
                : previewLines.FirstOrDefault(line =>
                    line.ListId is not null && string.Equals(line.ClientId, clientId, StringComparison.Ordinal))?.ListId;
            lists[index] = ownListId ?? firstListId ?? preload?.ListId;
        }

        return lists;
    }

    /// <summary>Returns the line lists with every service line lacking one set to the price plan's list, or throws OI-24.</summary>
    private async Task<IReadOnlyList<decimal?>> RequireLists(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<decimal?> lists,
        InvoiceHeaderDraft header,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        if (!LacksList(lines, lists))
        {
            return lists;
        }

        var plan = await _legacy.ResolvePricePlan(
            operatorContext.InfoCenterId, header.CompCode, header.SubCompCode, cancellationToken);
        var planListId = plan.ListId ?? throw OpenItem(OpenItemIds.OI24, PricePlanTitle);
        return lines
            .Select((line, index) => lists[index] ?? (IsBlank(line.ServiceId) ? null : planListId))
            .ToArray();
    }

    /// <summary>Returns each line's server-read profile on that line's own list, with package components nested, and the top-level profiles.</summary>
    private async Task<ProfileSet> BuildProfiles(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<decimal?> lists,
        CancellationToken cancellationToken)
    {
        var listOrder = new List<decimal>();
        var serviceIdsByList = new Dictionary<decimal, List<string>>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (lists[index] is not { } lineListId || Trimmed(lines[index].ServiceId) is not { } lineServiceId)
            {
                continue;
            }

            if (!serviceIdsByList.TryGetValue(lineListId, out var listServiceIds))
            {
                listServiceIds = new List<string>();
                serviceIdsByList[lineListId] = listServiceIds;
                listOrder.Add(lineListId);
            }

            if (!listServiceIds.Contains(lineServiceId, StringComparer.Ordinal))
            {
                listServiceIds.Add(lineServiceId);
            }
        }

        if (listOrder.Count == 0)
        {
            return ProfileSet.Empty(lines.Count) with { Lists = lists.ToArray() };
        }

        var read = new Dictionary<(decimal ListId, string ServiceId), ServiceProfile?>();
        var queueFlags = new Dictionary<decimal, Dictionary<string, int>>();
        foreach (var listId in listOrder)
        {
            var serviceIds = serviceIdsByList[listId];
            foreach (var serviceId in serviceIds)
            {
                read[(listId, serviceId)] = await _lookups.GetServiceProfile(serviceId, listId, cancellationToken);
            }

            var listFlags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (serviceId, flag) in await _lookups.GetServiceQueueFlags(serviceIds, listId, cancellationToken))
            {
                listFlags[serviceId.Trim()] = flag;
            }

            queueFlags[listId] = listFlags;
        }

        ServiceProfile? Own(int index)
        {
            if (lists[index] is not { } listId || Trimmed(lines[index].ServiceId) is not { } id)
            {
                return null;
            }

            int? flag = queueFlags[listId].TryGetValue(id, out var queued) ? queued : null;
            return read.GetValueOrDefault((listId, id)) switch
            {
                null when flag is null => null,
                null => new ServiceProfile { ServiceId = id, AddToQue = flag },
                { } profile when flag is null => profile,
                { } profile => profile with { AddToQue = flag },
            };
        }

        var own = Enumerable.Range(0, lines.Count).Select(Own).ToArray();
        var byLine = new ServiceProfile?[lines.Count];
        var nested = new bool[lines.Count];

        for (var parent = 0; parent < lines.Count; parent++)
        {
            if (!HasRole(lines[parent], ParentRole))
            {
                continue;
            }

            var instanceId = Trimmed(lines[parent].PackageInstanceId);
            var components = new List<ServiceProfile>();
            for (var component = 0; component < lines.Count; component++)
            {
                if (component == parent
                    || instanceId is null
                    || !HasRole(lines[component], ComponentRole)
                    || !string.Equals(Trimmed(lines[component].PackageInstanceId), instanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                nested[component] = true;
                if (own[component] is { } componentProfile)
                {
                    components.Add(componentProfile);
                }
            }

            var parentProfile = own[parent] ?? new ServiceProfile { ServiceId = Trimmed(lines[parent].ServiceId) };
            byLine[parent] = parentProfile with { Components = components.ToArray() };
        }

        var packageComponents = new Dictionary<(decimal ListId, string PackageId), IReadOnlyList<ServiceProfile>>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (HasRole(lines[index], ParentRole))
            {
                continue;
            }

            if (own[index] is { IsPackage: 1 } package
                && IsBlank(lines[index].PackageLineRole)
                && Trimmed(package.ServiceId) is { } packageId
                && lists[index] is { } packageListId)
            {
                if (!packageComponents.TryGetValue((packageListId, packageId), out var components))
                {
                    components = await _lookups.GetPackageComponentFlags(packageId, packageListId, cancellationToken);
                    packageComponents[(packageListId, packageId)] = components;
                }

                byLine[index] = package with { Components = components };
            }
            else
            {
                byLine[index] = own[index];
            }
        }

        var topLevel = new List<ServiceProfile>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (!nested[index] && byLine[index] is { } profile)
            {
                topLevel.Add(profile);
            }
        }

        return new ProfileSet(topLevel.ToArray(), byLine, lists.ToArray());
    }

    /// <summary>Returns the claim's requested service ids read once per distinct line list; empty when the claim number is blank.</summary>
    private async Task<IReadOnlyDictionary<decimal, IReadOnlyList<string>>> ReadRequestedServices(
        string? claimNo,
        IReadOnlyList<decimal?> lists,
        CancellationToken cancellationToken)
    {
        var requested = new Dictionary<decimal, IReadOnlyList<string>>();
        if (IsBlank(claimNo))
        {
            return requested;
        }

        foreach (var list in lists)
        {
            if (list is { } listId && !requested.ContainsKey(listId))
            {
                requested[listId] = await _lookups.GetRequestedServices(claimNo!, listId, cancellationToken);
            }
        }

        return requested;
    }

    /// <summary>Returns the requested service ids of the line's own list; empty when the line has no list.</summary>
    private static IReadOnlyList<string> RequestedFor(
        IReadOnlyDictionary<decimal, IReadOnlyList<string>> requested,
        IReadOnlyList<decimal?> lists,
        int index) =>
        lists[index] is { } listId && requested.TryGetValue(listId, out var services)
            ? services
            : Array.Empty<string>();

    private static bool IsRequested(string? serviceId, IReadOnlyList<string> requestedServices) =>
        Trimmed(serviceId) is { } id
        && requestedServices.Any(requested => string.Equals(Trimmed(requested), id, StringComparison.OrdinalIgnoreCase));

    private static DraftDto Sanitize(DraftDto draft, OperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var lines = draft.Lines ?? Array.Empty<InvoiceLineDraft>();
        if (lines.Any(line => line is null))
        {
            var failure = new ArgumentException(NullLineText, nameof(draft));
            failure.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(LineField, NullLineText) };
            throw failure;
        }

        var header = WithOperator(draft.Header ?? new InvoiceHeaderDraft(), operatorContext) with
        {
            PreAuthorization = null,
            OferId = null,
            DocId1 = null,
            SeqNo = null,
            InvDate = draft.DraftDate,
            DraftDate = draft.DraftDate,
        };

        return draft with
        {
            Header = header,
            Lines = lines.Select(line => line with { FixPay = null, PayRate = null }).ToArray(),
            Parameters = draft.Parameters ?? new InvoiceEntryParameters(),
        };
    }

    private static InvoiceHeaderDraft WithOperator(InvoiceHeaderDraft header, OperatorContext operatorContext) => header with
    {
        UserNo = operatorContext.UserNo,
        MachineN = operatorContext.MachineName,
        InfoCenterId = operatorContext.InfoCenterId,
    };

    private static bool ClaimNumberFromParameter(InvoiceEntryParameters parameters) =>
        parameters.ClaimFlag == ReviewedClaimFlag
        || (!string.IsNullOrEmpty(parameters.ClaimNo)
            && parameters.ClaimNo is not (NewConsultationClaimParameter or IndependentServiceClaimParameter));

    private static InvoiceHeaderDraft ApplySubPayType(InvoiceHeaderDraft header, RuleResult result) =>
        result.Adjusted.TryGetValue(SubPayTypeItem, out var value) ? header with { SubPayType = ToInt(value) } : header;

    private static InvoiceHeaderDraft ApplyDoctor(InvoiceHeaderDraft header, RuleResult result) =>
        result.Adjusted.TryGetValue(DocIdItem, out var value) ? header with { DocId = ToInt(value) } : header;

    private static InvoiceHeaderDraft ApplyDiscount(InvoiceHeaderDraft header, IReadOnlyDictionary<string, object?> adjusted)
    {
        var result = header;
        if (adjusted.TryGetValue(FinalDiscPercItem, out var percent))
        {
            result = result with { FinalDiscPerc = ToDecimal(percent) };
        }

        if (adjusted.TryGetValue(FinalDiscItem, out var amount))
        {
            result = result with { FinalDisc = ToDecimal(amount) };
        }

        if (adjusted.TryGetValue(DiscTItem, out var mode))
        {
            result = result with { DiscT = ToInt(mode) };
        }

        return result;
    }

    private static MessageDto ToDto(ValidationMessage message) => new()
    {
        Field = message.Field,
        Text = message.Text,
        Severity = message.Severity,
        Rule = message.Rule,
    };

    private static IReadOnlyList<MessageDto> ToDtos(IEnumerable<ValidationMessage> messages) =>
        messages.Select(ToDto).ToArray();

    private static InvoiceLineDraft ToLine(EngineLineInput line) => new()
    {
        ServiceId = line.ServiceId,
        Qty = line.Qty,
        PriceOverride = line.PriceOverride,
        UsePriceOverride = line.UsePriceOverride,
        DiscountType = line.DiscountType,
        Disc = line.Disc,
        MyDisc = line.MyDisc,
        TeethNo = line.TeethNo,
        ToothSurface = line.ToothSurface,
        TeethNo2 = line.TeethNo2,
        PatServReqRowId = line.PatServReqRowId,
        ApprovDate = line.ApprovDate,
        ApprovValidity = line.ApprovValidity,
        ApprovRefNo = line.ApprovRefNo,
        ClaimNo = line.ClaimNo,
        ReqNeedA = line.ReqNeedA,
        ReqAStatus = line.ReqAStatus,
        PackageServiceId = line.PackageServiceId,
        PackageInstanceId = line.PackageInstanceId,
        PackageLineRole = line.PackageLineRole,
        PackageComponentOrder = line.PackageComponentOrder,
        PackageParentLineId = line.PackageParentLineId,
        PackagePricingMethod = line.PackagePricingMethod,
        PackageDefinitionToken = line.PackageDefinitionToken,
        OfferId = line.OfferId,
        OfferDtlId = line.OfferDtlId,
        OfferType = line.OfferType,
        OfferInstanceId = line.OfferInstanceId,
        OfferLineRole = line.OfferLineRole,
        OfferParentLineId = line.OfferParentLineId,
        OfferPriceApplied = line.OfferPriceApplied,
        OfferDisApplied = line.OfferDisApplied,
        OfferNameSnapshot = line.OfferNameSnapshot,
        OfferObjectVersionNumber = line.OfferObjectVersionNumber,
        OfferDtlObjectVersionNumber = line.OfferDtlObjectVersionNumber,
        ClientId = NewId(),
        Price = null,
    };

    private static InvoiceLineDraft ToLine(EditablePreviewLine line) => new()
    {
        ServiceId = line.ServiceId,
        Qty = line.Qty,
        Disc = line.Disc,
        MyDisc = line.MyDisc,
        ReqNeedA = line.ReqNeedA,
        ReqAStatus = line.ReqAStatus,
        PackageServiceId = line.PackageServiceId,
        PackageInstanceId = line.PackageInstanceId,
        PackageLineRole = line.PackageLineRole,
        PackageComponentOrder = line.PackageComponentOrder,
        PackageParentLineId = line.PackageParentLineId,
        PackagePricingMethod = line.PackagePricingMethod,
        PackageDefinitionToken = line.PackageDefinitionToken,
        OfferId = line.OfferId,
        OfferDtlId = line.OfferDtlId,
        OfferType = line.OfferType,
        OfferInstanceId = line.OfferInstanceId,
        OfferLineRole = line.OfferLineRole,
        OfferParentLineId = line.OfferParentLineId,
        OfferPriceApplied = line.OfferPriceApplied,
        OfferDisApplied = line.OfferDisApplied,
        OfferNameSnapshot = line.OfferNameSnapshot,
        OfferObjectVersionNumber = line.OfferObjectVersionNumber,
        OfferDtlObjectVersionNumber = line.OfferDtlObjectVersionNumber,
        ClientId = IsBlank(line.ClientId) ? NewId() : line.ClientId,
        Price = line.Price,
        CatId = line.CatId,
    };

    private static PreviewTotals ToTotals(PreviewTotalsRow totals) => new()
    {
        LineCount = totals.LineCount,
        TotalGross = totals.TotalGross,
        TotalDiscount = totals.TotalDiscount,
        TotalNet = totals.TotalNet,
        PatPay = totals.PatPay,
        CompPay = totals.CompPay,
        VatTotalPat = totals.VatTotalPat,
        VatTotalCo = totals.VatTotalCo,
        CashCollected = totals.CashCollected,
        Amount1 = totals.Amount1,
        Amount2 = totals.Amount2,
        RemainingAmount = totals.RemainingAmount,
        PaymentStatus = totals.PaymentStatus,
    };

    private static IReadOnlyDictionary<string, string?> PostingFlags(FullInvoiceResultRow result) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["payment"] = result.PaymentPosted,
            ["queue"] = result.QueuePosted,
            ["stock"] = result.StockPosted,
            ["printUrl"] = result.PrintUrlBuilt,
            ["sms"] = result.SmsSent,
        };

    private static long RequireInvoiceNumber(FullInvoiceResultRow result) =>
        result.InvNo ?? throw new InvalidOperationException("The invoice package returned no invoice number.");

    private static void AddHeaderOpenItems(ISet<string> openItems, InvoiceHeaderDraft header)
    {
        if (header.CompCode == CashCompanyCode)
        {
            openItems.Add(OpenItemIds.OI12);
            openItems.Add(OpenItemIds.OI45);
        }

        if (!IsBlank(header.SubCompCode))
        {
            openItems.Add(OpenItemIds.OI21);
        }
    }

    private static bool HasAgeLimit(ClinicProfile? clinic) =>
        clinic is { } profile && (profile.AgeMin is not null || profile.AgeMax is not null);

    private static LovResponse Rows(string lov, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => new()
    {
        Name = lov,
        Rows = rows,
    };

    private static LovResponse MissingBind(string lov, string item) => new()
    {
        Name = lov,
        Rows = Array.Empty<IReadOnlyDictionary<string, object?>>(),
        Messages = new[]
        {
            new MessageDto
            {
                Field = item,
                Text = $"{item} is required for the {lov} list.",
                Severity = ValidationMessage.Blocking,
                Rule = null,
            },
        },
    };

    private static MessageDto Blocking(string field, string text) => new()
    {
        Field = field,
        Text = text,
        Severity = ValidationMessage.Blocking,
        Rule = null,
    };

    private static ValidateDraftResponse RejectedInput(string field, string text)
    {
        var findings = new Findings();
        findings.Add(Blocking(field, text));
        return findings.ToValidateResponse();
    }

    private static IReadOnlyList<MessageDto> PatientRequired() =>
        ToDtos(HeaderRecordRules.ValidateRecord(new InvoiceHeaderDraft { PatientNo = null }).Messages);

    private static ImportResultRow EmptyRequestImport() => new()
    {
        SourceType = RequestSourceType,
        SourceCount = 0,
        ImportedCount = 0,
        SkippedRejectedCount = 0,
        SkippedNeedApprovalCount = 0,
        SkippedInvalidCount = 0,
        HasPriceOverrides = NoFlag,
        Message = EmptyRequestImportMessage,
    };

    private static ArgumentException InvalidDocumentKind(string? kind)
    {
        var failure = new ArgumentException($"Unknown document kind '{kind}'.", nameof(kind));
        failure.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(KindField, DocumentKindText) };
        return failure;
    }

    private static string? BindText(IReadOnlyDictionary<string, string?> values, string item) =>
        values.TryGetValue(item, out var value) ? Trimmed(value) : null;

    private static int? BindInt(IReadOnlyDictionary<string, string?> values, string item) =>
        BindText(values, item) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static object? ColumnValue(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (row.TryGetValue(column, out var value))
        {
            return value;
        }

        foreach (var (key, candidate) in row)
        {
            if (string.Equals(key, column, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TextValue(IReadOnlyDictionary<string, object?> row, string column) => ColumnValue(row, column) switch
    {
        null or DBNull => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        { } other => other.ToString(),
    };

    private static DateTime? DateValue(IReadOnlyDictionary<string, object?> row, string column) => ColumnValue(row, column) switch
    {
        DateTime date => date,
        DateTimeOffset offset => offset.DateTime,
        string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => null,
    };

    private static decimal? ToDecimal(object? value) =>
        value is null or DBNull ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    private static int? ToInt(object? value) =>
        value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static decimal? Greater(decimal? first, decimal? second) =>
        first is null ? second : second is null ? first : Math.Max(first.Value, second.Value);

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string? Trimmed(string? value) => IsBlank(value) ? null : value!.Trim();

    private static string[] ServiceIds(IReadOnlyList<InvoiceLineDraft> lines) =>
        lines.Select(line => Trimmed(line.ServiceId)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Returns a blocking CLIENT_ID message for each line whose client id is blank or repeats an earlier line's.</summary>
    private static IReadOnlyList<MessageDto> ClientIdMessages(IReadOnlyList<InvoiceLineDraft> lines)
    {
        var messages = new List<MessageDto>();
        var firstLine = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            var clientId = lines[index].ClientId;
            string? text = null;
            if (IsBlank(clientId))
            {
                text = $"{ClientIdItem} is required on line {index + 1}.";
            }
            else if (firstLine.TryGetValue(clientId!, out var earlier))
            {
                text = $"{ClientIdItem} on line {index + 1} repeats line {earlier + 1}.";
            }
            else
            {
                firstLine[clientId!] = index;
            }

            if (text is not null)
            {
                messages.Add(new MessageDto
                {
                    Field = ClientIdItem,
                    Text = text,
                    Severity = ValidationMessage.Blocking,
                    Rule = null,
                });
            }
        }

        return messages;
    }

    /// <summary>Returns the first non-null list id of the preview lines, or null.</summary>
    private static decimal? FirstPreviewList(PreviewResult? preview) =>
        preview?.Lines.FirstOrDefault(line => line.ListId is not null)?.ListId;

    /// <summary>Returns true when a line with a service has no list.</summary>
    private static bool LacksList(IReadOnlyList<InvoiceLineDraft> lines, IReadOnlyList<decimal?> lists) =>
        lines.Where((line, index) => lists[index] is null && !IsBlank(line.ServiceId)).Any();

    private static bool HasRole(InvoiceLineDraft line, string role) =>
        string.Equals(Trimmed(line.PackageLineRole), role, StringComparison.OrdinalIgnoreCase);

    private static string? ClassText(int? classCode) => classCode?.ToString(CultureInfo.InvariantCulture);

    private static string NewId() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    private static bool HasWellFormedRequestId(DraftDto? draft) =>
        draft?.RequestId is { Length: RequestIdLength } requestId && requestId.All(char.IsAsciiHexDigitUpper);

    private static CreateInvoiceResponse RequestIdRejected() => new()
    {
        InvNo = null,
        Messages = new[]
        {
            new MessageDto
            {
                Field = RequestIdItem,
                Text = RequestIdText,
                Severity = ValidationMessage.Blocking,
                Rule = null,
            },
        },
        OpenItems = Array.Empty<string>(),
    };

    private sealed record ClaimPreloadData(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId);

    private sealed record ServerContext(InvoiceHeaderDraft Header, PatientCoverageSnapshot? Coverage, ClaimPreloadData? Preload);

    private sealed record GateInputs(decimal? MaxDeductable, int? UseAdvanced, int? CardId);

    private sealed record PreviewResult(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)
    {
        public IReadOnlySet<InvoiceLineDraft> CheckedOverrides { get; init; } = new HashSet<InvoiceLineDraft>();

        public IReadOnlySet<InvoiceLineDraft> RefusedOverrides { get; init; } = new HashSet<InvoiceLineDraft>();
    }

    private sealed record ParentScan(HashSet<int> Parents, IReadOnlyList<int> Unclassified);

    private sealed record ProfileSet(
        IReadOnlyList<ServiceProfile> TopLevel,
        IReadOnlyList<ServiceProfile?> ByLine,
        IReadOnlyList<decimal?> Lists)
    {
        public static ProfileSet Empty(int lineCount) =>
            new(Array.Empty<ServiceProfile>(), new ServiceProfile?[lineCount], new decimal?[lineCount]);
    }

    private sealed record LineContext(PreviewResult? Preview, ProfileSet Profiles, bool CoversDraft, bool Resolved)
    {
        public static LineContext Unavailable(int lineCount) => new(null, ProfileSet.Empty(lineCount), false, false);
    }

    private sealed class Findings
    {
        private readonly List<MessageDto> _messages = new();
        private readonly Dictionary<string, object?> _adjusted = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _openItems = new(StringComparer.Ordinal);

        public bool IsBlocking => _messages.Exists(message => message.Severity == ValidationMessage.Blocking);

        public IReadOnlyList<MessageDto> Messages =>
            _messages.OrderBy(message => message.Severity == ValidationMessage.Blocking ? 0 : 1).ToArray();

        public IReadOnlyList<MessageDto> Warnings =>
            _messages.Where(message => message.Severity != ValidationMessage.Blocking).ToArray();

        public IReadOnlyDictionary<string, object?> Adjusted => new Dictionary<string, object?>(_adjusted, StringComparer.Ordinal);

        public IReadOnlyList<string> OpenItems => _openItems.ToArray();

        public RuleResult Add(RuleResult result)
        {
            foreach (var message in result.Messages)
            {
                _messages.Add(ToDto(message));
            }

            foreach (var (item, value) in result.Adjusted)
            {
                _adjusted[item] = value;
            }

            return result;
        }

        public void Add(MessageDto message) => _messages.Add(message);

        public void Adjust(string item, object? value) => _adjusted[item] = value;

        public void AddOpenItem(string openItemId) => _openItems.Add(openItemId);

        public void AddOpenItems(IEnumerable<string> openItemIds) => _openItems.UnionWith(openItemIds);

        public ValidateDraftResponse ToValidateResponse(VisitLineChoice? visitLine = null) => new()
        {
            Messages = Messages,
            Adjusted = Adjusted,
            OpenItems = OpenItems,
            VisitLine = visitLine,
        };
    }
}
