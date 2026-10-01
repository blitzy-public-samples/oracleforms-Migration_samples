import type {
  AdjustedValues,
  BundledOfferRequest,
  CoverageResponse,
  CreateInvoiceRequest,
  CreateInvoiceResponse,
  DocumentKind,
  DraftDto,
  DraftRequestDto,
  DraftRequestHeader,
  DraftRequestLine,
  ImportRequestsRequest,
  ImportResponse,
  InvoiceEntryParameters,
  InvoiceViewResponse,
  LastInvoiceNoResponse,
  LookupItem,
  LovBinds,
  LovResponse,
  MessageDto,
  MessageSeverity,
  MoreDetailsResponse,
  NewDraftResponse,
  PackageImportRequest,
  PreviewResponse,
  ProblemPayload,
  ProblemType,
  ValidateDraftRequest,
  ValidateDraftResponse,
  VisitLineRequest,
} from './types';

const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

const machineNameMaxLength = 15;

const problemTypes: Record<ProblemType, true> = {
  'field-validation': true,
  'oracle-business-error': true,
  'operator-context-missing': true,
  'open-item': true,
  'oracle-unavailable': true,
  'oracle-error': true,
  'not-found': true,
};

/** Development operator identity sent as X-His-* headers. */
export const developmentOperator = {
  userNo: 1,
  userName: 'DEV',
  infoCenterId: '1',
  machineName: 'WEB-DEV',
  sessionId: crypto.randomUUID(),
};

interface ApiErrorInit {
  status: number;
  type: string;
  title: string;
  message: string;
  messages?: MessageDto[];
  openItems?: string[];
  openItemId?: string | null;
  oracleErrorNumber?: number | null;
  package?: string | null;
  field?: string | null;
  legacyText?: string | null;
  kind?: string | null;
  missing?: string[];
  adjusted?: AdjustedValues | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function textOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function numberOrNull(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function stringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

/** 'Warning' for exactly 'Warning', else 'Blocking'. */
function messageSeverity(value: unknown): MessageSeverity {
  return value === 'Warning' ? 'Warning' : 'Blocking';
}

function messageArray(value: unknown): MessageDto[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const messages: MessageDto[] = [];
  for (const item of value) {
    if (isRecord(item) && typeof item.text === 'string') {
      messages.push({
        field: stringOrNull(item.field),
        text: item.text,
        severity: messageSeverity(item.severity),
        rule: stringOrNull(item.rule),
      });
    }
  }
  return messages;
}

/** Normalised copy of a success body's message list; undefined unless every entry is an object with text. */
function successMessages(value: unknown): MessageDto[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }
  const messages: MessageDto[] = [];
  for (const item of value) {
    if (!isRecord(item) || typeof item.text !== 'string') {
      return undefined;
    }
    messages.push({
      field: stringOrNull(item.field),
      text: item.text,
      severity: messageSeverity(item.severity),
      rule: stringOrNull(item.rule),
    });
  }
  return messages;
}

function isProblemType(value: string): value is ProblemType {
  return Object.hasOwn(problemTypes, value);
}

function resolveType(status: number, payloadType: string | null): string {
  if (status === 503) {
    return 'oracle-unavailable';
  }
  if (payloadType !== null && isProblemType(payloadType)) {
    return payloadType;
  }
  return status === 404 ? 'not-found' : 'http-error';
}

/** Error raised for every failed Api call, carrying the normalised problem payload. */
export class ApiError extends Error {
  readonly status: number;
  readonly type: string;
  readonly title: string;
  readonly messages: MessageDto[];
  readonly openItems: string[];
  readonly openItemId: string | null;
  readonly oracleErrorNumber: number | null;
  readonly package: string | null;
  readonly field: string | null;
  readonly legacyText: string | null;
  readonly kind: string | null;
  readonly missing: string[];
  readonly adjusted: AdjustedValues | null;

