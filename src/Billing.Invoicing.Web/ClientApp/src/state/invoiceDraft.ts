import type {
  AdjustedKey,
  AdjustedValues,
  CoverageResponse,
  CreateInvoiceResponse,
  DiscountLimitChoice,
  DraftDto,
  EditablePreviewLine,
  ImportResponse,
  InvoiceHeaderDraft,
  InvoiceLineDraft,
  InvoiceViewResponse,
  MessageDto,
  MoreDetailsResponse,
  NewDraftResponse,
  PreviewResponse,
  ValidateDraftResponse,
} from '../api/types';
import type { ApiError } from '../api/client';

/** Shared T_INV / D_INV draft state for the Invoice and More details screens. */
export interface InvoiceDraftState {
  draft: DraftDto | null;
  display: Record<string, string | null>;
  lineDisplay: Record<string, { XCAT_NAMEX?: string | null; SERVICEDESC?: string | null }>;
  coverage: CoverageResponse | null;
  /** Trimmed patient number the stored coverage and the COVERAGE message source were read for. */
  coveragePatientNo: string | null;
  preview: PreviewResponse | null;
  currentLineIndex: number;
  moreDetails: MoreDetailsResponse | null;
  messages: Record<string, MessageDto[]>;
  openItems: Record<string, string[]>;
  openItemMessages: Record<string, string>;
  saved: { invNo: number; view: InvoiceViewResponse | null; createResponse: CreateInvoiceResponse | null } | null;
  readOnly: boolean;
  connectivityDown: boolean;
  idempotencyConflict: { text: string; oracleErrorNumber: number | null } | null;
  discountPrompt: { target: 'FINALDISC_PERC' | 'FINALDISC'; text: string } | null;
  formError: { text: string; oracleErrorNumber: number | null; package: string | null; kind: string | null } | null;
  fieldErrors: Record<string, { text: string; oracleErrorNumber: number | null; kind: string | null }>;
  entryErrors: Record<string, string>;
  /** Server judgement of PRICE editability by line client id, with the service, patient and company it was judged on. */
  priceEditable: Record<string, PriceJudgement>;
}

/** Server judgement of PRICE editability on one line, and the service, patient and company it was judged on. */
export interface PriceJudgement {
  serviceId: string | null;
  patientNo: string | null;
  compCode: string | null;
  editable: boolean;
}

/** Draft, and for patient-bound requests the patient number, a request was sent for. */
export interface RequestOrigin {
  requestId: string;
  patientNo?: string | null;
}

/** Actions the screens dispatch after their API calls and operator edits; `origin` names the request a response belongs to. */
export type InvoiceDraftAction =
  | { type: 'draftLoaded'; response: NewDraftResponse }
  | { type: 'headerFieldChanged'; field: keyof InvoiceHeaderDraft; value: InvoiceHeaderDraft[keyof InvoiceHeaderDraft] }
  | { type: 'lineFieldChanged'; index: number; field: keyof InvoiceLineDraft; value: InvoiceLineDraft[keyof InvoiceLineDraft] }
  | { type: 'lineAdded' }
  | { type: 'lineRemoved'; index: number }
  | { type: 'currentLineSelected'; index: number }
  | { type: 'displaySet'; values: Record<string, string | null>; lineClientId?: string }
  | { type: 'linesImported'; source: string; response: ImportResponse; origin?: RequestOrigin }
  | { type: 'linesReplaced'; lines: InvoiceLineDraft[]; origin?: RequestOrigin }
  | { type: 'validationApplied'; target: string; lineIndex: number | null; lineClientId?: string | null; response: ValidateDraftResponse; origin?: RequestOrigin }
  | { type: 'validationFailed'; target: string; lineIndex: number | null; lineClientId?: string | null; error: ApiError; origin?: RequestOrigin }
  | { type: 'discountChoiceMade'; choice: DiscountLimitChoice }
  | { type: 'coverageApplied'; response: CoverageResponse | null; origin: RequestOrigin }
  | { type: 'patientContextCleared'; origin: RequestOrigin }
  | { type: 'previewApplied'; response: PreviewResponse; sent?: readonly InvoiceLineDraft[]; origin: RequestOrigin }
  | { type: 'previewCleared' }
  | { type: 'saved'; response: CreateInvoiceResponse; origin: RequestOrigin }
  | { type: 'invoiceLoaded'; invNo: number; response: InvoiceViewResponse; origin?: RequestOrigin }
  | { type: 'moreDetailsLoaded'; response: MoreDetailsResponse }
  | { type: 'errorReceived'; source: string; lineClientId?: string | null; error: ApiError; origin?: RequestOrigin }
  | { type: 'connectivityLost' }
  | { type: 'connectivityRestored' }
  | { type: 'formErrorCleared' }
  | { type: 'messageDismissed'; source: string; index: number }
  | { type: 'entryRejected'; field: string; lineIndex: number | null; text: string }
  | { type: 'entryAccepted'; field: string; lineIndex: number | null };

/** State before any draft has been loaded. */
export const initialInvoiceDraftState: InvoiceDraftState = {
  draft: null,
  display: {},
  lineDisplay: {},
  coverage: null,
  coveragePatientNo: null,
  preview: null,
  currentLineIndex: 0,
  moreDetails: null,
  messages: {},
  openItems: {},
  openItemMessages: {},
  saved: null,
  readOnly: false,
  connectivityDown: false,
  idempotencyConflict: null,
  discountPrompt: null,
  formError: null,
  fieldErrors: {},
  entryErrors: {},
  priceEditable: {},
};

