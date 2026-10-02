import { useEffect, useId, useRef, useState } from 'react';
import type { ChangeEvent, Dispatch, FocusEvent, SetStateAction, SyntheticEvent } from 'react';
import { ApiError, getMoreDetails, previewInvoice, transferStock, validateDraft } from '../api/client';
import type { InvoiceLineDraft, MessageDto, MoreDetailsLineKey, MoreDetailsResponse } from '../api/types';
import {
  currentCoverage,
  dismissalOrder,
  fieldErrorFor,
  lineMessages,
  messageField,
  requestOrigin,
  sameDraftInputs,
} from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState, PlacedMessage, RequestOrigin } from '../state/invoiceDraft';
import FieldMessage, { fieldMessageRefs, SeverityLabel } from '../components/FieldMessage';
import { FIELD_CAPTIONS } from '../components/InvoiceLinesGrid';
import { readOnlyTextProps } from '../components/LovPicker';
import OpenItemNotice from '../components/OpenItemNotice';

/** Editable `D_INV` item of the MORE canvas and the draft line field it edits. */
interface EditableLineField {
  key: 'teethNo' | 'toothSurface' | 'approvDate' | 'approvValidity' | 'approvRefNo';
  item: MoreDetailsLineKey;
  label: string;
  kind: 'text' | 'date' | 'number';
  maxLength?: number;
}

/** Read-only `D_INV` item of the MORE canvas and the draft line field that carries it, if any. */
interface ReadOnlyLineField {
  item: MoreDetailsLineKey;
  label: string;
  draftKey: keyof InvoiceLineDraft | null;
  labels?: Readonly<Record<string, string>>;
  freeText?: boolean;
}

/** Text and selection of a number field under its entry key. */
interface EntrySnapshot {
  key: string;
  text: string;
  start: number;
  end: number;
  direction: 'forward' | 'backward' | 'none';
}

/** One field's messages in the Current Line section: element id, caption (null for messages naming no field), messages and field error. */
interface LineMessageGroup {
  id: string;
  caption: string | null;
  messages: PlacedMessage[];
  fieldError: { text: string; oracleErrorNumber: number | null } | null;
}

/** State of the saved-details load of the selected invoice. */
type LoadStatus = 'pending' | 'loaded' | 'failed';

const APPROV_REF_NO = 'APPROV_REF_NO';

const NUMBER_LITERAL = /^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/;

const NUMBER_ENTRY_ERROR = 'FRM-50016: Legal characters are 0-9 - + E .';

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})/;

const DATE_ENTRY = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/;

const DATE_ENTRY_ERROR = 'FRM-50026: Date must be entered in a format like dd/mm/yyyy.';

const EDITABLE_FIELDS: readonly EditableLineField[] = [
  { key: 'teethNo', item: 'TEETH_NO', label: 'Teeth No', kind: 'text', maxLength: 2 },
  { key: 'toothSurface', item: 'TOOTH_SURFACE', label: 'Tooth Surface', kind: 'text', maxLength: 7 },
  { key: 'approvDate', item: 'APPROV_DATE', label: 'Approval Date', kind: 'date', maxLength: 11 },
  { key: 'approvValidity', item: 'APPROV_VALIDITY', label: 'Approval Validity', kind: 'number', maxLength: 4 },
  { key: 'approvRefNo', item: APPROV_REF_NO, label: 'Approval Ref No', kind: 'text', maxLength: 20 },
];

const STATUS_FIELDS: readonly ReadOnlyLineField[] = [
  {
    item: 'REQ_NEED_A',
    label: 'Approval Status',
    draftKey: 'reqNeedA',
    labels: { '0': 'Open', '1': 'Need Approval', '2': 'Close' },
  },
  {
    item: 'REQ_A_STATUS',
    label: 'Req A Status',
    draftKey: 'reqAStatus',
    labels: { '1': 'Wating', '2': 'Approved', '3': 'Rejected' },
  },
];