  constructor(init: ApiErrorInit) {
    super(init.message);
    this.name = 'ApiError';
    this.status = init.status;
    this.type = init.type;
    this.title = init.title;
    this.messages = init.messages ?? [];
    this.openItems = init.openItems ?? [];
    this.openItemId = init.openItemId ?? null;
    this.oracleErrorNumber = init.oracleErrorNumber ?? null;
    this.package = init.package ?? null;
    this.field = init.field ?? null;
    this.legacyText = init.legacyText ?? null;
    this.kind = init.kind ?? null;
    this.missing = init.missing ?? [];
    this.adjusted = init.adjusted ?? null;
  }

  /** Builds an ApiError from an HTTP status and its parsed response body, if any. */
  static fromResponse(status: number, payload: unknown): ApiError {
    const body: Partial<Record<keyof ProblemPayload, unknown>> = isRecord(payload) ? payload : {};
    const title = textOrNull(body.title) ?? `HTTP ${status}`;
    const messages = messageArray(body.messages);
    return new ApiError({
      status,
      type: resolveType(status, stringOrNull(body.type)),
      title,
      message: textOrNull(body.message) ?? textOrNull(messages[0]?.text) ?? title,
      messages,
      openItems: stringArray(body.openItems),
      openItemId: stringOrNull(body.openItemId),
      oracleErrorNumber: numberOrNull(body.oracleErrorNumber),
      package: stringOrNull(body.package),
      field: stringOrNull(body.field),
      legacyText: stringOrNull(body.legacyText),
      kind: stringOrNull(body.kind),
      missing: stringArray(body.missing),
      adjusted: isRecord(body.adjusted) ? body.adjusted : null,
    });
  }
}

function networkError(error: unknown): ApiError {
  return new ApiError({
    status: 0,
    type: 'network-error',
    title: 'Network error',
    message: error instanceof Error ? error.message : String(error),
  });
}

/** ApiError for a successful response that carries no body. */
class EmptyResponseError extends ApiError {}

function operatorHeaders(withBody: boolean): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: 'application/json, application/problem+json',
    'X-His-User-No': String(developmentOperator.userNo),
    'X-His-User-Name': developmentOperator.userName,
    'X-His-Info-Center-Id': developmentOperator.infoCenterId,
    'X-His-Machine': developmentOperator.machineName.slice(0, machineNameMaxLength),
    'X-His-Session-Id': developmentOperator.sessionId,
  };
  if (withBody) {
    headers['Content-Type'] = 'application/json';
  }
  return headers;
}