/** Operator decimal entry: empty, the trimmed text as entered, or the reason it is rejected. */
export type DecimalEntry = { kind: 'empty' } | { kind: 'value'; text: string } | { kind: 'invalid'; message: string };

/** Field error of an entry that is not a decimal number. */
export const ENTRY_NOT_A_NUMBER = 'Enter a valid number.';

/** Field error of a decimal entry the Api cannot hold exactly. */
export const ENTRY_TOO_PRECISE = 'Enter a number of at most 28 significant digits and 28 decimal places.';

type AdjustedTarget =
  | { scope: 'header'; field: keyof InvoiceHeaderDraft }
  | { scope: 'line'; field: keyof InvoiceLineDraft }
  | { scope: 'display'; key: string };

/** Destination of each server-adjusted value: a header field, a line field or a display key. */
export const ADJUSTED_KEY_MAP: Readonly<Record<AdjustedKey, AdjustedTarget>> = {
  AMOUNT_1: { scope: 'header', field: 'amount1' },
  AMOUNT_2: { scope: 'header', field: 'amount2' },
  CLAIM_NO: { scope: 'header', field: 'claimNo' },
  ADD_TO_LIST: { scope: 'header', field: 'addToList' },
  SUB_PAYTYPE: { scope: 'header', field: 'subPayType' },
  PAYTYPE: { scope: 'header', field: 'payType' },
  FINALDISC_PERC: { scope: 'header', field: 'finalDiscPerc' },
  FINALDISC: { scope: 'header', field: 'finalDisc' },
  DISC_T: { scope: 'header', field: 'discT' },
  DOCIDX: { scope: 'header', field: 'docId' },
  LDISCT: { scope: 'line', field: 'discountType' },
  REUND: { scope: 'display', key: 'REUND' },
};

const DISCOUNT_LIMIT_TEXT = 'Maximum discount allawed is';
const STALE_KINDS: ReadonlySet<string> = new Set(['RequestLinesStale', 'DefinitionStale']);
const LINE_KEY = /^LINE:(\d+):(.*)$/;
const CLIENT_KEY = /^CLIENT:(.+):([^:]+)$/;
const DECIMAL_TEXT = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/;
const MAX_DECIMAL_DIGITS = 28;

/** D_INV item names whose errors belong to one line. */
const LINE_ITEMS: ReadonlySet<string> = new Set([
  'SERVICEID',
  'QTY',
  'PRICE',
  'LDISCT',
  'DISC',
  'MY_DISC',
  'CATID',
  'TEETH_NO',
  'TOOTH_SURFACE',
  'TEETH_NO2',
  'APPROV_DATE',
  'APPROV_VALIDITY',
  'APPROV_REF_NO',
  'REQ_NEED_A',
  'REQ_A_STATUS',
  'FIXPAY',
  'PAYRATE',
]);

type DiscountPromptTarget = 'FINALDISC_PERC' | 'FINALDISC';
type Lists<T> = Record<string, T[]>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Source key for validation messages: `LINE:<i>:<TARGET>` for a line, else the target. */
function messageKey(target: string, lineIndex: number | null): string {
  return lineIndex !== null ? `LINE:${lineIndex}:${target}` : target;
}

/** New upper-case client id for a draft line. */
function newClientId(): string {
  return crypto.randomUUID().toUpperCase();
}

/** Returns the line with a client id, assigning one when it is missing or empty. */
function withClientId(line: InvoiceLineDraft): InvoiceLineDraft {
  return line.clientId == null || line.clientId === '' ? { ...line, clientId: newClientId() } : line;
}

/** A blank draft line with rate discount type and a fresh client id. */
function emptyLine(): InvoiceLineDraft {
  return {
    serviceId: null,
    qty: null,
    priceOverride: null,
    usePriceOverride: null,
    discountType: 'R',
    disc: null,
    myDisc: null,
    teethNo: null,
    toothSurface: null,
    teethNo2: null,
    patServReqRowId: null,
    approvDate: null,
    approvValidity: null,
    approvRefNo: null,
    claimNo: null,
    reqNeedA: null,
    reqAStatus: null,
    packageServiceId: null,
    packageInstanceId: null,
    packageLineRole: null,
    packageComponentOrder: null,
    packageParentLineId: null,
    packagePricingMethod: null,
    packageDefinitionToken: null,
    offerId: null,
    offerDtlId: null,
    offerType: null,
    offerInstanceId: null,
    offerLineRole: null,
    offerParentLineId: null,
    offerPriceApplied: null,
    offerDisApplied: null,
    offerNameSnapshot: null,
    offerObjectVersionNumber: null,
    offerDtlObjectVersionNumber: null,
    clientId: newClientId(),
    price: null,
  };
}

/** Returns a copy of `obj` with `key` set to `value`. */
function withField<T extends object, K extends keyof T>(obj: T, key: K, value: T[K]): T {
  return { ...obj, [key]: value };
}

/** True when `key` is one of the adjusted keys the map routes. */
function isAdjustedKey(key: string): key is AdjustedKey {
  return Object.prototype.hasOwnProperty.call(ADJUSTED_KEY_MAP, key);
}

