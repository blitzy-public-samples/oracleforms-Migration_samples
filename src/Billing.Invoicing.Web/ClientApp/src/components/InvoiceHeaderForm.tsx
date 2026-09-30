import { useEffect, useId, useRef, useState } from 'react';
import type { ChangeEvent, Dispatch, FocusEvent } from 'react';
import { ApiError, getCurrencies, getInvoiceTypes } from '../api/client';
import type { InvoiceHeaderDraft, LookupItem, MessageDto, ValidateTarget } from '../api/types';
import type { InvoiceDraftAction, InvoiceDraftState } from '../state/invoiceDraft';
import LovPicker from './LovPicker';
import type { LovBinds, LovName } from './LovPicker';
import FieldMessage from './FieldMessage';
import OpenItemNotice from './OpenItemNotice';

type InvoiceHeaderFormProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onValidate: (target: ValidateTarget) => void;
  onPatientChanged: () => void;
  onShowReservations: () => void;
};

/** Header items this form validates or shows messages for. */
type HeaderTarget = Extract<ValidateTarget, 'PATIENTNO' | 'COMP_CODE' | 'DOCIDX' | 'CLINICID' | 'DEPT_WISE' | 'CALL'>;

/** LOVs this form opens. */
type HeaderLov = Extract<LovName, 'COMPANY1_2' | 'SUB_COMPANY' | 'THE_CLASS' | 'DOC'>;

type LovRow = Record<string, unknown>;

/** A queued validation target; `seq` increases with every queued target and each is fired once. */
type PendingValidation = { seq: number; target: HeaderTarget };

type MessageRef = { source: string; index: number };

type TargetMessages = {
  messages: MessageDto[];
  refs: MessageRef[][];
  fieldError: { text: string; oracleErrorNumber: number | null } | null;
  invalid: boolean;
};

/** Message sources rendered at form level by the screen. */
const SCREEN_LEVEL_SOURCES: ReadonlySet<string> = new Set(['IMPORT']);

/** Items of the Form that are not saved in this build. */
const NOT_SAVED_IDS: string[] = ['OI-33'];

const PATIENT_NO_MAX_LENGTH = 12;
const NOTE_NO_MAX_LENGTH = 40;

const ISO_DATE_TIME = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/;

/** A row or display value for an upper-case key, matching the key case-insensitively. */
function lookupValue(record: Record<string, unknown>, key: string): unknown {
  if (Object.hasOwn(record, key)) {
    return record[key];
  }
  const match = Object.keys(record).find((candidate) => candidate.toUpperCase() === key);
  return match === undefined ? undefined : record[match];
}

/** Text of a scalar value; '' for null, undefined and non-scalars. */
function shown(value: unknown): string {
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
    return String(value);
  }
  return '';
}

/** A scalar value as text, or null when it is empty. */
function toText(value: unknown): string | null {
  const text = shown(value);
  return text === '' ? null : text;
}

/** A scalar value as a finite number, or null. */
function toNumber(value: unknown): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  const text = shown(value).trim();
  if (text === '') {
    return null;
  }
  const parsed = Number(text);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Entered text as a draft value: null when empty, otherwise as typed. */
function enteredText(text: string): string | null {
  return text === '' ? null : text;
}

/** Splits an ISO `yyyy-mm-ddThh:mm:ss` value into `dd/mm/yyyy` and `hh:mm:ss` without time-zone conversion. */
function splitIsoDateTime(value: string | null | undefined): { date: string; time: string } {
  if (value == null || value === '') {
    return { date: '', time: '' };
  }
  const match = ISO_DATE_TIME.exec(value);
  if (match === null) {
    return { date: value, time: '' };
  }
  const [, year, month, day, hours, minutes, seconds] = match;
  return {
    date: `${day}/${month}/${year}`,
    time: hours === undefined ? '' : `${hours}:${minutes}:${seconds ?? '00'}`,
  };
}

/** Pay type label: 1 cash, 2 credit, otherwise the raw value. */
function payTypeText(payType: number | null | undefined): string {
  if (payType === 1) {
    return '1 - Cash';
  }
  if (payType === 2) {
    return '2 - Credit';
  }
  return shown(payType);
}

/** Select options for a lookup list, keeping a current value the list does not hold. */
function selectOptions(items: readonly LookupItem[], current: string): LookupItem[] {
  if (current === '') {
    return [{ code: '', name: '' }, ...items];
  }
  return items.some((item) => item.code === current) ? [...items] : [{ code: current, name: current }, ...items];
}

