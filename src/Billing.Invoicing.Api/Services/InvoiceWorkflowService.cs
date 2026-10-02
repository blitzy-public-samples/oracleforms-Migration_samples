using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Errors;
using Billing.Invoicing.Data.Errors;
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
    private const int DirectCompany = 1;
    private const int BundledOfferType = 0;
    private const int ValueDiscountMode = 0;
    private const int RateDiscountMode = 1;
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
    private const string ClinicIdItem = "CLINICID";
    private const string ClinicNameItem = "CLINICNAME";
    private const string DocNameItem = "DOC_NAME";
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
    private const int PatientNoBytes = 12;
    private const int ClaimNoBytes = 40;
    private const int VisitUniqueBytes = 39;
    private const int CompCodeBytes = 10;
    private const int SubCompCodeBytes = 10;
    private const int ServiceIdBytes = 20;
    private const string CurrCodeItem = "CURR_CODE";
    private const string ClaimFlagItem = "CLAIM_FLAG";
    private const string NoteNoItem = "NOTE_NO";
    private const string DiscountTypeItem = "LDISCT";
    private const string TeethNoItem = "TEETH_NO";
    private const string ToothSurfaceItem = "TOOTH_SURFACE";
    private const string TeethNo2Item = "TEETH_NO2";
    private const string ApprovRefNoItem = "APPROV_REF_NO";
    private const int CurrCodeBytes = 3;
    private const int ClaimFlagBytes = 2;
    private const int NoteNoBytes = 40;
    private const int DiscountTypeBytes = 1;
    private const int TeethNoBytes = 2;
    private const int ToothSurfaceBytes = 7;
    private const int TeethNo2Bytes = 2;
    private const int ApprovRefNoBytes = 20;
    private const string InvDateItem = "INVDATE";
    private const string ServiceIdItem = "SERVICEID";
    private const string VisitUniqueItem = "VISIT_UNIQUE";
    private const string TargetField = "TARGET";
    private const string LineField = "LINE";
    private const string KindField = "KIND";
    private const string LocalDocTypeItem = "LOCAL_DOC_TYPE";

    private const string UnknownTargetText = "Unknown validation target.";
    private const string LineIndexText = "LineIndex must address a line of the draft.";
    private const string NullLineText = "Draft lines must not contain null entries.";
    private const string PackageServiceRequiredText = "Package service id is required.";
    private const string DocumentKindText = "Document kind must be invoice, patient-card, barcode-sms or iqama-check.";
    private const string LocalDocTypeText = "LOCAL_DOC_TYPE must be 505, 532 or 783.";
    private const string VisitUniqueRequiredText = "Request import failed: visit unique is required.";
    private const string SelectedRequestRowUnreadableText = "Request import failed: a selected request line could not be read.";

    private const string RequestSourceType = "REQUEST";
    private const string NoFlag = "N";
    private const string EmptyRequestImportMessage = "Invoice request import completed. Expanded 0 request rows into 0 invoice lines.";
    private const string Amount1Item = "AMOUNT_1";
    private const string ClientIdItem = "CLIENT_ID";
    private const string RequestIdItem = "REQUEST_ID";
    private const int RequestIdLength = 32;
    private const string RequestIdText = "Request id must be 32 upper-case hexadecimal characters.";
    private const int DraftSealLength = 64;
    private const string DraftSealText = "Draft date does not match the date issued with this draft; start a new draft.";
    private const string PriceItem = "PRICE";
    private const int RefusedStatus = 422;
    private const int OpenItemStatus = 501;
    private const string PreviewRefusalKey = "Billing.Invoicing.Api.PreviewRefusal";

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
    private static readonly OracleFailureTranslator Failures = new();
    private static readonly IReadOnlySet<string> NoRequestedServiceIds = new HashSet<string>();

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
    /// <returns>The new draft with no lines, carrying a preset doctor's clinic, with the doctor and clinic names.</returns>
    public async Task<NewDraftResponse> NewDraft(
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (ParameterWidthMessages(parameters) is { Count: > 0 } tooWide)
        {
            throw WidthRejected(tooWide, nameof(parameters));
        }

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
        header = header with { PayType = await DecideDraftPayType(header.CompCode, null, parameters, preload, cancellationToken) };

        var display = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (await ReadDoctorClinic(header, cancellationToken) is { } doctor)
        {
            header = doctor.Header;
            display[DocNameItem] = doctor.DocName;
            display[ClinicNameItem] = doctor.ClinicName;
        }

        if (ClaimNumberFromParameter(parameters))
        {
            header = header with { ClaimNo = ClaimNumberRule.Build(header, parameters) };
        }

        var requestId = NewId();
        return new NewDraftResponse
        {
            Draft = new DraftDto
            {
                RequestId = requestId,
                DraftDate = databaseTime,
                DraftSeal = _invoiceApi.SealDraftDate(requestId, databaseTime),
                Header = header,
                Lines = Array.Empty<InvoiceLineDraft>(),
                Parameters = parameters,
                DiscountLimitChoice = null,
            },
            Display = display,
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
            "FINALDISC_PERC" or "FINALDISC" => await ValidateFinalDiscount(draft, target, operatorContext, cancellationToken),
            "AMOUNT_1" => await ValidateFirstAmount(draft, operatorContext, cancellationToken),
            "AMOUNT_2" => ValidateSecondAmount(draft),
            "SUB_PAYTYPE" => new ValidateDraftResponse(),
            "RECORD" => await ValidateRecord(draft, cancellationToken),
            "LINE" or "SERVICEID" or "QTY" or "LDISCT" or "APPROV_REF_NO" or "PRICE" or "DISC" or "MY_DISC" =>
                await ValidateLine(draft, request.LineIndex, operatorContext, cancellationToken),
            _ => RejectedInput(TargetField, UnknownTargetText),
        };
    }

    /// <summary>Returns the patient's coverage snapshot with its eligibility messages, pay type and advisory open items.</summary>
    /// <param name="patientNo">Patient number to read.</param>
    /// <param name="draftDate">Draft date the coverage is checked against; the database time when null.</param>
    /// <param name="parameters">Entry parameters of the draft.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The coverage response; the blocking DR-01 patient message when the patient number is blank; the blocking width messages when PATIENTNO, CLAIM_NO or VISIT_UNIQUE is wider than its item; the blocking DR-03 message alone when V_PAT_DATA has no row.</returns>
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

        if (PatientNoTooLong(patientNo) is { } tooLong)
        {
            return new CoverageResponse { Messages = new[] { tooLong } };
        }

        if (ParameterWidthMessages(parameters) is { Count: > 0 } tooWide)
        {
            return new CoverageResponse { Messages = tooWide };
        }

        var coverage = await _lookups.GetPatientCoverage(patientNo, cancellationToken);
        var asOf = draftDate ?? await _lookups.GetDatabaseTime(cancellationToken);
        var findings = new Findings();
        findings.Add(PatientEligibilityRules.Evaluate(coverage, parameters, asOf));
        if (coverage is null)
        {
            return new CoverageResponse { Messages = findings.Messages };
        }

        var preload = await ReadPatientClaimPreload(parameters, patientNo, cancellationToken);
        var payType = await DecidePayType(coverage?.CompCode, coverage, parameters, preload, cancellationToken);
        var (subCompCode, classCode) = ServerSubCompanyAndClass(payType, coverage, preload);

        var header = new InvoiceHeaderDraft
        {
            PatientNo = patientNo,
            PayType = payType,
            CompCode = coverage?.CompCode,
            SubCompCode = subCompCode,
            ClassCode = classCode,
            DraftDate = asOf,
        };
        return await CoverageOf(header, payType, coverage, preload, parameters, findings.Messages, cancellationToken);
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

        var profileReads = new ProfileReads(_lookups, _invoices, sanitized.Parameters.VisitUnique);
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

        var preview = await CalculateVettedPreview(header, sanitized.Lines, operatorContext, profileReads, cancellationToken);

        var lists = await RequireLists(
            sanitized.Lines, LineLists(sanitized.Lines, preview, context.Preload), header, operatorContext, cancellationToken);
        var profiles = await BuildProfiles(sanitized.Lines, lists, profileReads, cancellationToken);
        var findings = new Findings();
        var isDirect = profileReads.CompanyIsDirect(header, cancellationToken);
        await AddPriceOverrideRules(
            findings, header, sanitized.Lines, profiles.ByLine, preview, profileReads, cancellationToken);
        var priceEditableClientIds = new List<string>();
        for (var index = 0; index < sanitized.Lines.Count; index++)
        {
            var line = sanitized.Lines[index];
            if (await PriceEditable(header, line, profiles.ByLine[index], preview, isDirect))
            {
                priceEditableClientIds.Add(line.ClientId!);
            }
        }

        var openItems = OpenItemGate.Evaluate(
            header,
            profiles.TopLevel,
            gate.CardId,
            gate.MaxDeductable,
            gate.UseAdvanced,
            parameters);

        var amount1 = preview.Totals.Amount1 ?? header.Amount1;
        var amount2 = preview.Totals.Amount2 ?? header.Amount2;
        var refund = PaymentAllocationRules.Refund(new PaymentAllocation
        {
            Amount1 = amount1,
            Amount2 = amount2,
            CashPayed = header.CashPayed,
            SubPayType = header.SubPayType,
            SubPayType2 = header.SubPayType2,
        });

        return new PreviewResponse
        {
            Lines = preview.Lines,
            Totals = ToTotals(preview.Totals),
            Refund = refund,
            TotalCollected = PaymentAllocationRules.TotalCollected(amount1, amount2),
            Messages = findings.Messages,
            OpenItems = openItems,
            PriceEditableClientIds = priceEditableClientIds,
            PriceJudgedPatientNo = Trimmed(sanitized.Header.PatientNo),
            PriceJudgedCompCode = Trimmed(sanitized.Header.CompCode),
        };
    }

    /// <summary>Saves the draft as an invoice after re-running every check server-side, or replays an earlier create of the same request id.</summary>
    /// <param name="request">Draft to save, carrying its request id and discount-limit choice.</param>
    /// <param name="operatorContext">Operator identity bound into the package header.</param>
    /// <param name="cancellationToken">Cancels the reads and the save.</param>
    /// <returns>The saved or replayed invoice with its warnings and open items, or no invoice with the blocking and warning messages and gate ids when the draft cannot be saved.</returns>
    public async Task<CreateInvoiceOutcome> Create(
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

        if (await _invoices.GetCreateRequest(request.Draft.RequestId, cancellationToken) is { } recorded)
        {
            return Saved(await Replay(
                request.Draft.RequestId, request.Draft.Header?.PatientNo, request.Draft.DraftDate, recorded, operatorContext, cancellationToken));
        }

        if (!HasIssuedDraftDate(request.Draft))
        {
            return DraftDateRejected();
        }

        if (request.Draft.LinesRefusal is { } linesRefusal)
        {
            return LinesRefused(linesRefusal);
        }

        var engineLines = EngineLineCount(request.Draft.Lines ?? Array.Empty<InvoiceLineDraft>());
        if (engineLines > _invoiceApi.MaxDraftLines)
        {
            return LinesRejected(engineLines, _invoiceApi.MaxDraftLines);
        }

        var draft = Sanitize(request.Draft, operatorContext);

        if (!InvoiceStatePolicy.CanEdit(draft.Header.InvNo))
        {
            throw OpenItem(OpenItemIds.OI56, SavedInvoiceEditTitle);
        }

        var parameters = draft.Parameters;
        var lines = draft.Lines;
        var preload = await ReadPatientClaimPreload(parameters, draft.Header.PatientNo, cancellationToken);
        var coverage = await ReadCoverage(draft.Header.PatientNo, cancellationToken);
        if (LacksCoverageRow(draft.Header.PatientNo, coverage))
        {
            var missingRow = new Findings();
            missingRow.Add(PatientEligibilityRules.Evaluate(coverage, parameters, draft.DraftDate));
            return new CreateInvoiceOutcome { Messages = missingRow.Messages };
        }

        var context = await DecideServerContext(draft, preload, coverage, patientCompanyFirst: false, cancellationToken);
        var gate = await ReadGateInputs(context.Header, context.Coverage, context.Preload, readCard: true, cancellationToken);
        var maxDisc = await _lookups.GetUserMaxDiscount(operatorContext.UserNo, cancellationToken);
        var x422 = await ReadX422(parameters, cancellationToken);

        var findings = new Findings();
        var header = await ApplyVisitDoctor(context.Header, parameters, cancellationToken);
        header = ApplySubPayType(header, findings.Add(HeaderRecordRules.ApplyPaymentTypeDefault(header)));
        header = ApplyDoctor(header, findings.Add(DoctorSelectionRules.Validate(header, parameters)));
        header = (await ReadDoctorClinic(header, cancellationToken))?.Header ?? header;
        header = header with { ClaimNo = ClaimNumberRule.Build(header, parameters) };
        var clinic = await ReadClinic(header, cancellationToken);

        findings.Add(HeaderRecordRules.ValidateRecord(header));
        findings.Add(InvoiceDetailRules.RequireDetails(lines.Count));
        if (!IsBlank(draft.Header.PatientNo))
        {
            findings.Add(PatientEligibilityRules.Evaluate(context.Coverage, parameters, draft.DraftDate));
        }

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

        var profileReads = new ProfileReads(_lookups, _invoices, parameters.VisitUnique);
        var succeeded = new List<PreviewResult>();
        LineContext lineContext;
        IReadOnlyDictionary<decimal, IReadOnlySet<string>> requested;
        string? preflightOpenItem = null;
        try
        {
            lineContext = await ReadLineContext(
                lines, header, context.Preload, Array.Empty<string>(), previewAllowed, succeeded, operatorContext, profileReads, cancellationToken);
            requested = await ReadRequestedServices(header.ClaimNo, lineContext.Profiles.Lists, cancellationToken);
        }
        catch (Exception failure) when (RefusesDraft(failure))
        {
            preflightOpenItem = OpenItemIdOf(failure);
            (lineContext, requested) = await FallbackLineContext(
                lines, succeeded, context.Preload, header.ClaimNo, profileReads, cancellationToken);
            if (!findings.IsBlocking
                && !LineRulesBlock(header, lines, Enumerable.Range(0, lines.Count), lineContext.Preview, x422, lineContext.Profiles, requested)
                && !await PriceOverridesBlock(header, lines, lineContext.Profiles.ByLine, profileReads, cancellationToken)
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

        await AddPriceOverrideRules(
            findings, header, lines, profiles.ByLine, lineContext.Preview, profileReads, cancellationToken);

        var gateIds = new SortedSet<string>(
            OpenItemGate.Evaluate(header, profiles.TopLevel, gate.CardId, gate.MaxDeductable, gate.UseAdvanced, parameters),
            StringComparer.Ordinal);
        if (header.DeptWise == 1 || header.Call == 1)
        {
            gateIds.Add(OpenItemIds.OI33);
        }

        if (findings.IsBlocking)
        {
            if (preflightOpenItem is not null)
            {
                gateIds.Add(preflightOpenItem);
            }

            return new CreateInvoiceOutcome
            {
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
            var discounted = await RulesPreview(header, lines, NoExcludedLines, operatorContext, profileReads, cancellationToken);
            var reset = PaymentAllocationRules.ResetAfterDiscountChange(discounted?.Totals.CashCollected);
            header = header with
            {
                Amount1 = ToDecimal(reset.Adjusted[Amount1Item]),
                Amount2 = ToDecimal(reset.Adjusted[Amount2Item]),
            };
            header = ApplySubPayType(header, findings.Add(HeaderRecordRules.ApplyPaymentTypeDefault(header)));
        }

        return Saved(await CreateNew(draft.RequestId, header, lines, clinic, findings, operatorContext, cancellationToken));
    }

    /// <summary>Returns a saved invoice as a read-only view, restricted to the ROW_TYPE of the request's LOCAL_DOC_TYPE, or null when it is not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="parameters">Entry parameters of the request; LOCAL_DOC_TYPE is read.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The invoice header, lines and display values, or null.</returns>
    /// <exception cref="ArgumentException">LOCAL_DOC_TYPE is not 505, 532 or 783; nothing is read.</exception>
    public async Task<InvoiceViewResponse?> GetInvoice(
        long invNo,
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (parameters.LocalDocType is not (505 or 532 or 783))
        {
            throw InvalidLocalDocType(parameters.LocalDocType);
        }

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

    /// <summary>Returns the persisted insurance, line and transfer details of a saved invoice, restricted to the ROW_TYPE of the request's LOCAL_DOC_TYPE, or null when it is not found.</summary>
    /// <param name="invNo">Invoice number.</param>
    /// <param name="parameters">Entry parameters of the request; LOCAL_DOC_TYPE is read.</param>
    /// <param name="operatorContext">Operator identity of the request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The details, or null.</returns>
    /// <exception cref="ArgumentException">LOCAL_DOC_TYPE is not 505, 532 or 783; nothing is read.</exception>
    public async Task<MoreDetailsResponse?> GetMoreDetails(
        long invNo,
        InvoiceEntryParameters parameters,
        OperatorContext operatorContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (parameters.LocalDocType is not (505 or 532 or 783))
        {
            throw InvalidLocalDocType(parameters.LocalDocType);
        }

        if (await _invoices.GetMoreDetails(invNo, parameters.LocalDocType, cancellationToken) is not { } details)
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
    /// <returns>Imported lines, the import result (zero counts when no request row is selected) and the notices, or the blocking doctor, patient, visit or rejected-selected-row message.</returns>
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
        var x422 = await ReadX422(parameters, cancellationToken);

        IReadOnlyList<(long PatServReqRowId, string ServiceId, int? ReqAStatus, int? ReqNeedA, string? ApprovRefNo)> rows;
        try
        {
            rows = await _invoices.GetSelectedRequestRows(
                header.PatientNo!, parameters.VisitUnique!, payType, cancellationToken);
        }
        catch (Exception failure) when (failure is InvalidCastException or OverflowException)
        {
            return new ImportResponse
            {
                Messages = new[]
                {
                    new MessageDto
                    {
                        Field = null,
                        Text = SelectedRequestRowUnreadableText,
                        Severity = ValidationMessage.Blocking,
                        Rule = null,
                    },
                },
            };
        }

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
            try
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
            finally
            {
                await TryRollback(session);
            }
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
            try
            {
                visit = await _import.GetVisitLine(session, header, operatorContext, choice, cancellationToken);
            }
            finally
            {
                await TryRollback(session);
            }
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
    /// <returns>Imported lines, the import result, the warning and the adjusted ADD_TO_LIST, or the blocking SERVICEID message when the package service id is blank or wider than SERVICEID.</returns>
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

        if (TooWide(ServiceIdItem, request.PackageServiceId.Trim(), ServiceIdBytes) is { } packageTooWide)
        {
            return new ImportResponse { Messages = new[] { packageTooWide } };
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

        var profileReads = new ProfileReads(_lookups, _invoices, draft.Parameters.VisitUnique);
        var (_, preview) = await ContextPreview(
            draft.Lines,
            LocallyRejectedLines(draft.Lines),
            new[] { packageServiceId },
            preload,
            header,
            new List<PreviewResult>(),
            operatorContext,
            profileReads,
            cancellationToken);
        var listId = await ResolveListId(preview, preload, header, operatorContext, cancellationToken);

        (IReadOnlyList<EngineLineInput> Lines, ImportResultRow Result) package;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            try
            {
                package = await _invoiceApi.GetPackageLines(
                    session, packageServiceId, listId, request.ParentSourceId, cancellationToken);
            }
            finally
            {
                await TryRollback(session);
            }
        }

        var imported = package.Lines.Select(ToLine).ToArray();
        var findings = new Findings();
        findings.Add(PackageImportRules.Evaluate(imported.Length));

        var lists = LineLists(draft.Lines, preview, preload)
            .Select(list => (decimal?)(list ?? listId))
            .Concat(imported.Select(_ => (decimal?)listId))
            .ToArray();
        var profiles = await BuildProfiles(draft.Lines.Concat(imported).ToArray(), lists, profileReads, cancellationToken);
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
            try
            {
                lines = await _invoiceApi.GetBundledOfferLines(
                    session, header, operatorContext, request.OfferId, request.BundleQty, cancellationToken);
            }
            finally
            {
                await TryRollback(session);
            }
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
    /// <returns>The rows, the blocking messages naming every missing or refused item, or null.</returns>
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
                if (BindText(values, CompCodeItem) is not { } compCode)
                {
                    return MissingBind(lov, CompCodeItem);
                }

                return TooWide(CompCodeItem, compCode, CompCodeBytes) is { } compCodeTooLong
                    ? BindRejected(lov, compCodeTooLong)
                    : Rows(lov, await _lovs.SubCompany(compCode, operatorContext.InfoCenterId, cancellationToken));
            case "THE_CLASS":
                if (BindText(values, SubCompCodeItem) is not { } subCompCode)
                {
                    return MissingBind(lov, SubCompCodeItem);
                }

                return TooWide(SubCompCodeItem, subCompCode, SubCompCodeBytes) is { } subCompCodeTooLong
                    ? BindRejected(lov, subCompCodeTooLong)
                    : Rows(lov, await _lovs.TheClass(subCompCode, operatorContext.InfoCenterId, cancellationToken));
            case "PAY_TYPE1" or "PAY_TYPE2":
                return Rows(lov, await _lovs.PayTypes(cancellationToken));
            case "DOC":
                return Rows(lov, await _lovs.Doc(operatorContext.InfoCenterId, cancellationToken));
            case "RESERV_NO":
                var reservationRefusals = new List<MessageDto>();
                var (doctorPresent, doctor) = BindPositiveInt(values, DocIdItem);
                if (!doctorPresent)
                {
                    reservationRefusals.Add(RequiredBind(lov, DocIdItem));
                }
                else if (doctor is null)
                {
                    reservationRefusals.Add(Blocking(DocIdItem, $"{DocIdItem} must be a positive whole number."));
                }

                var patientNo = BindText(values, PatientNoItem);
                if (patientNo is null)
                {
                    reservationRefusals.Add(RequiredBind(lov, PatientNoItem));
                }
                else if (PatientNoTooLong(patientNo) is { } tooLong)
                {
                    reservationRefusals.Add(tooLong);
                }

                if (draftDate is null)
                {
                    reservationRefusals.Add(RequiredBind(lov, InvDateItem));
                }

                if (reservationRefusals.Count > 0 || doctor is not { } docId || patientNo is null || draftDate is not { } reservationDate)
                {
                    return BindRejected(lov, reservationRefusals.ToArray());
                }

                return Rows(lov, await _lovs.ReservNo(reservationDate, docId, patientNo, operatorContext.InfoCenterId, cancellationToken)) with
                {
                    ViewOnly = true,
                    OpenItems = new[] { OpenItemIds.OI42 },
                };
            case "OFFERS":
                var offerRefusals = new List<MessageDto>();
                var (payTypePresent, payTypeNumber) = BindPositiveInt(values, PayTypeItem);
                if (!payTypePresent)
                {
                    offerRefusals.Add(RequiredBind(lov, PayTypeItem));
                }
                else if (payTypeNumber is not (CashPayType or CreditPayType))
                {
                    offerRefusals.Add(Blocking(PayTypeItem, $"{PayTypeItem} must be 1 (Cash) or 2 (Credit)."));
                }

                if (draftDate is null)
                {
                    offerRefusals.Add(RequiredBind(lov, InvDateItem));
                }

                if (offerRefusals.Count > 0 || payTypeNumber is not { } payType || draftDate is not { } offerDate)
                {
                    return BindRejected(lov, offerRefusals.ToArray());
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

    /// <summary>Replays a recorded request id through the package and returns the advisory open items of the recorded invoice without any read after the commit.</summary>
    private async Task<CreateInvoiceResponse> Replay(
        string requestId,
        string? patientNo,
        DateTime draftDate,
        (long? InvNo, string? PatientNo, DateTimeOffset? CompletedAt, DateTime? InvDate, string? CompCode, string? SubCompCode, bool ClinicHasAgeLimit) recorded,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var invDate = recorded.InvDate ?? draftDate;
        var header = WithOperator(
            new InvoiceHeaderDraft { PatientNo = patientNo, DraftDate = invDate, InvDate = invDate },
            operatorContext);

        FullInvoiceResultRow result;
        var replayed = recorded;
        await using (var session = await _sessionFactory.Open(cancellationToken))
        {
            try
            {
                result = await _invoiceApi.CreateFullInvoice(
                    session, header, Array.Empty<InvoiceLineDraft>(), operatorContext, requestId, cancellationToken);
                var invNo = RequireInvoiceNumber(result);
                if (replayed.InvNo != invNo)
                {
                    replayed = await _invoices.GetCreateRequest(requestId, cancellationToken) is { } reread && reread.InvNo == invNo
                        ? reread
                        : throw new InvalidOperationException(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"The package replayed invoice {invNo}, which is not the invoice recorded for request {requestId}."));
                }
            }
            catch (Exception)
            {
                await TryRollback(session);
                throw;
            }

            await session.Commit(cancellationToken);
        }

        var openItems = new SortedSet<string>(StringComparer.Ordinal) { OpenItemIds.OI20 };
        AddHeaderOpenItems(openItems, new InvoiceHeaderDraft { CompCode = replayed.CompCode, SubCompCode = replayed.SubCompCode });
        if (replayed.ClinicHasAgeLimit)
        {
            openItems.Add(OpenItemIds.OI22);
        }

        return new CreateInvoiceResponse
        {
            InvNo = RequireInvoiceNumber(result),
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
                var replayed = await AnsweredByReplay(requestId, RequireInvoiceNumber(result), cancellationToken);

                if (ReceptionTransferRule.ShouldClear(isReplay: replayed, hasNewInvDocId: true)
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

                // A package replay skips the total check and lists OI-20.
                if (replayed)
                {
                    openItems.Add(OpenItemIds.OI20);
                }
                else
                {
                    try
                    {
                        await _legacy.ValidateTotalInvoice(session, RequireInvoiceNumber(result), cancellationToken);
                    }
                    catch (NotImplementedException failure) when (IsOpenItem(failure, OpenItemIds.OI20))
                    {
                        openItems.Add(OpenItemIds.OI20);
                    }
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
            InvNo = RequireInvoiceNumber(result),
            Message = result.Message,
            PostingFlags = PostingFlags(result),
            Messages = findings.Warnings,
            OpenItems = openItems.ToArray(),
        };
    }

    /// <summary>Returns whether the package answered the create with its replay, read as a request row already committed for the returned invoice.</summary>
    /// <exception cref="InvalidOperationException">The committed request row names another invoice than the one returned.</exception>
    private async Task<bool> AnsweredByReplay(string requestId, long invNo, CancellationToken cancellationToken)
    {
        if (await _invoices.GetCreateRequest(requestId, cancellationToken) is not { } committed)
        {
            return false;
        }

        if (committed.InvNo != invNo)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The package returned invoice {invNo}, which is not the invoice recorded for request {requestId}."));
        }

        return true;
    }

    private async Task<bool> ClearReceptionTransfer(IOracleSession session, string? patientNo, CancellationToken cancellationToken)
    {
        if (IsBlank(patientNo))
        {
            return true;
        }

        await session.Save(ReceptionTransferSavepoint, cancellationToken);
        try
        {
            await _patientTransfer.ClearReceptionTransfer(session, patientNo!, cancellationToken);
            return true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            await session.Rollback(ReceptionTransferSavepoint, cancellationToken);
            return false;
        }
    }

    private async Task<ValidateDraftResponse> ValidatePatient(DraftDto draft, CancellationToken cancellationToken)
    {
        var patientNo = draft.Header.PatientNo;
        var preload = await ReadPatientClaimPreload(draft.Parameters, patientNo, cancellationToken);
        var coverage = await ReadCoverage(patientNo, cancellationToken);
        var findings = new Findings();
        if (!IsBlank(patientNo))
        {
            findings.Add(PatientEligibilityRules.Evaluate(coverage, draft.Parameters, draft.DraftDate));
        }

        if (LacksCoverageRow(patientNo, coverage))
        {
            return findings.ToValidateResponse();
        }

        var context = await DecideServerContext(draft, preload, coverage, patientCompanyFirst: true, cancellationToken);
        findings.Adjust(PayTypeItem, context.Header.PayType);
        if (!IsBlank(context.Header.SubCompCode))
        {
            findings.AddOpenItem(OpenItemIds.OI21);
        }

        if (IsBlank(draft.Header.PatientNo) || context.Header.PayType is not int payType)
        {
            return findings.ToValidateResponse();
        }

        var coverageResponse = await CoverageOf(
            context.Header, payType, context.Coverage, context.Preload, draft.Parameters, findings.Messages, cancellationToken);
        findings.AddOpenItems(coverageResponse.OpenItems);
        return findings.ToValidateResponse() with { Coverage = coverageResponse };
    }

    /// <summary>Returns the coverage response of a server-read header: OI-24 always, OI-21 for a sub-company, OI-23 when the credit gate holds.</summary>
    private async Task<CoverageResponse> CoverageOf(
        InvoiceHeaderDraft header,
        int payType,
        PatientCoverageSnapshot? coverage,
        ClaimPreloadData? preload,
        InvoiceEntryParameters parameters,
        IReadOnlyList<MessageDto> messages,
        CancellationToken cancellationToken)
    {
        var openItems = new SortedSet<string>(StringComparer.Ordinal) { OpenItemIds.OI24 };
        if (!IsBlank(header.SubCompCode))
        {
            openItems.Add(OpenItemIds.OI21);
        }

        var gate = await ReadGateInputs(header, coverage, preload, readCard: false, cancellationToken);
        if (OpenItemGate.Evaluate(header, Array.Empty<ServiceProfile>(), null, gate.MaxDeductable, gate.UseAdvanced, parameters)
            .Contains(OpenItemIds.OI23, StringComparer.Ordinal))
        {
            openItems.Add(OpenItemIds.OI23);
        }

        return new CoverageResponse
        {
            Coverage = coverage,
            PayType = payType,
            Messages = messages,
            OpenItems = openItems.ToArray(),
        };
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
        var profileReads = new ProfileReads(_lookups, _invoices, draft.Parameters.VisitUnique);
        var parameters = draft.Parameters;
        var findings = new Findings();
        var header = ApplyDoctor(draft.Header, findings.Add(DoctorSelectionRules.Validate(draft.Header, parameters)));
        if (await ReadDoctorClinic(header, cancellationToken) is { } doctor)
        {
            header = doctor.Header;
            findings.Adjust(ClinicIdItem, header.ClinicId);
            findings.Adjust(ClinicNameItem, doctor.ClinicName);
            findings.Adjust(DocNameItem, doctor.DocName);
            findings.Adjust(ClaimNoItem, ClaimNumberRule.Build(header, parameters));
        }

        var visitLine = header.DocId is null
            ? VisitLineChoice.None
            : VisitLineRule.Choose(parameters.DoReview, parameters.ClaimNo, header.CompCode, header.ClinicId);

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
                profileReads,
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
        string target,
        OperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        // FINALDISC_PERC is checked in rate mode only, FINALDISC in value mode only; an empty FINALDISC only clears the percent.
        var rateTarget = string.Equals(target, FinalDiscPercItem, StringComparison.Ordinal);
        if ((draft.Header.DiscT ?? ValueDiscountMode) != (rateTarget ? RateDiscountMode : ValueDiscountMode))
        {
            return new ValidateDraftResponse();
        }

        if (!rateTarget && (draft.Header.FinalDisc ?? 0m) == 0m)
        {
            var cleared = new Findings();
            cleared.Adjust(FinalDiscPercItem, 0m);
            return cleared.ToValidateResponse();
        }

        var profileReads = new ProfileReads(_lookups, _invoices, draft.Parameters.VisitUnique);
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        await GuardDeductible(header, draft.Parameters, context, cancellationToken);

        var maxDisc = await _lookups.GetUserMaxDiscount(operatorContext.UserNo, cancellationToken);
        var preview = await RulesPreview(header, draft.Lines, NoExcludedLines, operatorContext, profileReads, cancellationToken);

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
                preview = await RulesPreview(adjusted, draft.Lines, NoExcludedLines, operatorContext, profileReads, cancellationToken);
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
        var profileReads = new ProfileReads(_lookups, _invoices, draft.Parameters.VisitUnique);
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header;
        await GuardDeductible(header, draft.Parameters, context, cancellationToken);

        var preview = await RulesPreview(header, draft.Lines, NoExcludedLines, operatorContext, profileReads, cancellationToken);
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

        var profileReads = new ProfileReads(_lookups, _invoices, draft.Parameters.VisitUnique);
        var parameters = draft.Parameters;
        var context = await ReadServerContext(draft, patientCompanyFirst: false, cancellationToken);
        var header = context.Header with { ClaimNo = ClaimNumberRule.Build(context.Header, parameters) };
        var line = draft.Lines[index];
        var x422 = await ReadX422(parameters, cancellationToken);
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
        IReadOnlyDictionary<decimal, IReadOnlySet<string>> requested;
        string? preflightOpenItem = null;
        try
        {
            lineContext = await ReadLineContext(
                draft.Lines, header, context.Preload, Array.Empty<string>(), previewAllowed, succeeded, operatorContext, profileReads, cancellationToken);
            requested = await ReadRequestedServices(header.ClaimNo, lineContext.Profiles.Lists, cancellationToken);
        }
        catch (Exception failure) when (RefusesDraft(failure))
        {
            preflightOpenItem = OpenItemIdOf(failure);
            (lineContext, requested) = await FallbackLineContext(
                draft.Lines, succeeded, context.Preload, header.ClaimNo, profileReads, cancellationToken);
            if (!findings.IsBlocking
                && !LineRulesBlock(header, draft.Lines, new[] { index }, lineContext.Preview, x422, lineContext.Profiles, requested)
                && !await PriceOverridesBlock(
                    header, new[] { draft.Lines[index] }, new[] { lineContext.Profiles.ByLine[index] }, profileReads, cancellationToken))
            {
                throw;
            }
        }

        var profiles = lineContext.Profiles;
        var profile = profiles.ByLine[index];
        AddLineRules(findings, header, line, profile, lineContext.Preview, x422, RequestedFor(requested, profiles.Lists, index));
        var isDirect = profileReads.CompanyIsDirect(header, cancellationToken);
        await AddPriceOverrideRules(
            findings, header, new[] { line }, new[] { profile }, lineContext.Preview, profileReads, cancellationToken);
        var priceEditable = await PriceEditable(header, line, profile, lineContext.Preview, isDirect);
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
            if (preflightOpenItem is not null)
            {
                findings.AddOpenItem(preflightOpenItem);
            }

            return WithPriceJudgement(findings.ToValidateResponse(), draft, line, priceEditable);
        }

        if (gateIds.Count > 0)
        {
            await ThrowGate(gateIds, findings.Warnings, header, cancellationToken);
        }

        return WithPriceJudgement(findings.ToValidateResponse(), draft, line, priceEditable);
    }

    private static void AddLineRules(
        Findings findings,
        InvoiceHeaderDraft header,
        InvoiceLineDraft line,
        ServiceProfile? profile,
        PreviewResult? preview,
        int? x422ApprovCheck,
        IReadOnlySet<string> requestedServices)
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
        IReadOnlyDictionary<decimal, IReadOnlySet<string>> requested)
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
        ProfileReads profileReads,
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
            profileReads,
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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<EditablePreviewLine>, ReqNeedAIndex> ReqNeedAIndexes = new();

    private static int? ServerReqNeedA(InvoiceLineDraft line, ServiceProfile? profile, PreviewResult? preview)
    {
        if (preview?.Lines is not { Count: > 0 } previewLines)
        {
            return profile?.ReqNeedA;
        }

        return ReqNeedAIndexes.GetValue(previewLines, ReqNeedAIndex.Of).Find(line) ?? profile?.ReqNeedA;
    }

    /// <summary>REQ_NEED_A of the preview lines by client id and by trimmed service id: per key, the first non-zero value in preview order, else 0, else null.</summary>
    private sealed class ReqNeedAIndex
    {
        private readonly Dictionary<string, int?> _byClientId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int?> _byServiceId = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Builds the index of the preview lines in one pass.</summary>
        /// <param name="previewLines">Preview lines in package order.</param>
        /// <returns>The index.</returns>
        public static ReqNeedAIndex Of(IReadOnlyList<EditablePreviewLine> previewLines)
        {
            var index = new ReqNeedAIndex();
            foreach (var previewLine in previewLines)
            {
                if (previewLine.ClientId is { } clientId)
                {
                    Fold(index._byClientId, clientId, previewLine.ReqNeedA);
                }

                if (Trimmed(previewLine.ServiceId) is { } serviceId)
                {
                    Fold(index._byServiceId, serviceId, previewLine.ReqNeedA);
                }
            }

            return index;
        }

        /// <summary>Returns the value of the line's client id; of its service id when no preview line has that client id; null when neither matches or the match has no value.</summary>
        /// <param name="line">Draft line.</param>
        public int? Find(InvoiceLineDraft line)
        {
            if (!IsBlank(line.ClientId) && _byClientId.TryGetValue(line.ClientId!, out var byClientId))
            {
                return byClientId;
            }

            return Trimmed(line.ServiceId) is { } serviceId && _byServiceId.TryGetValue(serviceId, out var byServiceId)
                ? byServiceId
                : null;
        }

        /// <summary>Records the key with its first value, then replaces a null with any value and a 0 with a non-zero value.</summary>
        private static void Fold(Dictionary<string, int?> values, string key, int? value)
        {
            if (!values.TryGetValue(key, out var current))
            {
                values.Add(key, value);
            }
            else if (value is not null && (current is null || (current == 0 && value != 0)))
            {
                values[key] = value;
            }
        }
    }

    private async Task AddPriceOverrideRules(
        Findings findings,
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<ServiceProfile?> profiles,
        PreviewResult? preview,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var isDirect = profileReads.CompanyIsDirect(header, cancellationToken);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!await IsManualPriceOverride(header, line, profileReads, cancellationToken))
            {
                continue;
            }

            var refused = !await OverrideAllowed(header, line, profiles[index], preview, isDirect);
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

    /// <summary>Returns true when the line's bound price override is the operator's: every override on a line that binds one, except on a selected request row of the line's service.</summary>
    private static async Task<bool> IsManualPriceOverride(
        InvoiceHeaderDraft header,
        InvoiceLineDraft line,
        ProfileReads profileReads,
        CancellationToken cancellationToken) =>
        line.PriceOverride is not null
        && BindsPriceOverride(line)
        && (line.PatServReqRowId is null || !await profileReads.IsSelectedRequestRow(header, line, cancellationToken));

    private static bool AcceptsManualPrice(InvoiceLineDraft line) =>
        line.PatServReqRowId is null
        && BindsPriceOverride(line);

    /// <summary>Returns true when the line is neither a package component nor an offer line.</summary>
    private static bool BindsPriceOverride(InvoiceLineDraft line) =>
        !HasRole(line, ComponentRole)
        && line.OfferId is null
        && IsBlank(line.OfferLineRole);

    private static async Task<bool> OverrideAllowed(
        InvoiceHeaderDraft header,
        InvoiceLineDraft line,
        ServiceProfile? profile,
        PreviewResult? preview,
        Lazy<Task<int?>> isDirect) =>
        preview is not null && preview.CheckedOverrides.Contains(line)
            ? !preview.RefusedOverrides.Contains(line)
            : await PriceOverrideAllowed(header, profile, isDirect);

    /// <summary>Returns true when an operator-entered PRICE is accepted on the line.</summary>
    private static async Task<bool> PriceEditable(
        InvoiceHeaderDraft header,
        InvoiceLineDraft line,
        ServiceProfile? profile,
        PreviewResult? preview,
        Lazy<Task<int?>> isDirect) =>
        !IsBlank(line.ServiceId) && AcceptsManualPrice(line) && await OverrideAllowed(header, line, profile, preview, isDirect);

    private static ValidateDraftResponse WithPriceJudgement(
        ValidateDraftResponse response,
        DraftDto draft,
        InvoiceLineDraft line,
        bool priceEditable) => response with
        {
            PriceEditable = priceEditable,
            PriceJudgedServiceId = Trimmed(line.ServiceId),
            PriceJudgedPatientNo = Trimmed(draft.Header.PatientNo),
            PriceJudgedCompCode = Trimmed(draft.Header.CompCode),
        };

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

    /// <summary>Returns true when the failure refuses the draft rather than a server read: the package's or binder's refusal of a workflow preview, or the price plan's OI-24.</summary>
    private static bool RefusesDraft(Exception failure) =>
        IsPreviewRefusal(failure)
        || (failure is NotImplementedException openItem && IsOpenItem(openItem, OpenItemIds.OI24));

    /// <summary>Returns true when the failure is the package's or binder's refusal (422) of a workflow preview.</summary>
    private static bool IsPreviewRefusal(Exception failure) => failure.Data[PreviewRefusalKey] is true;

    /// <summary>Returns true when the package refused a workflow preview as holding an unexpanded package parent.</summary>
    private static bool IsUnexpandedParentRefusal(Exception failure) =>
        IsPreviewRefusal(failure)
        && string.Equals(Failures.Translate(failure)?.Kind, OracleErrorCatalog.UnexpandedPackageParentKind, StringComparison.Ordinal);

    /// <summary>Returns the open-item id of a 501 failure, or null.</summary>
    private static string? OpenItemIdOf(Exception failure) =>
        Failures.Translate(failure) is { Status: OpenItemStatus, OpenItemId: { } openItemId } ? openItemId : null;

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
        var preload = await ReadPatientClaimPreload(draft.Parameters, draft.Header.PatientNo, cancellationToken);
        var coverage = await ReadCoverage(draft.Header.PatientNo, cancellationToken);
        return await DecideServerContext(draft, preload, coverage, patientCompanyFirst, cancellationToken);
    }

    private async Task<ServerContext> DecideServerContext(
        DraftDto draft,
        ClaimPreloadData? preload,
        PatientCoverageSnapshot? coverage,
        bool patientCompanyFirst,
        CancellationToken cancellationToken)
    {
        var header = draft.Header;
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

    /// <summary>Returns the DR-24 pay type to decide or bind; a null inherited pay type throws <see cref="PayTypeRequired"/>.</summary>
    private async Task<int> DecidePayType(
        string? compCode,
        PatientCoverageSnapshot? coverage,
        InvoiceEntryParameters parameters,
        ClaimPreloadData? preload,
        CancellationToken cancellationToken) =>
        await DecideDraftPayType(compCode, coverage, parameters, preload, cancellationToken) ?? throw PayTypeRequired();

    /// <summary>Returns the DR-24 pay type of the draft; null when the claim preload's inherited pay type is null.</summary>
    private async Task<int?> DecideDraftPayType(
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

    private const string PayTypeRequiredText = "FRM-40202: Field must be entered.";

    /// <summary>Returns the 422 field-validation failure carrying the blocking PAYTYPE required message.</summary>
    private static ArgumentException PayTypeRequired()
    {
        var failure = new ArgumentException(PayTypeRequiredText, PayTypeItem);
        failure.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(PayTypeItem, PayTypeRequiredText) };
        return failure;
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
        if (PreloadClaimNo(parameters) is not { } claimNo)
        {
            return null;
        }

        return await _invoices.GetClaimPreload(claimNo, cancellationToken) is { } preload
            ? new ClaimPreloadData(preload.Header, preload.ListId, preload.MaxDeductable, preload.CardId)
            : null;
    }

    /// <summary>Returns the claim number whose first invoice preloads the header, or null when the claim parameter preloads nothing.</summary>
    private static string? PreloadClaimNo(InvoiceEntryParameters parameters) =>
        Trimmed(parameters.ClaimNo) is { } claimNo
        && claimNo is not (NoClaimParameter or NewConsultationClaimParameter or IndependentServiceClaimParameter)
            ? claimNo
            : null;

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

    private static bool LacksCoverageRow(string? patientNo, PatientCoverageSnapshot? coverage) =>
        !IsBlank(patientNo) && coverage is null;

    private async Task<ClinicProfile?> ReadClinic(InvoiceHeaderDraft header, CancellationToken cancellationToken) =>
        header.ClinicId is int clinicId
            ? await _lookups.GetClinicProfile(clinicId, header.PatientNo, cancellationToken)
            : null;

    /// <summary>Returns the header carrying its doctor's clinic, with the clinic and doctor names, or null when the header has no doctor or the doctor has no single clinic row.</summary>
    private async Task<DoctorClinic?> ReadDoctorClinic(InvoiceHeaderDraft header, CancellationToken cancellationToken) =>
        header.DocId is int docId && await _lookups.GetDoctorClinic(docId, cancellationToken) is { } doctor
            ? new DoctorClinic(header with { ClinicId = doctor.ClinicId }, doctor.ClinicName, doctor.DocName)
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

    /// <summary>Returns X422_APPROV_CHECK: the PREF 422 value when it is an integer, null when it is null, otherwise the entry parameter (D-73).</summary>
    /// <param name="parameters">Entry parameters of the draft; X422_APPROV_CHECK is read.</param>
    /// <param name="cancellationToken">Cancels the preference read.</param>
    private async Task<int?> ReadX422(InvoiceEntryParameters parameters, CancellationToken cancellationToken)
    {
        var preferences = await _lookups.GetPreferences(cancellationToken);
        if (!preferences.TryGetValue(ApprovalPreference, out var value))
        {
            return parameters.X422ApprovCheck;
        }

        if (value is null)
        {
            return null;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode)
            ? mode
            : parameters.X422ApprovCheck;
    }

    /// <summary>Returns the rolled-back package preview of the service lines not in the excluded indexes, or null when none remains.</summary>
    private async Task<PreviewResult?> RulesPreview(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> excludedLines,
        OperatorContext operatorContext,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var eligible = lines
            .Where((line, index) => !IsBlank(line.ServiceId) && !excludedLines.Contains(index))
            .ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        return await CalculateVettedPreview(header, eligible, operatorContext, profileReads, cancellationToken);
    }

    private async Task<PreviewResult> CalculateVettedPreview(
        InvoiceHeaderDraft header,
        IReadOnlyList<InvoiceLineDraft> lines,
        OperatorContext operatorContext,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var manual = new HashSet<InvoiceLineDraft>();
        foreach (var line in lines)
        {
            if (await IsManualPriceOverride(header, line, profileReads, cancellationToken))
            {
                manual.Add(line);
            }
        }

        var probe = await CalculatePreviewLines(header, WithoutPriceOverrides(lines, manual), operatorContext, cancellationToken);
        if (manual.Count == 0)
        {
            return probe;
        }

        var isDirect = profileReads.CompanyIsDirect(header, cancellationToken);
        var listsByClientId = ListsByClientId(probe.Lines);
        var firstListId = FirstPreviewList(probe);
        var overrides = manual
            .Select(line => (
                Line: line,
                ListId: OwnOrFirstListId(line.ClientId, listsByClientId, firstListId),
                ServiceId: Trimmed(line.ServiceId)))
            .ToArray();
        await profileReads.Load(overrides.Select(entry => (entry.ListId, entry.ServiceId)), cancellationToken);
        var refused = new HashSet<InvoiceLineDraft>();
        foreach (var (line, listId, serviceId) in overrides)
        {
            if (!await PriceOverrideAllowed(header, profileReads.Get(listId, serviceId), isDirect))
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
        try
        {
            var (previewLines, totals) = await _invoiceApi.CalculatePreview(
                session, header, lines, operatorContext, header.Amount1 is null, cancellationToken);
            return new PreviewResult(previewLines, totals);
        }
        catch (Exception failure) when (Failures.Translate(failure) is { Status: RefusedStatus })
        {
            failure.Data[PreviewRefusalKey] = true;
            throw;
        }
        finally
        {
            await TryRollback(session);
        }
    }

    private static IReadOnlyList<InvoiceLineDraft> WithoutPriceOverrides(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<InvoiceLineDraft> stripped) =>
        stripped.Count == 0
            ? lines
            : lines.Select(line => stripped.Contains(line) ? line with { PriceOverride = null } : line).ToArray();

    private static decimal? OwnOrFirstListId(
        string? clientId,
        IReadOnlyDictionary<string, decimal> listsByClientId,
        decimal? firstListId) =>
        !IsBlank(clientId) && listsByClientId.TryGetValue(clientId!, out var listId) ? listId : firstListId;

    /// <summary>Returns the first non-null list id of the preview lines for each client id.</summary>
    private static Dictionary<string, decimal> ListsByClientId(IReadOnlyList<EditablePreviewLine> previewLines)
    {
        var lists = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var line in previewLines)
        {
            if (line.ClientId is { } clientId && line.ListId is { } listId)
            {
                lists.TryAdd(clientId, listId);
            }
        }

        return lists;
    }

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

    /// <summary>Finds unexpanded package parents and lines whose package role remains unknown (D-38).</summary>
    private async Task<ParentScan> UnexpandedParents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> rejected,
        IReadOnlyCollection<string> knownPackageIds,
        ClaimPreloadData? preload,
        InvoiceHeaderDraft header,
        bool previewAllowed,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        ProfileReads profileReads,
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
                ContextHeader(header), lines, discoveryExcluded, operatorContext, profileReads, cancellationToken);
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

        parents.UnionWith(await PackageLines(lines, unclassified, listId, profileReads, cancellationToken));
        return new ParentScan(parents, Array.Empty<int>());
    }

    /// <summary>Returns the candidate lines whose service is a package (IS_PACKAGE = 1) on the given list.</summary>
    private async Task<HashSet<int>> PackageLines(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<int> candidates,
        decimal listId,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        await profileReads.Load(
            candidates.Select(index => ((decimal?)listId, Trimmed(lines[index].ServiceId))), cancellationToken);

        var packages = new HashSet<int>();
        foreach (var index in candidates)
        {
            if (profileReads.Get(listId, Trimmed(lines[index].ServiceId))?.IsPackage == 1)
            {
                packages.Add(index);
            }
        }

        return packages;
    }

    /// <summary>Previews eligible lines, retrying once without unexpanded package parents (D-38).</summary>
    private async Task<(HashSet<int> Excluded, PreviewResult? Preview)> ContextPreview(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlySet<int> rejected,
        IReadOnlyCollection<string> knownPackageIds,
        ClaimPreloadData? preload,
        InvoiceHeaderDraft header,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var scan = await UnexpandedParents(
            lines, rejected, knownPackageIds, preload, header, previewAllowed: true, succeeded, operatorContext, profileReads, cancellationToken);
        var excluded = new HashSet<int>(scan.Parents);
        excluded.UnionWith(rejected);
        var contextHeader = ContextHeader(header);
        try
        {
            return (excluded, await RulesPreview(contextHeader, lines, excluded, operatorContext, profileReads, cancellationToken));
        }
        catch (Exception failure) when (scan.Unclassified.Count > 0 && IsUnexpandedParentRefusal(failure))
        {
            var listId = await ResolveListId(null, preload, header, operatorContext, cancellationToken);
            var parents = await PackageLines(lines, scan.Unclassified, listId, profileReads, cancellationToken);
            if (parents.Count == 0)
            {
                throw;
            }

            excluded.UnionWith(parents);
        }

        return (excluded, await RulesPreview(contextHeader, lines, excluded, operatorContext, profileReads, cancellationToken));
    }

    /// <summary>Reads the eligible lines' lists and profiles and collects successful previews (D-38).</summary>
    private async Task<LineContext> ReadLineContext(
        IReadOnlyList<InvoiceLineDraft> lines,
        InvoiceHeaderDraft header,
        ClaimPreloadData? preload,
        IReadOnlyCollection<string> knownPackageIds,
        bool previewAllowed,
        List<PreviewResult> succeeded,
        OperatorContext operatorContext,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var rejected = LocallyRejectedLines(lines);
        var (excluded, preview) = previewAllowed
            ? await ContextPreview(lines, rejected, knownPackageIds, preload, header, succeeded, operatorContext, profileReads, cancellationToken)
            : (rejected, (PreviewResult?)null);
        if (preview is not null)
        {
            succeeded.Add(preview);
        }

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

        var profiles = await BuildProfiles(lines, lists, profileReads, cancellationToken);
        return new LineContext(preview, profiles, preview is not null && excluded.Count == 0, resolved);
    }

    /// <summary>Returns the line context and requested services read on the lists of the previews that succeeded, else the claim preload's list, for use when the package refused a preview or the price plan returned OI-24.</summary>
    private async Task<(LineContext Context, IReadOnlyDictionary<decimal, IReadOnlySet<string>> Requested)> FallbackLineContext(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<PreviewResult> succeeded,
        ClaimPreloadData? preload,
        string? claimNo,
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var lists = LineLists(lines, succeeded.SelectMany(preview => preview.Lines).ToArray(), preload);
        var profiles = await BuildProfiles(lines, lists, profileReads, cancellationToken);
        var requested = await ReadRequestedServices(claimNo, profiles.Lists, cancellationToken);
        return (new LineContext(null, profiles, false, false), requested);
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
        var listsByClientId = ListsByClientId(previewLines);
        var lists = new decimal?[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            if (IsBlank(lines[index].ServiceId))
            {
                continue;
            }

            var clientId = lines[index].ClientId;
            decimal? ownListId = IsBlank(clientId)
                ? null
                : listsByClientId.TryGetValue(clientId!, out var clientListId) ? clientListId : null;
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
        ProfileReads profileReads,
        CancellationToken cancellationToken)
    {
        var listOrder = new List<decimal>();
        var serviceIdsByList = new Dictionary<decimal, List<string>>();
        var listed = new HashSet<(decimal ListId, string ServiceId)>();
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

            if (listed.Add((lineListId, lineServiceId)))
            {
                listServiceIds.Add(lineServiceId);
            }
        }

        if (listOrder.Count == 0)
        {
            return ProfileSet.Empty(lines.Count) with { Lists = lists.ToArray() };
        }

        var queueFlags = new Dictionary<decimal, Dictionary<string, int>>();
        foreach (var listId in listOrder)
        {
            var serviceIds = serviceIdsByList[listId];
            await profileReads.Load(serviceIds.Select(serviceId => ((decimal?)listId, (string?)serviceId)), cancellationToken);

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
            return profileReads.Get(listId, id) switch
            {
                null when flag is null => null,
                null => new ServiceProfile { ServiceId = id, AddToQue = flag },
                { } profile when flag is null => profile,
                { } profile => profile with { AddToQue = flag },
            };
        }

        var own = Enumerable.Range(0, lines.Count).Select(Own).ToArray();
        var listedComponents = await ReadPackageComponents(lines, lists, own, cancellationToken);
        var byLine = new ServiceProfile?[lines.Count];
        var nested = new bool[lines.Count];
        var nestedByParent = NestComponents(lines, own, listedComponents, nested);

        for (var index = 0; index < lines.Count; index++)
        {
            if (own[index] is { } package && listedComponents[index] is { } packageRows)
            {
                byLine[index] = package with
                {
                    Components = PackageComponents(lines, own, nestedByParent[index], packageRows),
                };
            }
            else if (own[index] is null && HasRole(lines[index], ParentRole))
            {
                byLine[index] = new ServiceProfile { ServiceId = Trimmed(lines[index].ServiceId) };
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

    /// <summary>Returns the PACKAGE_DTL component profiles of each line whose own profile is a package (IS_PACKAGE = 1), read once per list and package; null for every other line.</summary>
    private async Task<IReadOnlyList<ServiceProfile>?[]> ReadPackageComponents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<decimal?> lists,
        IReadOnlyList<ServiceProfile?> own,
        CancellationToken cancellationToken)
    {
        var read = new Dictionary<(decimal ListId, string PackageId), IReadOnlyList<ServiceProfile>>();
        var listed = new IReadOnlyList<ServiceProfile>?[lines.Count];
        for (var index = 0; index < lines.Count; index++)
        {
            if (own[index] is not { IsPackage: 1 } package
                || lists[index] is not { } listId
                || (Trimmed(package.ServiceId) ?? Trimmed(lines[index].ServiceId)) is not { } packageId)
            {
                continue;
            }

            if (!read.TryGetValue((listId, packageId), out var components))
            {
                components = await _lookups.GetPackageComponentFlags(packageId, listId, cancellationToken);
                read[(listId, packageId)] = components;
            }

            listed[index] = components;
        }

        return listed;
    }

    /// <summary>Marks and returns, per package parent line, the COMPONENT lines nested under it: same trimmed instance id, a PARENT line whose PACKAGE_DTL lists the component's service, and a component that is not itself a package.</summary>
    private static List<int>?[] NestComponents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<ServiceProfile?> own,
        IReadOnlyList<IReadOnlyList<ServiceProfile>?> listedComponents,
        bool[] nested)
    {
        var parentsByInstance = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var parent = 0; parent < lines.Count; parent++)
        {
            if (!HasRole(lines[parent], ParentRole)
                || listedComponents[parent] is null
                || Trimmed(lines[parent].PackageInstanceId) is not { } instanceId)
            {
                continue;
            }

            if (!parentsByInstance.TryGetValue(instanceId, out var instanceParents))
            {
                instanceParents = new List<int>();
                parentsByInstance[instanceId] = instanceParents;
            }

            instanceParents.Add(parent);
        }

        var nestedByParent = new List<int>?[lines.Count];
        for (var component = 0; component < lines.Count; component++)
        {
            if (!HasRole(lines[component], ComponentRole)
                || own[component] is { IsPackage: 1 }
                || Trimmed(lines[component].PackageInstanceId) is not { } instanceId
                || Trimmed(lines[component].ServiceId) is not { } serviceId
                || !parentsByInstance.TryGetValue(instanceId, out var instanceParents))
            {
                continue;
            }

            foreach (var parent in instanceParents)
            {
                if (ListsService(listedComponents[parent]!, serviceId))
                {
                    (nestedByParent[parent] ??= new List<int>()).Add(component);
                    nested[component] = true;
                }
            }
        }

        return nestedByParent;
    }

    /// <summary>Returns a package line's components: the own profiles of its nested lines, then each PACKAGE_DTL profile whose service no nested line with a profile represents.</summary>
    private static ServiceProfile[] PackageComponents(
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<ServiceProfile?> own,
        IReadOnlyList<int>? nestedComponents,
        IReadOnlyList<ServiceProfile> listed)
    {
        var components = new List<ServiceProfile>();
        var represented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in nestedComponents ?? Array.Empty<int>())
        {
            if (own[component] is { } profile && Trimmed(lines[component].ServiceId) is { } serviceId)
            {
                components.Add(profile);
                represented.Add(serviceId);
            }
        }

        foreach (var profile in listed)
        {
            if (profile is not null && (Trimmed(profile.ServiceId) is not { } serviceId || !represented.Contains(serviceId)))
            {
                components.Add(profile);
            }
        }

        return components.ToArray();
    }

    /// <summary>Returns true when a PACKAGE_DTL profile carries the service id, trimmed and ignoring case.</summary>
    private static bool ListsService(IReadOnlyList<ServiceProfile> components, string serviceId) =>
        components.Any(component =>
            component is not null && string.Equals(Trimmed(component.ServiceId), serviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the claim's trimmed requested service ids, compared ignoring case, read once per distinct line list; empty when the claim number is blank.</summary>
    private async Task<IReadOnlyDictionary<decimal, IReadOnlySet<string>>> ReadRequestedServices(
        string? claimNo,
        IReadOnlyList<decimal?> lists,
        CancellationToken cancellationToken)
    {
        var requested = new Dictionary<decimal, IReadOnlySet<string>>();
        if (IsBlank(claimNo))
        {
            return requested;
        }

        foreach (var list in lists)
        {
            if (list is { } listId && !requested.ContainsKey(listId))
            {
                var serviceIds = await _lookups.GetRequestedServices(claimNo!, listId, cancellationToken);
                requested[listId] = new HashSet<string>(serviceIds.Select(Trimmed).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            }
        }

        return requested;
    }

    /// <summary>Returns the requested service ids of the line's own list; empty when the line has no list.</summary>
    private static IReadOnlySet<string> RequestedFor(
        IReadOnlyDictionary<decimal, IReadOnlySet<string>> requested,
        IReadOnlyList<decimal?> lists,
        int index) =>
        lists[index] is { } listId && requested.TryGetValue(listId, out var services)
            ? services
            : NoRequestedServiceIds;

    private static bool IsRequested(string? serviceId, IReadOnlySet<string> requestedServices) =>
        Trimmed(serviceId) is { } id
        && requestedServices.Contains(id);

    /// <summary>Returns the draft with the operator identity and the server-owned fields reset; refuses lines left unbound over the line cap, a null line, or a PATIENTNO, CLAIM_NO, VISIT_UNIQUE, COMP_CODE, CURR_CODE, CLAIM_FLAG, NOTE_NO or line SERVICEID, LDISCT, TEETH_NO, TOOTH_SURFACE, TEETH_NO2 or APPROV_REF_NO wider than its item, before any read.</summary>
    private static DraftDto Sanitize(DraftDto draft, OperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.LinesRefusal is { } linesRefusal)
        {
            var refused = new ArgumentException(linesRefusal, nameof(draft));
            refused.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(LineField, linesRefusal) };
            throw refused;
        }

        var lines = draft.Lines ?? Array.Empty<InvoiceLineDraft>();
        if (lines.Any(line => line is null))
        {
            var failure = new ArgumentException(NullLineText, nameof(draft));
            failure.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(LineField, NullLineText) };
            throw failure;
        }

        var tooWide = new List<MessageDto>();
        AddIfTooWide(tooWide, PatientNoItem, draft.Header?.PatientNo, PatientNoBytes);
        tooWide.AddRange(ParameterWidthMessages(draft.Parameters));
        AddIfTooWide(tooWide, CompCodeItem, draft.Header?.CompCode, CompCodeBytes);
        AddIfTooWide(tooWide, CurrCodeItem, draft.Header?.CurrCode, CurrCodeBytes);
        AddIfTooWide(tooWide, ClaimFlagItem, draft.Header?.ClaimFlag, ClaimFlagBytes);
        AddIfTooWide(tooWide, NoteNoItem, draft.Header?.NoteNo, NoteNoBytes);
        for (var index = 0; index < lines.Count; index++)
        {
            AddIfTooWide(tooWide, ServiceIdItem, lines[index].ServiceId, ServiceIdBytes, index + 1);
            AddIfTooWide(tooWide, DiscountTypeItem, lines[index].DiscountType, DiscountTypeBytes, index + 1);
            AddIfTooWide(tooWide, TeethNoItem, lines[index].TeethNo, TeethNoBytes, index + 1);
            AddIfTooWide(tooWide, ToothSurfaceItem, lines[index].ToothSurface, ToothSurfaceBytes, index + 1);
            AddIfTooWide(tooWide, TeethNo2Item, lines[index].TeethNo2, TeethNo2Bytes, index + 1);
            AddIfTooWide(tooWide, ApprovRefNoItem, lines[index].ApprovRefNo, ApprovRefNoBytes, index + 1);
        }

        if (tooWide.Count > 0)
        {
            throw WidthRejected(tooWide, nameof(draft));
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
        DiscountType = line.ManualDiscountType,
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

    private static LovResponse MissingBind(string lov, string item) => BindRejected(lov, RequiredBind(lov, item));

    /// <summary>Returns the blocking message on <paramref name="item"/> that the list <paramref name="lov"/> requires.</summary>
    private static MessageDto RequiredBind(string lov, string item) => Blocking(item, $"{item} is required for the {lov} list.");

    /// <summary>Returns the list with no rows and the blocking messages refusing its binds, in the order given.</summary>
    private static LovResponse BindRejected(string lov, params MessageDto[] messages) => new()
    {
        Name = lov,
        Rows = Array.Empty<IReadOnlyDictionary<string, object?>>(),
        Messages = messages,
    };

    /// <summary>Returns the blocking PATIENTNO message when the patient number exceeds the item's width in characters or UTF-8 bytes, else null.</summary>
    private static MessageDto? PatientNoTooLong(string patientNo) => TooWide(PatientNoItem, patientNo, PatientNoBytes);

    /// <summary>Returns the blocking message on <paramref name="item"/> when the value exceeds <paramref name="maxBytes"/> in characters or UTF-8 bytes, else null; a line number names the draft line.</summary>
    private static MessageDto? TooWide(string item, string value, int maxBytes, int? lineNumber = null)
    {
        var subject = lineNumber is { } line ? $"{item} on line {line}" : item;
        if (value.Length > maxBytes)
        {
            return Blocking(item, $"{subject} has {value.Length} characters; at most {maxBytes} can be bound.");
        }

        var bytes = System.Text.Encoding.UTF8.GetByteCount(value);
        return bytes > maxBytes
            ? Blocking(item, $"{subject} has {bytes} bytes in UTF-8; at most {maxBytes} can be bound.")
            : null;
    }

    /// <summary>Adds the blocking message of a non-blank value wider than its item; a line number names the draft line.</summary>
    private static void AddIfTooWide(List<MessageDto> messages, string item, string? value, int maxBytes, int? lineNumber = null)
    {
        if (!IsBlank(value) && TooWide(item, value!, maxBytes, lineNumber) is { } message)
        {
            messages.Add(message);
        }
    }

    /// <summary>Returns the blocking CLAIM_NO and VISIT_UNIQUE messages of entry parameters wider than those items, in that order.</summary>
    private static List<MessageDto> ParameterWidthMessages(InvoiceEntryParameters? parameters)
    {
        var messages = new List<MessageDto>();
        AddIfTooWide(messages, ClaimNoItem, parameters?.ClaimNo, ClaimNoBytes);
        AddIfTooWide(messages, VisitUniqueItem, parameters?.VisitUnique, VisitUniqueBytes);
        return messages;
    }

    /// <summary>Returns the 422 field-validation failure carrying the blocking width messages.</summary>
    private static ArgumentException WidthRejected(IReadOnlyList<MessageDto> messages, string paramName)
    {
        var failure = new ArgumentException(string.Join(" ", messages.Select(message => message.Text)), paramName);
        failure.Data[ProblemDetailsWriter.MessagesDataKey] = messages.ToArray();
        return failure;
    }

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

    /// <summary>Returns the 422 field-validation failure for a LOCAL_DOC_TYPE other than 505, 532 or 783.</summary>
    private static ArgumentException InvalidLocalDocType(int localDocType)
    {
        var failure = new ArgumentException(
            string.Create(CultureInfo.InvariantCulture, $"Unknown LOCAL_DOC_TYPE {localDocType}."), nameof(localDocType));
        failure.Data[ProblemDetailsWriter.MessagesDataKey] = new[] { Blocking(LocalDocTypeItem, LocalDocTypeText) };
        return failure;
    }

    private static string? BindText(IReadOnlyDictionary<string, string?> values, string item) =>
        values.TryGetValue(item, out var value) ? Trimmed(value) : null;

    /// <summary>Reads an item's untrimmed value as a canonical positive whole number: ASCII digits with no sign, padding or leading zero.</summary>
    /// <returns>Whether the item has a non-blank value, and the number, or null when that value is not such a number.</returns>
    private static (bool Present, int? Number) BindPositiveInt(IReadOnlyDictionary<string, string?> values, string item)
    {
        if (!values.TryGetValue(item, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return (false, null);
        }

        return text[0] != '0'
            && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0
                ? (true, number)
                : (true, null);
    }

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

    /// <summary>Returns the outcome of a saved or replayed invoice, carrying its warnings and open items.</summary>
    private static CreateInvoiceOutcome Saved(CreateInvoiceResponse invoice) => new()
    {
        Invoice = invoice,
        Messages = invoice.Messages,
        OpenItems = invoice.OpenItems,
    };

    private static CreateInvoiceOutcome RequestIdRejected() => new()
    {
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

    /// <summary>Returns true when the draft carries the seal the gateway issues for its request id and draft date.</summary>
    private bool HasIssuedDraftDate(DraftDto draft) =>
        draft.DraftSeal is { Length: DraftSealLength } seal
        && _invoiceApi.SealDraftDate(draft.RequestId, draft.DraftDate) is { Length: DraftSealLength } issued
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(seal), Encoding.UTF8.GetBytes(issued));

    private static CreateInvoiceOutcome DraftDateRejected() => new()
    {
        Messages = new[] { Blocking(InvDateItem, DraftSealText) },
        OpenItems = Array.Empty<string>(),
    };

    /// <summary>Returns the draft's lines plus one rebuilt parent per distinct space-trimmed offer instance id of its bundled-offer lines; a null line counts as a line.</summary>
    private static int EngineLineCount(IReadOnlyList<InvoiceLineDraft?> lines) =>
        lines.Count
        + lines
            .Select(line => line is { OfferType: BundledOfferType } ? line.OfferInstanceId?.Trim(' ') : null)
            .Where(instanceId => !string.IsNullOrEmpty(instanceId))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static CreateInvoiceOutcome LinesRejected(int engineLines, int maxLines) => new()
    {
        Messages = new[]
        {
            Blocking(
                LineField,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The draft expands to {engineLines} invoice lines; an invoice can be created with at most {maxLines} lines.")),
        },
        OpenItems = Array.Empty<string>(),
    };

    /// <summary>Returns the outcome of a draft whose lines were left unbound over the line cap, carrying the refusal on LINE.</summary>
    private static CreateInvoiceOutcome LinesRefused(string refusal) => new()
    {
        Messages = new[] { Blocking(LineField, refusal) },
        OpenItems = Array.Empty<string>(),
    };

    private sealed record ClaimPreloadData(InvoiceHeaderDraft Header, decimal? ListId, decimal? MaxDeductable, int? CardId);

    private sealed record ServerContext(InvoiceHeaderDraft Header, PatientCoverageSnapshot? Coverage, ClaimPreloadData? Preload);

    private sealed record GateInputs(decimal? MaxDeductable, int? UseAdvanced, int? CardId);

    private sealed record DoctorClinic(InvoiceHeaderDraft Header, string? ClinicName, string? DocName);

    private sealed record PreviewResult(IReadOnlyList<EditablePreviewLine> Lines, PreviewTotalsRow Totals)
    {
        /// <summary>Lines whose manual price override the preview checked.</summary>
        public IReadOnlySet<InvoiceLineDraft> CheckedOverrides { get; init; } = new HashSet<InvoiceLineDraft>();

        /// <summary>Checked lines whose price override was refused; the preview priced them without it.</summary>
        public IReadOnlySet<InvoiceLineDraft> RefusedOverrides { get; init; } = new HashSet<InvoiceLineDraft>();
    }

    private sealed record ParentScan(HashSet<int> Parents, IReadOnlyList<int> Unclassified);

    private sealed record ProfileSet(
        IReadOnlyList<ServiceProfile> TopLevel,
        IReadOnlyList<ServiceProfile?> ByLine,
        IReadOnlyList<decimal?> Lists)
    {
        /// <summary>Returns a set with no top-level profiles and a null profile and list per line.</summary>
        /// <param name="lineCount">Number of draft lines.</param>
        public static ProfileSet Empty(int lineCount) =>
            new(Array.Empty<ServiceProfile>(), new ServiceProfile?[lineCount], new decimal?[lineCount]);
    }

    private sealed record LineContext(PreviewResult? Preview, ProfileSet Profiles, bool CoversDraft, bool Resolved);

    private sealed class Findings
    {
        private readonly List<MessageDto> _messages = new();
        private readonly Dictionary<string, object?> _adjusted = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _openItems = new(StringComparer.Ordinal);

        /// <summary>True when any recorded message is blocking.</summary>
        public bool IsBlocking => _messages.Exists(message => message.Severity == ValidationMessage.Blocking);

        /// <summary>Recorded messages, blocking ones first, otherwise in recording order.</summary>
        public IReadOnlyList<MessageDto> Messages =>
            _messages.OrderBy(message => message.Severity == ValidationMessage.Blocking ? 0 : 1).ToArray();

        /// <summary>Recorded non-blocking messages.</summary>
        public IReadOnlyList<MessageDto> Warnings =>
            _messages.Where(message => message.Severity != ValidationMessage.Blocking).ToArray();

        /// <summary>Copy of the adjusted values by item name.</summary>
        public IReadOnlyDictionary<string, object?> Adjusted => new Dictionary<string, object?>(_adjusted, StringComparer.Ordinal);

        /// <summary>Recorded open-item ids in ordinal order.</summary>
        public IReadOnlyList<string> OpenItems => _openItems.ToArray();

        /// <summary>Records a rule result's messages and adjusted values.</summary>
        /// <returns>The result, unchanged.</returns>
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

        /// <summary>Records a message.</summary>
        public void Add(MessageDto message) => _messages.Add(message);

        /// <summary>Sets an item's adjusted value, replacing any earlier one.</summary>
        public void Adjust(string item, object? value) => _adjusted[item] = value;

        /// <summary>Records an open-item id.</summary>
        public void AddOpenItem(string openItemId) => _openItems.Add(openItemId);

        /// <summary>Records several open-item ids.</summary>
        public void AddOpenItems(IEnumerable<string> openItemIds) => _openItems.UnionWith(openItemIds);

        /// <summary>Builds the validate response from the recorded messages, adjusted values and open items.</summary>
        /// <param name="visitLine">Visit-line choice to return, if any.</param>
        public ValidateDraftResponse ToValidateResponse(VisitLineChoice? visitLine = null) => new()
        {
            Messages = Messages,
            Adjusted = Adjusted,
            OpenItems = OpenItems,
            VisitLine = visitLine,
        };
    }

    /// <summary>Caches service profiles, company-direct flags and selected request rows for one workflow call.</summary>
    private sealed class ProfileReads
    {
        private readonly ILookupQueries _lookups;
        private readonly IInvoiceQueries _invoices;
        private readonly string? _visitUnique;
        private readonly Dictionary<(decimal ListId, string ServiceId), ServiceProfile?> _read = new();
        private readonly Dictionary<string, Lazy<Task<int?>>> _isDirect = new(StringComparer.Ordinal);
        private readonly Dictionary<(string PatientNo, int PayType), Task<IReadOnlyList<(long PatServReqRowId, string? ServiceId)>>> _selectedRows = new();

        /// <summary>Creates an empty set of reads over the lookups and the invoice queries of the call's visit.</summary>
        /// <param name="lookups">Lookups the profiles are read through.</param>
        /// <param name="invoices">Invoice queries the selected request rows are read through.</param>
        /// <param name="visitUnique">Visit of the call's entry parameters; blank verifies no request row.</param>
        public ProfileReads(ILookupQueries lookups, IInvoiceQueries invoices, string? visitUnique)
        {
            _lookups = lookups;
            _invoices = invoices;
            _visitUnique = visitUnique;
        }

        /// <summary>Reads the profiles of the keys with a list and a service that are not read yet, once per list; a service not on its list, or a blank one, reads as null.</summary>
        public async Task Load(IEnumerable<(decimal? ListId, string? ServiceId)> keys, CancellationToken cancellationToken)
        {
            var listOrder = new List<decimal>();
            var unreadByList = new Dictionary<decimal, List<string>>();
            var pending = new HashSet<(decimal ListId, string ServiceId)>();
            foreach (var (listId, serviceId) in keys)
            {
                if (listId is not { } list
                    || serviceId is not { } service
                    || _read.ContainsKey((list, service))
                    || !pending.Add((list, service)))
                {
                    continue;
                }

                if (!unreadByList.TryGetValue(list, out var unread))
                {
                    unread = new List<string>();
                    unreadByList[list] = unread;
                    listOrder.Add(list);
                }

                unread.Add(service);
            }

            foreach (var list in listOrder)
            {
                var serviceIds = unreadByList[list];
                if (serviceIds.Count == 1)
                {
                    var serviceId = serviceIds[0];
                    _read[(list, serviceId)] = IsBlank(serviceId)
                        ? null
                        : await _lookups.GetServiceProfile(serviceId, list, cancellationToken);
                }
                else
                {
                    var profiles = await _lookups.GetServiceProfiles(serviceIds, list, cancellationToken);
                    foreach (var serviceId in serviceIds)
                    {
                        _read[(list, serviceId)] = profiles.GetValueOrDefault(serviceId);
                    }
                }
            }
        }

        /// <summary>Returns the loaded profile of a service on a list; null when either is null or the service is not on the list.</summary>
        /// <exception cref="KeyNotFoundException">The service was not loaded on the list.</exception>
        public ServiceProfile? Get(decimal? listId, string? serviceId) =>
            listId is { } list && serviceId is { } service ? _read[(list, service)] : null;

        /// <summary>Returns the deferred COMPANYS.IS_DIRECT read of the header's company, the same one for every call with that company code; a blank company reads as null without a read.</summary>
        public Lazy<Task<int?>> CompanyIsDirect(InvoiceHeaderDraft header, CancellationToken cancellationToken)
        {
            if (IsBlank(header.CompCode))
            {
                return new Lazy<Task<int?>>(() => Task.FromResult<int?>(null));
            }

            var compCode = header.CompCode!;
            if (!_isDirect.TryGetValue(compCode, out var isDirect))
            {
                isDirect = new Lazy<Task<int?>>(() => _lookups.GetCompanyIsDirect(compCode, cancellationToken));
                _isDirect[compCode] = isDirect;
            }

            return isDirect;
        }

        /// <summary>Returns true when the line's request row is one of the rows selected for the header's patient and pay type on the visit, with the line's service; the rows are read once per patient and pay type.</summary>
        /// <param name="header">Server-decided header supplying the patient and the pay type.</param>
        /// <param name="line">Draft line carrying the request row id and the service id.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>False without a read when the line has no row id or service, or the patient, visit or pay type is missing.</returns>
        public async Task<bool> IsSelectedRequestRow(InvoiceHeaderDraft header, InvoiceLineDraft line, CancellationToken cancellationToken)
        {
            if (line.PatServReqRowId is not { } rowId
                || Trimmed(line.ServiceId) is not { } serviceId
                || IsBlank(header.PatientNo)
                || IsBlank(_visitUnique)
                || header.PayType is not { } payType)
            {
                return false;
            }

            var key = (header.PatientNo!, payType);
            if (!_selectedRows.TryGetValue(key, out var rows))
            {
                rows = ReadSelectedRows(header.PatientNo!, _visitUnique!, payType, cancellationToken);
                _selectedRows[key] = rows;
            }

            return (await rows).Any(row =>
                row.PatServReqRowId == rowId
                && string.Equals(Trimmed(row.ServiceId), serviceId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reads the selected request rows' ids and services; an unreadable row reads as no row.</summary>
        private async Task<IReadOnlyList<(long PatServReqRowId, string? ServiceId)>> ReadSelectedRows(
            string patientNo,
            string visitUnique,
            int payType,
            CancellationToken cancellationToken)
        {
            try
            {
                var rows = await _invoices.GetSelectedRequestRows(patientNo, visitUnique, payType, cancellationToken);
                return rows.Select(row => (row.PatServReqRowId, (string?)row.ServiceId)).ToArray();
            }
            catch (Exception failure) when (failure is InvalidCastException or OverflowException)
            {
                return Array.Empty<(long PatServReqRowId, string? ServiceId)>();
            }
        }
    }
}
