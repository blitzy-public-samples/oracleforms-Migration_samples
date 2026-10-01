import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
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
import {
  canSave,
  ENTRY_NOT_A_NUMBER,
  isSuperseded,
  parseDecimalEntry,
  recordRecheckNeeded,
  requestOrigin,
} from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState, JudgedLine, RequestOrigin } from '../state/invoiceDraft';
import FieldMessage, { announce, fieldMessageRefs } from '../components/FieldMessage';
import InvoiceHeaderForm, { WaitingListPanel } from '../components/InvoiceHeaderForm';
import InvoiceLinesGrid from '../components/InvoiceLinesGrid';
import LovPicker, { LovConnectivityContext } from '../components/LovPicker';
import OpenItemNotice from '../components/OpenItemNotice';
import PaymentPanel from '../components/PaymentPanel';
import TotalsPanel from '../components/TotalsPanel';

type InvoiceScreenProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onShowMore: () => void;
  /** True while the shell's first draft is loading. */
  initialLoading?: boolean;
};

/** Calls the screen shows as in flight: the Save and Save & Print presses until their create settles, and the calls they and the other actions make. */
type BusyAction =
  | 'save'
  | 'print'
  | 'create'
  | 'reload'
  | 'document'
  | 'sms'
  | 'coverage'
  | 'importRequest'
  | 'importPackage'
  | 'loadOffer'
  | 'lastInvoice'
  | 'newInvoice';

type BusyCounts = Readonly<Partial<Record<BusyAction, number>>>;

/** A follow-up call run after the render that carries the previous step's dispatches. */
type StepKind =
  | { kind: 'preview' }
  | { kind: 'validate'; target: ValidateTarget; lineIndex?: number }
  | { kind: 'visitLine' }
  | { kind: 'reimportRequests' };

/** A queued call with the draft's edit count when it was queued (D-145). */
type Step = StepKind & { editsSince: number };

/** LOVs this screen opens itself. */
type ScreenLov = 'RESERV_NO' | 'OFFERS';

/** Saved-invoice actions of the action bar. */
type SavedAction = 'sms' | Exclude<DocumentKind, 'invoice'>;

/** Latest outcome of an invoice action: its reducer source and label, which of its messages, open items and import result arrived with it, the text it arrived with, how many times in a row, and its arrival number. */
type ActionOutcome = {
  source: string;
  label: string;
  withMessages: boolean;
  withItems: boolean;
  withImport: boolean;
  signature: string;
  count: number;
  seq: number;
};

/** State the action outcome is detected from, as last seen. */
type OutcomeSnapshot = {
  messages: InvoiceDraftState['messages'];
  openItems: InvoiceDraftState['openItems'];
  importResult: ImportSummary | null;
  createResponse: object | null;
  latest: ActionOutcome | null;
  arrivals: number;
};

/** Action bar labels of the saved-invoice actions. */
const SAVED_ACTION_LABELS: Record<SavedAction, string> = {
  sms: 'Send invoice',
  'barcode-sms': 'Send barcode',
  'patient-card': 'Print Card',
  'iqama-check': 'Print Check',
};

/** Custom property on the root element holding the block size the outcome strip covers at the top of the viewport. */
const STRIP_BLOCK_SIZE = '--outcome-strip-block-size';

/** Custom property on the outcome strip holding the sticky offset below the connectivity banner. */
const STRIP_OFFSET = '--outcome-strip-offset';

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

const IMPORT_SOURCE = 'IMPORT';
const LINE_SOURCE = /^LINE:(\d+):/;

/** Sources holding the outcome of the last Save or import press, replaced by the next press. */
const ATTEMPT_SOURCES: ReadonlySet<string> = new Set(['CREATE', IMPORT_SOURCE]);
const SAVED_READ_ONLY_OPEN_ITEM = 'OI-56';

/** Enabled controls the keyboard can reach. */
const FOCUSABLE =
  'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])';

/** Input types that act as buttons. */
const BUTTON_INPUT_TYPES: ReadonlySet<string> = new Set(['button', 'submit', 'reset', 'image']);

/** Operator-started imports that a Save press waits for. */
const DRAFT_IMPORTS: ReadonlySet<BusyAction> = new Set<BusyAction>(['importRequest', 'importPackage', 'loadOffer']);

/** Text entry fields, which validate when left. */
const ENTRY_FIELDS = 'input:not([type="checkbox"], [type="radio"], [type="button"], [type="submit"], [type="reset"], [type="image"]), textarea';

/** True while at least one call of `action` is in flight. */
function isBusy(busy: BusyCounts, action: BusyAction): boolean {
  return (busy[action] ?? 0) > 0;
}

/** Status text naming the screen's most significant work in flight (D-146), or '' when idle. */
function busyText(busy: BusyCounts, head: Step | undefined, initialLoading: boolean): string {
  if (isBusy(busy, 'create')) {
    return 'Saving…';
  }
  if (initialLoading || isBusy(busy, 'newInvoice') || isBusy(busy, 'lastInvoice') || isBusy(busy, 'reload')) {
    return 'Loading invoice…';
  }
  if (
    isBusy(busy, 'importRequest') ||
    isBusy(busy, 'importPackage') ||
    isBusy(busy, 'loadOffer') ||
    head?.kind === 'visitLine' ||
    head?.kind === 'reimportRequests'
  ) {
    return 'Importing…';
  }
  if (isBusy(busy, 'coverage')) {
    return 'Reading coverage…';
  }
  if (head?.kind === 'validate') {
    return 'Validating…';
  }
  if (head?.kind === 'preview') {
    return 'Recalculating…';
  }
  if (isBusy(busy, 'save') || isBusy(busy, 'print')) {
    return 'Saving…';
  }
  if (isBusy(busy, 'document')) {
    return 'Printing…';
  }
  return isBusy(busy, 'sms') ? 'Sending…' : '';
}

/** Button text showing `active` while `busy`, else `idle`, at the width of the wider of the two. */
function BusyLabel({ busy, idle, active }: { busy: boolean; idle: string; active: string }) {
  return (
    <span className="busy-label" data-alternate={busy ? idle : active}>
      <span>{busy ? active : idle}</span>
    </span>
  );
}

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