/** Space-separated class names of the entry flags that apply. */
function classNames(entries: Record<string, boolean>): string | undefined {
  const names = Object.keys(entries).filter((name) => entries[name]);
  return names.length === 0 ? undefined : names.join(' ');
}

/** Messages whose field is the target, from every header-level source, each distinct text once, plus its Oracle field error. */
function targetMessages(state: InvoiceDraftState, target: HeaderTarget): TargetMessages {
  const messages: MessageDto[] = [];
  const refs: MessageRef[][] = [];
  const positions = new Map<string, number>();
  const sources = [target, ...Object.keys(state.messages).filter((source) => source !== target)];
  for (const source of sources) {
    if (source.startsWith('LINE:') || SCREEN_LEVEL_SOURCES.has(source)) {
      continue;
    }
    const list = state.messages[source] ?? [];
    list.forEach((message, index) => {
      if (message.field == null || message.field.toUpperCase() !== target) {
        return;
      }
      const key = `${message.severity}\u0000${message.text}`;
      const position = positions.get(key);
      if (position === undefined) {
        positions.set(key, messages.length);
        messages.push(message);
        refs.push([{ source, index }]);
      } else {
        refs[position].push({ source, index });
      }
    });
  }
  const error = state.fieldErrors[target];
  const fieldError = error === undefined ? null : { text: error.text, oracleErrorNumber: error.oracleErrorNumber };
  return {
    messages,
    refs,
    fieldError,
    invalid: fieldError !== null || messages.some((message) => message.severity === 'Blocking'),
  };
}

/** Dismiss references ordered so that each index stays valid while earlier ones are removed. */
function dismissOrder(refs: readonly MessageRef[]): MessageRef[] {
  return [...refs].sort((a, b) => (a.source === b.source ? b.index - a.index : a.source < b.source ? -1 : 1));
}

/** Bind values of a header LOV, taken from the current record. */
function lovBinds(name: HeaderLov, header: InvoiceHeaderDraft | null): LovBinds {
  switch (name) {
    case 'SUB_COMPANY':
      return { compCode: header?.compCode ?? null };
    case 'THE_CLASS':
      return { subCompCode: header?.subCompCode ?? null };
    case 'COMPANY1_2':
    case 'DOC':
      return {};
  }
}

