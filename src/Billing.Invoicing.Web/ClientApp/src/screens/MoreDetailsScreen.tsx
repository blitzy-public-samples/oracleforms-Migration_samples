import { useEffect, useId, useRef } from 'react';
import type { ChangeEvent, Dispatch, FocusEvent } from 'react';
import { ApiError, getMoreDetails, transferStock, validateDraft } from '../api/client';
import type { InvoiceLineDraft, MessageDto, MoreDetailsLineKey } from '../api/types';
import type { InvoiceDraftAction, InvoiceDraftState } from '../state/invoiceDraft';
import FieldMessage from '../components/FieldMessage';
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
}

const APPROV_REF_NO = 'APPROV_REF_NO';

const EDITABLE_FIELDS: readonly EditableLineField[] = [
  { key: 'teethNo', item: 'TEETH_NO', label: 'Teeth No', kind: 'text', maxLength: 2 },
  { key: 'toothSurface', item: 'TOOTH_SURFACE', label: 'Tooth Surface', kind: 'text', maxLength: 7 },
  { key: 'approvDate', item: 'APPROV_DATE', label: 'Approval Date', kind: 'date' },
  { key: 'approvValidity', item: 'APPROV_VALIDITY', label: 'Approval Validity', kind: 'number' },
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
  { item: 'REGULAR_LENSES_TYPE', label: 'Regular Lenses Type', draftKey: 'regularLensesType' },
  { item: 'LENS_SPECIFICATIONS', label: 'Lens Specifications', draftKey: 'lensSpecifications' },
  { item: 'CONTACT_LENSES_TYPE', label: 'Contact Lenses Type', draftKey: 'contactLensesType' },
  { item: 'F_L_INDICATOR', label: 'F L Indicator', draftKey: 'flIndicator' },
  { item: 'NUMBER_OF_PAIRS', label: 'Number Of Pairs', draftKey: 'numberOfPairs' },
  { item: 'INS_EMP', label: 'Insurance Emp', draftKey: 'insEmp' },
  { item: 'INS_EMP_NAME', label: 'Insurance Employe', draftKey: null },
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

/** The `yyyy-MM-dd` part of an ISO date-time text, or the text unchanged. */
function datePart(value: unknown): string {
  const text = displayText(value);
  return /^\d{4}-\d{2}-\d{2}/.test(text) ? text.slice(0, 10) : text;
}

/** The list-element label of a list item value, or the value itself. */
function listLabel(value: unknown, labels: Readonly<Record<string, string>> | undefined): string {
  const text = displayText(value);
  return labels !== undefined && Object.hasOwn(labels, text) ? labels[text] : text;
}

/** Input value text for an editable line field. */
function inputText(field: EditableLineField, value: unknown): string {
  return field.kind === 'date' ? datePart(value) : displayText(value);
}

/** Draft value for an editable line field from its input text; empty text is null. */
function parseInput(field: EditableLineField, text: string): string | number | null {
  if (text === '') {
    return null;
  }
  return field.kind === 'number' ? Number(text) : text;
}

/** Element at `index`, or undefined when the index is outside the list. */
function itemAt<T>(items: readonly T[] | undefined, index: number): T | undefined {
  return items !== undefined && Number.isInteger(index) && index >= 0 && index < items.length ? items[index] : undefined;
}

/** Reducer source key of a line validation target. */
function lineKey(lineIndex: number, target: string): string {
  return `LINE:${lineIndex}:${target}`;
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

/** Oracle error number in `ORA-nnnnn` form. */
function oraText(oracleErrorNumber: number): string {
  return `ORA-${String(Math.abs(oracleErrorNumber)).padStart(5, '0')}`;
}

/** One labelled read-only text field. */
function ReadOnlyField({ id, label, value }: { id: string; label: string; value: string }) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} type="text" className="read-only" value={value} readOnly />
    </div>
  );
}