/** Merges server-adjusted values into the draft header, the given line and the display values. */
function applyAdjusted(
  draft: DraftDto,
  display: Record<string, string | null>,
  adjusted: AdjustedValues | null | undefined,
  lineIndex: number | null,
): { draft: DraftDto; display: Record<string, string | null> } {
  if (adjusted == null) {
    return { draft, display };
  }
  let header = draft.header;
  let lines = draft.lines;
  let nextDisplay = display;
  for (const key of Object.keys(adjusted)) {
    const value = adjusted[key];
    if (value === undefined || !isAdjustedKey(key)) {
      continue;
    }
    const target = ADJUSTED_KEY_MAP[key];
    switch (target.scope) {
      case 'header':
        header = withField(header, target.field, value as InvoiceHeaderDraft[keyof InvoiceHeaderDraft]);
        break;
      case 'line':
        if (lineIndex !== null && lineIndex >= 0 && lineIndex < lines.length) {
          const updated = withField(lines[lineIndex], target.field, value as InvoiceLineDraft[keyof InvoiceLineDraft]);
          lines = lines.map((line, i) => (i === lineIndex ? updated : line));
        }
        break;
      case 'display':
        nextDisplay = { ...nextDisplay, [target.key]: value == null ? null : String(value) };
        break;
    }
  }
  const changed = header !== draft.header || lines !== draft.lines;
  return { draft: changed ? { ...draft, header, lines } : draft, display: nextDisplay };
}

/** Sets `record[key]` to the list, or removes the key when the list is empty. */
function replaceSource<T>(record: Lists<T>, key: string, list: readonly T[] | null | undefined): Lists<T> {
  const next = { ...record };
  if (list != null && list.length > 0) {
    next[key] = [...list];
  } else {
    delete next[key];
  }
  return next;
}

/** Sets `record[key]` to a copy of the list, empty lists included. */
function setSource<T>(record: Lists<T>, key: string, list: readonly T[]): Lists<T> {
  return { ...record, [key]: [...list] };
}

/** Distinct ids in first-seen order, without nulls or empty strings. */
function uniq(ids: readonly (string | null | undefined)[]): string[] {
  const seen = new Set<string>();
  for (const id of ids) {
    if (id != null && id !== '') {
      seen.add(id);
    }
  }
  return [...seen];
}

/** True for the two final-discount prompt targets. */
function isDiscountPromptTarget(value: string | null | undefined): value is DiscountPromptTarget {
  return value === 'FINALDISC_PERC' || value === 'FINALDISC';
}

/** Builds the DISC_ALERT prompt from a Blocking maximum-discount message, or returns null. */
function discountPromptFrom(
  messages: readonly MessageDto[] | null | undefined,
  target: string,
  header: InvoiceHeaderDraft | null | undefined,
): InvoiceDraftState['discountPrompt'] {
  const message = (messages ?? []).find(
    (m) => m.severity === 'Blocking' && typeof m.text === 'string' && m.text.startsWith(DISCOUNT_LIMIT_TEXT),
  );
  if (message === undefined) {
    return null;
  }
  const promptTarget: DiscountPromptTarget = isDiscountPromptTarget(message.field)
    ? message.field
    : isDiscountPromptTarget(target)
      ? target
      : header?.discT === 1
        ? 'FINALDISC_PERC'
        : 'FINALDISC';
  return { target: promptTarget, text: message.text };
}

/** Drops `LINE:<index>:*` keys and shifts higher line keys down by one. */
function shiftLineKeys<T>(record: Record<string, T>, index: number): Record<string, T> {
  const next: Record<string, T> = {};
  for (const [key, value] of Object.entries(record)) {
    const match = LINE_KEY.exec(key);
    if (match === null) {
      next[key] = value;
      continue;
    }
    const lineNo = Number(match[1]);
    if (lineNo === index) {
      continue;
    }
    next[lineNo > index ? `LINE:${lineNo - 1}:${match[2]}` : key] = value;
  }
  return next;
}

/** Removes every `LINE:` key. */
function withoutLineKeys<T>(record: Record<string, T>): Record<string, T> {
  const next: Record<string, T> = {};
  for (const [key, value] of Object.entries(record)) {
    if (!key.startsWith('LINE:')) {
      next[key] = value;
    }
  }
  return next;
}

/** Error key of a field: the upper-case field on the header, `CLIENT:<clientId>:<FIELD>` on a line. */
function errorKey(field: string, lineClientId?: string): string {
  const upper = field.toUpperCase();
  return lineClientId === undefined ? upper : `CLIENT:${lineClientId}:${upper}`;
}

/** Client id of the draft line at `index`, or null when there is no such line or it has no client id. */
function lineClientIdAt(draft: DraftDto | null, index: number): string | null {
  if (draft === null || !Number.isInteger(index) || index < 0 || index >= draft.lines.length) {
    return null;
  }
  const clientId = draft.lines[index].clientId;
  return clientId == null || clientId === '' ? null : clientId;
}

/** Error key of `field` on the header (`lineIndex` null) or on the draft line at `lineIndex`; null when that line has no client id. */
function entryKey(draft: DraftDto | null, field: string, lineIndex: number | null): string | null {
  if (lineIndex === null) {
    return errorKey(field);
  }
  const clientId = lineClientIdAt(draft, lineIndex);
  return clientId === null ? null : errorKey(field, clientId);
}

/** Where a line-scoped result belongs now: the current index of the line with `lineClientId`, or null when no client id came with it or that line has left the draft. */
function lineIndexOf(draft: DraftDto | null, lineClientId: string | null | undefined): number | null {
  if (draft === null || lineClientId == null || lineClientId === '') {
    return null;
  }
  const index = draft.lines.findIndex((line) => line.clientId === lineClientId);
  return index < 0 ? null : index;
}