function parseErrorBody(text: string, contentType: string | null): unknown {
  if (text.trim() === '' || contentType === null || !contentType.toLowerCase().includes('json')) {
    return undefined;
  }
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/** Reads a parsed success body as its endpoint's contract; undefined when the body does not match it. */
type BodyReader<T> = (payload: unknown) => T | undefined;

/** Object body whose `messages` list is replaced by its normalised copy. */
function messagesBody<T extends { messages: MessageDto[] }>(payload: unknown): T | undefined {
  if (!isRecord(payload)) {
    return undefined;
  }
  const messages = successMessages(payload.messages);
  return messages === undefined ? undefined : ({ ...payload, messages } as T);
}

/** Object body. */
function objectBody<T>(payload: unknown): T | undefined {
  return isRecord(payload) ? (payload as T) : undefined;
}

/** Array body. */
function arrayBody<T>(payload: unknown): T[] | undefined {
  return Array.isArray(payload) ? (payload as T[]) : undefined;
}

/** ValidateDraft body with its own and its nested coverage's message lists normalised. */
function validateBody(payload: unknown): ValidateDraftResponse | undefined {
  const response = messagesBody<ValidateDraftResponse>(payload);
  if (response === undefined || response.coverage === null || response.coverage === undefined) {
    return response;
  }
  const coverage = messagesBody<CoverageResponse>(response.coverage);
  return coverage === undefined ? undefined : { ...response, coverage };
}

async function request<T>(
  method: 'GET' | 'POST',
  path: string,
  read: BodyReader<T>,
  body?: unknown,
  signal?: AbortSignal,
): Promise<T> {
  const init: RequestInit = { method, headers: operatorHeaders(body !== undefined) };
  if (body !== undefined) {
    init.body = JSON.stringify(body);
  }
  if (signal !== undefined) {
    init.signal = signal;
  }

  let response: Response;
  try {
    response = await fetch(apiBase + path, init);
  } catch (error) {
    throw networkError(error);
  }

  let text: string;
  try {
    text = await response.text();
  } catch (error) {
    if (response.ok) {
      throw networkError(error);
    }
    text = '';
  }

  if (!response.ok) {
    throw ApiError.fromResponse(response.status, parseErrorBody(text, response.headers.get('Content-Type')));
  }
  if (text.trim() === '') {
    throw new EmptyResponseError({
      status: response.status,
      type: 'invalid-response',
      title: 'Empty response',
      message: `The response to ${method} ${path} has no body.`,
    });
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text) as unknown;
  } catch {
    throw new ApiError({
      status: response.status,
      type: 'invalid-response',
      title: 'Invalid response',
      message: `The response to ${method} ${path} is not JSON.`,
    });
  }
  const result = read(parsed);
  if (result === undefined) {
    throw new ApiError({
      status: response.status,
      type: 'invalid-response',
      title: 'Invalid response',
      message: `The response to ${method} ${path} does not match its contract.`,
    });
  }
  return result;
}

/** Sends a request whose success has no body; resolves once the call succeeds, with or without a body. */
async function requestNoContent(method: 'POST', path: string): Promise<void> {
  try {
    await request<unknown>(method, path, (payload) => payload);
  } catch (error) {
    if (!(error instanceof EmptyResponseError)) {
      throw error;
    }
  }
}

function segment(value: string | number): string {
  return encodeURIComponent(String(value));
}

function appendQuery(query: URLSearchParams, values: object): void {
  for (const [key, value] of Object.entries(values) as [string, unknown][]) {
    if (typeof value === 'string' && value !== '') {
      query.append(key, value);
    } else if ((typeof value === 'number' && Number.isFinite(value)) || typeof value === 'boolean') {
      query.append(key, String(value));
    }
  }
}

function withQuery(path: string, query: URLSearchParams): string {
  const text = query.toString();
  return text === '' ? path : `${path}?${text}`;
}

/** Header members a request body carries. */
const draftRequestHeaderMembers: Record<keyof DraftRequestHeader, true> = {
  patientNo: true,
  invDate: true,
  invTypeId: true,
  payType: true,
  subPayType: true,
  subPayType2: true,
  clinicId: true,
  docId: true,
  currCode: true,
  claimNo: true,
  claimFlag: true,
  noteNo: true,
  finalDiscPerc: true,
  finalDisc: true,
  amount1: true,
  amount2: true,
  addToList: true,
  userNo: true,
  machineN: true,
  infoCenterId: true,
  draftDate: true,
  invNo: true,
  deptWise: true,
  call: true,
  discT: true,
  cashPayed: true,
  compCode: true,
  subCompCode: true,
  classCode: true,
  insNumber: true,
  cardEnd: true,
  patPolicyNo: true,
};

