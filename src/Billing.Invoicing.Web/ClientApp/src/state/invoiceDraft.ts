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
  PatientCoverageSnapshot,
  PreviewResponse,
  RequestOmittedHeaderMember,
  RequestOmittedLineMember,
  ValidateDraftResponse,
  ValidateTarget,
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
  /** Idempotency conflict of a create, with `repeat` as for `formError`. */
  idempotencyConflict: { text: string; oracleErrorNumber: number | null; repeat: number } | null;
  discountPrompt: { target: 'FINALDISC_PERC' | 'FINALDISC'; text: string } | null;
  /** Form-level error and the source whose answer raised it: the request source, or `CLIENT:<clientId>:<TARGET>` for a line; `repeat` counts the operator requests in a row that raised it, 0 for an automatic one. */
  formError: { text: string; oracleErrorNumber: number | null; package: string | null; kind: string | null; source: string; repeat: number } | null;
  /** Field errors by error key, each with the source whose answer raised it, named as for `formError`. */
  fieldErrors: Record<string, { text: string; oracleErrorNumber: number | null; kind: string | null; source: string }>;
  entryErrors: Record<string, string>;
  /** Server judgement of PRICE editability by line client id, with the service, patient and company it was judged on. */
  priceEditable: Record<string, PriceJudgement>;
  /** Patient, sub-company and class the claim's first invoice preloaded into the draft header; null when none was preloaded. */
  claimPreload: ClaimPreload | null;
  /** Number of operator field edits so far, never reset (D-145). */
  editCount: number;
  /** Edit count of each field's last operator edit, keyed by header member, or `<clientId>:<member>` on a line; a patient change records `claimNo` at its edit count (D-186). */
  editedAt: Record<string, number>;
  /** Number of successful API outcomes applied; a lookup list that failed to load is re-requested when it changes. */
  successCount: number;
  /** Number of patient changes so far, never reset (D-186). */
  patientContextCount: number;
}

/** Trimmed patient number, sub-company and class of a claim preload. */
export interface ClaimPreload {
  patientNo: string;
  subCompCode: string | null;
  classCode: number | null;
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
  /** Discount-limit choice the request carried; absent when not recorded. */
  discountLimitChoice?: DiscountLimitChoice | null;
  /** Patient change count when the request was sent; absent when the request is not bound to it (D-186). */
  patientContextCount?: number;
}

/** Header and line a line validation was sent with. */
export interface JudgedLine {
  header: InvoiceHeaderDraft;
  line: InvoiceLineDraft;
}

/** Position of a stored message: its source key and its index in that source's list. */
export interface MessageRef {
  source: string;
  index: number;
}

/** A message shown once, with every stored copy of it. */
export interface PlacedMessage {
  message: MessageDto;
  refs: MessageRef[];
}

/** Actions the screens dispatch after their API calls and operator edits; `origin` names the request a response belongs to; `judged` the inputs a line verdict was sent with; `editsSince` the edit count when the request was queued, so fields edited after it keep the operator's value; `automatic` marks a failure of a request no operator action started. */
export type InvoiceDraftAction =
  | { type: 'draftLoaded'; response: NewDraftResponse }
  | { type: 'headerFieldChanged'; field: keyof InvoiceHeaderDraft; value: InvoiceHeaderDraft[keyof InvoiceHeaderDraft] }
  | { type: 'lineFieldChanged'; index: number; field: keyof InvoiceLineDraft; value: InvoiceLineDraft[keyof InvoiceLineDraft] }
  | { type: 'lineAdded' }
  | { type: 'lineRemoved'; index: number }
  | { type: 'currentLineSelected'; index: number }
  | { type: 'displaySet'; values: Record<string, string | null>; lineClientId?: string }
  | { type: 'linesImported'; source: string; response: ImportResponse; origin?: RequestOrigin; editsSince?: number }
  | { type: 'linesReplaced'; lines: InvoiceLineDraft[]; origin?: RequestOrigin }
  | { type: 'validationApplied'; target: string; lineIndex: number | null; lineClientId?: string | null; response: ValidateDraftResponse; origin?: RequestOrigin; judged?: JudgedLine; editsSince?: number }
  | { type: 'validationFailed'; target: string; lineIndex: number | null; lineClientId?: string | null; error: ApiError; origin?: RequestOrigin; judged?: JudgedLine; editsSince?: number }
  | { type: 'discountChoiceMade'; choice: DiscountLimitChoice }
  | { type: 'coverageApplied'; response: CoverageResponse | null; origin: RequestOrigin; editsSince?: number }
  | { type: 'patientContextCleared'; origin: RequestOrigin }
  | { type: 'previewApplied'; response: PreviewResponse; sent?: readonly InvoiceLineDraft[]; origin: RequestOrigin }
  | { type: 'previewCleared' }
  | { type: 'saved'; response: CreateInvoiceResponse; origin: RequestOrigin }
  | { type: 'invoiceLoaded'; invNo: number; response: InvoiceViewResponse; origin?: RequestOrigin }
  | { type: 'moreDetailsLoaded'; response: MoreDetailsResponse }
  | { type: 'errorReceived'; source: string; lineClientId?: string | null; error: ApiError; origin?: RequestOrigin; judged?: JudgedLine; automatic?: boolean }
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
  claimPreload: null,
  editCount: 0,
  editedAt: {},
  successCount: 0,
  patientContextCount: 0,
};

/** Operator decimal entry: empty, the trimmed text as entered, or the reason it is rejected. */
export type DecimalEntry = { kind: 'empty' } | { kind: 'value'; text: string } | { kind: 'invalid'; message: string };

/** Field error of an entry that is not a decimal number. */
export const ENTRY_NOT_A_NUMBER = 'Enter a valid number.';

/** Field error of a decimal entry the Api cannot hold exactly. */
export const ENTRY_TOO_PRECISE = 'Enter a number of at most 28 significant digits and 28 decimal places.';

type AdjustedTarget =
  | { scope: 'header'; field: keyof InvoiceHeaderDraft; follows?: keyof InvoiceHeaderDraft }
  | { scope: 'line'; field: keyof InvoiceLineDraft }
  | { scope: 'display'; key: string; follows?: keyof InvoiceHeaderDraft };