/** True when the draft's patient number is not blank. */
function hasPatient(draft: DraftDto): boolean {
  const patientNo = draft.header.patientNo;
  return patientNo != null && patientNo.trim() !== '';
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

/** Text identity of a form error or idempotency conflict. */
function outcomeKey(outcome: { text: string; oracleErrorNumber: number | null }): string {
  return `${outcome.text}\u0000${outcome.oracleErrorNumber ?? ''}`;
}

/** Times in a row an outcome with the same text arrived as a new object: 0 while none is shown, 1 for the first. */
function useRepeatCount<T extends object>(value: T | null, key: (value: T) => string): number {
  const last = useRef<{ value: T; key: string; count: number } | null>(null);
  if (value === null) {
    last.current = null;
    return 0;
  }
  const previous = last.current;
  if (previous !== null && previous.value === value) {
    return previous.count;
  }
  const text = key(value);
  const count = previous !== null && previous.key === text ? previous.count + 1 : 1;
  last.current = { value, key: text, count };
  return count;
}

/** True when focus is on no usable control: none, the body, a disabled control or a removed element. */
function focusLost(): boolean {
  const active = document.activeElement;
  return active === null || active === document.body || !active.isConnected || active.matches(':disabled');
}

/** Note naming how many times in a row an outcome arrived, such as `(repeated 2 times)`. */
function repeatNote(count: number, verb: string): string {
  return `(${verb} ${count} times)`;
}

/** Action-bar label of a reducer source holding an invoice action's outcome, or null for a source no action owns. */
function actionLabel(source: string, importLabel: string): string | null {
  if (source.startsWith('SAVED:')) {
    const action = source.slice('SAVED:'.length);
    return Object.hasOwn(SAVED_ACTION_LABELS, action) ? SAVED_ACTION_LABELS[action as SavedAction] : null;
  }
  switch (source) {
    case 'CREATE':
      return 'Save';
    case 'PRINT':
      return 'Save & Print';
    case 'SAVED':
      return 'Invoice';
    case 'NEW':
      return 'New Invoice';
    case IMPORT_SOURCE:
      return importLabel;
    default:
      return null;
  }
}

/** Open items of an action source; the standing OI-56 of a saved invoice is not an action outcome. */
function outcomeOpenItems(openItems: InvoiceDraftState['openItems'], source: string): string[] {
  return source === 'SAVED' ? [] : (openItems[source] ?? []);
}

/** Text of an action outcome: its label or import result, then its first open item and first message, from the parts that arrived with it. */
function outcomeText(
  outcome: ActionOutcome,
  messages: InvoiceDraftState['messages'],
  openItems: InvoiceDraftState['openItems'],
  importResult: ImportSummary | null,
): string {
  const items = outcome.withItems ? outcomeOpenItems(openItems, outcome.source) : [];
  const list = outcome.withMessages ? (messages[outcome.source] ?? []) : [];
  const summary = outcome.withImport && importResult !== null ? importSummaryText(importResult) : null;
  const parts: string[] = [];
  if (items.length > 0) {
    parts.push(`not available in this build — open item ${items[0]}`);
  }
  const first = list.find((message) => message.severity === 'Blocking') ?? list[0];
  if (first !== undefined) {
    parts.push(list.length > 1 ? `${first.text} (+${list.length - 1} more)` : first.text);
  }
  if (summary !== null) {
    return [summary, ...parts].join(' — ');
  }
  return parts.length === 0 ? outcome.label : `${outcome.label}: ${parts.join(' — ')}`;
}

/** Latest invoice-action outcome: a source's new messages (not only dismissals), new open items or a new import result; a create clears it. */
function useActionOutcome(
  state: InvoiceDraftState,
  importResult: ImportSummary | null,
  importLabel: string,
): ActionOutcome | null {
  const seen = useRef<OutcomeSnapshot | null>(null);
  const { messages, openItems } = state;
  const createResponse = state.saved?.createResponse ?? null;
  const previous = seen.current;
  if (previous === null) {
    seen.current = { messages, openItems, importResult, createResponse, latest: null, arrivals: 0 };
    return null;
  }
  if (
    previous.messages === messages &&
    previous.openItems === openItems &&
    previous.importResult === importResult &&
    previous.createResponse === createResponse
  ) {
    return previous.latest;
  }
  let latest = createResponse !== null && createResponse !== previous.createResponse ? null : previous.latest;
  let arrivals = previous.arrivals;
  const sources = new Set([...Object.keys(messages), ...Object.keys(openItems), IMPORT_SOURCE]);
  for (const source of sources) {
    const label = actionLabel(source, importLabel);
    if (label === null || (source === 'CREATE' && state.saved !== null)) {
      continue;
    }
    const list = messages[source];
    const before = previous.messages[source] ?? [];
    const freshMessages = list !== undefined && list !== previous.messages[source] && list.some((message) => !before.includes(message));
    const items = outcomeOpenItems(openItems, source);
    const freshItems = items.length > 0 && items !== previous.openItems[source];
    const freshImport = source === IMPORT_SOURCE && importResult !== null && importResult !== previous.importResult;
    if (!freshMessages && !freshItems && !freshImport) {
      continue;
    }
    const summary = freshImport && importResult !== null ? importSummaryText(importResult) : '';
    const shownItems = freshItems ? items : [];
    const shownMessages = freshMessages ? (list ?? []) : [];
    const signature = [label, summary, ...shownItems, ...shownMessages.map((message) => `${message.severity}:${message.text}`)].join('\u0000');
    const count = latest !== null && latest.signature === signature ? latest.count + 1 : 1;
    arrivals += 1;
    latest = {
      source,
      label,
      withMessages: freshMessages,
      withItems: freshItems,
      withImport: freshImport,
      signature,
      count,
      seq: arrivals,
    };
  }
  if (
    latest !== null &&
    !(latest.withMessages && messages[latest.source] !== undefined) &&
    !(latest.withItems && outcomeOpenItems(openItems, latest.source).length > 0) &&
    !(latest.withImport && importResult !== null)
  ) {
    latest = null;
  }
  seen.current = { messages, openItems, importResult, createResponse, latest, arrivals };
  return latest;
}

/** True when a sibling component on this screen already renders the message beside its field or line; `lineCount` is the number of rows the grid renders. */
function shownElsewhere(source: string, message: MessageDto, lineCount: number): boolean {
  if (source === IMPORT_SOURCE) {
    return false;
  }
  // The grid renders every message of every `LINE:<i>:*` source of the rows it shows.
  const line = LINE_SOURCE.exec(source);
  if (line !== null) {
    return Number(line[1]) < lineCount;
  }
  const field = message.field?.toUpperCase() ?? '';
  return HEADER_FIELDS.has(field) || PAYMENT_FIELDS.has(field);
}

/** Messages no field or line renders, each distinct severity and text once, with every source position it came from; `lineCount` is the number of rows the grid renders. */
function formLevelMessages(messages: Record<string, MessageDto[]>, lineCount: number): FormLevelMessages {
  const result: FormLevelMessages = { messages: [], refs: [] };
  const positions = new Map<string, number>();
  for (const [source, list] of Object.entries(messages)) {
    list.forEach((message, index) => {
      if (shownElsewhere(source, message, lineCount)) {
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
export default function InvoiceScreen({ state, dispatch, onShowMore, initialLoading = false }: InvoiceScreenProps) {
  const baseId = useId();
  const latest = useRef(state);
  latest.current = state;
  const verdicts = useRef(new Map<string, AttemptVerdict>());
  recordVerdicts(state, verdicts.current);

  const [queue, setQueue] = useState<Step[]>([]);
  const running = useRef<Step | null>(null);
  /** Steps queued and not yet finished, updated with every enqueue and removal. */
  const outstanding = useRef(new Set<Step>());
  const idleWaiters = useRef<(() => void)[]>([]);
  /** Operator-started imports in flight. */
  const importing = useRef(0);
  /** Creates in flight; the runner starts no step while one is. */
  const creating = useRef(0);
  const [holdTick, setHoldTick] = useState(0);
  const commitWaiters = useRef<(() => void)[]>([]);
  const [commitTick, setCommitTick] = useState(0);
  const [busy, setBusy] = useState<BusyCounts>({});
  const [lov, setLov] = useState<ScreenLov | null>(null);
  const [bundleQty, setBundleQty] = useState('1');
  const [bundleQtyError, setBundleQtyError] = useState<string | null>(null);
  const [importSummary, setImportSummary] = useState<ImportSummary | null>(null);
  const importLabel = useRef('Import Request');
  const formRef = useRef<HTMLFormElement>(null);
  const messagesRef = useRef<HTMLDivElement>(null);
  const formErrorRef = useRef<HTMLDivElement>(null);
  const announcedPaymentStatus = useRef<{ requestId: string | null; status: string } | null>(null);
  const stripRef = useRef<HTMLDivElement>(null);
  const savedStatusRef = useRef<HTMLDivElement>(null);
  const conflictRef = useRef<HTMLDivElement>(null);
  const outcomeRef = useRef<HTMLDivElement>(null);
  const focusedCreate = useRef<object | null>(null);
  const focusHeaderOnLoad = useRef(false);
  const conflictCount = useRepeatCount(state.idempotencyConflict, outcomeKey);
  const formErrorCount = useRepeatCount(state.formError, outcomeKey);

  // Request id of the loaded draft whose preset doctor is validated once its patient is validated.
  const presetDoctor = useRef<string | null>(null);
  // Request id of the last draft checked for a preset doctor.
  const presetChecked = useRef<string | null>(null);
  // Step kinds built for a preset doctor, and the queued steps made from them.
  const presetKinds = useRef(new WeakSet<StepKind>());
  const presetSteps = useRef(new WeakSet<Step>());
  // Most recently queued DOCIDX validation step.
  const latestDoctorStep = useRef<Step | null>(null);
  const draftRequestId = state.draft?.requestId ?? null;

  /** Queues steps stamped with edit count `editsSince`; a DOCIDX step that is not a preset step cancels the pending preset run and drops its queued, not yet running steps. */
  const enqueue = useCallback((editsSince: number, ...kinds: StepKind[]) => {
    const steps: Step[] = kinds.map((kind) => {
      const step: Step = { ...kind, editsSince };
      if (presetKinds.current.has(kind)) {
        presetSteps.current.add(step);
      }
      return step;
    });
    const doctorSteps = steps.filter((step) => step.kind === 'validate' && step.target === 'DOCIDX');
    const doctorPicked = doctorSteps.some((step) => !presetSteps.current.has(step));
    if (doctorSteps.length > 0) {
      latestDoctorStep.current = doctorSteps[doctorSteps.length - 1];
    }
    const dropped = (step: Step): boolean => doctorPicked && step !== running.current && presetSteps.current.has(step);
    if (doctorPicked) {
      presetDoctor.current = null;
      for (const step of outstanding.current) {
        if (dropped(step)) {
          outstanding.current.delete(step);
        }
      }
    }
    for (const step of steps) {
      outstanding.current.add(step);
    }
    setQueue((current) => [...current.filter((step) => !dropped(step)), ...steps]);
  }, []);

  /** The DOCIDX then CLINICID validation steps of a preset doctor. */
  function presetDoctorSteps(): StepKind[] {
    const kinds: StepKind[] = [
      { kind: 'validate', target: 'DOCIDX' },
      { kind: 'validate', target: 'CLINICID' },
    ];
    for (const kind of kinds) {
      presetKinds.current.add(kind);
    }
    return kinds;
  }

  /** Queues one record check stamped with edit count `editsSince`, unless one is already waiting. */
  const enqueueRecordCheck = useCallback((editsSince: number) => {
    for (const step of outstanding.current) {
      if (step !== running.current && step.kind === 'validate' && step.target === 'RECORD') {
        return;
      }
    }
    const step: Step = { kind: 'validate', target: 'RECORD', editsSince };
    outstanding.current.add(step);
    setQueue((current) => [...current, step]);
  }, []);

  // Runs the head step, unless a create is in flight, and removes it from the queue when it finishes.
  useEffect(() => {
    const head = queue[0];
    if (head === undefined || running.current === head || creating.current > 0) {
      return;
    }
    running.current = head;
    void runStep(head).finally(() => {
      running.current = null;
      outstanding.current.delete(head);
      releaseIdleWaiters();
      setQueue((current) => current.filter((step) => step !== head));
    });
  }, [queue, holdTick]);

  // Queues the DOCIDX then CLINICID validation of a loaded draft's preset doctor, or waits for its patient's validation.
  useEffect(() => {
    if (presetChecked.current === draftRequestId) {
      return;
    }
    presetChecked.current = draftRequestId;
    presetDoctor.current = null;
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current) || draft.header.docId == null) {
      return;
    }
    if (hasPatient(draft)) {
      enqueue(current.editCount, ...presetDoctorSteps());
    } else {
      presetDoctor.current = draft.requestId;
    }
  }, [draftRequestId, enqueue]);

  // Resolves the waiters of the commit that carries their tick.
  useEffect(() => {
    for (const resolve of commitWaiters.current.splice(0)) {
      resolve();
    }
  }, [commitTick]);

  /** True when no queued step is waiting or running and no operator-started import is in flight. */
  function isIdle(): boolean {
    return outstanding.current.size === 0 && importing.current === 0;
  }

  /** Resolves the idle waiters once the screen is idle. */
  function releaseIdleWaiters(): void {
    if (isIdle()) {
      for (const resolve of idleWaiters.current.splice(0)) {
        resolve();
      }
    }
  }

  /** Resolves once the screen is idle. */
  function whenIdle(): Promise<void> {
    return isIdle() ? Promise.resolve() : new Promise((resolve) => idleWaiters.current.push(resolve));
  }

  /** Resolves once React has committed every update scheduled before the call, with its effects. */
  function whenCommitted(): Promise<void> {
    return new Promise((resolve) => {
      commitWaiters.current.push(resolve);
      setCommitTick((tick) => tick + 1);
    });
  }

  /** Leaves the focused entry field of the form, then waits until its validation, every queued step and every operator-started import have run and rendered; returns the field it left, if any. */
  async function flushEntry(): Promise<HTMLElement | null> {
    const active = document.activeElement;
    const entry =
      active instanceof HTMLElement && active.matches(ENTRY_FIELDS) && formRef.current?.contains(active) === true ? active : null;
    entry?.blur();
    await whenCommitted();
    while (!isIdle()) {
      await whenIdle();
      await whenCommitted();
    }
    return entry;
  }

  /** Counts one call of `action` into (1) or out of (-1) flight. */
  function adjustBusy(action: BusyAction, delta: 1 | -1): void {
    setBusy((current) => ({ ...current, [action]: Math.max(0, (current[action] ?? 0) + delta) }));
  }

  /** Runs `work` while one call of `action` counts as in flight. */
  async function track<T>(action: BusyAction, work: () => Promise<T>): Promise<T> {
    const draftImport = DRAFT_IMPORTS.has(action);
    adjustBusy(action, 1);
    if (draftImport) {
      importing.current += 1;
    }
    try {
      return await work();
    } finally {
      adjustBusy(action, -1);
      if (draftImport) {
        importing.current -= 1;
        releaseIdleWaiters();
      }
    }
  }

  const onValidate = useCallback(
    (target: ValidateTarget) => enqueue(latest.current.editCount, { kind: 'validate', target }),
    [enqueue],
  );

  const onValidateLine = useCallback(
    (index: number, target: ValidateTarget) => enqueue(latest.current.editCount, { kind: 'validate', target, lineIndex: index }),
    [enqueue],
  );

  const reportLovConnectivity = useCallback(
    (available: boolean) => dispatch({ type: available ? 'connectivityRestored' : 'connectivityLost' }),
    [dispatch],
  );

  /** Routes a failed call to the reducer and starts the recovery a stale-data error asks for, unless `origin` is superseded; `lineClientId` and `judged` name the line of a line source and the inputs it was sent with; `editsSince` is the failed call's edit count, which the recovery inherits. */
  function handleError(
    error: unknown,
    source: string,
    origin?: RequestOrigin,
    lineClientId?: string | null,
    judged?: JudgedLine,
    editsSince?: number,
  ): void {
    if (!(error instanceof ApiError)) {
      throw error;
    }
    if (error.type === 'field-validation') {
      dispatch({ type: 'validationFailed', target: source, lineIndex: null, error, origin, editsSince });
      return;
    }
    dispatch({ type: 'errorReceived', source, lineClientId, error, origin, judged });
    if (error.type !== 'oracle-business-error' || (origin !== undefined && isSuperseded(latest.current, origin))) {
      return;
    }
    const stamp = editsSince ?? latest.current.editCount;
    if (error.kind === 'RequestLinesStale') {
      const draft = latest.current.draft;
      if (draft !== null) {
        dispatch({ type: 'linesReplaced', lines: draft.lines.filter((line) => line.patServReqRowId == null), origin });
      }
      enqueue(stamp, { kind: 'reimportRequests' });
    } else if (error.kind === 'DefinitionStale') {
      enqueue(stamp, { kind: 'preview' });
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
        return runPreview(step.editsSince);
      case 'validate':
        return validate(step.target, step.lineIndex ?? null, step.editsSince);
      case 'visitLine':
        return importVisit(step.editsSince);
      case 'reimportRequests':
        return importRequestLines(true, step.editsSince);
    }
  }

  /** Reads patient coverage for a step queued at edit count `editsSince`; resolves false on an Oracle outage and stores no coverage for blank or failed reads. */
  async function readCoverage(draft: DraftDto, origin: RequestOrigin, editsSince: number): Promise<boolean> {
    const patientNo = draft.header.patientNo;
    if (patientNo == null || patientNo.trim() === '') {
      dispatch({ type: 'coverageApplied', response: null, origin, editsSince });
      return true;
    }
    try {
      const response = await track('coverage', () => getCoverage(patientNo, draft.draftDate, draft.parameters));
      dispatch({ type: 'coverageApplied', response, origin, editsSince });
      return true;
    } catch (error) {
      dispatch({ type: 'coverageApplied', response: null, origin, editsSince });
      handleError(error, 'COVERAGE', origin, undefined, undefined, editsSince);
      return !(error instanceof ApiError && (error.status === 503 || error.type === 'oracle-unavailable'));
    }
  }

  /** Validates a target queued at edit count `editsSince`, reading coverage first for PATIENTNO, and queues its visit-line or preview follow-up. */
  async function validate(target: ValidateTarget, lineIndex: number | null, editsSince: number): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current) || (lineIndex !== null && lineIndex >= draft.lines.length)) {
      return;
    }
    if (target === 'DOCIDX' && presetDoctor.current === draft.requestId) {
      presetDoctor.current = null;
    }
    const lineClientId = lineIndex === null ? null : (draft.lines[lineIndex]?.clientId ?? null);
    const sentLine = lineIndex === null ? undefined : draft.lines[lineIndex];
    const judged = sentLine === undefined ? undefined : { header: draft.header, line: sentLine };
    const origin = requestOrigin(draft, target === 'PATIENTNO');
    if (target === 'PATIENTNO') {
      const reachable = await readCoverage(draft, origin, editsSince);
      if (!reachable || isSuperseded(latest.current, origin)) {
        return;
      }
    }
    try {
      const response = await validateDraft({ draft, target, lineIndex });
      const recheck = lineIndex === null && recordRecheckNeeded(latest.current, target);
      dispatch({ type: 'validationApplied', target, lineIndex, lineClientId, response, origin, judged, editsSince });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      const followUps: StepKind[] = [];
      const now = latest.current.draft;
      if (
        target === 'PATIENTNO' &&
        presetDoctor.current === draft.requestId &&
        now !== null &&
        now.header.docId != null &&
        hasPatient(now)
      ) {
        presetDoctor.current = null;
        followUps.push(...presetDoctorSteps());
      }
      // A later queued DOCIDX validation chooses the visit line of the doctor it validates.
      if (
        target === 'DOCIDX' &&
        response.visitLine != null &&
        response.visitLine.kind !== 'None' &&
        latestDoctorStep.current === running.current
      ) {
        followUps.push({ kind: 'visitLine' });
      }
      if (PREVIEW_AFTER.has(target)) {
        followUps.push({ kind: 'preview' });
      }
      if (followUps.length > 0) {
        enqueue(editsSince, ...followUps);
      }
      // While a record-check message is held, a header answer is followed by the record check.
      if (recheck) {
        enqueueRecordCheck(editsSince);
      }
    } catch (error) {
      if (isFieldValidation(error)) {
        const recheck = lineIndex === null && recordRecheckNeeded(latest.current, target);
        dispatch({ type: 'validationFailed', target, lineIndex, lineClientId, error, origin, judged, editsSince });
        // While a record-check message is held, a header answer is followed by the record check.
        if (recheck && !isSuperseded(latest.current, origin)) {
          enqueueRecordCheck(editsSince);
        }
        return;
      }
      handleError(error, messageKey(target, lineIndex), origin, lineClientId, judged, editsSince);
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
    enqueue(current.editCount, { kind: 'validate', target: 'PATIENTNO' });
  }

  /** POST /api/invoices/preview for an editable draft with lines, queued at edit count `editsSince`; an editable draft without lines has its preview cleared. */
  async function runPreview(editsSince: number): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    if (draft.lines.length === 0) {
      dispatch({ type: 'previewCleared' });
      return;
    }
    const origin = requestOrigin(draft);
    try {
      const response = await previewInvoice(draft);
      dispatch({ type: 'previewApplied', response, sent: draft.lines, origin });
    } catch (error) {
      handleError(error, 'PREVIEW', origin, undefined, undefined, editsSince);
    }
  }

  /** Removes an unsaved line and recalculates. */
  const onRemoveLine = useCallback(
    (index: number) => {
      dispatch({ type: 'lineRemoved', index });
      enqueue(latest.current.editCount, { kind: 'preview' });
    },
    [dispatch, enqueue],
  );

  /** Saves the draft once the focused entry and the queued steps have settled (at once while another create is in flight), reloads it read-only and, for Save & Print, requests the invoice document, while the draft is still current. */
  async function save(print: boolean): Promise<void> {
    const pressed = latest.current;
    if (pressed.draft === null || !saveAllowed(pressed, verdicts.current)) {
      return;
    }
    const button: BusyAction = print ? 'print' : 'save';
    adjustBusy(button, 1);
    let draft = pressed.draft;
    let editsSince = pressed.editCount;
    if (creating.current === 0) {
      const left = await flushEntry();
      const settled = latest.current;
      if (settled.draft === null || isSuperseded(settled, requestOrigin(draft)) || !saveAllowed(settled, verdicts.current)) {
        adjustBusy(button, -1);
        const active = document.activeElement;
        if (left !== null && left.isConnected && (active === null || active === document.body)) {
          left.focus();
        }
        return;
      }
      draft = settled.draft;
      editsSince = settled.editCount;
    }
    const sent = draft;
    const origin = requestOrigin(sent);
    let invNo: number;
    creating.current += 1;
    try {
      const response = await track('create', () => createInvoice({ draft: sent }));
      dispatch({ type: 'saved', response, origin });
      invNo = response.invNo;
    } catch (error) {
      if (isFieldValidation(error)) {
        dispatch({ type: 'validationFailed', target: 'CREATE', lineIndex: null, error, origin, editsSince });
        return;
      }
      handleError(error, 'CREATE', origin, undefined, undefined, editsSince);
      return;
    } finally {
      creating.current -= 1;
      adjustBusy(button, -1);
      if (creating.current === 0) {
        setHoldTick((tick) => tick + 1);
      }
    }
    if (isSuperseded(latest.current, origin)) {
      return;
    }
    try {
      const view = await track('reload', () => getInvoice(invNo, window.location.search));
      dispatch({ type: 'invoiceLoaded', invNo, response: view, origin });
    } catch (error) {
      handleError(error, 'SAVED', origin);
    }
    if (print && !isSuperseded(latest.current, origin)) {
      try {
        await track('document', () => buildDocument(invNo, 'invoice'));
        dispatch({ type: 'connectivityRestored' });
      } catch (error) {
        handleError(error, 'PRINT', origin);
      }
    }
  }

  /** Imports the visit's selected service requests, then recalculates when lines were added or `linesChanged` is set; `editsSince` defaults to the edit count at the press. */
  async function importRequestLines(linesChanged = false, editsSince = latest.current.editCount): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const origin = requestOrigin(draft, true);
    importLabel.current = 'Import Request';
    try {
      const response = await importRequests({ draft });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin, editsSince });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Import Request', draft.requestId, response);
      if (linesChanged || (response.lines ?? []).length > 0) {
        enqueue(editsSince, { kind: 'preview' });
      }
    } catch (error) {
      if (isFieldValidation(error)) {
        dispatch({ type: 'validationFailed', target: IMPORT_SOURCE, lineIndex: null, error, origin, editsSince });
        return;
      }
      handleError(error, IMPORT_SOURCE, origin, undefined, undefined, editsSince);
    }
  }

  /** Expands the current line's package service into its parent and component lines. */
  async function importPackageLines(): Promise<void> {
    const current = latest.current;
    const editsSince = current.editCount;
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
    importLabel.current = 'Import Package';
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
        dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response: { ...response, lines: [] }, origin, editsSince });
      } else {
        dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin, editsSince });
      }
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Import Package', draft.requestId, response);
      if (imported.length > 0) {
        enqueue(editsSince, { kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin, undefined, undefined, editsSince);
    }
  }

  /** Loads the lines of the bundled offer chosen in the OFFERS list. */
  async function loadOffer(row: Record<string, unknown>): Promise<void> {
    const current = latest.current;
    const editsSince = current.editCount;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    importLabel.current = 'Load offer';
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
      dispatch({ type: 'validationFailed', target: IMPORT_SOURCE, lineIndex: null, error, origin, editsSince });
      return;
    }
    try {
      const response = await importBundledOffer({
        draft,
        offerId,
        bundleQty: quantity.text,
      });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin, editsSince });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Load offer', draft.requestId, response);
      if ((response.lines ?? []).length > 0) {
        enqueue(editsSince, { kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin, undefined, undefined, editsSince);
    }
  }

  /** Adds the automatic consultation, review or fixed-service visit line for a step queued at edit count `editsSince`. */
  async function importVisit(editsSince: number): Promise<void> {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || isLocked(current)) {
      return;
    }
    const origin = requestOrigin(draft, true);
    importLabel.current = 'Visit line';
    try {
      const response = await importVisitLine({ draft });
      dispatch({ type: 'linesImported', source: IMPORT_SOURCE, response, origin, editsSince });
      if (isSuperseded(latest.current, origin)) {
        return;
      }
      rememberImport('Visit line', draft.requestId, response);
      if ((response.lines ?? []).length > 0) {
        enqueue(editsSince, { kind: 'preview' });
      }
    } catch (error) {
      handleError(error, IMPORT_SOURCE, origin, undefined, undefined, editsSince);
    }
  }

  /** SMS and document requests for the saved invoice on screen (D-122). */
  async function runSavedAction(action: SavedAction): Promise<void> {
    const invNo = savedInvoiceNo(latest.current);
    if (invNo === null) {
      return;
    }
    const draft = latest.current.draft;
    const origin = draft === null ? undefined : requestOrigin(draft);
    try {
      if (action === 'sms') {
        await track('sms', () => sendSms(invNo));
      } else {
        await track(action === 'barcode-sms' ? 'sms' : 'document', () => buildDocument(invNo, action));
      }
      dispatch({ type: 'connectivityRestored' });
    } catch (error) {
      handleError(error, `SAVED:${action}`, origin);
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
    for (const step of outstanding.current) {
      if (step !== running.current) {
        outstanding.current.delete(step);
      }
    }
    releaseIdleWaiters();
    setQueue((current) => current.filter((step) => step === running.current));
    setLov(null);
    setBundleQty('1');
    setBundleQtyError(null);
    try {
      const response = await newDraft(window.location.search);
      focusHeaderOnLoad.current = true;
      dispatch({ type: 'draftLoaded', response });
    } catch (error) {
      handleError(error, 'NEW');
    }
  }

  const draft = state.draft;
  const header = draft?.header ?? null;
  const locked = isLocked(state);
  const bundleQtyMessage = locked ? null : bundleQtyError;
  const bundleQtyMessageId = `${baseId}-bundle-qty-msg`;
  const bundleQtyFieldError = bundleQtyMessage === null ? null : { text: bundleQtyMessage, oracleErrorNumber: null };
  const bundleQtyRefs = fieldMessageRefs(bundleQtyMessageId, [], bundleQtyFieldError);
  const saveEnabled = saveAllowed(state, verdicts.current);
  const savedInvNo = savedInvoiceNo(state);
  const currentServiceId = draft?.lines[state.currentLineIndex]?.serviceId?.trim() ?? '';

  const activity = busyText(busy, queue[0], initialLoading);
  const totalsPending =
    !locked && queue.some((step) => step.kind === 'preview' || (step.kind === 'validate' && PREVIEW_AFTER.has(step.target)));
  const busyTargets: ReadonlySet<string> = new Set(
    locked ? [] : queue.flatMap((step) => (step.kind === 'validate' && step.lineIndex === undefined ? [step.target] : [])),
  );
  const saving = isBusy(busy, 'save');
  const printing = isBusy(busy, 'print');

  const draftPreview = state.saved !== null ? null : state.preview;
  const paymentStatus = draftPreview?.totals?.paymentStatus ?? null;

  // Announces the payment status politely once per changed non-empty value of each draft.
  useEffect(() => {
    const last = announcedPaymentStatus.current;
    if (
      paymentStatus === null ||
      paymentStatus === '' ||
      (last !== null && last.requestId === draftRequestId && last.status === paymentStatus)
    ) {
      return;
    }
    announcedPaymentStatus.current = { requestId: draftRequestId, status: paymentStatus };
    announce(`Payment status: ${paymentStatus}`);
  }, [paymentStatus, draftRequestId]);

  const formLevel = formLevelMessages(state.messages, (state.saved?.view?.lines ?? state.draft?.lines ?? []).length);
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
  const outcome = useActionOutcome(state, shownImport, importLabel.current);
  const outcomeItems = outcome === null || !outcome.withItems ? [] : outcomeOpenItems(state.openItems, outcome.source);
  const hasOutcome = savedInvNo !== null || state.idempotencyConflict !== null || state.formError !== null || outcome !== null;
  const outcomeSeq = outcome?.seq ?? null;

  // Focuses each arriving action outcome when focus was lost.
  useEffect(() => {
    if (outcomeSeq !== null && focusLost()) {
      outcomeRef.current?.focus({ preventScroll: true });
    }
  }, [outcomeSeq]);

  // Offsets the outcome strip below a sticky or fixed connectivity banner and publishes the block size both cover.
  useLayoutEffect(() => {
    const root = document.documentElement;
    const strip = stripRef.current;
    if (strip === null) {
      root.style.removeProperty(STRIP_BLOCK_SIZE);
      return;
    }
    const banner = document.querySelector<HTMLElement>('.connectivity-banner');
    const measure = (): void => {
      const position = banner !== null && banner.isConnected ? getComputedStyle(banner).position : '';
      const offset = banner !== null && (position === 'sticky' || position === 'fixed') ? banner.getBoundingClientRect().height : 0;
      strip.style.setProperty(STRIP_OFFSET, `${offset}px`);
      const height = strip.getBoundingClientRect().height;
      if (height > 0) {
        root.style.setProperty(STRIP_BLOCK_SIZE, `calc(${Math.ceil(height + offset)}px + var(--space-2))`);
      } else {
        root.style.removeProperty(STRIP_BLOCK_SIZE);
      }
    };
    measure();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(strip);
    if (banner !== null) {
      observer?.observe(banner);
    }
    return () => {
      observer?.disconnect();
      root.style.removeProperty(STRIP_BLOCK_SIZE);
    };
  }, [hasOutcome, state.connectivityDown]);

  const createResponse = state.saved?.createResponse ?? null;
  const loadedInvNo = state.saved !== null && createResponse === null ? savedInvNo : null;

  // Focuses the saved status once per successful create.
  useEffect(() => {
    if (createResponse === null || createResponse === focusedCreate.current) {
      return;
    }
    focusedCreate.current = createResponse;
    savedStatusRef.current?.focus({ preventScroll: true });
  }, [createResponse]);

  // Focuses each idempotency conflict as it arrives.
  useEffect(() => {
    if (state.idempotencyConflict !== null) {
      conflictRef.current?.focus({ preventScroll: true });
    }
  }, [state.idempotencyConflict]);

  // Focuses an arriving form error when focus was lost.
  useEffect(() => {
    if (state.formError !== null && focusLost()) {
      formErrorRef.current?.focus({ preventScroll: true });
    }
  }, [state.formError]);

  // Focuses the status of a loaded Last Invoice when focus was lost.
  useEffect(() => {
    if (loadedInvNo !== null && focusLost()) {
      savedStatusRef.current?.focus({ preventScroll: true });
    }
  }, [loadedInvNo]);

  // After New Invoice, focuses the first enabled header control, preferring one the operator can edit.
  useEffect(() => {
    if (!focusHeaderOnLoad.current || draft === null) {
      return;
    }
    focusHeaderOnLoad.current = false;
    const controls = Array.from(formRef.current?.querySelector('.header-grid')?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
    (controls.find((control) => !control.matches('[readonly]')) ?? controls[0])?.focus();
  }, [draft]);

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

  /** Clears the form error and, when focus falls to the body, focuses the first control after it that is still enabled. */
  const dismissFormError = (): void => {
    const block = formErrorRef.current;
    const following =
      block === null || formRef.current === null
        ? []
        : Array.from(formRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
            (element) =>
              !block.contains(element) && (block.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0,
          );
    dispatch({ type: 'formErrorCleared' });
    window.setTimeout(() => {
      const active = document.activeElement;
      if (active === null || active === document.body) {
        following.find((element) => element.isConnected && element.matches(FOCUSABLE))?.focus();
      }
    }, 0);
  };

  /** Saves on form submission without navigating. */
  const onSubmit = (event: FormEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void save(false);
  };

  /** Keeps Enter in an input, other than one that acts as a button outside a modal dialog, from submitting the form (D-144). */
  const onFormKeyDown = (event: KeyboardEvent<HTMLFormElement>): void => {
    const target = event.target;
    if (
      event.key === 'Enter' &&
      target instanceof HTMLInputElement &&
      (!BUTTON_INPUT_TYPES.has(target.type) || target.closest('[aria-modal="true"]') !== null)
    ) {
      event.preventDefault();
    }
  };

  const screen = (
    <main aria-labelledby={`${baseId}-title`}>
    <div className="busy-anchor">
      <div className="busy-status" role="status" aria-live="polite">
        {activity}
      </div>
    </div>
    <form ref={formRef} className="screen" aria-labelledby={`${baseId}-title`} aria-busy={activity !== '' || undefined} noValidate onSubmit={onSubmit} onKeyDown={onFormKeyDown}>
      <h1 className="screen-title" id={`${baseId}-title`}>
        Front Office Cashier Invoice
      </h1>

      {hasOutcome && (
        <div ref={stripRef} className="outcome-strip">
          {savedInvNo !== null && (
            <div ref={savedStatusRef} className="saved-status" role="status" tabIndex={-1}>
              {state.saved?.createResponse != null ? (
                <>
                  <span>
                    Invoice <strong>{savedInvNo}</strong> saved.
                  </span>
                  {showCreateMessage && <span className="saved-status-message">{createMessage}</span>}
                </>
              ) : (
                <span>
                  Invoice <strong>{savedInvNo}</strong> — saved invoice, read-only.
                </span>
              )}
            </div>
          )}

          {state.idempotencyConflict !== null && (
            <div
              key={`conflict-${conflictCount}`}
              ref={conflictRef}
              className={conflictCount > 1 ? 'form-error outcome-flash' : 'form-error'}
              role="alert"
              tabIndex={-1}
            >
              <span>{state.idempotencyConflict.text}</span>
              {state.idempotencyConflict.oracleErrorNumber !== null && (
                <span className="oracle-number">{oraText(state.idempotencyConflict.oracleErrorNumber)}</span>
              )}
              {conflictCount > 1 && <span>{repeatNote(conflictCount, 'repeated')}</span>}
            </div>
          )}

          {state.formError !== null && (
            <div
              key={`form-error-${formErrorCount}`}
              ref={formErrorRef}
              className={formErrorCount > 1 ? 'form-error outcome-flash' : 'form-error'}
              role="alert"
              tabIndex={-1}
            >
              <span>{state.formError.text}</span>
              {state.formError.oracleErrorNumber !== null && (
                <span className="oracle-number">{oraText(state.formError.oracleErrorNumber)}</span>
              )}
              {formErrorCount > 1 && <span>{repeatNote(formErrorCount, 'repeated')}</span>}
              <button
                type="button"
                className="msg-dismiss"
                aria-label="Dismiss error"
                onClick={dismissFormError}
              >
                ×
              </button>
            </div>
          )}

          {outcome !== null && (
            <div ref={outcomeRef} className="outcome-notice" role="status" tabIndex={-1}>
              <span key={outcome.seq} className={outcome.count > 1 ? 'outcome-flash' : undefined}>
                {outcomeText(outcome, state.messages, state.openItems, shownImport)}
                {outcome.count > 1 && ` ${repeatNote(outcome.count, 'repeated')}`}
              </span>
            </div>
          )}
        </div>
      )}

      <InvoiceHeaderForm
        state={state}
        dispatch={dispatch}
        onValidate={onValidate}
        onPatientChanged={() => void patientChanged()}
        busyTargets={busyTargets}
      />

      <InvoiceLinesGrid state={state} dispatch={dispatch} onValidateLine={onValidateLine} onRemoveLine={onRemoveLine} />

      <div className="lower-band">
        <WaitingListPanel state={state} onShowReservations={() => setLov('RESERV_NO')} />
        <TotalsPanel state={state} pending={totalsPending} />
        <PaymentPanel state={state} dispatch={dispatch} onValidate={onValidate} busyTargets={busyTargets} />
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

      <OpenItemNotice
        ids={openItemIds}
        serverMessages={state.openItemMessages}
        recent={outcome === null || outcomeItems.length === 0 ? undefined : { id: outcomeItems[0], count: outcome.count }}
      />

      <div className="action-bar" role="group" aria-label="Invoice actions">
        <button
          type="button"
          aria-busy={isBusy(busy, 'lastInvoice') || undefined}
          onClick={() => void track('lastInvoice', showLastInvoice)}
        >
          <BusyLabel busy={isBusy(busy, 'lastInvoice')} idle="Last Invoice" active="Loading…" />
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('sms')}>
          Send invoice
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('barcode-sms')}>
          Send barcode
        </button>
        <button type="button" disabled={!saveEnabled} aria-busy={printing || undefined} onClick={() => void save(true)}>
          <BusyLabel busy={printing} idle="Save & Print" active="Saving…" />
        </button>
        <button type="submit" disabled={!saveEnabled} aria-busy={saving || undefined}>
          <BusyLabel busy={saving} idle="Save" active="Saving…" />
        </button>
        {paymentStatus !== null && paymentStatus !== '' && <span className="payment-status">{paymentStatus}</span>}
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('patient-card')}>
          Print Card
        </button>
        <button type="button" disabled={savedInvNo === null} onClick={() => void runSavedAction('iqama-check')}>
          Print Check
        </button>
        <button type="button" onClick={onShowMore}>
          More
        </button>
        <button
          type="button"
          disabled={locked}
          aria-busy={isBusy(busy, 'importRequest') || undefined}
          onClick={() => void track('importRequest', () => importRequestLines())}
        >
          <BusyLabel busy={isBusy(busy, 'importRequest')} idle="Import Request" active="Importing…" />
        </button>
        <button
          type="button"
          disabled={locked || currentServiceId === ''}
          aria-busy={isBusy(busy, 'importPackage') || undefined}
          onClick={() => void track('importPackage', importPackageLines)}
        >
          <BusyLabel busy={isBusy(busy, 'importPackage')} idle="Import Package" active="Importing…" />
        </button>
        {/* Bundle qty label and input render as one group that wraps as a unit. */}
        <span className="action-group">
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
            aria-describedby={bundleQtyRefs.describedBy}
            aria-errormessage={bundleQtyRefs.errorMessage}
            value={bundleQty}
            onChange={(event) => {
              setBundleQty(event.currentTarget.value);
              setBundleQtyError(null);
            }}
          />
        </span>
        {bundleQtyFieldError !== null && (
          <FieldMessage id={bundleQtyMessageId} messages={[]} fieldError={bundleQtyFieldError} />
        )}
        <button
          type="button"
          disabled={locked}
          aria-haspopup="dialog"
          aria-busy={isBusy(busy, 'loadOffer') || undefined}
          onClick={() => setLov('OFFERS')}
        >
          <BusyLabel busy={isBusy(busy, 'loadOffer')} idle="Load offer" active="Loading offer…" />
        </button>
        <button
          type="button"
          aria-busy={isBusy(busy, 'newInvoice') || undefined}
          onClick={() => void track('newInvoice', startNewInvoice)}
        >
          <BusyLabel busy={isBusy(busy, 'newInvoice')} idle="New Invoice" active="Loading…" />
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
          onPick={(row) => void track('loadOffer', () => loadOffer(row))}
          onClose={() => setLov(null)}
        />
      )}
    </form>
    </main>
  );

  return <LovConnectivityContext.Provider value={reportLovConnectivity}>{screen}</LovConnectivityContext.Provider>;
}
