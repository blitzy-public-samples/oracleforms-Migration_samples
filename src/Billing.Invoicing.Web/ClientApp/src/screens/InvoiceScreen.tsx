import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { Dispatch, FormEvent, KeyboardEvent } from 'react';
import {
  ApiError,
  buildDocument,
  createInvoice,
  getCoverage,
  getInvoice,
  getLastInvoiceNo,
  importBundledOffer,
  importPackage,
  importRequests,
  importVisitLine,
  newDraft,
  previewInvoice,
  sendSms,
  validateDraft,
} from '../api/client';
import type {
  DocumentKind,
  DraftDto,
  ImportResponse,
  ImportResultRow,
  InvoiceLineDraft,
  MessageDto,
  ValidateTarget,
} from '../api/types';
import { canSave, ENTRY_NOT_A_NUMBER, isSuperseded, parseDecimalEntry, requestOrigin } from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState, RequestOrigin } from '../state/invoiceDraft';
import FieldMessage from '../components/FieldMessage';
import InvoiceHeaderForm from '../components/InvoiceHeaderForm';
import InvoiceLinesGrid from '../components/InvoiceLinesGrid';
import LovPicker, { LovConnectivityContext } from '../components/LovPicker';
import OpenItemNotice from '../components/OpenItemNotice';
import PaymentPanel from '../components/PaymentPanel';
import TotalsPanel from '../components/TotalsPanel';

type InvoiceScreenProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onShowMore: () => void;
};

/** A follow-up call run after the render that carries the previous step's dispatches. */
type Step =
  | { kind: 'preview' }
  | { kind: 'validate'; target: ValidateTarget; lineIndex?: number }
  | { kind: 'visitLine' }
  | { kind: 'reimportRequests' };

/** LOVs this screen opens itself. */
type ScreenLov = 'RESERV_NO' | 'OFFERS';

/** Saved-invoice actions of the action bar. */
type SavedAction = 'sms' | Exclude<DocumentKind, 'invoice'>;

type MessageRef = { source: string; index: number };

type FormLevelMessages = { messages: MessageDto[]; refs: MessageRef[][] };

/** Last import result shown under the lines, tied to the draft it was imported into. */
type ImportSummary = { requestId: string; label: string; result: ImportResultRow };

/** Validation targets followed by a preview. */
const PREVIEW_AFTER = new Set<ValidateTarget>([
  'SERVICEID',
  'QTY',
  'PRICE',
  'LINE',
  'FINALDISC_PERC',
  'FINALDISC',
  'DISC',
  'MY_DISC',
  'LDISCT',
  'AMOUNT_1',
  'AMOUNT_2',
]);

/** Fields whose messages InvoiceHeaderForm renders beside the field. */
const HEADER_FIELDS: ReadonlySet<string> = new Set(['PATIENTNO', 'COMP_CODE', 'DOCIDX', 'CLINICID', 'DEPT_WISE', 'CALL']);

/** Fields whose messages PaymentPanel renders beside the field. */
const PAYMENT_FIELDS: ReadonlySet<string> = new Set([
  'DISC_T',
  'FINALDISC_PERC',
  'FINALDISC',
  'SUB_PAYTYPE',
  'AMOUNT_1',
  'SUB_PAYTYPE2',
  'AMOUNT_2',
  'CASH_PAYED',
  'REUND',
]);

/** Line targets whose `LINE:<i>:<TARGET>` messages InvoiceLinesGrid renders under the line. */
const GRID_TARGETS: ReadonlySet<string> = new Set(['SERVICEID', 'QTY', 'PRICE', 'LDISCT', 'DISC', 'MY_DISC']);

const IMPORT_SOURCE = 'IMPORT';
const LINE_SOURCE = /^LINE:\d+:(.+)$/;

/** Sources holding the outcome of the last Save or import press, replaced by the next press. */
const ATTEMPT_SOURCES: ReadonlySet<string> = new Set(['CREATE', IMPORT_SOURCE]);
const SAVED_READ_ONLY_OPEN_ITEM = 'OI-56';

/** Enabled controls the keyboard can reach. */
const FOCUSABLE =
  'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])';

/** Reducer source key of a validation target: `LINE:<i>:<TARGET>` for a line, else the target. */
function messageKey(target: ValidateTarget, lineIndex: number | null): string {
  return lineIndex !== null ? `LINE:${lineIndex}:${target}` : target;
}