/** Field errors without the error of the target a validation source names: `LINE:<i>:<TARGET>` on the line with `lineClientId`, else the source as a header item. */
function withoutSourceFieldError(
  state: InvoiceDraftState,
  source: string,
  lineClientId: string | null,
): InvoiceDraftState['fieldErrors'] {
  const line = LINE_KEY.exec(source);
  let key: string | null = source.toUpperCase();
  if (line !== null) {
    key = lineClientId === null ? null : errorKey(line[2], lineClientId);
  }
  if (key === null || !Object.hasOwn(state.fieldErrors, key)) {
    return state.fieldErrors;
  }
  const next = { ...state.fieldErrors };
  delete next[key];
  return next;
}

/** Keeps header keys and the `CLIENT:<clientId>:*` keys whose client id passes `keep`. */
function keepClientKeys<T>(record: Record<string, T>, keep: (clientId: string) => boolean): Record<string, T> {
  const next: Record<string, T> = {};
  for (const [key, value] of Object.entries(record)) {
    const match = CLIENT_KEY.exec(key);
    if (match === null || keep(match[1])) {
      next[key] = value;
    }
  }
  return next;
}

/** Clamps a line index to `[0, max(0, count - 1)]`. */
function clampIndex(index: number, count: number): number {
  return Math.min(Math.max(index, 0), Math.max(0, count - 1));
}

/** The state without its preview and `PREVIEW` messages and open items, or the same state when it holds none (D-64). */
function withoutPreview(state: InvoiceDraftState): InvoiceDraftState {
  if (state.preview === null && !Object.hasOwn(state.messages, 'PREVIEW') && !Object.hasOwn(state.openItems, 'PREVIEW')) {
    return state;
  }
  return {
    ...state,
    preview: null,
    messages: replaceSource(state.messages, 'PREVIEW', null),
    openItems: replaceSource(state.openItems, 'PREVIEW', null),
  };
}

/** Applies only the draft-wide effects of a failure whose line has left the draft: lost connectivity and missing operator context. */
function applyDetachedError(state: InvoiceDraftState, source: string, error: ApiError): InvoiceDraftState {
  const draftWide = error.status === 503 || error.type === 'oracle-unavailable' || error.type === 'operator-context-missing';
  return draftWide ? applyError(state, source, error) : state;
}

/** Patient number without surrounding blanks; null and undefined are empty. */
function trimmedPatientNo(patientNo: string | null | undefined): string {
  return patientNo == null ? '' : String(patientNo).trim();
}

/** True for a 503 or `oracle-unavailable` failure. */
function isUnavailable(error: ApiError): boolean {
  return error.status === 503 || error.type === 'oracle-unavailable';
}

/** Applies an API failure for `source` to messages, open items, errors or the connectivity flag; `lineClientId` names the line of a `LINE:<i>:<TARGET>` source. */
function applyError(state: InvoiceDraftState, source: string, error: ApiError, lineClientId: string | null = null): InvoiceDraftState {
  const message = error.message ?? '';
  const oracleErrorNumber = error.oracleErrorNumber ?? null;
  const pkg = error.package ?? null;
  const kind = error.kind ?? null;

  if (error.status === 503 || error.type === 'oracle-unavailable') {
    return { ...state, connectivityDown: true };
  }

  if (error.status === 501 || error.type === 'open-item') {
    const openItemId = error.openItemId ?? null;
    const openItemMessages =
      openItemId !== null && openItemId !== '' && message !== ''
        ? { ...state.openItemMessages, [openItemId]: message }
        : state.openItemMessages;
    const errorMessages = error.messages ?? [];
    return {
      ...state,
      openItems: replaceSource(state.openItems, source, uniq([openItemId, ...(error.openItems ?? [])])),
      openItemMessages,
      messages: errorMessages.length > 0 ? replaceSource(state.messages, source, errorMessages) : state.messages,
    };
  }

  if (error.type === 'oracle-business-error') {
    if (kind === 'IdempotencyConflict') {
      return { ...state, idempotencyConflict: { text: message, oracleErrorNumber } };
    }
    const fieldErrors = withoutSourceFieldError(state, source, lineClientId);
    if (error.field != null && error.field !== '') {
      const field = error.field.toUpperCase();
      const entry = { text: error.legacyText ?? message, oracleErrorNumber, kind };
      if (!LINE_ITEMS.has(field)) {
        return { ...state, fieldErrors: { ...fieldErrors, [field]: entry } };
      }
      const line = LINE_KEY.exec(source);
      const clientId = line !== null && line[2] === field ? lineClientId : null;
      if (clientId === null) {
        return { ...state, fieldErrors, formError: { text: entry.text, oracleErrorNumber, package: pkg, kind } };
      }
      return { ...state, fieldErrors: { ...fieldErrors, [errorKey(field, clientId)]: entry } };
    }
    return { ...state, fieldErrors, formError: { text: message, oracleErrorNumber, package: pkg, kind } };
  }

  if (error.type === 'operator-context-missing') {
    const missing = error.missing ?? [];
    return {
      ...state,
      formError:
        missing.length > 0
          ? { text: 'Operator context missing: ' + missing.join(', '), oracleErrorNumber: null, package: null, kind: null }
          : { text: message, oracleErrorNumber, package: pkg, kind: null },
    };
  }

  if (error.type === 'field-validation') {
    const errorOpenItems = error.openItems ?? [];
    const prompt = discountPromptFrom(error.messages, source, state.draft?.header);
    return {
      ...state,
      messages: setSource(state.messages, source, error.messages ?? []),
      openItems: errorOpenItems.length > 0 ? replaceSource(state.openItems, source, errorOpenItems) : state.openItems,
      discountPrompt: prompt ?? state.discountPrompt,
    };
  }

  if (error.type === 'oracle-error' || error.status === 500) {
    return { ...state, formError: { text: message, oracleErrorNumber, package: pkg, kind: null } };
  }

  return { ...state, formError: { text: message, oracleErrorNumber: null, package: null, kind: null } };
}