/** Header fields of the invoice: patient, payer, doctor, clinic and entry flags. */
export default function InvoiceHeaderForm({
  state,
  dispatch,
  onValidate,
  onPatientChanged,
  onShowReservations,
}: InvoiceHeaderFormProps) {
  const id = useId();
  const [invoiceTypes, setInvoiceTypes] = useState<LookupItem[]>([]);
  const [currencies, setCurrencies] = useState<LookupItem[]>([]);
  const [lov, setLov] = useState<HeaderLov | null>(null);
  const [pending, setPending] = useState<PendingValidation[]>([]);
  const nextSeq = useRef(0);
  const lastFiredSeq = useRef(0);
  const patientOnFocus = useRef<{ value: string | null } | null>(null);

  useEffect(() => {
    let cancelled = false;
    const fail = (source: string) => (reason: unknown) => {
      if (cancelled) {
        return;
      }
      const error =
        reason instanceof ApiError
          ? reason
          : new ApiError({
              status: 0,
              type: 'client-error',
              title: 'Lookup failed',
              message: reason instanceof Error ? reason.message : String(reason),
            });
      dispatch({ type: 'errorReceived', source, error });
    };
    getInvoiceTypes().then((items) => {
      if (!cancelled) {
        setInvoiceTypes(Array.isArray(items) ? items : []);
      }
    }, fail('INVTYPEID'));
    getCurrencies().then((items) => {
      if (!cancelled) {
        setCurrencies(Array.isArray(items) ? items : []);
      }
    }, fail('CURR_CODE'));
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const fresh = pending.filter((entry) => entry.seq > lastFiredSeq.current);
    if (fresh.length === 0) {
      return;
    }
    const last = fresh[fresh.length - 1].seq;
    lastFiredSeq.current = last;
    setPending((current) => current.filter((entry) => entry.seq > last));
    for (const entry of fresh) {
      onValidate(entry.target);
    }
  }, [pending, onValidate]);

  const savedView = state.saved?.view ?? null;
  const header: InvoiceHeaderDraft | null = savedView?.header ?? state.draft?.header ?? null;
  const names: Record<string, unknown> = savedView !== null ? savedView.display : state.display;
  const locked = state.readOnly || state.draft === null;

  const nameOf = (key: string): string => shown(lookupValue(names, key));

  const snapshot = savedView === null ? (state.coverage?.coverage ?? null) : null;
  const snapshotName =
    snapshot !== null && (snapshot.patientNo == null || snapshot.patientNo === header?.patientNo)
      ? shown(snapshot.patientName)
      : '';
  const patientName = nameOf('PATIENTNAME') || snapshotName;

  const dateSource = savedView !== null ? (savedView.header.invDate ?? savedView.header.draftDate) : state.draft?.draftDate;
  const { date: invoiceDate, time: invoiceTime } = splitIsoDateTime(dateSource);
  const invNo = header?.invNo ?? state.saved?.invNo ?? null;

  const invTypeValue = shown(header?.invTypeId);
  const currencyValue = shown(header?.currCode);

  const setField = <K extends keyof InvoiceHeaderDraft>(field: K, value: InvoiceHeaderDraft[K]): void => {
    if (!locked) {
      dispatch({ type: 'headerFieldChanged', field, value });
    }
  };

  const setNames = (values: Record<string, string | null>): void => {
    if (!locked) {
      dispatch({ type: 'displaySet', values });
    }
  };

  const queue = (...targets: HeaderTarget[]): void => {
    const entries = targets.map((target) => {
      nextSeq.current += 1;
      return { seq: nextSeq.current, target };
    });
    setPending((current) => [...current, ...entries]);
  };

  const pick = (name: HeaderLov, row: LovRow): void => {
    if (locked) {
      return;
    }
    switch (name) {
      case 'COMPANY1_2':
        setField('compCode', toText(lookupValue(row, 'COMP_CODE')));
        setField('currCode', toText(lookupValue(row, 'CURR_CODE')));
        setNames({ COMP_NAME: toText(lookupValue(row, 'COMP_NAME')) });
        queue('COMP_CODE');
        break;
      case 'SUB_COMPANY':
        setField('subCompCode', toText(lookupValue(row, 'COMP_CODE')));
        setNames({ SUB_COMP_NAME: toText(lookupValue(row, 'COMP_NAME')) });
        break;
      case 'THE_CLASS':
        setField('classCode', toNumber(lookupValue(row, 'CLASS_CODE')));
        setNames({ CLASS_NAME: toText(lookupValue(row, 'CLASS_NAME')) });
        break;
      case 'DOC':
        setField('docId', toNumber(lookupValue(row, 'DOCID')));
        setNames({ DOC_NAME: toText(lookupValue(row, 'DOC_NAME')) });
        setField('clinicId', toNumber(lookupValue(row, 'CLINICID')));
        setNames({ CLINICNAME: toText(lookupValue(row, 'CLINICNAME')) });
        queue('DOCIDX', 'CLINICID');
        break;
    }
  };

  const onPatientFocus = (event: FocusEvent<HTMLInputElement>): void => {
    patientOnFocus.current = { value: enteredText(event.currentTarget.value) };
  };

  const onPatientBlur = (event: FocusEvent<HTMLInputElement>): void => {
    const before = patientOnFocus.current;
    patientOnFocus.current = null;
    if (!locked && before !== null && enteredText(event.currentTarget.value) !== before.value) {
      onPatientChanged();
    }
  };

  const onFlagChange =
    (field: 'deptWise' | 'call', target: HeaderTarget) =>
    (event: ChangeEvent<HTMLInputElement>): void => {
      if (locked) {
        return;
      }
      setField(field, event.currentTarget.checked ? 1 : 0);
      queue(target);
    };

  const dismiss = (refs: readonly MessageRef[]): void => {
    for (const ref of dismissOrder(refs)) {
      dispatch({ type: 'messageDismissed', source: ref.source, index: ref.index });
    }
  };

  const renderMessages = (entry: TargetMessages) => (
    <FieldMessage
      messages={entry.messages}
      fieldError={entry.fieldError}
      onDismiss={(index) => dismiss(entry.refs[index] ?? [])}
    />
  );

  const patientMessages = targetMessages(state, 'PATIENTNO');
  const companyMessages = targetMessages(state, 'COMP_CODE');
  const doctorMessages = targetMessages(state, 'DOCIDX');
  const clinicMessages = targetMessages(state, 'CLINICID');
  const deptWiseMessages = targetMessages(state, 'DEPT_WISE');
  const callMessages = targetMessages(state, 'CALL');

  const inputClass = (invalid = false): string | undefined => classNames({ 'read-only': locked, invalid });
  const fixedClass = (invalid = false): string | undefined => classNames({ 'read-only': true, invalid });
  // Not-saved items are disabled on a draft and read-only on a saved invoice.
  const notSavedDisabled = savedView === null;

  const renderPickerButton = (name: HeaderLov, label: string) => (
    <button type="button" aria-label={label} disabled={locked} onClick={() => setLov(name)}>
      …
    </button>
  );

  const renderFlag = (
    field: 'deptWise' | 'call',
    target: HeaderTarget,
    label: string,
    entry: TargetMessages,
    inputId: string,
  ) => (
    <div className="field">
      <label htmlFor={inputId}>{label}</label>
      <div className="check-field">
        <input
          id={inputId}
          type="checkbox"
          checked={header?.[field] === 1}
          disabled={locked}
          className={entry.invalid ? 'invalid' : undefined}
          aria-invalid={entry.invalid || undefined}
          onChange={onFlagChange(field, target)}
        />
      </div>
      {renderMessages(entry)}
      <OpenItemNotice ids={state.openItems[target] ?? []} serverMessages={state.openItemMessages} />
    </div>
  );

  return (
    <>
      <div className="header-grid">
        <div className="header-col">
          <div className="field">
            <label htmlFor={`${id}-invtype`}>Type</label>
            <select
              id={`${id}-invtype`}
              value={invTypeValue}
              disabled={locked}
              className={inputClass()}
              onChange={(event) => setField('invTypeId', toNumber(event.currentTarget.value))}
            >
              {selectOptions(invoiceTypes, invTypeValue).map((item, index) => (
                <option key={`${index}-${item.code}`} value={item.code}>
                  {item.name}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor={`${id}-invno`}>Invoice No</label>
            <div className="field-row">
              <input id={`${id}-invno`} type="text" readOnly className="read-only" value={shown(invNo)} />
              <input type="text" readOnly aria-label="Invoice date" className="read-only" value={invoiceDate} />
              <label htmlFor={`${id}-invtime`}>Time</label>
              <input id={`${id}-invtime`} type="text" readOnly className="read-only" value={invoiceTime} />
            </div>
          </div>

          <div className="field">
            <label htmlFor={`${id}-patientno`}>MR#</label>
            <div className="field-row">
              <input
                id={`${id}-patientno`}
                type="text"
                autoComplete="off"
                maxLength={PATIENT_NO_MAX_LENGTH}
                readOnly={locked}
                className={inputClass(patientMessages.invalid)}
                aria-invalid={patientMessages.invalid || undefined}
                value={header?.patientNo ?? ''}
                onFocus={onPatientFocus}
                onBlur={onPatientBlur}
                onChange={(event) => setField('patientNo', enteredText(event.currentTarget.value))}
              />
              <input type="text" readOnly aria-label="Patient name" className="read-only" value={patientName} />
            </div>
            {renderMessages(patientMessages)}
          </div>

          <div className="field">
            <label htmlFor={`${id}-compcode`}>Ins. Company</label>
            <div className="field-row">
              <input
                id={`${id}-compcode`}
                type="text"
                readOnly
                className={fixedClass(companyMessages.invalid)}
                aria-invalid={companyMessages.invalid || undefined}
                value={shown(header?.compCode)}
              />
              <input type="text" readOnly aria-label="Company name" className="read-only" value={nameOf('COMP_NAME')} />
              {renderPickerButton('COMPANY1_2', 'Choose company')}
            </div>
            {renderMessages(companyMessages)}
          </div>

          <div className="field">
            <label htmlFor={`${id}-subcompcode`}>Sub. Company</label>
            <div className="field-row">
              <input
                id={`${id}-subcompcode`}
                type="text"
                readOnly
                className="read-only"
                value={shown(header?.subCompCode)}
              />
              <input
                type="text"
                readOnly
                aria-label="Sub company name"
                className="read-only"
                value={nameOf('SUB_COMP_NAME')}
              />
              {renderPickerButton('SUB_COMPANY', 'Choose sub company')}
            </div>
          </div>

          <div className="field">
            <label htmlFor={`${id}-classcode`}>Class</label>
            <div className="field-row">
              <input id={`${id}-classcode`} type="text" readOnly className="read-only" value={shown(header?.classCode)} />
              <input type="text" readOnly aria-label="Class name" className="read-only" value={nameOf('CLASS_NAME')} />
              {renderPickerButton('THE_CLASS', 'Choose class')}
            </div>
          </div>

          <div className="field">
            <label htmlFor={`${id}-noteno`}>Note</label>
            <input
              id={`${id}-noteno`}
              type="text"
              autoComplete="off"
              maxLength={NOTE_NO_MAX_LENGTH}
              readOnly={locked}
              className={inputClass()}
              value={header?.noteNo ?? ''}
              onChange={(event) => setField('noteNo', enteredText(event.currentTarget.value))}
            />
          </div>
        </div>

        <div className="header-col">
          <div className="field">
            <label htmlFor={`${id}-claimno`}>Claim No</label>
            <input id={`${id}-claimno`} type="text" readOnly className="read-only" value={shown(header?.claimNo)} />
          </div>

          <div className="field">
            <label htmlFor={`${id}-paytype`}>Paytype</label>
            <input
              id={`${id}-paytype`}
              type="text"
              readOnly
              className="read-only"
              value={payTypeText(header?.payType)}
            />
          </div>

          <div className="field">
            <label htmlFor={`${id}-currcode`}>Currency</label>
            <select
              id={`${id}-currcode`}
              value={currencyValue}
              disabled={locked}
              className={inputClass()}
              onChange={(event) => setField('currCode', enteredText(event.currentTarget.value))}
            >
              {selectOptions(currencies, currencyValue).map((item, index) => (
                <option key={`${index}-${item.code}`} value={item.code}>
                  {item.name}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label htmlFor={`${id}-docidx`}>Doc.ID</label>
            <div className="field-row">
              <input
                id={`${id}-docidx`}
                type="text"
                readOnly
                className={fixedClass(doctorMessages.invalid)}
                aria-invalid={doctorMessages.invalid || undefined}
                value={shown(header?.docId)}
              />
              <input type="text" readOnly aria-label="Doctor name" className="read-only" value={nameOf('DOC_NAME')} />
              {renderPickerButton('DOC', 'Choose doctor')}
            </div>
            {renderMessages(doctorMessages)}
          </div>

          <div className="field">
            <label htmlFor={`${id}-clinicid`}>Clinicid</label>
            <div className="field-row">
              <input
                id={`${id}-clinicid`}
                type="text"
                readOnly
                className={fixedClass(clinicMessages.invalid)}
                aria-invalid={clinicMessages.invalid || undefined}
                value={shown(header?.clinicId)}
              />
              <input type="text" readOnly aria-label="Clinic name" className="read-only" value={nameOf('CLINICNAME')} />
            </div>
            {renderMessages(clinicMessages)}
          </div>

          <div className="field">
            <label htmlFor={`${id}-docid1`}>Trans.By</label>
            <div className="field-row">
              <input
                id={`${id}-docid1`}
                type="text"
                readOnly
                disabled={notSavedDisabled}
                className="read-only"
                value={shown(header?.docId1)}
              />
              <input
                type="text"
                readOnly
                disabled={notSavedDisabled}
                aria-label="Transferring doctor name"
                className="read-only"
                value={nameOf('DOC_NAME1')}
              />
            </div>
            <OpenItemNotice ids={NOT_SAVED_IDS} />
          </div>

          <div className="field">
            <label htmlFor={`${id}-oferid`}>Discount Package</label>
            <div className="field-row">
              <input
                id={`${id}-oferid`}
                type="text"
                readOnly
                disabled={notSavedDisabled}
                className="read-only"
                value={shown(header?.oferId)}
              />
              <input
                type="text"
                readOnly
                disabled={notSavedDisabled}
                aria-label="Discount package name"
                className="read-only"
                value={nameOf('OFFER_NAME')}
              />
            </div>
            <OpenItemNotice ids={NOT_SAVED_IDS} />
          </div>

          {renderFlag('deptWise', 'DEPT_WISE', 'ER/ Dept Wise', deptWiseMessages, `${id}-deptwise`)}
          {renderFlag('call', 'CALL', 'Call', callMessages, `${id}-call`)}

          <div className="field">
            <label htmlFor={`${id}-seqno`}>Seq No</label>
            <div className="field-row">
              <input
                id={`${id}-seqno`}
                type="text"
                readOnly
                disabled={notSavedDisabled}
                className="read-only"
                value={shown(header?.seqNo)}
              />
              <button type="button" disabled={locked} onClick={onShowReservations}>
                Show reservations
              </button>
            </div>
            <OpenItemNotice ids={NOT_SAVED_IDS} />
          </div>

          <div className="field">
            <label htmlFor={`${id}-reservtime`}>Reserv Time</label>
            <input
              id={`${id}-reservtime`}
              type="text"
              readOnly
              disabled={notSavedDisabled}
              className="read-only"
              value={nameOf('RESERV_THE_TIME')}
            />
            <OpenItemNotice ids={NOT_SAVED_IDS} />
          </div>
        </div>
      </div>

      {lov !== null && !locked && (
        <LovPicker
          name={lov}
          binds={lovBinds(lov, header)}
          onPick={(row) => pick(lov, row)}
          onClose={() => setLov(null)}
        />
      )}
    </>
  );
}