const NOT_SAVED_FIELDS: readonly ReadOnlyLineField[] = [
  { item: 'REGULAR_LENSES_TYPE', label: 'Regular Lenses Type', draftKey: 'regularLensesType', freeText: true },
  { item: 'LENS_SPECIFICATIONS', label: 'Lens Specifications', draftKey: 'lensSpecifications', freeText: true },
  { item: 'CONTACT_LENSES_TYPE', label: 'Contact Lenses Type', draftKey: 'contactLensesType', freeText: true },
  { item: 'F_L_INDICATOR', label: 'F L Indicator', draftKey: 'flIndicator' },
  { item: 'NUMBER_OF_PAIRS', label: 'Number Of Pairs', draftKey: 'numberOfPairs' },
  { item: 'INS_EMP', label: 'Insurance Emp', draftKey: 'insEmp' },
  { item: 'INS_EMP_NAME', label: 'Insurance Employe', draftKey: null, freeText: true },
];

/** Text shown for a value as returned; null and undefined show as empty. */
function displayText(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  return JSON.stringify(value);
}

/** `dd/mm/yyyy` text of an ISO date or date-time value without time-zone conversion, or the text unchanged. */
function dateText(value: unknown): string {
  const text = displayText(value);
  const match = ISO_DATE.exec(text);
  return match === null ? text : `${match[3]}/${match[2]}/${match[1]}`;
}

/** First 10 characters of a draft date value, its `yyyy-mm-dd` part when ISO; null when empty. */
function draftDateOf(value: unknown): string | null {
  const text = displayText(value);
  return text === '' ? null : text.slice(0, 10);
}

/** Number of days in `month` (1-12) of Gregorian `year`. */
function daysInMonth(year: number, month: number): number {
  if (month === 2) {
    return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0 ? 29 : 28;
  }
  return month === 4 || month === 6 || month === 9 || month === 11 ? 30 : 31;
}