/** Compile-time exhaustiveness guard for the reducer switch. */
function exhaustive(_action: never, state: InvoiceDraftState): InvoiceDraftState {
  return state;
}


// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

/** Reduces draft-screen actions into the shared invoice draft state. */
export function invoiceDraftReducer(state: InvoiceDraftState, action: InvoiceDraftAction): InvoiceDraftState {
  // A response for a superseded draft or patient changes nothing but the connectivity flag of a 503.
  if ('origin' in action && isSuperseded(state, action.origin)) {
    return 'error' in action && isUnavailable(action.error) && !state.connectivityDown ? { ...state, connectivityDown: true } : state;
  }
  switch (action.type) {
    case 'draftLoaded': {
      const loaded = action.response.draft;
      return {
        ...initialInvoiceDraftState,
        draft: { ...loaded, lines: (loaded.lines ?? []).map(withClientId) },
        messages: replaceSource({}, 'NEW', action.response.messages),
        openItems: replaceSource({}, 'NEW', action.response.openItems),
      };
    }

    case 'headerFieldChanged': {
      if (state.draft === null || state.readOnly) {
        return state;
      }
      return {
        ...state,
        draft: { ...state.draft, header: withField(state.draft.header, action.field, action.value) },
      };
    }

    case 'lineFieldChanged': {
      const draft = state.draft;
      if (draft === null || state.readOnly || action.index < 0 || action.index >= draft.lines.length) {
        return state;
      }
      const lines = draft.lines.map((line, i) => (i === action.index ? withField(line, action.field, action.value) : line));
      const previous = draft.lines[action.index];
      const clientId = previous.clientId;
      if (action.field !== 'serviceId' || Object.is(action.value, previous.serviceId)) {
        return { ...state, draft: { ...draft, lines } };
      }
      // A changed service discards the stored preview and the line's previewed price and description (D-64).
      let lineDisplay = state.lineDisplay;
      const display = clientId == null || clientId === '' ? undefined : lineDisplay[clientId];
      if (clientId != null && display !== undefined && 'SERVICEDESC' in display) {
        const entry = { ...display };
        delete entry.SERVICEDESC;
        lineDisplay = { ...lineDisplay, [clientId]: entry };
      }
      return withoutPreview({
        ...state,
        draft: { ...draft, lines: lines.map((line, i) => (i === action.index ? { ...line, price: null } : line)) },
        lineDisplay,
      });
    }

    case 'lineAdded': {
      const draft = state.draft;
      if (draft === null || state.readOnly) {
        return state;
      }
      const lines = [...draft.lines, emptyLine()];
      return { ...state, draft: { ...draft, lines }, currentLineIndex: lines.length - 1 };
    }

    case 'lineRemoved': {
      const draft = state.draft;
      if (draft === null || state.readOnly || state.saved !== null || action.index < 0 || action.index >= draft.lines.length) {
        return state;
      }
      const removedClientId = draft.lines[action.index].clientId;
      const lines = draft.lines.filter((_, i) => i !== action.index);
      const lineDisplay = { ...state.lineDisplay };
      if (removedClientId != null) {
        delete lineDisplay[removedClientId];
      }
      const kept = (clientId: string) => clientId !== removedClientId;
      const shifted = state.currentLineIndex > action.index ? state.currentLineIndex - 1 : state.currentLineIndex;
      return withoutPreview({
        ...state,
        draft: { ...draft, lines },
        messages: shiftLineKeys(state.messages, action.index),
        openItems: shiftLineKeys(state.openItems, action.index),
        lineDisplay,
        fieldErrors: keepClientKeys(state.fieldErrors, kept),
        entryErrors: keepClientKeys(state.entryErrors, kept),
        currentLineIndex: clampIndex(shifted, lines.length),
      });
    }

    case 'currentLineSelected': {
      // Rows are the loaded saved invoice's lines when present, else the draft's lines.
      const rows = state.saved?.view?.lines ?? state.draft?.lines ?? [];
      if (action.index < 0 || action.index >= rows.length || action.index === state.currentLineIndex) {
        return state;
      }
      return { ...state, currentLineIndex: action.index };
    }

    case 'displaySet': {
      if (action.lineClientId === undefined) {
        return { ...state, display: { ...state.display, ...action.values } };
      }
      const entry = { ...(state.lineDisplay[action.lineClientId] ?? {}) };
      if ('XCAT_NAMEX' in action.values) {
        entry.XCAT_NAMEX = action.values.XCAT_NAMEX;
      }
      if ('SERVICEDESC' in action.values) {
        entry.SERVICEDESC = action.values.SERVICEDESC;
      }
      return { ...state, lineDisplay: { ...state.lineDisplay, [action.lineClientId]: entry } };
    }

    case 'linesImported': {
      const response = action.response;
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        messages: replaceSource(state.messages, action.source, response.messages),
        openItems: replaceSource(state.openItems, action.source, response.openItems),
      };
      if (state.draft !== null && !state.readOnly) {
        const appended: DraftDto = {
          ...state.draft,
          lines: [...state.draft.lines, ...(response.lines ?? []).map(withClientId)],
        };
        const merged = applyAdjusted(appended, state.display, response.adjusted, null);
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      return next;
    }

    case 'linesReplaced': {
      const draft = state.draft;
      if (draft === null || state.readOnly) {
        return state;
      }
      const lines = action.lines.map(withClientId);
      const kept = new Set(lines.map((line) => line.clientId));
      const lineDisplay: InvoiceDraftState['lineDisplay'] = {};
      for (const [clientId, entry] of Object.entries(state.lineDisplay)) {
        if (kept.has(clientId)) {
          lineDisplay[clientId] = entry;
        }
      }
      const present = (clientId: string) => kept.has(clientId);
      return {
        ...state,
        draft: { ...draft, lines },
        messages: withoutLineKeys(state.messages),
        openItems: withoutLineKeys(state.openItems),
        lineDisplay,
        fieldErrors: keepClientKeys(state.fieldErrors, present),
        entryErrors: keepClientKeys(state.entryErrors, present),
        currentLineIndex: clampIndex(state.currentLineIndex, lines.length),
      };
    }

    case 'validationApplied': {
      const lineClientId = action.lineIndex === null ? null : (action.lineClientId ?? null);
      const lineIndex = lineClientId === null ? null : lineIndexOf(state.draft, lineClientId);
      if (action.lineIndex !== null && lineIndex === null) {
        return state.connectivityDown ? { ...state, connectivityDown: false } : state;
      }
      const key = messageKey(action.target, lineIndex);
      const response = action.response;
      const fieldErrors = { ...state.fieldErrors };
      if (lineClientId === null) {
        delete fieldErrors[action.target];
      } else {
        delete fieldErrors[errorKey(action.target, lineClientId)];
      }
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        messages: replaceSource(state.messages, key, response.messages),
        openItems: replaceSource(state.openItems, key, response.openItems),
        fieldErrors,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, response.adjusted, lineIndex);
        next = { ...next, draft: merged.draft, display: merged.display };
        if (response.priceEditable != null && lineClientId !== null && lineIndex !== null) {
          next = {
            ...next,
            priceEditable: {
              ...state.priceEditable,
              [lineClientId]: {
                serviceId: response.priceJudgedServiceId ?? null,
                patientNo: response.priceJudgedPatientNo ?? null,
                compCode: response.priceJudgedCompCode ?? null,
                editable: response.priceEditable,
              },
            },
          };
        }
      }
      return next;
    }

    case 'validationFailed': {
      const error = action.error;
      const lineClientId = action.lineIndex === null ? null : (action.lineClientId ?? null);
      const lineIndex = lineClientId === null ? null : lineIndexOf(state.draft, lineClientId);
      if (action.lineIndex !== null && lineIndex === null) {
        return error.type === 'field-validation' ? state : applyDetachedError(state, messageKey(action.target, action.lineIndex), error);
      }
      const key = messageKey(action.target, lineIndex);
      let next: InvoiceDraftState = {
        ...state,
        messages: setSource(state.messages, key, error.messages ?? []),
        openItems: error.openItems != null ? setSource(state.openItems, key, error.openItems) : state.openItems,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, error.adjusted, lineIndex);
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      const prompt = discountPromptFrom(error.messages, action.target, next.draft?.header);
      if (prompt !== null) {
        next = { ...next, discountPrompt: prompt };
      }
      return error.type === 'field-validation' ? next : applyError(next, key, error, lineClientId);
    }

    case 'discountChoiceMade': {
      if (state.draft === null) {
        return state;
      }
      return { ...state, draft: { ...state.draft, discountLimitChoice: action.choice }, discountPrompt: null };
    }

    case 'coverageApplied': {
      const response = action.response;
      const coveragePatientNo = trimmedPatientNo(action.origin.patientNo);
      if (response === null) {
        return {
          ...state,
          coverage: null,
          coveragePatientNo,
          messages: replaceSource(state.messages, 'COVERAGE', null),
          openItems: replaceSource(state.openItems, 'COVERAGE', null),
        };
      }
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        coverage: response,
        coveragePatientNo,
        messages: replaceSource(state.messages, 'COVERAGE', response.messages),
        openItems: replaceSource(state.openItems, 'COVERAGE', response.openItems),
      };
      if (state.draft !== null && !state.readOnly && response.payType != null) {
        next = {
          ...next,
          draft: { ...state.draft, header: withField(state.draft.header, 'payType', response.payType) },
        };
      }
      return next;
    }

    case 'patientContextCleared': {
      // Drops the previous patient's coverage, pay type and PATIENTNO / COVERAGE messages and open items.
      if (state.draft === null || state.readOnly) {
        return state;
      }
      return {
        ...state,
        draft: { ...state.draft, header: withField(state.draft.header, 'payType', null) },
        coverage: null,
        coveragePatientNo: null,
        messages: replaceSource(replaceSource(state.messages, 'COVERAGE', null), 'PATIENTNO', null),
        openItems: replaceSource(replaceSource(state.openItems, 'COVERAGE', null), 'PATIENTNO', null),
      };
    }

    case 'previewApplied': {
      const response = action.response;
      const pickedCategories = new Set<string>();
      if (state.draft !== null && !state.readOnly) {
        const sentLines = new Map<string, InvoiceLineDraft>();
        for (const sentLine of action.sent ?? []) {
          if (sentLine.clientId != null && sentLine.clientId !== '') {
            sentLines.set(sentLine.clientId, sentLine);
          }
        }
        // A response for a draft without lines, naming a line the draft no longer holds, or sent before a line's
        // service changed, is discarded (D-64).
        const clientIds = new Set(state.draft.lines.map((line) => line.clientId));
        const stale =
          state.draft.lines.length === 0 ||
          (response.lines ?? []).some(
            (previewLine) => previewLine.clientId != null && previewLine.clientId !== '' && !clientIds.has(previewLine.clientId),
          ) ||
          state.draft.lines.some((line) => {
            const sentLine = line.clientId == null ? undefined : sentLines.get(line.clientId);
            return sentLine !== undefined && (sentLine.serviceId ?? null) !== (line.serviceId ?? null);
          });
        if (stale) {
          return state.connectivityDown ? { ...state, connectivityDown: false } : state;
        }
        // Lines whose category was picked after the request was sent keep the picked category (D-64).
        for (const line of state.draft.lines) {
          const sentLine = line.clientId == null ? undefined : sentLines.get(line.clientId);
          if (line.clientId != null && sentLine !== undefined && (sentLine.catId ?? null) !== (line.catId ?? null)) {
            pickedCategories.add(line.clientId);
          }
        }
      }
      const fieldErrors: InvoiceDraftState['fieldErrors'] = {};
      for (const [field, entry] of Object.entries(state.fieldErrors)) {
        if (entry.kind === null || !STALE_KINDS.has(entry.kind)) {
          fieldErrors[field] = entry;
        }
      }
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        preview: response,
        messages: replaceSource(state.messages, 'PREVIEW', response.messages),
        openItems: replaceSource(state.openItems, 'PREVIEW', response.openItems),
        formError: state.formError?.kind != null && STALE_KINDS.has(state.formError.kind) ? null : state.formError,
        fieldErrors,
      };
      const draft = state.draft;
      if (draft !== null && !state.readOnly) {
        const byClientId = new Map<string, EditablePreviewLine>();
        for (const previewLine of response.lines ?? []) {
          if (previewLine.clientId != null && previewLine.clientId !== '') {
            byClientId.set(previewLine.clientId, previewLine);
          }
        }
        const lineDisplay = { ...state.lineDisplay };
        const editableClientIds = new Set(response.priceEditableClientIds ?? []);
        const priceEditable = { ...state.priceEditable };
        const lines = draft.lines.map((line) => {
          const match = line.clientId != null ? byClientId.get(line.clientId) : undefined;
          if (match === undefined || line.clientId == null) {
            return line;
          }
          const entry = { ...(lineDisplay[line.clientId] ?? {}), SERVICEDESC: match.serviceDesc };
          priceEditable[line.clientId] = {
            serviceId: match.serviceId,
            patientNo: response.priceJudgedPatientNo ?? null,
            compCode: response.priceJudgedCompCode ?? null,
            editable: editableClientIds.has(line.clientId),
          };
          if (pickedCategories.has(line.clientId)) {
            lineDisplay[line.clientId] = entry;
            return { ...line, price: match.price };
          }
          // A picked category name is dropped when the package returns another category (D-64).
          if (match.catId !== line.catId) {
            delete entry.XCAT_NAMEX;
          }
          lineDisplay[line.clientId] = entry;
          return { ...line, price: match.price, catId: match.catId };
        });
        next = { ...next, draft: { ...draft, lines }, lineDisplay, priceEditable };
      }
      return next;
    }

    case 'previewCleared':
      return withoutPreview(state);

    case 'saved': {
      const response = action.response;
      return {
        ...state,
        connectivityDown: false,
        preview: null,
        saved: { invNo: response.invNo, view: null, createResponse: response },
        readOnly: true,
        entryErrors: {},
        messages: replaceSource(state.messages, 'CREATE', response.messages),
        openItems: replaceSource(replaceSource(state.openItems, 'CREATE', response.openItems), 'SAVED', ['OI-56']),
      };
    }

    case 'invoiceLoaded': {
      const sameInvoice = state.saved?.invNo === action.invNo;
      const lineCount = (action.response.lines ?? []).length;
      const savedOpenItems = uniq(['OI-56', ...(action.response.openItems ?? [])]);
      const loaded: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        preview: null,
        readOnly: true,
        entryErrors: {},
      };
      if (sameInvoice) {
        // Reloading the invoice on screen keeps its messages and open items.
        return {
          ...loaded,
          saved: { invNo: action.invNo, view: action.response, createResponse: state.saved?.createResponse ?? null },
          openItems: replaceSource(state.openItems, 'SAVED', savedOpenItems),
          currentLineIndex: clampIndex(state.currentLineIndex, lineCount),
        };
      }
      // Another invoice starts on its first line with only its own open items.
      return {
        ...loaded,
        saved: { invNo: action.invNo, view: action.response, createResponse: null },
        messages: {},
        openItems: replaceSource({}, 'SAVED', savedOpenItems),
        fieldErrors: {},
        formError: null,
        currentLineIndex: 0,
      };
    }

    case 'moreDetailsLoaded': {
      const response: unknown = action.response;
      const invNo = state.saved?.invNo;
      if (invNo == null || typeof response !== 'object' || response === null || action.response.invNo !== invNo) {
        return state;
      }
      return { ...state, moreDetails: action.response };
    }

    case 'errorReceived': {
      const line = LINE_KEY.exec(action.source);
      if (line === null) {
        return applyError(state, action.source, action.error);
      }
      const lineClientId = action.lineClientId ?? null;
      const lineIndex = lineClientId === null ? null : lineIndexOf(state.draft, lineClientId);
      if (lineIndex === null) {
        return applyDetachedError(state, action.source, action.error);
      }
      return applyError(state, messageKey(line[2], lineIndex), action.error, lineClientId);
    }

    case 'connectivityLost':
      return state.connectivityDown ? state : { ...state, connectivityDown: true };

    case 'connectivityRestored':
      return state.connectivityDown ? { ...state, connectivityDown: false } : state;

    case 'formErrorCleared':
      return state.formError === null ? state : { ...state, formError: null };

    case 'messageDismissed': {
      const list = state.messages[action.source];
      if (list === undefined || action.index < 0 || action.index >= list.length || list[action.index].severity !== 'Warning') {
        return state;
      }
      const remaining = list.filter((_, i) => i !== action.index);
      return { ...state, messages: replaceSource(state.messages, action.source, remaining) };
    }

    case 'entryRejected': {
      if (state.draft === null || state.readOnly) {
        return state;
      }
      const key = entryKey(state.draft, action.field, action.lineIndex);
      if (key === null || (Object.hasOwn(state.entryErrors, key) && state.entryErrors[key] === action.text)) {
        return state;
      }
      return { ...state, entryErrors: { ...state.entryErrors, [key]: action.text } };
    }

    case 'entryAccepted': {
      const key = entryKey(state.draft, action.field, action.lineIndex);
      if (key === null || !Object.hasOwn(state.entryErrors, key)) {
        return state;
      }
      const entryErrors = { ...state.entryErrors };
      delete entryErrors[key];
      return { ...state, entryErrors };
    }

    default:
      return exhaustive(action, state);
  }
}

