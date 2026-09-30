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
}

/** Actions the screens dispatch after their API calls and operator edits. */
export type InvoiceDraftAction =
  | { type: 'draftLoaded'; response: NewDraftResponse }
  | { type: 'headerFieldChanged'; field: keyof InvoiceHeaderDraft; value: InvoiceHeaderDraft[keyof InvoiceHeaderDraft] }
  | { type: 'lineFieldChanged'; index: number; field: keyof InvoiceLineDraft; value: InvoiceLineDraft[keyof InvoiceLineDraft] }
  | { type: 'lineAdded' }
  | { type: 'lineRemoved'; index: number }
  | { type: 'currentLineSelected'; index: number }
  | { type: 'displaySet'; values: Record<string, string | null>; lineClientId?: string }
  | { type: 'linesImported'; source: string; response: ImportResponse }
  | { type: 'linesReplaced'; lines: InvoiceLineDraft[] }
  | { type: 'validationApplied'; target: string; lineIndex: number | null; response: ValidateDraftResponse }
  | { type: 'validationFailed'; target: string; lineIndex: number | null; error: ApiError }
  | { type: 'discountChoiceMade'; choice: DiscountLimitChoice }
  | { type: 'coverageApplied'; response: CoverageResponse }
  | { type: 'previewApplied'; response: PreviewResponse }
  | { type: 'saved'; response: CreateInvoiceResponse }
  | { type: 'invoiceLoaded'; invNo: number; response: InvoiceViewResponse }
  | { type: 'moreDetailsLoaded'; response: MoreDetailsResponse }
  | { type: 'errorReceived'; source: string; error: ApiError }
  | { type: 'connectivityLost' }
  | { type: 'connectivityRestored' }
  | { type: 'formErrorCleared' }
  | { type: 'messageDismissed'; source: string; index: number };

/** State before any draft has been loaded. */
export const initialInvoiceDraftState: InvoiceDraftState = {
  draft: null,
  display: {},
  lineDisplay: {},
  coverage: null,
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
};

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

/** Clamps a line index to `[0, max(0, count - 1)]`. */
function clampIndex(index: number, count: number): number {
  return Math.min(Math.max(index, 0), Math.max(0, count - 1));
}

/** Applies an API failure for `source` to messages, open items, errors or the connectivity flag. */
function applyError(state: InvoiceDraftState, source: string, error: ApiError): InvoiceDraftState {
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
    if (error.field != null && error.field !== '') {
      return {
        ...state,
        fieldErrors: {
          ...state.fieldErrors,
          [error.field.toUpperCase()]: { text: error.legacyText ?? message, oracleErrorNumber, kind },
        },
      };
    }
    return { ...state, formError: { text: message, oracleErrorNumber, package: pkg, kind } };
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
      return { ...state, draft: { ...draft, lines } };
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
      const shifted = state.currentLineIndex > action.index ? state.currentLineIndex - 1 : state.currentLineIndex;
      return {
        ...state,
        draft: { ...draft, lines },
        messages: shiftLineKeys(state.messages, action.index),
        openItems: shiftLineKeys(state.openItems, action.index),
        lineDisplay,
        currentLineIndex: clampIndex(shifted, lines.length),
      };
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
      return {
        ...state,
        draft: { ...draft, lines },
        messages: withoutLineKeys(state.messages),
        openItems: withoutLineKeys(state.openItems),
        lineDisplay,
        currentLineIndex: clampIndex(state.currentLineIndex, lines.length),
      };
    }

    case 'validationApplied': {
      const key = messageKey(action.target, action.lineIndex);
      const response = action.response;
      const fieldErrors = { ...state.fieldErrors };
      delete fieldErrors[action.target];
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        messages: replaceSource(state.messages, key, response.messages),
        openItems: replaceSource(state.openItems, key, response.openItems),
        fieldErrors,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, response.adjusted, action.lineIndex);
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      return next;
    }

    case 'validationFailed': {
      const key = messageKey(action.target, action.lineIndex);
      const error = action.error;
      let next: InvoiceDraftState = {
        ...state,
        messages: setSource(state.messages, key, error.messages ?? []),
        openItems: error.openItems != null ? setSource(state.openItems, key, error.openItems) : state.openItems,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, error.adjusted, action.lineIndex);
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      const prompt = discountPromptFrom(error.messages, action.target, next.draft?.header);
      if (prompt !== null) {
        next = { ...next, discountPrompt: prompt };
      }
      return error.type === 'field-validation' ? next : applyError(next, key, error);
    }

    case 'discountChoiceMade': {
      if (state.draft === null) {
        return state;
      }
      return { ...state, draft: { ...state.draft, discountLimitChoice: action.choice }, discountPrompt: null };
    }

    case 'coverageApplied': {
      const response = action.response;
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        coverage: response,
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

    case 'previewApplied': {
      const response = action.response;
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
        const lines = draft.lines.map((line) => {
          const match = line.clientId != null ? byClientId.get(line.clientId) : undefined;
          if (match === undefined || line.clientId == null) {
            return line;
          }
          lineDisplay[line.clientId] = { ...(lineDisplay[line.clientId] ?? {}), SERVICEDESC: match.serviceDesc };
          return { ...line, price: match.price, catId: match.catId };
        });
        next = { ...next, draft: { ...draft, lines }, lineDisplay };
      }
      return next;
    }

    case 'saved': {
      const response = action.response;
      return {
        ...state,
        connectivityDown: false,
        saved: { invNo: response.invNo ?? 0, view: null, createResponse: response },
        readOnly: true,
        messages: replaceSource(state.messages, 'CREATE', response.messages),
        openItems: replaceSource(replaceSource(state.openItems, 'CREATE', response.openItems), 'SAVED', ['OI-56']),
      };
    }

    case 'invoiceLoaded': {
      const createResponse = state.saved?.invNo === action.invNo ? state.saved.createResponse : null;
      return {
        ...state,
        connectivityDown: false,
        saved: { invNo: action.invNo, view: action.response, createResponse },
        readOnly: true,
        openItems: replaceSource(state.openItems, 'SAVED', uniq(['OI-56', ...(action.response.openItems ?? [])])),
      };
    }

    case 'moreDetailsLoaded':
      return { ...state, connectivityDown: false, moreDetails: action.response };

    case 'errorReceived':
      return applyError(state, action.source, action.error);

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

    default:
      return exhaustive(action, state);
  }
}

/** True when the draft can be submitted: loaded, unsaved, editable, conflict-free and without Blocking messages. */
export function canSave(state: InvoiceDraftState): boolean {
  if (state.draft === null || state.saved !== null || state.readOnly || state.idempotencyConflict !== null) {
    return false;
  }
  return !Object.values(state.messages).some((list) => list.some((m) => m.severity === 'Blocking'));
}