/** True for a 422 `field-validation` problem. */
function isFieldValidation(error: unknown): error is ApiError {
  return error instanceof ApiError && error.type === 'field-validation';
}

/** True when the screen holds no editable draft: none loaded, saved, or a read-only invoice. */
function isLocked(state: InvoiceDraftState): boolean {
  return state.draft === null || state.saved !== null || state.readOnly;
}

/** Messages of a Save or import press and the draft on screen when they arrived. */
type AttemptVerdict = { messages: MessageDto[]; draft: InvoiceDraftState['draft'] };

/** Records each Save or import press message list with its draft; a list that only lost dismissed messages keeps its draft. */
function recordVerdicts(state: InvoiceDraftState, verdicts: Map<string, AttemptVerdict>): void {
  for (const source of ATTEMPT_SOURCES) {
    const messages = state.messages[source];
    const recorded = verdicts.get(source);
    if (messages === undefined) {
      verdicts.delete(source);
    } else if (recorded === undefined || recorded.messages !== messages) {
      const dismissedOnly = recorded !== undefined && messages.every((message) => recorded.messages.includes(message));
      verdicts.set(source, { messages, draft: dismissedOnly ? recorded.draft : state.draft });
    }
  }
}

/** Save enablement: `canSave` without the Save or import press messages returned for an earlier draft (D-59). */
function saveAllowed(state: InvoiceDraftState, verdicts: ReadonlyMap<string, AttemptVerdict>): boolean {
  const messages = Object.fromEntries(
    Object.entries(state.messages).filter(([source, list]) => {
      const recorded = verdicts.get(source);
      return !ATTEMPT_SOURCES.has(source) || recorded === undefined || recorded.messages !== list || recorded.draft === state.draft;
    }),
  );
  return canSave({ ...state, messages });
}

/** Invoice number of the saved or queried invoice on screen, or null. */
function savedInvoiceNo(state: InvoiceDraftState): number | null {
  const invNo = state.saved?.invNo;
  return typeof invNo === 'number' && invNo > 0 ? invNo : null;
}

/** Value of an upper-case column in an LOV row, matching the key case-insensitively. */
function rowValue(row: Record<string, unknown>, column: string): unknown {
  if (Object.hasOwn(row, column)) {
    return row[column];
  }
  const key = Object.keys(row).find((candidate) => candidate.toUpperCase() === column);
  return key === undefined ? undefined : row[key];
}

/** OFERID of an OFFERS row as decimal text: its trimmed text, or a safe integer's digits; null when blank or not exact. */
function offerIdText(row: Record<string, unknown>): string | null {
  const value = rowValue(row, 'OFERID');
  if (typeof value === 'string') {
    const text = value.trim();
    return text === '' ? null : text;
  }
  return typeof value === 'number' && Number.isSafeInteger(value) ? String(value) : null;
}

/** Oracle error number in `ORA-nnnnn` form. */
function oraText(oracleErrorNumber: number): string {
  return `ORA-${String(Math.abs(oracleErrorNumber)).padStart(5, '0')}`;
}

/** True when a sibling component on this screen already renders the message beside its field or line. */
function shownElsewhere(source: string, message: MessageDto): boolean {
  if (source === IMPORT_SOURCE) {
    return false;
  }
  const line = LINE_SOURCE.exec(source);
  if (line !== null) {
    return GRID_TARGETS.has(line[1]);
  }
  const field = message.field?.toUpperCase() ?? '';
  return HEADER_FIELDS.has(field) || PAYMENT_FIELDS.has(field);
}

/** Messages no field or line renders, each distinct severity and text once, with every source position it came from. */
function formLevelMessages(messages: Record<string, MessageDto[]>): FormLevelMessages {
  const result: FormLevelMessages = { messages: [], refs: [] };
  const positions = new Map<string, number>();
  for (const [source, list] of Object.entries(messages)) {
    list.forEach((message, index) => {
      if (shownElsewhere(source, message)) {
        return;
      }
      const key = `${message.severity}\u0000${message.text}`;
      const position = positions.get(key);
      if (position === undefined) {
        positions.set(key, result.messages.length);
        result.messages.push(message);
        result.refs.push([{ source, index }]);
      } else {
        result.refs[position].push({ source, index });
      }
    });
  }
  return result;
}