/** True when the draft can be submitted: loaded, unsaved, editable, conflict-free, without rejected entries and without Blocking messages. */
export function canSave(state: InvoiceDraftState): boolean {
  if (state.draft === null || state.saved !== null || state.readOnly || state.idempotencyConflict !== null) {
    return false;
  }
  if (Object.keys(state.entryErrors).length > 0) {
    return false;
  }
  return !Object.values(state.messages).some((list) => list.some((m) => m.severity === 'Blocking'));
}

/** Parses operator text into a decimal entry, keeping the text as entered and rejecting what the Api would read differently. */
export function parseDecimalEntry(raw: string): DecimalEntry {
  const text = raw.trim();
  if (text === '') {
    return { kind: 'empty' };
  }
  if (!DECIMAL_TEXT.test(text)) {
    return { kind: 'invalid', message: ENTRY_NOT_A_NUMBER };
  }
  const [mantissa, expText = ''] = text.replace(/^[+-]/, '').split(/[eE]/);
  const [whole, fraction = ''] = mantissa.split('.');
  const digits = (whole + fraction).replace(/^0+/, '');
  const significant = digits.replace(/0+$/, '');
  if (significant === '') {
    return { kind: 'value', text };
  }
  const exponent = expText === '' ? 0 : Number(expText);
  const lastDigitPower = exponent - fraction.length + (digits.length - significant.length);
  const fits =
    significant.length + Math.max(lastDigitPower, 0) <= MAX_DECIMAL_DIGITS && Math.max(-lastDigitPower, 0) <= MAX_DECIMAL_DIGITS;
  return fits ? { kind: 'value', text } : { kind: 'invalid', message: ENTRY_TOO_PRECISE };
}