/** Destination of each server-adjusted value: a header field, a line field or a display key, and the header field (`follows`) whose later edit also skips it. */
export const ADJUSTED_KEY_MAP: Readonly<Record<AdjustedKey, AdjustedTarget>> = {
  AMOUNT_1: { scope: 'header', field: 'amount1' },
  AMOUNT_2: { scope: 'header', field: 'amount2' },
  CLAIM_NO: { scope: 'header', field: 'claimNo', follows: 'docId' },
  ADD_TO_LIST: { scope: 'header', field: 'addToList' },
  SUB_PAYTYPE: { scope: 'header', field: 'subPayType' },
  PAYTYPE: { scope: 'header', field: 'payType' },
  FINALDISC_PERC: { scope: 'header', field: 'finalDiscPerc' },
  FINALDISC: { scope: 'header', field: 'finalDisc' },
  DISC_T: { scope: 'header', field: 'discT' },
  DOCIDX: { scope: 'header', field: 'docId' },
  CLINICID: { scope: 'header', field: 'clinicId' },
  CLINICNAME: { scope: 'display', key: 'CLINICNAME', follows: 'clinicId' },
  DOC_NAME: { scope: 'display', key: 'DOC_NAME', follows: 'docId' },
  LDISCT: { scope: 'line', field: 'discountType' },
  REUND: { scope: 'display', key: 'REUND' },
};

/** Adjusted keys whose header field is set to null, the package's auto mode, instead of the adjusted value (D-148). */
const AUTO_DEFAULTED_KEYS: ReadonlySet<AdjustedKey> = new Set<AdjustedKey>(['AMOUNT_1']);