/** Line members a request body carries. */
const draftRequestLineMembers: Record<keyof DraftRequestLine, true> = {
  serviceId: true,
  qty: true,
  priceOverride: true,
  usePriceOverride: true,
  discountType: true,
  disc: true,
  myDisc: true,
  teethNo: true,
  toothSurface: true,
  teethNo2: true,
  patServReqRowId: true,
  approvDate: true,
  approvValidity: true,
  approvRefNo: true,
  claimNo: true,
  reqNeedA: true,
  reqAStatus: true,
  packageServiceId: true,
  packageInstanceId: true,
  packageLineRole: true,
  packageComponentOrder: true,
  packageParentLineId: true,
  packagePricingMethod: true,
  packageDefinitionToken: true,
  offerId: true,
  offerDtlId: true,
  offerType: true,
  offerInstanceId: true,
  offerLineRole: true,
  offerParentLineId: true,
  offerPriceApplied: true,
  offerDisApplied: true,
  offerNameSnapshot: true,
  offerObjectVersionNumber: true,
  offerDtlObjectVersionNumber: true,
  clientId: true,
  price: true,
};

/** New object holding the own members of `source` that `members` lists. */
function pickMembers<T extends object>(source: T, members: Record<keyof T, true>): T {
  const picked: Partial<T> = {};
  for (const member of Object.keys(members) as (keyof T)[]) {
    if (Object.hasOwn(source, member)) {
      picked[member] = source[member];
    }
  }
  return picked as T;
}

/** Request-body copy of a draft, holding only the members the Api's request contract carries. */
function toDraftRequest(draft: DraftRequestDto): DraftRequestDto {
  return {
    requestId: draft.requestId,
    draftDate: draft.draftDate,
    draftSeal: draft.draftSeal,
    header: pickMembers(draft.header, draftRequestHeaderMembers),
    lines: draft.lines.map((line) => pickMembers(line, draftRequestLineMembers)),
    parameters: draft.parameters,
    discountLimitChoice: draft.discountLimitChoice,
  };
}

/** GET /api/drafts/new with the entry parameters of a query string such as window.location.search. */
export async function newDraft(search: string): Promise<NewDraftResponse> {
  const query = search === '' || search.startsWith('?') ? search : `?${search}`;
  return request('GET', `/api/drafts/new${query}`, messagesBody<NewDraftResponse>);
}

/** POST /api/drafts/validate: runs the item, line or record checks for one validated target. */
export async function validateDraft(req: ValidateDraftRequest): Promise<ValidateDraftResponse> {
  const body: ValidateDraftRequest = { draft: toDraftRequest(req.draft), target: req.target, lineIndex: req.lineIndex };
  return request('POST', '/api/drafts/validate', validateBody, body);
}

/** GET /api/patients/{patientNo}/coverage for the draft date and entry parameters. */
export async function getCoverage(
  patientNo: string | number,
  draftDate: string,
  parameters: InvoiceEntryParameters,
): Promise<CoverageResponse> {
  const query = new URLSearchParams();
  appendQuery(query, { draftDate });
  appendQuery(query, parameters);
  return request('GET', withQuery(`/api/patients/${segment(patientNo)}/coverage`, query), messagesBody<CoverageResponse>);
}

/** POST /api/invoices/preview: package-calculated lines, totals and payment status of a draft. */
export async function previewInvoice(draft: DraftDto): Promise<PreviewResponse> {
  return request('POST', '/api/invoices/preview', messagesBody<PreviewResponse>, toDraftRequest(draft));
}

/** POST /api/invoices: saves the draft and resolves with the 201 body. */
export async function createInvoice(req: CreateInvoiceRequest): Promise<CreateInvoiceResponse> {
  const body: CreateInvoiceRequest = { draft: toDraftRequest(req.draft) };
  return request('POST', '/api/invoices', messagesBody<CreateInvoiceResponse>, body);
}

/** GET /api/invoices/{invNo} with the entry parameters of a query string such as window.location.search; the Api reads LOCAL_DOC_TYPE. */
export async function getInvoice(invNo: number, search: string): Promise<InvoiceViewResponse> {
  const query = search === '' || search.startsWith('?') ? search : `?${search}`;
  return request('GET', `/api/invoices/${segment(invNo)}${query}`, objectBody<InvoiceViewResponse>);
}