/** ISO `yyyy-mm-dd` of `d/m/yyyy` entry text naming a real date in years 0001-9999, or null. */
function parseDateEntry(text: string): string | null {
  const match = DATE_ENTRY.exec(text);
  if (match === null) {
    return null;
  }
  const day = Number(match[1]);
  const month = Number(match[2]);
  const year = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > daysInMonth(year, month)) {
    return null;
  }
  return `${match[3]}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** True when date entry text was typed over draft value `value`, which has not changed since. */
function dateEntryCurrent(entry: { text: string; draftDate?: string | null }, value: unknown): boolean {
  return (entry.draftDate ?? null) === draftDateOf(value);
}

/** The list-element label of a list item value, or the value itself. */
function listLabel(value: unknown, labels: Readonly<Record<string, string>> | undefined): string {
  const text = displayText(value);
  return labels !== undefined && Object.hasOwn(labels, text) ? labels[text] : text;
}

/** Input value text for an editable line field. */
function inputText(field: EditableLineField, value: unknown): string {
  return field.kind === 'date' ? dateText(value) : displayText(value);
}

/** Draft value for an editable line field from its input text; empty text is null. */
function parseInput(field: EditableLineField, text: string): string | number | null {
  if (text === '') {
    return null;
  }
  return field.kind === 'number' ? Number(text) : text;
}

/** Entry error for number `text`: illegal characters, then more characters than the field's maximum length; null when valid. */
function numberEntryError(field: EditableLineField, text: string): string | null {
  if (!NUMBER_LITERAL.test(text) || !Number.isFinite(Number(text))) {
    return NUMBER_ENTRY_ERROR;
  }
  if (field.maxLength !== undefined && text.length > field.maxLength) {
    return `${field.label} accepts at most ${field.maxLength} characters.`;
  }
  return null;
}

/** Current text and selection of `input` under entry key `key`; a missing selection is a caret at the end. */
function snapshotOf(key: string, input: HTMLInputElement): EntrySnapshot {
  const length = input.value.length;
  return {
    key,
    text: input.value,
    start: input.selectionStart ?? length,
    end: input.selectionEnd ?? length,
    direction: input.selectionDirection ?? 'none',
  };
}

/** Element at `index`, or undefined when the index is outside the list. */
function itemAt<T>(items: readonly T[] | undefined, index: number): T | undefined {
  return items !== undefined && Number.isInteger(index) && index >= 0 && index < items.length ? items[index] : undefined;
}

/** Reducer source key of a line validation target. */
function lineKey(lineIndex: number, target: string): string {
  return `LINE:${lineIndex}:${target}`;
}

/** Message groups of line `lineIndex` on fields MORE does not show: each named field in first-seen order with the field error of the line with `clientId`, then the messages naming no field. */
function otherLineMessageGroups(
  state: InvoiceDraftState,
  lineIndex: number,
  clientId: string | null,
  baseId: string,
): LineMessageGroup[] {
  const shown: ReadonlySet<string> = new Set(EDITABLE_FIELDS.map((field) => field.item));
  const named = new Map<string, LineMessageGroup>();
  const unnamed: PlacedMessage[] = [];
  const groupOf = (field: string): LineMessageGroup => {
    const existing = named.get(field);
    if (existing !== undefined) {
      return existing;
    }
    const group: LineMessageGroup = {
      id: `${baseId}-line-messages-${encodeURIComponent(field)}`,
      caption: Object.hasOwn(FIELD_CAPTIONS, field) ? FIELD_CAPTIONS[field] : field,
      messages: [],
      fieldError: null,
    };
    named.set(field, group);
    return group;
  };
  for (const entry of lineMessages(state, lineIndex)) {
    const field = messageField(entry.message);
    if (field === null) {
      unnamed.push(entry);
    } else if (!shown.has(field)) {
      groupOf(field).messages.push(entry);
    }
  }
  if (clientId !== null && clientId !== '') {
    const prefix = `CLIENT:${clientId}:`;
    for (const key of new Set([...Object.keys(state.entryErrors), ...Object.keys(state.fieldErrors)])) {
      const field = key.startsWith(prefix) ? key.slice(prefix.length) : '';
      if (field === '' || field.includes(':') || shown.has(field)) {
        continue;
      }
      const error = fieldErrorFor(state, field, clientId);
      if (error !== null) {
        groupOf(field).fieldError = { text: error.text, oracleErrorNumber: error.oracleErrorNumber };
      }
    }
  }
  const groups = [...named.values()];
  if (unnamed.length > 0) {
    groups.push({ id: `${baseId}-line-messages`, caption: null, messages: unnamed, fieldError: null });
  }
  return groups;
}

/** Distinct open-item ids across every message source, plus `extra`. */
function distinctOpenItems(openItems: Record<string, string[]>, extra: readonly string[]): string[] {
  return [...new Set([...Object.values(openItems).flat(), ...extra])];
}

/** The rejection as an ApiError, wrapping any other value. */
function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  return new ApiError({
    status: 0,
    type: 'client-error',
    title: 'Unexpected error',
    message: error instanceof Error ? error.message : String(error),
  });
}

/** True when a more-details response is an object carrying the details of invoice `invNo`. */
function isDetailsOf(response: unknown, invNo: number): response is MoreDetailsResponse {
  return typeof response === 'object' && response !== null && 'invNo' in response && response.invNo === invNo;
}

/** ApiError for a more-details response that carries no details of invoice `invNo`. */
function invalidDetailsError(invNo: number): ApiError {
  return new ApiError({
    status: 200,
    type: 'http-error',
    title: 'Invalid response',
    message: `The more-details response carries no details of invoice ${invNo}.`,
  });
}

/** Status text of the saved-details load of invoice `invNo`. */
function loadStatusText(status: LoadStatus, invNo: number): string {
  switch (status) {
    case 'pending':
      return `Loading saved details of invoice ${invNo}…`;
    case 'loaded':
      return `Saved details of invoice ${invNo} loaded.`;
    case 'failed':
      return `Saved details of invoice ${invNo} were not loaded. Return and open More Details again to retry.`;
  }
}

/** Oracle error number in `ORA-nnnnn` form. */
function oraText(oracleErrorNumber: number): string {
  return `ORA-${String(Math.abs(oracleErrorNumber)).padStart(5, '0')}`;
}

/** Title and tab-stop props of a read-only input: free text stays a tab stop, a short value does not. */
function readOnlyProps(value: string, freeText: boolean) {
  return freeText ? readOnlyTextProps(value) : { tabIndex: -1, title: value === '' ? undefined : value };
}

/** One labelled read-only text field. */
function ReadOnlyField({
  id,
  label,
  value,
  freeText = false,
}: {
  id: string;
  label: string;
  value: string;
  freeText?: boolean;
}) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} type="text" className="read-only" value={value} readOnly {...readOnlyProps(value, freeText)} />
    </div>
  );
}

/** MORE canvas: insurance, dental, approval and lens details of the current line, and store transfers. */
export default function MoreDetailsScreen({
  state,
  dispatch,
  active,
  outageCount,
  onBack,
}: {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  active: boolean;
  outageCount: () => number;
  onBack: () => void;
}) {
  const latest = useRef(state);
  latest.current = state;
  const requestedInvNo = useRef<number | null>(null);
  const failedInvNo = useRef<number | null>(null);
  const failedLoadText = useRef<string | null>(null);
  const [failedInvNoShown, setFailedInvNoShown] = useState<number | null>(null);
  const [entryText, setEntryText] = useState<{ key: string; text: string; draftDate?: string | null } | null>(null);
  const [entryError, setEntryError] = useState<{ key: string; text: string } | null>(null);
  const entrySnapshot = useRef<EntrySnapshot | null>(null);
  const refNoOnFocus = useRef('');
  const actionsRef = useRef<HTMLDivElement>(null);
  const [previewRequests, setPreviewRequests] = useState(0);
  const baseId = useId();
  const pointerHeld = useRef(false);
  const heldEntryError = useRef<(() => void) | null>(null);

  // Tracks a pointer press in progress and applies an entry-error change held during it once the press has completed.
  useEffect(() => {
    const press = () => {
      pointerHeld.current = true;
    };
    const release = () => {
      pointerHeld.current = false;
      const apply = heldEntryError.current;
      heldEntryError.current = null;
      if (apply !== null) {
        window.setTimeout(apply, 0);
      }
    };
    document.addEventListener('pointerdown', press, true);
    document.addEventListener('pointerup', release, true);
    document.addEventListener('pointercancel', release, true);
    window.addEventListener('blur', release);
    return () => {
      document.removeEventListener('pointerdown', press, true);
      document.removeEventListener('pointerup', release, true);
      document.removeEventListener('pointercancel', release, true);
      window.removeEventListener('blur', release);
    };
  }, []);

  /** Sets the entry error now, or once the pointer press in progress has completed. */
  function setEntryErrorAfterPress(next: SetStateAction<{ key: string; text: string } | null>) {
    if (pointerHeld.current) {
      heldEntryError.current = () => setEntryError(next);
    } else {
      setEntryError(next);
    }
  }

  const invNo = state.saved?.invNo;
  const isSaved = invNo != null;
  const editable = !isSaved && !state.readOnly;
  const index = state.currentLineIndex;
  const loadedInvNo = state.moreDetails?.invNo;
  const details = isSaved && state.moreDetails?.invNo === invNo ? state.moreDetails : null;
  const loadStatus: LoadStatus =
    details !== null ? 'loaded' : isSaved && failedInvNoShown === invNo ? 'failed' : 'pending';

  /** Loads persisted MORE fields and clears recovered errors; explicit entry may clear a prior outage. */
  function load(forInvNo: number, deliberate: boolean) {
    const outagesAtStart = outageCount();
    requestedInvNo.current = forInvNo;
    failedInvNo.current = null;
    setFailedInvNoShown(null);

    /** Ends the attempt; true when `forInvNo` is still the selected invoice. */
    const settle = (): boolean => {
      if (requestedInvNo.current === forInvNo) {
        requestedInvNo.current = null;
      }
      return latest.current.saved?.invNo === forInvNo;
    };

    /** Records the failed attempt and reports its error, as automatic unless the attempt is deliberate. */
    const fail = (error: ApiError) => {
      failedInvNo.current = forInvNo;
      failedLoadText.current = error.message;
      setFailedInvNoShown(forInvNo);
      dispatch({ type: 'errorReceived', source: 'SAVED', error, automatic: !deliberate });
    };

    getMoreDetails(forInvNo, window.location.search).then(
      (response: unknown) => {
        if (!settle()) {
          return;
        }
        if (!isDetailsOf(response, forInvNo)) {
          fail(invalidDetailsError(forInvNo));
          return;
        }
        dispatch({ type: 'moreDetailsLoaded', response });
        if (failedLoadText.current !== null && latest.current.formError?.text === failedLoadText.current) {
          dispatch({ type: 'formErrorCleared' });
        }
        failedLoadText.current = null;
        if (deliberate && outageCount() === outagesAtStart) {
          dispatch({ type: 'connectivityRestored' });
        }
      },
      (error: unknown) => {
        if (settle()) {
          fail(toApiError(error));
        }
      },
    );
  }

  // Starts one attempt on each entry to MORE while the saved invoice's details are absent and none is in flight.
  useEffect(() => {
    const current = latest.current;
    const forInvNo = current.saved?.invNo;
    if (!active || forInvNo == null || current.moreDetails?.invNo === forInvNo || requestedInvNo.current === forInvNo) {
      return;
    }
    load(forInvNo, true);
  }, [active]);

  // Loads the persisted MORE fields once per saved invoice number, unless its last attempt failed; no invoice resets the failure.
  useEffect(() => {
    if (invNo == null) {
      failedInvNo.current = null;
      setFailedInvNoShown(null);
      return;
    }
    if (loadedInvNo === invNo || requestedInvNo.current === invNo || failedInvNo.current === invNo) {
      return;
    }
    load(invNo, false);
  }, [invNo, loadedInvNo, dispatch]);

  // Previews the committed draft after each clean approval-reference validation.
  useEffect(() => {
    if (previewRequests > 0) {
      previewCurrentDraft();
    }
  }, [previewRequests]);

  const header = isSaved ? details : (currentCoverage(state)?.coverage ?? null);
  const draftLine = isSaved ? undefined : itemAt(state.draft?.lines, index);
  const savedLine = isSaved ? itemAt(details?.lines, index) : undefined;
  const lineEditable = editable && draftLine !== undefined;
  const serviceId = displayText(isSaved ? savedLine?.SERVICEID : draftLine?.serviceId);
  const lineGroups = otherLineMessageGroups(state, index, draftLine?.clientId ?? null, baseId);
  const transfers = details?.transMRowIds ?? [];

  const titleId = `${baseId}-title`;
  const insuranceTitleId = `${baseId}-insurance`;
  const lineTitleId = `${baseId}-line`;
  const transfersTitleId = `${baseId}-transfers`;
  const transferLabelId = `${baseId}-trans-m-row-id`;

  /** Value of a read-only line item on the current line. */
  function readOnlyValue(field: ReadOnlyLineField): unknown {
    if (isSaved) {
      return savedLine?.[field.item];
    }
    return field.draftKey !== null ? draftLine?.[field.draftKey] : undefined;
  }

  /** Key of an editable field of the current line for its local entry text and entry error. */
  function entryKey(field: EditableLineField): string {
    return `${index}:${draftLine?.clientId ?? ''}:${field.item}`;
  }

  /** Writes an edited line field into the current draft line; number text that is not a number or is too long is rejected, keeping the `shown` text and its selection. */
  function changeLineField(field: EditableLineField, event: ChangeEvent<HTMLInputElement>, shown: string) {
    const input = event.target;
    const text = input.value;
    if (field.kind === 'number') {
      const key = entryKey(field);
      const error = text === '' ? null : numberEntryError(field, text);
      if (error !== null) {
        const before = entrySnapshot.current;
        const kept = before !== null && before.key === key && before.text === shown ? before : null;
        input.value = shown;
        input.setSelectionRange(kept?.start ?? shown.length, kept?.end ?? shown.length, kept?.direction ?? 'none');
        entrySnapshot.current = snapshotOf(key, input);
        setEntryError({ key, text: error });
        return;
      }
      entrySnapshot.current = snapshotOf(key, input);
      setEntryError((current) => (current?.key === key ? null : current));
      setEntryText(text === '' ? null : { key, text });
    }
    dispatch({ type: 'lineFieldChanged', index, field: field.key, value: parseInput(field, text) });
  }

  /** Keeps date entry text over the draft date it is typed on; the draft line is written when the field is left. */
  function changeDateField(field: EditableLineField, event: ChangeEvent<HTMLInputElement>) {
    setEntryText({ key: entryKey(field), text: event.target.value, draftDate: draftDateOf(draftLine?.[field.key]) });
  }

  /** On leaving the field writes empty entry text (null) or a valid date (ISO) to the draft line; other text sets the FRM-50026 entry error and is dropped. */
  function blurDateField(field: EditableLineField, event: FocusEvent<HTMLInputElement>) {
    const key = entryKey(field);
    if (entryText?.key !== key) {
      return;
    }
    setEntryText(null);
    const text = event.target.value;
    const parsed = parseDateEntry(text);
    if (text !== '' && parsed === null) {
      setEntryErrorAfterPress({ key, text: DATE_ENTRY_ERROR });
      return;
    }
    setEntryErrorAfterPress((current) => (current?.key === key ? null : current));
    if (parsed !== draftDateOf(draftLine?.[field.key])) {
      dispatch({ type: 'lineFieldChanged', index, field: field.key, value: parsed });
    }
  }

  /** Remembers the text and selection of a number field before its next change. */
  function selectNumberField(field: EditableLineField, event: SyntheticEvent<HTMLInputElement>) {
    entrySnapshot.current = snapshotOf(entryKey(field), event.currentTarget);
  }

  /** Clears the entry error of a number field when it loses focus, once the pointer press in progress, if any, has completed. */
  function blurNumberField(field: EditableLineField) {
    const key = entryKey(field);
    setEntryErrorAfterPress((current) => (current?.key === key ? null : current));
  }

  /** Remembers the approval reference held when the field gains focus. */
  function focusRefNo(event: FocusEvent<HTMLInputElement>) {
    refNoOnFocus.current = event.target.value;
  }

  /** Validates the approval reference of the current line when it changed since focus. */
  function blurRefNo(event: FocusEvent<HTMLInputElement>) {
    const value = event.target.value;
    if (value === refNoOnFocus.current) {
      return;
    }
    refNoOnFocus.current = value;
    const draft = latest.current.draft;
    if (draft === null) {
      return;
    }
    const lineIndex = index;
    const sentLine = draft.lines[lineIndex];
    const lineClientId = sentLine?.clientId ?? null;
    const judged = sentLine === undefined ? undefined : { header: draft.header, line: sentLine };
    const origin = requestOrigin(draft);
    validateDraft({ draft, target: APPROV_REF_NO, lineIndex }).then(
      (response) => {
        const onScreen = latest.current.draft;
        const unchanged = onScreen !== null && sameDraftInputs(draft, onScreen);
        dispatch({ type: 'validationApplied', target: APPROV_REF_NO, lineIndex, lineClientId, response, origin, judged });
        if (unchanged) {
          setPreviewRequests((count) => count + 1);
        }
      },
      (error: unknown) => {
        const apiError = toApiError(error);
        if (apiError.status === 422 && apiError.type === 'field-validation') {
          dispatch({ type: 'validationFailed', target: APPROV_REF_NO, lineIndex, lineClientId, error: apiError, origin, judged });
        } else {
          dispatch({ type: 'errorReceived', source: lineKey(lineIndex, APPROV_REF_NO), lineClientId, error: apiError, origin, judged });
        }
      },
    );
  }

  /** POST /api/invoices/preview for the editable draft on screen; the result is applied only while the draft still sends the same request. */
  function previewCurrentDraft() {
    const current = latest.current;
    const draft = current.draft;
    if (draft === null || draft.lines.length === 0 || current.saved !== null || current.readOnly) {
      return;
    }
    // The preview and its failure are bound to the patient change count at send time (D-186).
    const origin: RequestOrigin = { ...requestOrigin(draft), patientContextCount: current.patientContextCount };
    previewInvoice(draft).then(
      (response) => {
        const now = latest.current.draft;
        if (now !== null && sameDraftInputs(draft, now)) {
          dispatch({ type: 'previewApplied', response, sent: draft.lines, origin });
        }
      },
      (error: unknown) => {
        const apiError = toApiError(error);
        if (apiError.type === 'field-validation') {
          dispatch({ type: 'validationFailed', target: 'PREVIEW', lineIndex: null, error: apiError, origin });
        } else {
          dispatch({ type: 'errorReceived', source: 'PREVIEW', error: apiError, origin });
        }
      },
    );
  }

  /** Requests the store transfer of the saved invoice. */
  function addStoreTrans() {
    if (invNo == null) {
      return;
    }
    transferStock(invNo).then(
      () => dispatch({ type: 'connectivityRestored' }),
      (error: unknown) => dispatch({ type: 'errorReceived', source: 'SAVED', error: toApiError(error) }),
    );
  }

  /** Clears the form error and, when focus falls to the body, focuses the first action after it. */
  function dismissFormError() {
    dispatch({ type: 'formErrorCleared' });
    window.setTimeout(() => {
      const active = document.activeElement;
      if (active === null || active === document.body) {
        actionsRef.current?.querySelector<HTMLElement>('button:not(:disabled)')?.focus();
      }
    }, 0);
  }

  /** Dismisses every stored copy of a current-line message group's message. */
  function dismissLineMessage(group: LineMessageGroup, messageIndex: number) {
    for (const ref of dismissalOrder(group.messages[messageIndex]?.refs ?? [])) {
      dispatch({ type: 'messageDismissed', source: ref.source, index: ref.index });
    }
  }

  /** One editable line item with its messages. */
  function renderEditable(field: EditableLineField) {
    const id = `${baseId}-${field.item}`;
    // The current line's messages naming this field, whichever line source holds them.
    const placed = lineMessages(state, index).filter((entry) => messageField(entry.message) === field.item);
    const messages: MessageDto[] = placed.map((entry) => entry.message);
    const fieldError = isSaved ? null : fieldErrorFor(state, field.item, draftLine?.clientId ?? null);
    const invalid = fieldError !== null || messages.some((message) => message.severity === 'Blocking');
    const isRefNo = field.item === APPROV_REF_NO;
    const isNumber = field.kind === 'number';
    const value = isSaved ? savedLine?.[field.item] : draftLine?.[field.key];
    const key = entryKey(field);
    const entryInvalid = lineEditable && entryError?.key === key;
    const typed =
      lineEditable &&
      entryText?.key === key &&
      (field.kind === 'date' ? dateEntryCurrent(entryText, value) : Number(entryText.text) === value)
        ? entryText.text
        : null;
    const shown = typed ?? inputText(field, value);
    const ariaInvalid = lineEditable && (invalid || entryInvalid);
    const entryErrorId = entryInvalid ? `${id}-entry-error` : undefined;
    const refs = fieldMessageRefs(`${id}-msg`, messages, fieldError);
    const describedBy = [entryErrorId, refs.describedBy].filter((part) => part !== undefined).join(' ');
    const errorMessage = [entryErrorId, invalid ? refs.errorMessage : undefined].filter((part) => part !== undefined).join(' ');
    return (
      <div className="field" key={field.item}>
        <label htmlFor={id}>{field.label}</label>
        <input
          id={id}
          type="text"
          inputMode={field.kind === 'number' ? 'decimal' : undefined}
          className={lineEditable ? (invalid || entryInvalid ? 'invalid' : undefined) : 'read-only'}
          value={shown}
          maxLength={field.kind === 'number' ? undefined : field.maxLength}
          readOnly={!lineEditable}
          aria-invalid={ariaInvalid ? true : undefined}
          aria-describedby={describedBy === '' ? undefined : describedBy}
          aria-errormessage={ariaInvalid && errorMessage !== '' ? errorMessage : undefined}
          onChange={
            lineEditable
              ? (event) => (field.kind === 'date' ? changeDateField(field, event) : changeLineField(field, event, shown))
              : undefined
          }
          onSelect={lineEditable && isNumber ? (event) => selectNumberField(field, event) : undefined}
          onFocus={lineEditable && isRefNo ? focusRefNo : undefined}
          onBlur={
            !lineEditable
              ? undefined
              : isRefNo
                ? blurRefNo
                : field.kind === 'date'
                  ? (event) => blurDateField(field, event)
                  : isNumber
                    ? () => blurNumberField(field)
                    : undefined
          }
          {...(lineEditable ? {} : readOnlyProps(shown, isRefNo))}
        />
        {entryInvalid && (
          <div id={`${id}-entry-error`} className="msg msg-blocking" role="alert">
            <SeverityLabel severity="Blocking" />{' '}
            <span>{entryError?.text}</span>
          </div>
        )}
        <FieldMessage
          id={`${id}-msg`}
          messages={messages}
          fieldError={fieldError}
          onDismiss={(messageIndex) => {
            for (const ref of dismissalOrder(placed[messageIndex]?.refs ?? [])) {
              dispatch({ type: 'messageDismissed', source: ref.source, index: ref.index });
            }
            document.getElementById(id)?.focus();
          }}
        />
      </div>
    );
  }

  // A draft shows its header's claim-preload fields while no coverage is stored, else only coverage read for its current patient.
  const insurance = isSaved || state.coverage !== null ? header : (state.draft?.header ?? null);

  return (
    <main className="screen" aria-labelledby={titleId}>
      <h1 className="screen-title" id={titleId}>More Details</h1>
      {invNo != null && (
        <div className="msg" role="status">
          {loadStatusText(loadStatus, invNo)}
        </div>
      )}

      <div className="more-grid" aria-busy={isSaved && loadStatus === 'pending' ? true : undefined}>
        <section aria-labelledby={insuranceTitleId}>
          <div className="panel-title" role="heading" aria-level={2} id={insuranceTitleId}>
            Insurance
          </div>
          <ReadOnlyField
            id={`${baseId}-INS_NUMBER`}
            label="Insurance Number"
            value={displayText(insurance?.insNumber)}
            freeText
          />
          <ReadOnlyField id={`${baseId}-CARD_END`} label="Card Expire Date" value={dateText(insurance?.cardEnd)} />
          <ReadOnlyField
            id={`${baseId}-PAT_POLICY_NO`}
            label="Policy No"
            value={displayText(insurance?.patPolicyNo)}
            freeText
          />
        </section>

        <section className="more-current-line" aria-labelledby={lineTitleId}>
          <div className="panel-title" role="heading" aria-level={2} id={lineTitleId}>
            {serviceId === '' ? 'Current Line' : `Current Line · Service ${serviceId}`}
          </div>
          {lineGroups.length > 0 && (
            // The current line's messages on fields MORE does not show, one captioned group per field.
            <div role="group" aria-label="Current line messages">
              {lineGroups.map((group) =>
                group.caption === null ? (
                  <FieldMessage
                    key={group.id}
                    id={`${group.id}-msg`}
                    messages={group.messages.map((entry) => entry.message)}
                    fieldError={group.fieldError}
                    onDismiss={(messageIndex) => dismissLineMessage(group, messageIndex)}
                  />
                ) : (
                  <div key={group.id} id={group.id} className="field">
                    <span className="field-caption">{group.caption}</span>
                    <FieldMessage
                      id={`${group.id}-msg`}
                      messages={group.messages.map((entry) => entry.message)}
                      fieldError={group.fieldError}
                      onDismiss={(messageIndex) => dismissLineMessage(group, messageIndex)}
                    />
                  </div>
                ),
              )}
            </div>
          )}
          {EDITABLE_FIELDS.map(renderEditable)}
          {STATUS_FIELDS.map((field) => (
            <ReadOnlyField
              key={field.item}
              id={`${baseId}-${field.item}`}
              label={field.label}
              value={listLabel(readOnlyValue(field), field.labels)}
            />
          ))}
          {NOT_SAVED_FIELDS.map((field) => (
            <ReadOnlyField
              key={field.item}
              id={`${baseId}-${field.item}`}
              label={field.label}
              value={displayText(readOnlyValue(field))}
              freeText={field.freeText}
            />
          ))}
          <OpenItemNotice ids={['OI-33']} />
        </section>

        <section aria-labelledby={transfersTitleId}>
          <div className="panel-title" role="heading" aria-level={2} id={transfersTitleId}>
            Store Transfers
          </div>
          <div className="field">
            <label id={transferLabelId} htmlFor={`${transferLabelId}-0`}>
              Trans M Row Id
            </label>
            {(transfers.length === 0 ? [''] : transfers).map((rowId, rowIndex) => (
              <input
                key={`${rowIndex}-${rowId}`}
                id={`${transferLabelId}-${rowIndex}`}
                type="text"
                className="read-only"
                value={rowId}
                aria-labelledby={transferLabelId}
                readOnly
                {...readOnlyTextProps(rowId)}
              />
            ))}
          </div>
        </section>
      </div>

      <OpenItemNotice
        ids={distinctOpenItems(state.openItems, isSaved ? ['OI-56'] : [])}
        serverMessages={state.openItemMessages}
      />

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
            onClick={dismissFormError}
          >
            ×
          </button>
        </div>
      )}

      <div ref={actionsRef} className="action-bar" role="group" aria-label="More details actions">
        {isSaved && (
          <button type="button" onClick={addStoreTrans}>
            Add Store Trans
          </button>
        )}
        <button type="button" onClick={onBack}>
          Return
        </button>
      </div>
    </main>
  );
}