/** Error key of `field` on the header (`lineClientId` undefined) or on the line with that client id; null for a line without one. */
function scopedErrorKey(field: string, lineClientId: string | null | undefined): string | null {
  if (lineClientId === undefined) {
    return errorKey(field);
  }
  return lineClientId === null || lineClientId === '' ? null : errorKey(field, lineClientId);
}

/** Rejected-entry text of a header field, or of a line field when `lineClientId` is given; null when there is none. */
export function entryErrorFor(state: InvoiceDraftState, field: string, lineClientId?: string | null): string | null {
  const key = scopedErrorKey(field, lineClientId);
  return key !== null && Object.hasOwn(state.entryErrors, key) ? state.entryErrors[key] : null;
}

/** Error shown on a header field, or on a line field when `lineClientId` is given: the rejected entry first, else the mapped Oracle error. */
export function fieldErrorFor(
  state: InvoiceDraftState,
  field: string,
  lineClientId?: string | null,
): InvoiceDraftState['fieldErrors'][string] | null {
  const key = scopedErrorKey(field, lineClientId);
  if (key === null) {
    return null;
  }
  if (Object.hasOwn(state.entryErrors, key)) {
    return { text: state.entryErrors[key], oracleErrorNumber: null, kind: null };
  }
  return Object.hasOwn(state.fieldErrors, key) ? state.fieldErrors[key] : null;
}

/** Origin of a request sent for `draft`; `withPatient` also binds it to the draft's patient number. */
export function requestOrigin(draft: DraftDto, withPatient = false): RequestOrigin {
  return withPatient ? { requestId: draft.requestId, patientNo: draft.header.patientNo } : { requestId: draft.requestId };
}

/** True when a response for `origin` no longer belongs to the state's draft or, for a patient-bound origin, its patient. */
export function isSuperseded(state: InvoiceDraftState, origin: RequestOrigin | undefined): boolean {
  if (origin === undefined) {
    return false;
  }
  const draft = state.draft;
  if (draft === null || draft.requestId !== origin.requestId) {
    return true;
  }
  return origin.patientNo !== undefined && trimmedPatientNo(origin.patientNo) !== trimmedPatientNo(draft.header.patientNo);
}

/** Stored coverage when it was read for the current draft's patient number; null otherwise. */
export function currentCoverage(state: InvoiceDraftState): CoverageResponse | null {
  if (state.draft === null || state.coverage === null) {
    return null;
  }
  return state.coveragePatientNo === trimmedPatientNo(state.draft.header.patientNo) ? state.coverage : null;
}