/** MORE canvas: insurance, dental, approval and lens details of the current line, and store transfers. */
export default function MoreDetailsScreen({
  state,
  dispatch,
  onBack,
}: {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onBack: () => void;
}) {
  const latest = useRef(state);
  latest.current = state;
  const requestedInvNo = useRef<number | null>(null);
  const refNoOnFocus = useRef('');
  const baseId = useId();

  const invNo = state.saved?.invNo;
  const isSaved = invNo != null;
  const editable = !isSaved && !state.readOnly;
  const index = state.currentLineIndex;
  const loadedInvNo = state.moreDetails?.invNo;
  const details = isSaved && state.moreDetails !== null && state.moreDetails.invNo === invNo ? state.moreDetails : null;

  // Loads the persisted MORE fields once per saved invoice number.
  useEffect(() => {
    if (invNo == null) {
      requestedInvNo.current = null;
      return;
    }
    if (loadedInvNo === invNo || requestedInvNo.current === invNo) {
      return;
    }
    requestedInvNo.current = invNo;
    getMoreDetails(invNo).then(
      (response) => dispatch({ type: 'moreDetailsLoaded', response }),
      (error: unknown) => dispatch({ type: 'errorReceived', source: 'SAVED', error: toApiError(error) }),
    );
  }, [invNo, loadedInvNo, dispatch]);

  const header = isSaved ? details : (state.coverage?.coverage ?? null);
  const draftLine = isSaved ? undefined : itemAt(state.draft?.lines, index);
  const savedLine = isSaved ? itemAt(details?.lines, index) : undefined;
  const lineEditable = editable && draftLine !== undefined;
  const serviceId = displayText(isSaved ? savedLine?.SERVICEID : draftLine?.serviceId);
  const transfers = details?.transMRowIds ?? [];

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

  /** Writes an edited line field into the current draft line. */
  function changeLineField(field: EditableLineField, event: ChangeEvent<HTMLInputElement>) {
    dispatch({ type: 'lineFieldChanged', index, field: field.key, value: parseInput(field, event.target.value) });
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
    validateDraft({ draft, target: APPROV_REF_NO, lineIndex }).then(
      (response) => dispatch({ type: 'validationApplied', target: APPROV_REF_NO, lineIndex, response }),
      (error: unknown) => {
        const apiError = toApiError(error);
        if (apiError.status === 422 && apiError.type === 'field-validation') {
          dispatch({ type: 'validationFailed', target: APPROV_REF_NO, lineIndex, error: apiError });
        } else {
          dispatch({ type: 'errorReceived', source: lineKey(lineIndex, APPROV_REF_NO), error: apiError });
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

  /** One editable line item with its messages. */
  function renderEditable(field: EditableLineField) {
    const id = `${baseId}-${field.item}`;
    const source = lineKey(index, field.item);
    const messages: MessageDto[] = state.messages[source] ?? [];
    const fieldError = state.fieldErrors[field.item] ?? null;
    const invalid = fieldError !== null || messages.some((message) => message.severity === 'Blocking');
    const isRefNo = field.item === APPROV_REF_NO;
    const value = isSaved ? savedLine?.[field.item] : draftLine?.[field.key];
    return (
      <div className="field" key={field.item}>
        <label htmlFor={id}>{field.label}</label>
        <input
          id={id}
          type={field.kind}
          className={lineEditable ? (invalid ? 'invalid' : undefined) : 'read-only'}
          value={inputText(field, value)}
          maxLength={field.maxLength}
          readOnly={!lineEditable}
          aria-invalid={lineEditable && invalid ? true : undefined}
          onChange={lineEditable ? (event) => changeLineField(field, event) : undefined}
          onFocus={lineEditable && isRefNo ? focusRefNo : undefined}
          onBlur={lineEditable && isRefNo ? blurRefNo : undefined}
        />
        <FieldMessage
          messages={messages}
          fieldError={fieldError}
          onDismiss={(messageIndex) => dispatch({ type: 'messageDismissed', source, index: messageIndex })}
        />
      </div>
    );
  }

  return (
    <div className="screen">
      <h1 className="screen-title">More Details</h1>

      <div className="more-grid">
        <section aria-labelledby={insuranceTitleId}>
          <div className="panel-title" role="heading" aria-level={2} id={insuranceTitleId}>
            Insurance
          </div>
          <ReadOnlyField id={`${baseId}-INS_NUMBER`} label="Insurance Number" value={displayText(header?.insNumber)} />
          <ReadOnlyField id={`${baseId}-CARD_END`} label="Card Expire Date" value={datePart(header?.cardEnd)} />
          <ReadOnlyField id={`${baseId}-PAT_POLICY_NO`} label="Policy No" value={displayText(header?.patPolicyNo)} />
        </section>

        <section aria-labelledby={lineTitleId}>
          <div className="panel-title" role="heading" aria-level={2} id={lineTitleId}>
            {serviceId === '' ? 'Current Line' : `Current Line · Service ${serviceId}`}
          </div>
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
              />
            ))}
          </div>
        </section>
      </div>

      <OpenItemNotice
        ids={distinctOpenItems(state.openItems, isSaved ? ['OI-56'] : [])}
        serverMessages={state.openItemMessages}
      />

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

      <div className="action-bar">
        {isSaved && (
          <button type="button" onClick={addStoreTrans}>
            Add Store Trans
          </button>
        )}
        <button type="button" onClick={onBack}>
          Return
        </button>
      </div>
    </div>
  );
}