/** Text of an import result: the returned counts and the package message, as returned. */
function importSummaryText(summary: ImportSummary): string {
  const { result } = summary;
  const counts: [string, number | null][] = [
    ['found', result.sourceCount],
    ['imported', result.importedCount],
    ['skipped rejected', result.skippedRejectedCount],
    ['skipped need approval', result.skippedNeedApprovalCount],
    ['skipped invalid', result.skippedInvalidCount],
  ];
  const shown = counts.filter(([, value]) => value !== null && value !== undefined).map(([label, value]) => `${label} ${value}`);
  const message = result.message != null && result.message.trim() !== '' ? result.message : '';
  const body = [shown.join(', '), message].filter((part) => part !== '').join('. ');
  return body === '' ? summary.label : `${summary.label}: ${body}`;
}

/** Invoice screen (canvas CANVAS2): header, lines, totals and payment of the shared draft, with the invoice actions. */
export default function InvoiceScreen({ state, dispatch, onShowMore }: InvoiceScreenProps) {
  const baseId = useId();
  const latest = useRef(state);
  latest.current = state;
  const verdicts = useRef(new Map<string, AttemptVerdict>());
  recordVerdicts(state, verdicts.current);

  const [queue, setQueue] = useState<Step[]>([]);
  const running = useRef<Step | null>(null);
  const [lov, setLov] = useState<ScreenLov | null>(null);
  const [bundleQty, setBundleQty] = useState('1');
  const [bundleQtyError, setBundleQtyError] = useState<string | null>(null);
  const [importSummary, setImportSummary] = useState<ImportSummary | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const messagesRef = useRef<HTMLDivElement>(null);

  const enqueue = useCallback((...steps: Step[]) => {
    setQueue((current) => [...current, ...steps]);
  }, []);

  // Runs the head step and removes it from the queue when it finishes.
  useEffect(() => {
    const head = queue[0];
    if (head === undefined || running.current === head) {
      return;
    }
    running.current = head;
    void runStep(head).finally(() => {
      running.current = null;
      setQueue((current) => current.filter((step) => step !== head));
    });
  }, [queue]);

  const onValidate = useCallback((target: ValidateTarget) => enqueue({ kind: 'validate', target }), [enqueue]);

  const onValidateLine = useCallback(
    (index: number, target: ValidateTarget) => enqueue({ kind: 'validate', target, lineIndex: index }),
    [enqueue],
  );

  const reportLovConnectivity = useCallback(
    (available: boolean) => dispatch({ type: available ? 'connectivityRestored' : 'connectivityLost' }),
    [dispatch],
  );

  /** Routes a failed call to the reducer and starts the recovery a stale-data error asks for, unless `origin` is superseded; `lineClientId` names the line of a line source. */
  function handleError(error: unknown, source: string, origin?: RequestOrigin, lineClientId?: string | null): void {
    if (!(error instanceof ApiError)) {
      throw error;
    }
    if (error.type === 'field-validation') {
      dispatch({ type: 'validationFailed', target: source, lineIndex: null, error, origin });
      return;
    }
    dispatch({ type: 'errorReceived', source, lineClientId, error, origin });
    if (error.type !== 'oracle-business-error' || (origin !== undefined && isSuperseded(latest.current, origin))) {
      return;
    }
    if (error.kind === 'RequestLinesStale') {
      const draft = latest.current.draft;
      if (draft !== null) {
        dispatch({ type: 'linesReplaced', lines: draft.lines.filter((line) => line.patServReqRowId == null), origin });
      }
      enqueue({ kind: 'reimportRequests' });
    } else if (error.kind === 'DefinitionStale') {
      enqueue({ kind: 'preview' });
    }
  }

  /** Keeps the import result of the current draft for display. */
  function rememberImport(label: string, requestId: string, response: ImportResponse): void {
    setImportSummary(response.result != null ? { requestId, label, result: response.result } : null);
  }

  /** Runs one queued step against the latest state. */
  async function runStep(step: Step): Promise<void> {
    switch (step.kind) {
      case 'preview':
        return runPreview();
      case 'validate':
        return validate(step.target, step.lineIndex ?? null);
      case 'visitLine':
        return importVisit();
      case 'reimportRequests':
        return importRequestLines(true);
    }
  }

  /** GET /api/patients/{patientNo}/coverage for the draft's patient number as entered; a blank number or a failed read stores no coverage; resolves false when Oracle is unavailable. */
  async function readCoverage(draft: DraftDto, origin: RequestOrigin): Promise<boolean> {
    const patientNo = draft.header.patientNo;
    if (patientNo == null || patientNo.trim() === '') {
      dispatch({ type: 'coverageApplied', response: null, origin });
      return true;
    }
    try {
      const response = await getCoverage(patientNo, draft.draftDate, draft.parameters);
      dispatch({ type: 'coverageApplied', response, origin });
      return true;
    } catch (error) {
      dispatch({ type: 'coverageApplied', response: null, origin });
      handleError(error, 'COVERAGE', origin);
      return !(error instanceof ApiError && (error.status === 503 || error.type === 'oracle-unavailable'));
    }
  }

  /** POST /api/drafts/validate for one target, after the coverage GET for a PATIENTNO target (none when that GET finds Oracle unavailable), then the visit line or preview it leads to. */
  async function validate(target: ValidateTarget, lineIndex: number | null): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current) || (lineIndex !== null && lineIndex >= draft.lines.length)) {
      return;
    }
    const lineClientId = lineIndex === null ? null : (draft.lines[lineIndex]?.clientId ?? null);
    const origin = requestOrigin(draft, target === 'PATIENTNO');
    if (target === 'PATIENTNO') {
      const reachable = await readCoverage(draft, origin);
      if (!reachable || isSuperseded(latest.current, origin)) {
        return;
      }
    }
    try {
      const response = await validateDraft({ draft, target, lineIndex });
      dispatch({ type: 'validationApplied', target, lineIndex, lineClientId, response, origin });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      const followUps: Step[] = [];
      if (target === 'DOCIDX' && response.visitLine != null && response.visitLine.kind !== 'None') {
        followUps.push({ kind: 'visitLine' });
      }
      if (PREVIEW_AFTER.has(target)) {
        followUps.push({ kind: 'preview' });
      }
      if (followUps.length > 0) {
        enqueue(...followUps);
      }
    } catch (error) {
      if (isFieldValidation(error)) {
        dispatch({ type: 'validationFailed', target, lineIndex, lineClientId, error, origin });
        return;
      }
      handleError(error, messageKey(target, lineIndex), origin, lineClientId);
    }
  }

  /** Clears the previous patient's context, then queues the PATIENTNO step: the new patient's coverage GET, then its validation. */
  async function patientChanged(): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    dispatch({ type: 'patientContextCleared', origin: requestOrigin(draft, true) });
    enqueue({ kind: 'validate', target: 'PATIENTNO' });
  }

  /** POST /api/invoices/preview for an editable draft with lines; an editable draft without lines has its preview cleared. */
  async function runPreview(): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    if (draft.lines.length === 0) {
      dispatch({ type: 'previewCleared' });
      return;
    }
    try {
      const response = await previewInvoice(draft);
      dispatch({ type: 'previewApplied', response, sent: draft.lines });
    } catch (error) {
      handleError(error, 'PREVIEW');
    }
  }

  /** Removes an unsaved line and recalculates. */
  const onRemoveLine = useCallback(
    (index: number) => {
      dispatch({ type: 'lineRemoved', index });
      enqueue({ kind: 'preview' });
    },
    [dispatch, enqueue],
  );

  /** Saves the draft, reloads it read-only and, for Save & Print, requests the invoice document, while the draft is still current. */
  async function save(print: boolean): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || !saveAllowed(current, verdicts.current)) {
      return;
    }
    const origin = requestOrigin(draft);
    let invNo: number;
    try {
      const response = await createInvoice({ draft });
      dispatch({ type: 'saved', response, origin });
      invNo = response.invNo;
    } catch (error) {
      if (isFieldValidation(error)) {
        dispatch({ type: 'validationFailed', target: 'CREATE', lineIndex: null, error, origin });
        return;
      }
      handleError(error, 'CREATE', origin);
      return;
    }
    if (isSuperseded(latest.current, origin)) {
      return;
    }
    try {
      const view = await getInvoice(invNo, window.location.search);
      dispatch({ type: 'invoiceLoaded', invNo, response: view, origin });
    } catch (error) {
      handleError(error, 'SAVED', origin);
    }
    if (print && !isSuperseded(latest.current, origin)) {
      try {
        await buildDocument(invNo, 'invoice');
        dispatch({ type: 'connectivityRestored' });
      } catch (error) {
        handleError(error, 'PRINT', origin);
      }
    }
  }

  /** Imports the visit's selected service requests, then recalculates when lines were added or `linesChanged` is set. */
  async function importRequestLines(linesChanged = false): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const origin = requestOrigin(draft, true);
    try {
      const response = await importRequests({ draft });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Import Request', draft.requestId, response);
      if (linesChanged || (response.lines ?? []).length > 0) {
        enqueue({ kind: 'preview' });
      }
    } catch (error) {
      if (isFieldValidation(error)) {
        dispatch({ type: 'validationFailed', target: IMPORT_SOURCE, lineIndex: null, error, origin });
        return;
      }
      handleError(error, IMPORT_SOURCE, origin);
    }
  }

  /** Expands the current line's package service into its parent and component lines. */
  async function importPackageLines(): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const line = draft.lines[current.currentLineIndex];
    const packageServiceId = line?.serviceId?.trim() ?? '';
    if (line === undefined || packageServiceId === '') {
      return;
    }
    const origin = requestOrigin(draft, true);
    try {
      const response = await importPackage({ draft, packageServiceId, parentSourceId: null });
      const imported = response.lines ?? [];
      const now = latest.current.draft;
      const position =
        now === null
          ? -1
          : now.lines.findIndex(
              (candidate) => candidate === line || (line.clientId != null && candidate.clientId === line.clientId),
            );
      if (now !== null && position >= 0 && imported.some((candidate) => candidate.packageLineRole === 'PARENT')) {
        const lines: InvoiceLineDraft[] = [...now.lines.slice(0, position), ...imported, ...now.lines.slice(position + 1)];
        dispatch({ type: 'linesReplaced', lines, origin });
        dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response: { ...response, lines: [] }, origin });
      } else {
        dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin });
      }
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Import Package', draft.requestId, response);
      if (imported.length > 0) {
        enqueue({ kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin);
    }
  }

  /** Loads the lines of the bundled offer chosen in the OFFERS list. */
  async function loadOffer(row: Record<string, unknown>): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const quantity = parseDecimalEntry(bundleQty);
    if (quantity.kind !== 'value') {
      setBundleQtyError(quantity.kind === 'invalid' ? quantity.message : ENTRY_NOT_A_NUMBER);
      return;
    }
    const origin = requestOrigin(draft, true);
    const offerId = offerIdText(row);
    if (offerId === null) {
      const text = 'Offer id must be a positive whole number.';
      const error = new ApiError({
        status: 422,
        type: 'field-validation',
        title: 'Validation failed',
        message: text,
        messages: [{ field: 'OFERID', text, severity: 'Blocking', rule: null }],
      });
      dispatch({ type: 'validationFailed', target: IMPORT_SOURCE, lineIndex: null, error, origin });
      return;
    }
    try {
      const response = await importBundledOffer({
        draft,
        offerId,
        bundleQty: quantity.text,
      });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Load offer', draft.requestId, response);
      if ((response.lines ?? []).length > 0) {
        enqueue({ kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin);
    }
  }

  /** Adds the automatic consultation, review or fixed-service visit line. */
  async function importVisit(): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const origin = requestOrigin(draft, true);
    try {
      const response = await importVisitLine({ draft });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Visit line', draft.requestId, response);
      if ((response.lines ?? []).length > 0) {
        enqueue({ kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin);
    }
  }

  /** SMS and document requests for the saved invoice on screen. */
  async function runSavedAction(action: SavedAction): Promise<void> {
    const invNo = savedInvoiceNo(latest.current);
    if (invNo === null) {
      return;
    }
    try {
      if (action === 'sms') {
        await sendSms(invNo);
      } else {
        await buildDocument(invNo, action);
      }
      dispatch({ type: 'connectivityRestored' });
    } catch (error) {
      // Each saved action reports under its own source.
      handleError(error, `SAVED:${action}`);
    }
  }

  /** Shows the operator centre's last invoice read-only. */
  async function showLastInvoice(): Promise<void> {
    let invNo: number | null = null;
    try {
      const response = await getLastInvoiceNo();
      const value: unknown = response?.invNo;
      invNo = typeof value === 'number' && Number.isFinite(value) ? value : null;
    } catch (error) {
      handleError(error, 'SAVED');
      return;
    }
    if (invNo === null) {
      dispatch({ type: 'connectivityRestored' });
      return;
    }
    try {
      const view = await getInvoice(invNo, window.location.search);
      dispatch({ type: 'invoiceLoaded', invNo, response: view });
    } catch (error) {
      handleError(error, 'SAVED');
    }
  }

  /** Starts a new draft with a new request id. */
  async function startNewInvoice(): Promise<void> {
    setQueue((current) => current.filter((step) => step === running.current));
    setLov(null);
    setBundleQty('1');
    setBundleQtyError(null);
    try {
      const response = await newDraft(window.location.search);
      dispatch({ type: 'draftLoaded', response });
    } catch (error) {
      handleError(error, 'NEW');
    }
  }

  const draft = state.draft;
  const header = draft?.header ?? null;
  const locked = isLocked(state);
  const bundleQtyMessage = locked ? null : bundleQtyError;
  const saveEnabled = saveAllowed(state, verdicts.current);
  const savedInvNo = savedInvoiceNo(state);
  const currentServiceId = draft?.lines[state.currentLineIndex]?.serviceId?.trim() ?? '';

  // A saved or queried invoice shows no draft preview.
  const draftPreview = state.saved !== null ? null : state.preview;
  const paymentStatus = draftPreview?.totals?.paymentStatus ?? null;

  const formLevel = formLevelMessages(state.messages);
  const createMessage = state.saved?.createResponse?.message ?? null;
  const showCreateMessage =
    createMessage !== null &&
    createMessage.trim() !== '' &&
    !Object.values(state.messages).some((list) => list.some((message) => message.text === createMessage));
  // The import result shows only while the screen shows its draft or the invoice saved from it.
  const shownImport =
    importSummary !== null &&
    draft !== null &&
    importSummary.requestId === draft.requestId &&
    (state.saved === null || state.saved.createResponse != null)
      ? importSummary
      : null;
  const openItemIds = [
    ...new Set([
      ...Object.values(state.openItems).flat(),
      ...(state.saved !== null ? [SAVED_READ_ONLY_OPEN_ITEM] : []),
    ]),
  ];

  const dismissFormLevel = (index: number): void => {
    // The first control after the messages group, focused when the dismissal leaves focus on the body.
    const group = messagesRef.current;
    const next =
      group === null || formRef.current === null
        ? undefined
        : Array.from(formRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).find(
            (element) =>
              !group.contains(element) && (group.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0,
          );
    const refs = [...(formLevel.refs[index] ?? [])].sort((a, b) => b.index - a.index);
    for (const ref of refs) {
      dispatch({ type: 'messageDismissed', source: ref.source, index: ref.index });
    }
    window.setTimeout(() => {
      const active = document.activeElement;
      if ((active === null || active === document.body) && next?.isConnected && next.matches(FOCUSABLE)) {
        next.focus();
      }
    }, 0);
  };

  /** Saves on form submission without navigating. */
  const onSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void save(false);
  };

  /** Keeps Enter in an input of a modal dialog from submitting the form. */
  const onFormKeyDown = (event: KeyboardEvent<HTMLFormElement>): void => {
    if (event.key === 'Enter' && event.target instanceof HTMLInputElement && event.target.closest('[aria-modal="true"]') !== null) {
      event.preventDefault();
    }
  };

  const screen = (
    <main aria-labelledby={`${baseId}-title`}>
    <form ref={formRef} className="screen" aria-labelledby={`${baseId}-title`} noValidate onSubmit={onSubmit} onKeyDown={onFormKeyDown}>
      <h1 className="screen-title" id={`${baseId}-title`}>
        Front Office Cashier Invoice
      </h1>

      <InvoiceHeaderForm
        state={state}
        dispatch={dispatch}
        onValidate={onValidate}
        onPatientChanged={() => void patientChanged()}
        onShowReservations={() => setLov('RESERV_NO')}
      />

      <InvoiceLinesGrid state={state} dispatch={dispatch} onValidateLine={onValidateLine} onRemoveLine={onRemoveLine} />

      <div className="lower-band">
        <TotalsPanel state={state} />
        <PaymentPanel state={state} dispatch={dispatch} onValidate={onValidate} />
      </div>

      {formLevel.messages.length > 0 && (
        <div ref={messagesRef} role="group" aria-label="Invoice messages">
          <FieldMessage messages={formLevel.messages} onDismiss={dismissFormLevel} />
        </div>
      )}

      {shownImport !== null && (
        <div className="msg" role="status">
          {importSummaryText(shownImport)}
        </div>
      )}

      {state.idempotencyConflict !== null && (
        <div className="form-error" role="alert">
          <span>{state.idempotencyConflict.text}</span>
          {state.idempotencyConflict.oracleErrorNumber !== null && (
            <span className="oracle-number">{oraText(state.idempotencyConflict.oracleErrorNumber)}</span>
          )}
        </div>
      )}

      {state.formError !== null && (
        <div className="form-error" role="alert">
          <span>{state.formError.text}</span>
          {state.formError.oracleErrorNumber !== null && (
            <span className="oracle-number">{oraText(state.formError.oracleErrorNumber)}</span>
          )}
          <button
            type="button"
            className="msg-dismiss"
            aria-label="Dismiss error"
            onClick={() => dispatch({ type: 'formErrorCleared' })}
          >
            ×
          </button>
        </div>
      )}

      {showCreateMessage && (
        <div className="msg" role="status">
          {createMessage}
        </div>
      )}

      <OpenItemNotice ids={openItemIds} serverMessages={state.openItemMessages} />

      <div className="action-bar" role="group" aria-label="Invoice actions">
        <button type="button" onClick={() => void showLastInvoice()}>
          Last Invoice
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('sms')}>
          Send invoice
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('barcode-sms')}>
          Send barcode
        </button>
        <button type="button" disabled={!saveEnabled} onClick={() => void save(true)}>
          Save &amp; Print
        </button>
        <button type="submit" disabled={!saveEnabled}>
          Save
        </button>
        {paymentStatus !== null && paymentStatus !== '' && (
          <span className="payment-status" role="status" aria-live="off" aria-label="Payment status">
            {paymentStatus}
          </span>
        )}
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('patient-card')}>
          Print Card
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('iqama-check')}>
          Print Check
        </button>
        <button type="button" onClick={onShowMore}>
          More
        </button>
        <button type="button" disabled={locked} onClick={() => void importRequestLines()}>
          Import Request
        </button>
        <button type="button" disabled={locked || currentServiceId === ''} onClick={() => void importPackageLines()}>
          Import Package
        </button>
        <label htmlFor={`${baseId}-bundle-qty`}>Bundle qty</label>
        <input
          id={`${baseId}-bundle-qty`}
          type="number"
          min={1}
          step={1}
          inputMode="numeric"
          disabled={locked}
          className={bundleQtyMessage !== null ? 'invalid' : undefined}
          aria-invalid={bundleQtyMessage !== null || undefined}
          value={bundleQty}
          onChange={(event) => {
            setBundleQty(event.currentTarget.value);
            setBundleQtyError(null);
          }}
        />
        {bundleQtyMessage !== null && (
          <FieldMessage messages={[]} fieldError={{ text: bundleQtyMessage, oracleErrorNumber: null }} />
        )}
        <button type="button" disabled={locked} aria-haspopup="dialog" onClick={() => setLov('OFFERS')}>
          Load offer
        </button>
        <button type="button" onClick={() => void startNewInvoice()}>
          New Invoice
        </button>
      </div>

      {lov === 'RESERV_NO' && draft !== null && (
        <LovPicker
          name="RESERV_NO"
          binds={{ draftDate: draft.draftDate, docIdx: header?.docId ?? null, patientNo: header?.patientNo ?? null }}
          onPick={() => undefined}
          onClose={() => setLov(null)}
        />
      )}

      {lov === 'OFFERS' && draft !== null && !locked && (
        <LovPicker
          name="OFFERS"
          binds={{ payType: header?.payType ?? null, draftDate: draft.draftDate }}
          onPick={(row) => void loadOffer(row)}
          onClose={() => setLov(null)}
        />
      )}
    </form>
    </main>
  );

  return <LovConnectivityContext.Provider value={reportLovConnectivity}>{screen}</LovConnectivityContext.Provider>;
}