/** Validation targets whose message source holds a Save verdict. */
const VALIDATE_TARGETS: Readonly<Record<ValidateTarget, true>> = {
  PATIENTNO: true,
  COMP_CODE: true,
  DOCIDX: true,
  CLINICID: true,
  DEPT_WISE: true,
  CALL: true,
  FINALDISC_PERC: true,
  FINALDISC: true,
  AMOUNT_1: true,
  AMOUNT_2: true,
  SUB_PAYTYPE: true,
  SERVICEID: true,
  QTY: true,
  LDISCT: true,
  APPROV_REF_NO: true,
  PRICE: true,
  DISC: true,
  MY_DISC: true,
  LINE: true,
  RECORD: true,
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

/** Field of the messages that judge the draft's set of lines. */
const LINE_FIELD = 'LINE';

/** T_INV item names whose mapped Oracle error InvoiceHeaderForm or PaymentPanel renders beside the field (D-173). */
const RENDERED_HEADER_ITEMS: ReadonlySet<string> = new Set([
  'PATIENTNO',
  'COMP_CODE',
  'DOCIDX',
  'CLINICID',
  'DEPT_WISE',
  'CALL',
  'OFERID',
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

/** Validation targets whose verdict covers the whole line. */
const LINE_VERDICT_TARGETS: ReadonlySet<string> = new Set(['LINE', 'SERVICEID', 'QTY', 'LDISCT', 'APPROV_REF_NO', 'PRICE', 'DISC', 'MY_DISC']);

/** Header validation targets and the header member holding the value each judges. */
const HEADER_TARGET_MEMBERS: Readonly<Record<string, keyof InvoiceHeaderDraft>> = {
  PATIENTNO: 'patientNo',
  COMP_CODE: 'compCode',
  DOCIDX: 'docId',
  CLINICID: 'clinicId',
  DEPT_WISE: 'deptWise',
  CALL: 'call',
  FINALDISC_PERC: 'finalDiscPerc',
  FINALDISC: 'finalDisc',
  AMOUNT_1: 'amount1',
  AMOUNT_2: 'amount2',
  SUB_PAYTYPE: 'subPayType',
};

/** Rule id of the form-level 'Invoice without Details' message. */
const DETAILS_REQUIRED_RULE = 'DR-02';

/** Rule id of the header record check, which reports only its first failing field. */
const RECORD_RULE = 'DR-01';

/** Sources whose answers carry a complete header record verdict. */
const RECORD_VERDICT_TARGETS: ReadonlySet<string> = new Set(['RECORD', 'CREATE']);

/** Header members a whole-line verdict is judged under: the patient and the payer. */
const LINE_VERDICT_HEADER_MEMBERS: readonly (keyof InvoiceHeaderDraft)[] = ['patientNo', 'payType', 'compCode', 'subCompCode', 'classCode'];

/** Header members a request body leaves out. */
const OMITTED_HEADER_MEMBERS: Readonly<Record<RequestOmittedHeaderMember, true>> = {
  preAuthorization: true,
  oferId: true,
  docId1: true,
  seqNo: true,
};

/** Line members a request body leaves out. */
const OMITTED_LINE_MEMBERS: Readonly<Record<RequestOmittedLineMember, true>> = {
  catId: true,
  fixPay: true,
  payRate: true,
  regularLensesType: true,
  lensSpecifications: true,
  contactLensesType: true,
  flIndicator: true,
  numberOfPairs: true,
  insEmp: true,
};

/** Entry CLAIM_NO values that preload no claim: blank, '0', '1' and '2'. */
const NON_PRELOAD_CLAIM_NOS: ReadonlySet<string> = new Set(['', '0', '1', '2']);

const CREDIT_PAY_TYPE = 2;

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

/** A blank draft line with quantity 1, rate discount type and a fresh client id. */
function emptyLine(): InvoiceLineDraft {
  return {
    serviceId: null,
    qty: 1,
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

/** The text and null display values of a new draft response; any other value, or a display that is not an object, is dropped. */
function loadedDisplay(display: NewDraftResponse['display']): Record<string, string | null> {
  const values: Record<string, string | null> = {};
  if (display == null || typeof display !== 'object' || Array.isArray(display)) {
    return values;
  }
  for (const [key, value] of Object.entries(display)) {
    if (typeof value === 'string' || value === null) {
      values[key] = value;
    }
  }
  return values;
}

/** Edit key of a line member: `<clientId>:<member>`. */
function lineEditKey(clientId: string | null | undefined, field: string): string {
  return `${clientId ?? ''}:${field}`;
}

/** Edit-key predicate of the fields the operator edited after edit count `editsSince`; none when it is absent (D-145). */
function editedAfter(state: InvoiceDraftState, editsSince: number | undefined): (key: string) => boolean {
  return (key) => editsSince !== undefined && Object.hasOwn(state.editedAt, key) && state.editedAt[key] > editsSince;
}

/** The state with one more operator edit, recorded against edit key `key` when given. */
function withEdit(state: InvoiceDraftState, key: string | null): InvoiceDraftState {
  const editCount = state.editCount + 1;
  return { ...state, editCount, editedAt: key === null ? state.editedAt : { ...state.editedAt, [key]: editCount } };
}

/** Merges server-adjusted values into the draft header, the given line and the display values, skipping fields `edited` names and values whose `follows` field it names; an adjusted `AMOUNT_1` sets Amount 1 to null (auto). */
function applyAdjusted(
  draft: DraftDto,
  display: Record<string, string | null>,
  adjusted: AdjustedValues | null | undefined,
  lineIndex: number | null,
  edited: (key: string) => boolean = () => false,
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
        if (!edited(target.field) && (target.follows === undefined || !edited(target.follows))) {
          const entered = AUTO_DEFAULTED_KEYS.has(key) ? null : value;
          header = withField(header, target.field, entered as InvoiceHeaderDraft[keyof InvoiceHeaderDraft]);
        }
        break;
      case 'line':
        if (lineIndex !== null && lineIndex >= 0 && lineIndex < lines.length && !edited(lineEditKey(lines[lineIndex].clientId, target.field))) {
          const updated = withField(lines[lineIndex], target.field, value as InvoiceLineDraft[keyof InvoiceLineDraft]);
          lines = lines.map((line, i) => (i === lineIndex ? updated : line));
        }
        break;
      case 'display':
        if (target.follows === undefined || !edited(target.follows)) {
          nextDisplay = { ...nextDisplay, [target.key]: value == null ? null : String(value) };
        }
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

/** Messages without those `drop` matches; untouched lists stay as they are and emptied sources are removed. */
function withoutMessages(messages: Lists<MessageDto>, drop: (source: string, message: MessageDto) => boolean): Lists<MessageDto> {
  let next = messages;
  for (const [source, list] of Object.entries(messages)) {
    const kept = list.filter((message) => !drop(source, message));
    if (kept.length !== list.length) {
      next = replaceSource(next, source, kept);
    }
  }
  return next;
}

/** For a header target whose field holds a non-blank value on the draft, the messages without that field's messages, other than record-check ones, in every other non-line source; otherwise the messages. */
function withoutJudgedFieldMessages(state: InvoiceDraftState, target: string, messages: Lists<MessageDto>): Lists<MessageDto> {
  const member = Object.hasOwn(HEADER_TARGET_MEMBERS, target) ? HEADER_TARGET_MEMBERS[target] : undefined;
  const value: unknown = member === undefined || state.draft === null ? null : state.draft.header[member];
  if (value == null || (typeof value === 'string' && value.trim() === '')) {
    return messages;
  }
  return withoutMessages(
    messages,
    (source, message) =>
      source !== target && !source.startsWith('LINE:') && messageField(message) === target && message.rule !== RECORD_RULE,
  );
}

/** Messages without the record-check messages of every non-line source other than `target`. */
function withoutRecordMessages(messages: Lists<MessageDto>, target: string): Lists<MessageDto> {
  return withoutMessages(messages, (source, message) => source !== target && !source.startsWith('LINE:') && message.rule === RECORD_RULE);
}

/** Messages without the 'Invoice without Details' message in every non-line source. */
function withoutDetailsRequired(messages: Lists<MessageDto>): Lists<MessageDto> {
  return withoutMessages(messages, (source, message) => !source.startsWith('LINE:') && message.rule === DETAILS_REQUIRED_RULE);
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

/** Header fields whose edit drops the discount-limit choice. */
const DISCOUNT_ENTRY_FIELDS: ReadonlySet<keyof InvoiceHeaderDraft> = new Set<keyof InvoiceHeaderDraft>(['finalDiscPerc', 'finalDisc', 'discT']);

/** True for the two final-discount prompt targets. */
function isDiscountPromptTarget(value: string | null | undefined): value is DiscountPromptTarget {
  return value === 'FINALDISC_PERC' || value === 'FINALDISC';
}

/** True when the editable draft carries a discount-limit choice. */
function hasDiscountChoice(state: InvoiceDraftState): boolean {
  return state.draft !== null && !state.readOnly && state.draft.discountLimitChoice != null;
}

/** True when the request behind `origin` carried a discount-limit choice; an origin without a recorded choice counts as carrying the draft's. */
function carriedDiscountChoice(origin: RequestOrigin | undefined): boolean {
  return origin?.discountLimitChoice === undefined || origin.discountLimitChoice !== null;
}

/** True when a response for `origin` may raise the DISC_ALERT prompt: its request carried no choice and no answer is pending. */
function mayRaiseDiscountPrompt(state: InvoiceDraftState, origin: RequestOrigin | undefined): boolean {
  return !hasDiscountChoice(state) && (origin?.discountLimitChoice ?? null) === null;
}

/** The other final-discount target of each final-discount target. */
const OTHER_DISCOUNT_TARGET: Readonly<Record<DiscountPromptTarget, DiscountPromptTarget>> = {
  FINALDISC_PERC: 'FINALDISC',
  FINALDISC: 'FINALDISC_PERC',
};

/** The state with the draft's discount-limit choice and the other final-discount target's messages dropped when `source` is a final-discount target whose request carried the choice; otherwise the state. */
function withoutDiscountChoice(state: InvoiceDraftState, source: string, origin: RequestOrigin | undefined): InvoiceDraftState {
  if (state.draft === null || !hasDiscountChoice(state) || !isDiscountPromptTarget(source) || !carriedDiscountChoice(origin)) {
    return state;
  }
  return {
    ...state,
    draft: { ...state.draft, discountLimitChoice: null },
    messages: replaceSource(state.messages, OTHER_DISCOUNT_TARGET[source], null),
  };
}

/** True for an adjusted value that is the number zero or a decimal text equal to zero. */
function isZeroValue(value: unknown): boolean {
  if (typeof value === 'number') {
    return value === 0;
  }
  return typeof value === 'string' && DECIMAL_TEXT.test(value.trim()) && Number(value) === 0;
}

/** Builds the DISC_ALERT prompt from a Blocking maximum-discount message, or returns null, also when `adjusted` zeroes FINALDISC_PERC and FINALDISC. */
function discountPromptFrom(
  messages: readonly MessageDto[] | null | undefined,
  target: string,
  header: InvoiceHeaderDraft | null | undefined,
  adjusted: AdjustedValues | null | undefined,
): InvoiceDraftState['discountPrompt'] {
  if (adjusted != null && isZeroValue(adjusted.FINALDISC_PERC) && isZeroValue(adjusted.FINALDISC)) {
    return null;
  }
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

/** For a `LINE:<i>:<TARGET>` source of a whole-line target, the state without line i's messages and open items on every such target and, given `lineClientId`, its field errors on them and the non-stale-kind errors those targets raised; otherwise the state. */
function withoutLineVerdicts(state: InvoiceDraftState, source: string, lineClientId: string | null = null): InvoiceDraftState {
  const line = LINE_KEY.exec(source);
  if (line === null || !LINE_VERDICT_TARGETS.has(line[2])) {
    return state;
  }
  const lineIndex = Number(line[1]);
  const messages = { ...state.messages };
  const openItems = { ...state.openItems };
  const fieldErrors = { ...state.fieldErrors };
  for (const target of LINE_VERDICT_TARGETS) {
    delete messages[messageKey(target, lineIndex)];
    delete openItems[messageKey(target, lineIndex)];
    if (lineClientId !== null) {
      delete fieldErrors[errorKey(target, lineClientId)];
    }
  }
  const next = { ...state, messages, openItems, fieldErrors };
  if (lineClientId === null) {
    return next;
  }
  // The form error and field errors any whole-line target of this line raised are dropped with its verdicts (D-174).
  const verdictSources = new Set([...LINE_VERDICT_TARGETS].map((target) => errorKey(target, lineClientId)));
  return withoutErrors(next, (error) => !isStaleKind(error.kind) && verdictSources.has(error.source));
}

/** True when `a` and `b` hold the same value, a missing one as null, in every member outside `omitted`. */
function sameMembers<T extends object>(a: T, b: T, omitted: Readonly<Record<string, true>>): boolean {
  const left = a as Record<string, unknown>;
  const right = b as Record<string, unknown>;
  for (const member of new Set([...Object.keys(left), ...Object.keys(right)])) {
    if (!Object.hasOwn(omitted, member) && !Object.is(left[member] ?? null, right[member] ?? null)) {
      return false;
    }
  }
  return true;
}

/** True when the state's line `lineIndex` and the header members its verdict is judged under still hold the inputs `judged` was sent with. */
function judgesCurrentLine(state: InvoiceDraftState, lineIndex: number | null, judged: JudgedLine | undefined): boolean {
  const draft = state.draft;
  const line = draft === null || lineIndex === null ? undefined : draft.lines[lineIndex];
  if (draft === null || line === undefined || judged === undefined) {
    return false;
  }
  return (
    sameMembers(judged.line, line, OMITTED_LINE_MEMBERS) &&
    LINE_VERDICT_HEADER_MEMBERS.every((member) => Object.is(judged.header[member] ?? null, draft.header[member] ?? null))
  );
}

/** True when a failed line validation is still the server's verdict on the line: a `field-validation` 422, an open item, or a package refusal other than an idempotency conflict. */
function isLineVerdictFailure(error: ApiError): boolean {
  return (
    error.type === 'field-validation' ||
    error.status === 501 ||
    error.type === 'open-item' ||
    (error.type === 'oracle-business-error' && error.kind !== 'IdempotencyConflict')
  );
}

/** True when two drafts send the same request: the same draft, header, lines in order and discount-limit choice. */
export function sameDraftInputs(a: DraftDto, b: DraftDto): boolean {
  return (
    a.requestId === b.requestId &&
    a.discountLimitChoice === b.discountLimitChoice &&
    sameMembers(a.header, b.header, OMITTED_HEADER_MEMBERS) &&
    a.lines.length === b.lines.length &&
    a.lines.every((line, index) => sameMembers(line, b.lines[index], OMITTED_LINE_MEMBERS))
  );
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

/** Applies only the draft-wide effects of a failure whose line has left the draft: lost connectivity and missing operator context; `automatic` as for `applyError`. */
function applyDetachedError(state: InvoiceDraftState, source: string, error: ApiError, automatic = false): InvoiceDraftState {
  const draftWide = error.status === 503 || error.type === 'oracle-unavailable' || error.type === 'operator-context-missing';
  return draftWide ? applyError(state, source, error, null, automatic) : state;
}

/** Patient number without surrounding blanks; null and undefined are empty. */
function trimmedPatientNo(patientNo: string | null | undefined): string {
  return patientNo == null ? '' : String(patientNo).trim();
}

/** Claim preload of a new draft: its header's trimmed patient, sub-company and class when the entry CLAIM_NO names a claim and the header has a patient; else null. */
function claimPreloadOf(draft: DraftDto): ClaimPreload | null {
  const claimNo = String(draft.parameters?.claimNo ?? '').trim();
  const patientNo = trimmedPatientNo(draft.header.patientNo);
  if (NON_PRELOAD_CLAIM_NOS.has(claimNo) || patientNo === '') {
    return null;
  }
  return { patientNo, subCompCode: draft.header.subCompCode ?? null, classCode: draft.header.classCode ?? null };
}

/** Sub-company and class with their names that coverage of `patientNo` puts on the header: none unless `payType` is credit, the claim preload's when it is for that patient and has a sub-company, else the coverage's. */
function coverageSubCompanyAndClass(
  coverage: PatientCoverageSnapshot,
  payType: number | null | undefined,
  patientNo: string,
  preload: ClaimPreload | null,
): { subCompCode: string | null; subCompName: string | null; classCode: number | null; className: string | null } {
  if (payType !== CREDIT_PAY_TYPE) {
    return { subCompCode: null, subCompName: null, classCode: null, className: null };
  }
  if (preload !== null && preload.patientNo === patientNo && preload.subCompCode != null && preload.subCompCode.trim() !== '') {
    const sameSubCompany = preload.subCompCode === coverage.subCompCode;
    return {
      subCompCode: preload.subCompCode,
      subCompName: sameSubCompany ? (coverage.subCompName ?? null) : null,
      classCode: preload.classCode,
      className: sameSubCompany && preload.classCode === coverage.classCode ? (coverage.className ?? null) : null,
    };
  }
  return {
    subCompCode: coverage.subCompCode ?? null,
    subCompName: coverage.subCompName ?? null,
    classCode: coverage.classCode ?? null,
    className: coverage.className ?? null,
  };
}

/** True for a 503 or `oracle-unavailable` failure. */
function isUnavailable(error: ApiError): boolean {
  return error.status === 503 || error.type === 'oracle-unavailable';
}

/** True for a source whose messages hold a Save verdict: `PREVIEW`, a `LINE:<i>:<TARGET>` key or a validation target. */
function isVerdictSource(source: string): boolean {
  return source === 'PREVIEW' || LINE_KEY.test(source) || Object.hasOwn(VALIDATE_TARGETS, source);
}

/** Stable source of an error: `CLIENT:<clientId>:<TARGET>` for a `LINE:<i>:<TARGET>` key of a line with a client id, else the key. */
function errorSource(source: string, lineClientId: string | null): string {
  const line = LINE_KEY.exec(source);
  return line !== null && lineClientId !== null && lineClientId !== '' ? errorKey(line[2], lineClientId) : source;
}

/** True for the kinds of a stale-data refusal: `RequestLinesStale` and `DefinitionStale`. */
function isStaleKind(kind: string | null): boolean {
  return kind !== null && STALE_KINDS.has(kind);
}

/** True for a source whose answer judges the draft: `CREATE`, `PREVIEW`, `COVERAGE`, `IMPORT`, a validation target, or a `LINE:` or `CLIENT:` key. */
function judgesDraft(source: string): boolean {
  return (
    source === 'CREATE' ||
    source === 'COVERAGE' ||
    source === 'IMPORT' ||
    source.startsWith('LINE:') ||
    source.startsWith('CLIENT:') ||
    isVerdictSource(source)
  );
}

/** The state without the form error and the field errors whose source and kind `drop` matches; the same state when none match. */
function withoutErrors(state: InvoiceDraftState, drop: (error: { source: string; kind: string | null }) => boolean): InvoiceDraftState {
  const fieldErrors: InvoiceDraftState['fieldErrors'] = {};
  let dropped = false;
  for (const [key, entry] of Object.entries(state.fieldErrors)) {
    if (drop(entry)) {
      dropped = true;
    } else {
      fieldErrors[key] = entry;
    }
  }
  const formError = state.formError !== null && drop(state.formError) ? null : state.formError;
  if (!dropped && formError === state.formError) {
    return state;
  }
  return { ...state, formError, fieldErrors: dropped ? fieldErrors : state.fieldErrors };
}

/** The state without the errors a new answer from `source` replaces: that source's own except stale-kind ones and, for `CREATE`, every stale-kind error. */
function withoutAnsweredErrors(state: InvoiceDraftState, source: string): InvoiceDraftState {
  return withoutErrors(state, (error) => (isStaleKind(error.kind) ? source === 'CREATE' : error.source === source));
}

/** Times in a row operator requests raised an outcome: 0 when `automatic`, else one more than an operator-raised `previous` with the same source, text and Oracle number, else 1. */
function repeatOf(
  previous: { text: string; oracleErrorNumber: number | null; source?: string; repeat: number } | null,
  next: { text: string; oracleErrorNumber: number | null; source?: string },
  automatic: boolean,
): number {
  if (automatic) {
    return 0;
  }
  return previous !== null &&
    previous.repeat >= 1 &&
    previous.source === next.source &&
    previous.text === next.text &&
    previous.oracleErrorNumber === next.oracleErrorNumber
    ? previous.repeat + 1
    : 1;
}

/** Applies an API failure for `source` to messages, open items, errors or the connectivity flag, a non-outage failure first dropping the errors it supersedes; `lineClientId` names the line of a `LINE:<i>:<TARGET>` source; `automatic` marks a failure of a request no operator action started. */
function applyError(current: InvoiceDraftState, source: string, error: ApiError, lineClientId: string | null = null, automatic = false): InvoiceDraftState {
  const message = error.message ?? '';
  const oracleErrorNumber = error.oracleErrorNumber ?? null;
  const pkg = error.package ?? null;
  const kind = error.kind ?? null;

  if (error.status === 503 || error.type === 'oracle-unavailable') {
    return { ...current, connectivityDown: true };
  }

  const sourceId = errorSource(source, lineClientId);
  const state = withoutAnsweredErrors(current, sourceId);
  /** `entry` as the arriving form error, with its repeat count over the form error shown before this answer. */
  const arrived = (entry: Omit<NonNullable<InvoiceDraftState['formError']>, 'repeat'>): NonNullable<InvoiceDraftState['formError']> => ({
    ...entry,
    repeat: repeatOf(current.formError, entry, automatic),
  });

  // A success body the client rejected leaves a verdict source with one Blocking form-level message until its next result (D-119).
  if (error.type === 'invalid-response' && isVerdictSource(source)) {
    const rejected: MessageDto = { field: null, text: message, severity: 'Blocking', rule: null };
    return { ...state, messages: replaceSource(state.messages, source, [rejected]) };
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
      const conflict = { text: message, oracleErrorNumber };
      return { ...state, idempotencyConflict: { ...conflict, repeat: repeatOf(current.idempotencyConflict, conflict, automatic) } };
    }
    const fieldErrors = withoutSourceFieldError(state, source, lineClientId);
    if (error.field != null && error.field !== '') {
      const field = error.field.toUpperCase();
      const entry = { text: error.legacyText ?? message, oracleErrorNumber, kind, source: sourceId };
      if (!LINE_ITEMS.has(field)) {
        // A non-line item no component renders takes the form-level slot (D-173).
        return RENDERED_HEADER_ITEMS.has(field)
          ? { ...state, fieldErrors: { ...fieldErrors, [field]: entry } }
          : { ...state, fieldErrors, formError: arrived({ text: entry.text, oracleErrorNumber, package: pkg, kind, source: sourceId }) };
      }
      const line = LINE_KEY.exec(source);
      const clientId = line !== null && line[2] === field ? lineClientId : null;
      if (clientId === null) {
        return { ...state, fieldErrors, formError: arrived({ text: entry.text, oracleErrorNumber, package: pkg, kind, source: sourceId }) };
      }
      return { ...state, fieldErrors: { ...fieldErrors, [errorKey(field, clientId)]: entry } };
    }
    return { ...state, fieldErrors, formError: arrived({ text: message, oracleErrorNumber, package: pkg, kind, source: sourceId }) };
  }

  if (error.type === 'operator-context-missing') {
    const missing = error.missing ?? [];
    return {
      ...state,
      formError: arrived(
        missing.length > 0
          ? { text: 'Operator context missing: ' + missing.join(', '), oracleErrorNumber: null, package: null, kind: null, source: sourceId }
          : { text: message, oracleErrorNumber, package: pkg, kind: null, source: sourceId },
      ),
    };
  }

  if (error.type === 'field-validation') {
    const errorOpenItems = error.openItems ?? [];
    // No prompt is raised while a discount-limit answer is pending.
    const prompt = mayRaiseDiscountPrompt(state, undefined)
      ? discountPromptFrom(error.messages, source, state.draft?.header, error.adjusted)
      : null;
    return withoutDiscountChoice(
      {
        ...state,
        messages: setSource(state.messages, source, error.messages ?? []),
        openItems: errorOpenItems.length > 0 ? replaceSource(state.openItems, source, errorOpenItems) : state.openItems,
        discountPrompt: prompt ?? state.discountPrompt,
      },
      source,
      undefined,
    );
  }

  if (error.type === 'oracle-error' || error.status === 500) {
    return { ...state, formError: arrived({ text: message, oracleErrorNumber, package: pkg, kind: null, source: sourceId }) };
  }

  return { ...state, formError: arrived({ text: message, oracleErrorNumber: null, package: null, kind: null, source: sourceId }) };
}

/** Compile-time exhaustiveness guard for the reducer switch. */
function exhaustive(_action: never, state: InvoiceDraftState): InvoiceDraftState {
  return state;
}


// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

/** True when the action applies a successful API outcome for the state's current draft. */
function appliesSuccess(state: InvoiceDraftState, action: InvoiceDraftAction): boolean {
  switch (action.type) {
    case 'coverageApplied':
      return action.response !== null && !isSuperseded(state, action.origin);
    case 'draftLoaded':
    case 'linesImported':
    case 'validationApplied':
    case 'previewApplied':
    case 'saved':
    case 'invoiceLoaded':
    case 'connectivityRestored':
      return !('origin' in action && isSuperseded(state, action.origin));
    default:
      return false;
  }
}

/** Reduces draft-screen actions into the shared invoice draft state and counts the successful outcomes applied. */
export function invoiceDraftReducer(state: InvoiceDraftState, action: InvoiceDraftAction): InvoiceDraftState {
  const next = reduceInvoiceDraft(state, action);
  const successCount = appliesSuccess(state, action) ? state.successCount + 1 : state.successCount;
  return next.successCount === successCount ? next : { ...next, successCount };
}

/** Reduces draft-screen actions into the shared invoice draft state. */
function reduceInvoiceDraft(state: InvoiceDraftState, action: InvoiceDraftAction): InvoiceDraftState {
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
        display: loadedDisplay(action.response.display),
        messages: replaceSource({}, 'NEW', action.response.messages),
        openItems: replaceSource({}, 'NEW', action.response.openItems),
        claimPreload: claimPreloadOf(loaded),
        editCount: state.editCount,
        patientContextCount: state.patientContextCount,
      };
    }

    case 'headerFieldChanged': {
      if (state.draft === null || state.readOnly) {
        return state;
      }
      const edited = { ...state.draft, header: withField(state.draft.header, action.field, action.value) };
      // A changed final-discount entry drops the discount-limit choice.
      const choiceDropped = DISCOUNT_ENTRY_FIELDS.has(action.field) && !Object.is(state.draft.header[action.field], action.value);
      return {
        ...withEdit(state, action.field),
        draft: choiceDropped ? { ...edited, discountLimitChoice: null } : edited,
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
      const edited = withEdit(state, clientId == null || clientId === '' ? null : lineEditKey(clientId, action.field));
      if (action.field !== 'serviceId' || Object.is(action.value, previous.serviceId)) {
        return { ...edited, draft: { ...draft, lines } };
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
        ...edited,
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
      // A removal also drops the Blocking line-item messages of non-line sources, the Blocking LINE messages of every source and the errors the removed line's answers raised (D-174).
      const messages = withoutMessages(
        shiftLineKeys(state.messages, action.index),
        (source, message) =>
          message.severity === 'Blocking' &&
          (messageField(message) === LINE_FIELD || (!LINE_KEY.test(source) && LINE_ITEMS.has(messageField(message) ?? ''))),
      );
      const released =
        removedClientId == null || removedClientId === ''
          ? state
          : withoutErrors(state, (error) => CLIENT_KEY.exec(error.source)?.[1] === removedClientId);
      return withoutPreview({
        ...released,
        draft: { ...draft, lines },
        messages,
        openItems: shiftLineKeys(state.openItems, action.index),
        lineDisplay,
        fieldErrors: keepClientKeys(released.fieldErrors, kept),
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
        const merged = applyAdjusted(appended, state.display, response.adjusted, null, editedAfter(state, action.editsSince));
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      return withoutAnsweredErrors(next, action.source);
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
      const judgedLine = judgesCurrentLine(state, lineIndex, action.judged);
      const cleared = judgedLine ? withoutLineVerdicts(state, key, lineClientId) : state;
      // A header verdict releases its field's messages held by other sources, a record verdict every record-check message;
      // a judged line releases 'Invoice without Details'.
      let released = cleared.messages;
      if (lineIndex === null) {
        released = withoutJudgedFieldMessages(state, action.target, released);
        if (RECORD_VERDICT_TARGETS.has(action.target)) {
          released = withoutRecordMessages(released, action.target);
        }
      } else if (judgedLine) {
        released = withoutDetailsRequired(released);
      }
      const fieldErrors = { ...cleared.fieldErrors };
      if (lineClientId === null) {
        delete fieldErrors[action.target];
      } else {
        delete fieldErrors[errorKey(action.target, lineClientId)];
      }
      let next: InvoiceDraftState = {
        ...cleared,
        connectivityDown: false,
        messages: replaceSource(released, key, response.messages),
        openItems: replaceSource(cleared.openItems, key, response.openItems),
        fieldErrors,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, response.adjusted, lineIndex, editedAfter(state, action.editsSince));
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
      return withoutDiscountChoice(withoutAnsweredErrors(next, errorSource(key, lineClientId)), key, action.origin);
    }

    case 'validationFailed': {
      const error = action.error;
      const lineClientId = action.lineIndex === null ? null : (action.lineClientId ?? null);
      const lineIndex = lineClientId === null ? null : lineIndexOf(state.draft, lineClientId);
      if (action.lineIndex !== null && lineIndex === null) {
        return error.type === 'field-validation' ? state : applyDetachedError(state, messageKey(action.target, action.lineIndex), error);
      }
      const key = messageKey(action.target, lineIndex);
      const cleared =
        isLineVerdictFailure(error) && judgesCurrentLine(state, lineIndex, action.judged) ? withoutLineVerdicts(state, key) : state;
      // A header field-validation answer naming its own field releases that field's messages held by other sources.
      const judgedField =
        lineIndex === null &&
        error.type === 'field-validation' &&
        (error.messages ?? []).some((message) => messageField(message) === action.target);
      const fieldReleased = judgedField ? withoutJudgedFieldMessages(state, action.target, cleared.messages) : cleared.messages;
      // A record verdict carrying a record-check message replaces the record-check messages held by other sources.
      const recordJudged =
        lineIndex === null &&
        error.type === 'field-validation' &&
        RECORD_VERDICT_TARGETS.has(action.target) &&
        (error.messages ?? []).some((message) => message.rule === RECORD_RULE);
      const released = recordJudged ? withoutRecordMessages(fieldReleased, action.target) : fieldReleased;
      let next: InvoiceDraftState = {
        ...state,
        messages: setSource(released, key, error.messages ?? []),
        openItems: error.openItems != null ? setSource(cleared.openItems, key, error.openItems) : cleared.openItems,
      };
      if (state.draft !== null && !state.readOnly) {
        const merged = applyAdjusted(state.draft, state.display, error.adjusted, lineIndex, editedAfter(state, action.editsSince));
        next = { ...next, draft: merged.draft, display: merged.display };
      }
      // A response to a request that carried a discount-limit choice, or one arriving while an answer is pending, raises no prompt.
      const prompt = mayRaiseDiscountPrompt(state, action.origin)
        ? discountPromptFrom(error.messages, action.target, next.draft?.header, error.adjusted)
        : null;
      if (prompt !== null) {
        next = { ...next, discountPrompt: prompt };
      }
      return error.type === 'field-validation'
        ? withoutDiscountChoice(withoutAnsweredErrors(next, errorSource(key, lineClientId)), key, action.origin)
        : applyError(next, key, error, lineClientId);
    }

    case 'discountChoiceMade': {
      if (state.draft === null || state.readOnly || state.discountPrompt === null) {
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
      // A coverage response drops the COVERAGE errors; a null response keeps them.
      const answered = withoutAnsweredErrors(state, 'COVERAGE');
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        coverage: response,
        coveragePatientNo,
        messages: replaceSource(state.messages, 'COVERAGE', response.messages),
        openItems: replaceSource(state.openItems, 'COVERAGE', response.openItems),
        formError: answered.formError,
        fieldErrors: answered.fieldErrors,
      };
      const edited = editedAfter(state, action.editsSince);
      if (state.draft !== null && !state.readOnly && response.payType != null && !edited('payType')) {
        next = {
          ...next,
          draft: { ...state.draft, header: withField(state.draft.header, 'payType', response.payType) },
        };
      }
      const snapshot = response.coverage;
      const covered = next.draft;
      if (covered !== null && !state.readOnly && snapshot != null) {
        const party = coverageSubCompanyAndClass(snapshot, response.payType, coveragePatientNo, state.claimPreload);
        const header = { ...covered.header };
        const display = { ...next.display };
        // A company, sub-company or class the operator picked after the read was queued keeps the pick and its name.
        if (!edited('compCode')) {
          header.compCode = snapshot.compCode ?? null;
          display.COMP_NAME = snapshot.compName ?? null;
        }
        if (!edited('subCompCode')) {
          header.subCompCode = party.subCompCode;
          display.SUB_COMP_NAME = party.subCompName;
        }
        if (!edited('classCode')) {
          header.classCode = party.classCode;
          display.CLASS_NAME = party.className;
        }
        next = { ...next, draft: { ...covered, header }, display };
      }
      return next;
    }

    case 'patientContextCleared': {
      // Drops the previous patient's coverage, pay type, company, sub-company and class with their names, and PATIENTNO / COVERAGE messages, open items and errors.
      if (state.draft === null || state.readOnly) {
        return state;
      }
      const errorsKept = withoutErrors(state, (error) => error.source === 'COVERAGE' || error.source === 'PATIENTNO');
      // Also drops a claim number other than the entry parameter's, the stored preview, the lines' previewed prices and the validated refund, and records the claim number as edited at the current edit count (D-186).
      const header = state.draft.header;
      const entryClaimNo = state.draft.parameters?.claimNo || null;
      const display: Record<string, string | null> = { ...state.display, COMP_NAME: null, SUB_COMP_NAME: null, CLASS_NAME: null };
      delete display.REUND;
      return withoutPreview({
        ...state,
        draft: {
          ...state.draft,
          header: {
            ...header,
            payType: null,
            compCode: null,
            subCompCode: null,
            classCode: null,
            claimNo: header.claimNo === entryClaimNo ? header.claimNo : null,
          },
          lines: state.draft.lines.map((line) => ({ ...line, price: null })),
        },
        display,
        coverage: null,
        coveragePatientNo: null,
        messages: replaceSource(replaceSource(state.messages, 'COVERAGE', null), 'PATIENTNO', null),
        openItems: replaceSource(replaceSource(state.openItems, 'COVERAGE', null), 'PATIENTNO', null),
        formError: errorsKept.formError,
        fieldErrors: errorsKept.fieldErrors,
        editedAt: { ...state.editedAt, claimNo: state.editCount },
        patientContextCount: state.patientContextCount + 1,
      });
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
      const answered = withoutAnsweredErrors(state, 'PREVIEW');
      // A preview that priced lines of the editable draft releases 'Invoice without Details'.
      const priced = state.draft !== null && !state.readOnly && (response.lines ?? []).length > 0;
      let next: InvoiceDraftState = {
        ...state,
        connectivityDown: false,
        preview: response,
        messages: replaceSource(priced ? withoutDetailsRequired(state.messages) : state.messages, 'PREVIEW', response.messages),
        openItems: replaceSource(state.openItems, 'PREVIEW', response.openItems),
        formError: answered.formError,
        fieldErrors: answered.fieldErrors,
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
      // The preview's refund replaces a refund an earlier validation returned (D-148).
      if (Object.hasOwn(next.display, 'REUND')) {
        const display = { ...next.display };
        delete display.REUND;
        next = { ...next, display };
      }
      return next;
    }

    case 'previewCleared':
      return withoutPreview(state);

    case 'saved': {
      const response = action.response;
      const errorsKept = withoutErrors(state, (error) => judgesDraft(error.source) || isStaleKind(error.kind));
      return {
        ...state,
        connectivityDown: false,
        preview: null,
        saved: { invNo: response.invNo, view: null, createResponse: response },
        readOnly: true,
        entryErrors: {},
        // Replaces the CREATE messages and drops the IMPORT messages; IMPORT open items stay (D-192).
        messages: replaceSource(replaceSource(state.messages, 'CREATE', response.messages), 'IMPORT', null),
        openItems: replaceSource(replaceSource(state.openItems, 'CREATE', response.openItems), 'SAVED', ['OI-56']),
        formError: errorsKept.formError,
        fieldErrors: errorsKept.fieldErrors,
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
        // Reloading the invoice on screen keeps its messages and open items and drops the errors of the sources that judged its draft.
        const errorsKept = withoutErrors(state, (error) => judgesDraft(error.source));
        return {
          ...loaded,
          saved: { invNo: action.invNo, view: action.response, createResponse: state.saved?.createResponse ?? null },
          openItems: replaceSource(state.openItems, 'SAVED', savedOpenItems),
          currentLineIndex: clampIndex(state.currentLineIndex, lineCount),
          formError: errorsKept.formError,
          fieldErrors: errorsKept.fieldErrors,
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
      const automatic = action.automatic === true;
      const line = LINE_KEY.exec(action.source);
      if (line === null) {
        return applyError(state, action.source, action.error, null, automatic);
      }
      const lineClientId = action.lineClientId ?? null;
      const lineIndex = lineClientId === null ? null : lineIndexOf(state.draft, lineClientId);
      if (lineIndex === null) {
        return applyDetachedError(state, action.source, action.error, automatic);
      }
      const key = messageKey(line[2], lineIndex);
      const cleared =
        isLineVerdictFailure(action.error) && judgesCurrentLine(state, lineIndex, action.judged) ? withoutLineVerdicts(state, key) : state;
      return applyError(cleared, key, action.error, lineClientId, automatic);
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

/** True when the draft can be submitted: loaded, unsaved, editable, conflict-free, without rejected entries and without messages other than warnings. */
export function canSave(state: InvoiceDraftState): boolean {
  if (state.draft === null || state.saved !== null || state.readOnly || state.idempotencyConflict !== null) {
    return false;
  }
  if (Object.keys(state.entryErrors).length > 0) {
    return false;
  }
  return !Object.values(state.messages).some((list) => list.some((m) => m.severity !== 'Warning'));
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
    return { text: state.entryErrors[key], oracleErrorNumber: null, kind: null, source: key };
  }
  return Object.hasOwn(state.fieldErrors, key) ? state.fieldErrors[key] : null;
}

/** Origin of a request sent for `draft`, with the discount-limit choice it carries; `withPatient` also binds it to the draft's patient number. */
export function requestOrigin(draft: DraftDto, withPatient = false): RequestOrigin {
  const discountLimitChoice = draft.discountLimitChoice ?? null;
  return withPatient
    ? { requestId: draft.requestId, patientNo: draft.header.patientNo, discountLimitChoice }
    : { requestId: draft.requestId, discountLimitChoice };
}

/** True when a response for `origin` no longer belongs to the state's draft or, for a patient-bound origin, its patient, or was sent before the last patient change. */
export function isSuperseded(state: InvoiceDraftState, origin: RequestOrigin | undefined): boolean {
  if (origin === undefined) {
    return false;
  }
  const draft = state.draft;
  if (draft === null || draft.requestId !== origin.requestId) {
    return true;
  }
  // An origin stamped with the patient change count is superseded by any later patient change (D-186).
  if (origin.patientContextCount !== undefined && origin.patientContextCount !== state.patientContextCount) {
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

/** Upper-case trimmed field a message names, or null when it names none. */
export function messageField(message: MessageDto): string | null {
  const field = typeof message.field === 'string' ? message.field.trim().toUpperCase() : '';
  return field === '' ? null : field;
}

/** True when `target` is a header item target and a non-line source other than the target holds a record-check message, so the record check must judge the header again. */
export function recordRecheckNeeded(state: InvoiceDraftState, target: string): boolean {
  if (!Object.hasOwn(HEADER_TARGET_MEMBERS, target) || RECORD_VERDICT_TARGETS.has(target)) {
    return false;
  }
  return Object.entries(state.messages).some(
    ([source, list]) => source !== target && !source.startsWith('LINE:') && list.some((message) => message.rule === RECORD_RULE),
  );
}


/** Messages of every `LINE:<lineIndex>:*` source, each distinct field, severity and text once in first-seen order, with every stored copy. */
export function lineMessages(state: InvoiceDraftState, lineIndex: number): PlacedMessage[] {
  const placed: PlacedMessage[] = [];
  const positions = new Map<string, number>();
  for (const [source, list] of Object.entries(state.messages)) {
    const line = LINE_KEY.exec(source);
    if (line === null || Number(line[1]) !== lineIndex) {
      continue;
    }
    list.forEach((message, index) => {
      const key = `${messageField(message) ?? ''}\u0000${message.severity}\u0000${message.text}`;
      const position = positions.get(key);
      if (position === undefined) {
        positions.set(key, placed.length);
        placed.push({ message, refs: [{ source, index }] });
      } else {
        placed[position].refs.push({ source, index });
      }
    });
  }
  return placed;
}

/** Dismissal order of stored copies: by source, highest index first within each source. */
export function dismissalOrder(refs: readonly MessageRef[]): MessageRef[] {
  return [...refs].sort((a, b) => (a.source === b.source ? b.index - a.index : a.source < b.source ? -1 : 1));
}
