import type {
  AdjustedValues,
  BundledOfferRequest,
  CoverageResponse,
  CreateInvoiceRequest,
  CreateInvoiceResponse,
  DocumentKind,
  DraftDto,
  ImportRequestsRequest,
  ImportResponse,
  InvoiceEntryParameters,
  InvoiceViewResponse,
  LastInvoiceNoResponse,
  LookupItem,
  LovBinds,
  LovResponse,
  MessageDto,
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
        severity: item.severity === 'Warning' ? 'Warning' : 'Blocking',
        rule: stringOrNull(item.rule),
      });
    }
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

async function request<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  const init: RequestInit = { method, headers: operatorHeaders(body !== undefined) };
  if (body !== undefined) {
    init.body = JSON.stringify(body);
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
    return undefined as T;
  }
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new ApiError({
      status: response.status,
      type: 'http-error',
      title: 'Invalid response',
      message: `The response to ${method} ${path} is not JSON.`,
    });
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

/** GET /api/drafts/new with the entry parameters of a query string such as window.location.search. */
export async function newDraft(search: string): Promise<NewDraftResponse> {
  const query = search === '' || search.startsWith('?') ? search : `?${search}`;
  return request<NewDraftResponse>('GET', `/api/drafts/new${query}`);
}

/** POST /api/drafts/validate: runs the item, line or record checks for one validated target. */
export async function validateDraft(req: ValidateDraftRequest): Promise<ValidateDraftResponse> {
  return request<ValidateDraftResponse>('POST', '/api/drafts/validate', req);
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
  return request<CoverageResponse>('GET', withQuery(`/api/patients/${segment(patientNo)}/coverage`, query));
}

/** POST /api/invoices/preview: package-calculated lines, totals and payment status of a draft. */
export async function previewInvoice(draft: DraftDto): Promise<PreviewResponse> {
  return request<PreviewResponse>('POST', '/api/invoices/preview', draft);
}

/** POST /api/invoices: saves the draft and resolves with the 201 body. */
export async function createInvoice(req: CreateInvoiceRequest): Promise<CreateInvoiceResponse> {
  return request<CreateInvoiceResponse>('POST', '/api/invoices', req);
}

/** GET /api/invoices/{invNo}: read-only view of a saved invoice. */
export async function getInvoice(invNo: number): Promise<InvoiceViewResponse> {
  return request<InvoiceViewResponse>('GET', `/api/invoices/${segment(invNo)}`);
}

/** GET /api/invoices/last: highest invoice number of the operator's information centre. */
export async function getLastInvoiceNo(): Promise<LastInvoiceNoResponse> {
  return request<LastInvoiceNoResponse>('GET', '/api/invoices/last');
}

/** GET /api/invoices/{invNo}/more: persisted MORE-canvas fields of a saved invoice. */
export async function getMoreDetails(invNo: number): Promise<MoreDetailsResponse> {
  return request<MoreDetailsResponse>('GET', `/api/invoices/${segment(invNo)}/more`);
}

/** POST /api/invoices/{invNo}/sms. */
export async function sendSms(invNo: number): Promise<void> {
  await request<void>('POST', `/api/invoices/${segment(invNo)}/sms`);
}

/** POST /api/invoices/{invNo}/documents/{kind}. */
export async function buildDocument(invNo: number, kind: DocumentKind): Promise<void> {
  await request<void>('POST', `/api/invoices/${segment(invNo)}/documents/${segment(kind)}`);
}

/** POST /api/invoices/{invNo}/stock-transfer. */
export async function transferStock(invNo: number): Promise<void> {
  await request<void>('POST', `/api/invoices/${segment(invNo)}/stock-transfer`);
}

/** POST /api/imports/requests: imports the visit's selected service requests as draft lines. */
export async function importRequests(req: ImportRequestsRequest): Promise<ImportResponse> {
  return request<ImportResponse>('POST', '/api/imports/requests', req);
}

/** POST /api/imports/visit-line: the consultation, review or fixed-service visit line. */
export async function importVisitLine(req: VisitLineRequest): Promise<ImportResponse> {
  return request<ImportResponse>('POST', '/api/imports/visit-line', req);
}

/** POST /api/imports/package: the component lines of a package service. */
export async function importPackage(req: PackageImportRequest): Promise<ImportResponse> {
  return request<ImportResponse>('POST', '/api/imports/package', req);
}

/** POST /api/imports/bundled-offer: the lines of a bundled offer. */
export async function importBundledOffer(req: BundledOfferRequest): Promise<ImportResponse> {
  return request<ImportResponse>('POST', '/api/imports/bundled-offer', req);
}

/** GET /api/lov/{name} with the non-empty item binds. */
export async function getLov(name: string, binds: LovBinds = {}): Promise<LovResponse> {
  const query = new URLSearchParams();
  appendQuery(query, {
    compCode: binds.compCode,
    subCompCode: binds.subCompCode,
    docIdx: binds.docIdx,
    patientNo: binds.patientNo,
    payType: binds.payType,
    draftDate: binds.draftDate,
  });
  return request<LovResponse>('GET', withQuery(`/api/lov/${segment(name)}`, query));
}

/** GET /api/lookups/invoice-types. */
export async function getInvoiceTypes(): Promise<LookupItem[]> {
  return request<LookupItem[]>('GET', '/api/lookups/invoice-types');
}

/** GET /api/lookups/currencies. */
export async function getCurrencies(): Promise<LookupItem[]> {
  return request<LookupItem[]>('GET', '/api/lookups/currencies');
}