/** GET /api/invoices/last: highest invoice number of the operator's information centre. */
export async function getLastInvoiceNo(): Promise<LastInvoiceNoResponse> {
  return request('GET', '/api/invoices/last', objectBody<LastInvoiceNoResponse>);
}

/** GET /api/invoices/{invNo}/more with the entry parameters of a query string such as window.location.search; the Api reads LOCAL_DOC_TYPE. */
export async function getMoreDetails(invNo: number, search: string): Promise<MoreDetailsResponse> {
  const query = search === '' || search.startsWith('?') ? search : `?${search}`;
  return request('GET', `/api/invoices/${segment(invNo)}/more${query}`, objectBody<MoreDetailsResponse>);
}

/** POST /api/invoices/{invNo}/sms. */
export async function sendSms(invNo: number): Promise<void> {
  await requestNoContent('POST', `/api/invoices/${segment(invNo)}/sms`);
}

/** POST /api/invoices/{invNo}/documents/{kind}. */
export async function buildDocument(invNo: number, kind: DocumentKind): Promise<void> {
  await requestNoContent('POST', `/api/invoices/${segment(invNo)}/documents/${segment(kind)}`);
}

/** POST /api/invoices/{invNo}/stock-transfer. */
export async function transferStock(invNo: number): Promise<void> {
  await requestNoContent('POST', `/api/invoices/${segment(invNo)}/stock-transfer`);
}

/** POST /api/imports/requests: imports the visit's selected service requests as draft lines. */
export async function importRequests(req: ImportRequestsRequest): Promise<ImportResponse> {
  const body: ImportRequestsRequest = { draft: toDraftRequest(req.draft) };
  return request('POST', '/api/imports/requests', messagesBody<ImportResponse>, body);
}

/** POST /api/imports/visit-line: the consultation, review or fixed-service visit line. */
export async function importVisitLine(req: VisitLineRequest): Promise<ImportResponse> {
  const body: VisitLineRequest = { draft: toDraftRequest(req.draft) };
  return request('POST', '/api/imports/visit-line', messagesBody<ImportResponse>, body);
}

/** POST /api/imports/package: the component lines of a package service. */
export async function importPackage(req: PackageImportRequest): Promise<ImportResponse> {
  const body: PackageImportRequest = {
    draft: toDraftRequest(req.draft),
    packageServiceId: req.packageServiceId,
    parentSourceId: req.parentSourceId,
  };
  return request('POST', '/api/imports/package', messagesBody<ImportResponse>, body);
}

/** POST /api/imports/bundled-offer: the lines of a bundled offer. */
export async function importBundledOffer(req: BundledOfferRequest): Promise<ImportResponse> {
  const body: BundledOfferRequest = { draft: toDraftRequest(req.draft), offerId: req.offerId, bundleQty: req.bundleQty };
  return request('POST', '/api/imports/bundled-offer', messagesBody<ImportResponse>, body);
}

/** GET /api/lov/{name} with the non-empty item binds; `signal` aborts the request. */
export async function getLov(name: string, binds: LovBinds = {}, signal?: AbortSignal): Promise<LovResponse> {
  const query = new URLSearchParams();
  appendQuery(query, {
    compCode: binds.compCode,
    subCompCode: binds.subCompCode,
    docIdx: binds.docIdx,
    patientNo: binds.patientNo,
    payType: binds.payType,
    draftDate: binds.draftDate,
  });
  return request('GET', withQuery(`/api/lov/${segment(name)}`, query), messagesBody<LovResponse>, undefined, signal);
}

/** GET /api/lookups/invoice-types. */
export async function getInvoiceTypes(): Promise<LookupItem[]> {
  return request('GET', '/api/lookups/invoice-types', arrayBody<LookupItem>);
}

/** GET /api/lookups/currencies. */
export async function getCurrencies(): Promise<LookupItem[]> {
  return request('GET', '/api/lookups/currencies', arrayBody<LookupItem>);
}
