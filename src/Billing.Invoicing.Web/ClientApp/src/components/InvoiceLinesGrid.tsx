import { useEffect, useMemo, useRef, useState, type Dispatch } from 'react';
import type { EditablePreviewLine, InvoiceLineDraft, MessageDto, PreviewResponse, ValidateTarget } from '../api/types';
import { entryErrorFor, fieldErrorFor, parseDecimalEntry } from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState } from '../state/invoiceDraft';
import FieldMessage from './FieldMessage';
import LovPicker from './LovPicker';
import OpenItemNotice from './OpenItemNotice';

type LineTarget = Extract<ValidateTarget, 'SERVICEID' | 'QTY' | 'PRICE' | 'LDISCT' | 'DISC' | 'MY_DISC'>;

type LineDisplay = Record<string, unknown>;

type LovRow = Record<string, unknown>;

type PendingValidation = { index: number; target: LineTarget };

type CatPickerTarget = { index: number; clientId: string | null };

type CellStatus = {
  target: LineTarget;
  source: string;
  messages: MessageDto[];
  fieldError: { text: string; oracleErrorNumber: number | null } | null;
  rejected: boolean;
  invalid: boolean;
};

type InvoiceLinesGridProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onValidateLine: (index: number, target: ValidateTarget) => void;
  onRemoveLine: (index: number) => void;
};

/** Column size classes in grid order: CATID, XCAT_NAMEX, SERVICEID … FIXPAY, PAYRATE, actions. */
const COLUMN_CLASSES: readonly string[] = [
  'lines-col-code',
  'lines-col-name',
  'lines-col-code',
  'lines-col-desc',
  'lines-col-qty',
  'lines-col-price',
  'lines-col-choice',
  'lines-col-disc',
  'lines-col-disc',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-rate',
  'lines-col-rate',
  'lines-col-action',
];

/** LDISCT radio values and labels. */
const DISCOUNT_TYPES: readonly { value: string; label: string }[] = [
  { value: 'R', label: 'Rate' },
  { value: 'V', label: 'Value' },
];

const NOT_SAVED_OPEN_ITEMS: string[] = ['OI-33'];

/** Line validation targets in grid column order. */
const LINE_TARGETS: readonly LineTarget[] = ['SERVICEID', 'QTY', 'PRICE', 'LDISCT', 'DISC', 'MY_DISC'];

/** Text of a displayed value: empty for null or undefined, else the value as returned. */
function displayText(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}

/** Value of an upper-case key in a record, matching the key case-insensitively. */
function lookup(record: LineDisplay | null | undefined, key: string): unknown {
  if (record === null || record === undefined) {
    return undefined;
  }
  if (Object.hasOwn(record, key)) {
    return record[key];
  }
  const match = Object.keys(record).find((candidate) => candidate.toUpperCase() === key);
  return match === undefined ? undefined : record[match];
}

/** Parses an LOV cell into a number, or null when it is empty or not numeric. */
function toNumberOrNull(value: unknown): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value !== 'string' || value.trim() === '') {
    return null;
  }
  const parsed = Number(value.trim());
  return Number.isFinite(parsed) ? parsed : null;
}

/** True when a text field holds no value. */
function isBlank(value: string | null | undefined): boolean {
  return value === null || value === undefined || value.trim() === '';
}

/** Trimmed text, with null and blank both read as the empty string. */
function trimmed(value: string | null | undefined): string {
  return value?.trim() ?? '';
}

/** True when the server has judged PRICE editable on the line's current service, patient and company. */
function serverAllowsPrice(state: InvoiceDraftState, line: InvoiceLineDraft): boolean {
  const clientId = line.clientId;
  if (clientId === null || clientId === undefined || clientId === '' || !Object.hasOwn(state.priceEditable, clientId)) {
    return false;
  }
  const entry = state.priceEditable[clientId];
  const header = state.draft?.header;
  const judgedServiceId = trimmed(entry.serviceId).toUpperCase();
  return (
    entry.editable &&
    judgedServiceId !== '' &&
    judgedServiceId === trimmed(line.serviceId).toUpperCase() &&
    trimmed(entry.patientNo) === trimmed(header?.patientNo) &&
    trimmed(entry.compCode) === trimmed(header?.compCode)
  );
}

/** Preview lines by client id, keeping the first line of each non-empty client id. */
function previewLinesByClientId(preview: PreviewResponse | null): Map<string, EditablePreviewLine> {
  const byClientId = new Map<string, EditablePreviewLine>();
  for (const previewLine of preview?.lines ?? []) {
    const clientId = previewLine.clientId;
    if (clientId !== null && clientId !== undefined && clientId !== '' && !byClientId.has(clientId)) {
      byClientId.set(clientId, previewLine);
    }
  }
  return byClientId;
}

/** Display values of a saved view's line, taken from LINE_DISPLAY aligned with the lines. */
function savedLineDisplay(state: InvoiceDraftState, index: number): LineDisplay | undefined {
  const all = lookup(state.saved?.view?.display, 'LINE_DISPLAY');
  if (!Array.isArray(all)) {
    return undefined;
  }
  const entry: unknown = all[index];
  return typeof entry === 'object' && entry !== null ? (entry as LineDisplay) : undefined;
}

/** Dispatches a typed change of one line field. */
function setLineField<K extends keyof InvoiceLineDraft>(
  dispatch: Dispatch<InvoiceDraftAction>,
  index: number,
  field: K,
  value: InvoiceLineDraft[K],
): void {
  dispatch({ type: 'lineFieldChanged', index, field, value });
}

/** Messages, the line's rejected entry or Oracle field error, and the rejected and invalid flags of one line cell. */
function cellStatus(state: InvoiceDraftState, index: number, target: LineTarget): CellStatus {
  const source = `LINE:${index}:${target}`;
  const messages = state.messages[source] ?? [];
  const clientId = (state.saved?.view?.lines ?? state.draft?.lines ?? [])[index]?.clientId ?? null;
  const mapped = fieldErrorFor(state, target, clientId);
  const fieldError = mapped === null ? null : { text: mapped.text, oracleErrorNumber: mapped.oracleErrorNumber };
  return {
    target,
    source,
    messages,
    fieldError,
    rejected: entryErrorFor(state, target, clientId) !== null,
    invalid: fieldError !== null || messages.some((message) => message.severity === 'Blocking'),
  };
}

/** Moves focus to the first enabled form control with the given name. */
function focusByName(name: string): void {
  const control = Array.from(document.getElementsByName(name)).find(
    (element): element is HTMLInputElement | HTMLSelectElement =>
      (element instanceof HTMLInputElement || element instanceof HTMLSelectElement) && !element.disabled,
  );
  control?.focus();
}

/** Space-separated class list without empty entries, or undefined when none apply. */
function classNames(...names: (string | false)[]): string | undefined {
  const joined = names.filter((name): name is string => name !== false && name !== '').join(' ');
  return joined === '' ? undefined : joined;
}

type EditableCellProps = {
  name: string;
  label: string;
  text: string;
  changeToken: string;
  numeric: boolean;
  readOnly: boolean;
  invalid: boolean;
  rejected: boolean;
  onText: (raw: string) => void;
  onChanged: () => void;
  onRejected?: (message: string) => void;
  onAccepted?: () => void;
};

/** Text input cell that keeps the raw text while focused or rejected, keeps a non-decimal numeric entry out of the line, rejects it on blur and reports a changed value. */
function EditableCell({
  name,
  label,
  text,
  changeToken,
  numeric,
  readOnly,
  invalid,
  rejected,
  onText,
  onChanged,
  onRejected,
  onAccepted,
}: EditableCellProps) {
  const [raw, setRaw] = useState<string | null>(null);
  const [focused, setFocused] = useState(false);
  const focusToken = useRef<string | null>(null);
  const shown = (focused || (rejected && !readOnly)) && raw !== null ? raw : text;

  return (
    <td>
      <input
        type="text"
        name={name}
        inputMode={numeric ? 'decimal' : undefined}
        className={classNames(readOnly && 'read-only', invalid && 'invalid')}
        aria-label={label}
        aria-invalid={invalid || undefined}
        autoComplete="off"
        readOnly={readOnly}
        value={shown}
        title={text === '' ? undefined : text}
        onFocus={() => {
          if (!readOnly) {
            focusToken.current = changeToken;
            setFocused(true);
            setRaw((current) => (rejected && current !== null ? current : text));
          }
        }}
        onChange={(event) => {
          if (readOnly) {
            return;
          }
          const value = event.target.value;
          setRaw(value);
          if (!numeric || parseDecimalEntry(value).kind !== 'invalid') {
            onText(value);
          } else if (focusToken.current !== null && focusToken.current !== changeToken) {
            onText(focusToken.current);
          }
        }}
        onBlur={() => {
          const before = focusToken.current;
          focusToken.current = null;
          setFocused(false);
          if (before === null) {
            setRaw(null);
            return;
          }
          if (numeric) {
            const entry = parseDecimalEntry(raw ?? text);
            if (entry.kind === 'invalid') {
              onRejected?.(entry.message);
              return;
            }
          }
          setRaw(null);
          if (rejected) {
            onAccepted?.();
          }
          if (before !== changeToken || rejected) {
            onChanged();
          }
        }}
      />
    </td>
  );
}

/** Read-only display cell showing a value exactly as returned. */
function ReadOnlyCell({ name, label, value }: { name: string; label: string; value: unknown }) {
  const text = displayText(value);
  return (
    <td>
      <input
        type="text"
        name={name}
        className="read-only"
        aria-label={label}
        readOnly
        tabIndex={-1}
        value={text}
        title={text === '' ? undefined : text}
      />
    </td>
  );
}

type LineRowProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  line: InvoiceLineDraft;
  index: number;
  editable: boolean;
  fromSavedView: boolean;
  previewLine: EditablePreviewLine | undefined;
  onValidateLine: (index: number, target: ValidateTarget) => void;
  onQueueValidation: (index: number, target: LineTarget) => void;
  onRemoveLine: (index: number) => void;
  onOpenCategory: (target: CatPickerTarget) => void;
};

/** One D_INV row: operator-entered fields, package-returned values, row actions and the line's messages. */
function LineRow({
  state,
  dispatch,
  line,
  index,
  editable,
  fromSavedView,
  previewLine,
  onValidateLine,
  onQueueValidation,
  onRemoveLine,
  onOpenCategory,
}: LineRowProps) {
  const lineNo = index + 1;
  const isCurrent = index === state.currentLineIndex;
  const label = (header: string) => `${header}, line ${lineNo}`;
  const name = (target: string) => `line-${index}-${target}`;
  const display: LineDisplay | undefined = fromSavedView
    ? savedLineDisplay(state, index)
    : line.clientId !== null && line.clientId !== ''
      ? state.lineDisplay[line.clientId]
      : undefined;
  const packageValue = (savedKey: string, previewValue: unknown) => (fromSavedView ? lookup(display, savedKey) : previewValue);

  const priceEditable =
    editable &&
    isBlank(line.packageServiceId) &&
    (line.offerId === null || line.offerId === undefined) &&
    serverAllowsPrice(state, line);
  const priceValue = line.priceOverride !== null && line.priceOverride !== undefined ? line.priceOverride : (previewLine?.price ?? line.price);
  const discountType = line.discountType ?? '';
  const knownDiscountType = DISCOUNT_TYPES.some((option) => option.value === discountType);

  const status: Record<LineTarget, CellStatus> = {
    SERVICEID: cellStatus(state, index, 'SERVICEID'),
    QTY: cellStatus(state, index, 'QTY'),
    PRICE: cellStatus(state, index, 'PRICE'),
    LDISCT: cellStatus(state, index, 'LDISCT'),
    DISC: cellStatus(state, index, 'DISC'),
    MY_DISC: cellStatus(state, index, 'MY_DISC'),
  };
  const withMessages = LINE_TARGETS.map((target) => status[target]).filter(
    (cell) => cell.messages.length > 0 || cell.fieldError !== null,
  );

  const numericField = (field: 'qty' | 'disc' | 'myDisc') => (raw: string) => {
    const entry = parseDecimalEntry(raw);
    if (entry.kind !== 'invalid') {
      setLineField(dispatch, index, field, entry.kind === 'value' ? entry.text : null);
    }
  };
  const rejectEntry = (target: LineTarget) => (text: string) =>
    dispatch({ type: 'entryRejected', field: target, lineIndex: index, text });
  const acceptEntry = (target: LineTarget) => () => dispatch({ type: 'entryAccepted', field: target, lineIndex: index });
  const selectLine = () => dispatch({ type: 'currentLineSelected', index });
  const rowClass = isCurrent ? 'current-line' : undefined;

  return (
    <>
      <tr className={rowClass} aria-current={isCurrent ? 'true' : undefined} onClick={selectLine} onFocus={selectLine}>
        <td>
          <div className="field-row">
            <input
              type="text"
              name={name('CATID')}
              className="read-only"
              aria-label={label('Catid')}
              readOnly
              tabIndex={-1}
              value={displayText(line.catId)}
            />
            {editable && (
              <button
                type="button"
                aria-label={`Choose category, line ${lineNo}`}
                aria-haspopup="dialog"
                onClick={() => onOpenCategory({ index, clientId: line.clientId })}
              >
                …
              </button>
            )}
          </div>
        </td>
        <ReadOnlyCell name={name('XCAT_NAMEX')} label={label('Category name')} value={lookup(display, 'XCAT_NAMEX')} />
        <EditableCell
          name={name('SERVICEID')}
          label={label('Serviceid')}
          text={displayText(line.serviceId)}
          changeToken={displayText(line.serviceId)}
          numeric={false}
          readOnly={!editable}
          invalid={status.SERVICEID.invalid}
          rejected={false}
          onText={(raw) => setLineField(dispatch, index, 'serviceId', raw.trim() === '' ? null : raw.trim())}
          onChanged={() => onValidateLine(index, 'SERVICEID')}
        />
        <ReadOnlyCell
          name={name('SERVICEDESC')}
          label={label('Servicedesc')}
          value={previewLine?.serviceDesc ?? lookup(display, 'SERVICEDESC')}
        />
        <EditableCell
          name={name('QTY')}
          label={label('Qty')}
          text={displayText(line.qty)}
          changeToken={displayText(line.qty)}
          numeric
          readOnly={!editable}
          invalid={status.QTY.invalid}
          rejected={status.QTY.rejected}
          onText={numericField('qty')}
          onChanged={() => onValidateLine(index, 'QTY')}
          onRejected={rejectEntry('QTY')}
          onAccepted={acceptEntry('QTY')}
        />
        <EditableCell
          name={name('PRICE')}
          label={label('Price')}
          text={displayText(priceValue)}
          changeToken={displayText(line.priceOverride)}
          numeric
          readOnly={!priceEditable}
          invalid={status.PRICE.invalid}
          rejected={status.PRICE.rejected}
          onText={(raw) => {
            const entry = parseDecimalEntry(raw);
            if (entry.kind !== 'invalid') {
              const override = entry.kind === 'value' ? entry.text : null;
              setLineField(dispatch, index, 'priceOverride', override);
              setLineField(dispatch, index, 'usePriceOverride', override === null ? 'N' : 'Y');
            }
          }}
          onChanged={() => onValidateLine(index, 'PRICE')}
          onRejected={rejectEntry('PRICE')}
          onAccepted={acceptEntry('PRICE')}
        />
        <td>
          <select
            name={name('LDISCT')}
            aria-label={label('Disc Type')}
            aria-invalid={status.LDISCT.invalid || undefined}
            className={classNames(!editable && 'read-only', status.LDISCT.invalid && 'invalid')}
            disabled={!editable}
            value={discountType}
            onChange={(event) => {
              setLineField(dispatch, index, 'discountType', event.target.value);
              onQueueValidation(index, 'LDISCT');
            }}
          >
            {!knownDiscountType && <option value={discountType}>{discountType}</option>}
            {DISCOUNT_TYPES.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </td>
        <EditableCell
          name={name('DISC')}
          label={label('Disc')}
          text={displayText(line.disc)}
          changeToken={displayText(line.disc)}
          numeric
          readOnly={!editable}
          invalid={status.DISC.invalid}
          rejected={status.DISC.rejected}
          onText={numericField('disc')}
          onChanged={() => onValidateLine(index, 'DISC')}
          onRejected={rejectEntry('DISC')}
          onAccepted={acceptEntry('DISC')}
        />
        <EditableCell
          name={name('MY_DISC')}
          label={label('My Disc')}
          text={displayText(line.myDisc)}
          changeToken={displayText(line.myDisc)}
          numeric
          readOnly={!editable}
          invalid={status.MY_DISC.invalid}
          rejected={status.MY_DISC.rejected}
          onText={numericField('myDisc')}
          onChanged={() => onValidateLine(index, 'MY_DISC')}
          onRejected={rejectEntry('MY_DISC')}
          onAccepted={acceptEntry('MY_DISC')}
        />
        <ReadOnlyCell name={name('THE_PAY')} label={label('The Pay')} value={packageValue('THE_PAY', previewLine?.thePay)} />
        <ReadOnlyCell name={name('THE_COMP')} label={label('The Comp')} value={packageValue('THE_COMP', previewLine?.theComp)} />
        <ReadOnlyCell
          name={name('VAT_VAL_PAT')}
          label={label('VAT Pat')}
          value={packageValue('VAT_VAL_PAT', previewLine?.vatValPat)}
        />
        <ReadOnlyCell name={name('VAT_VAL_CO')} label={label('VAT Co')} value={packageValue('VAT_VAL_CO', previewLine?.vatValCo)} />
        <ReadOnlyCell name={name('MY_NET')} label={label('Net')} value={packageValue('MY_NET', previewLine?.myNet)} />
        <ReadOnlyCell name={name('FIXPAY')} label={label('Fixpay')} value={line.fixPay} />
        <ReadOnlyCell name={name('PAYRATE')} label={label('Rate')} value={line.payRate} />
        <td>
          {editable && (
            <button
              type="button"
              aria-label={`Remove line ${lineNo}`}
              onClick={(event) => {
                event.stopPropagation();
                onRemoveLine(index);
              }}
            >
              Remove
            </button>
          )}
        </td>
      </tr>
      {withMessages.length > 0 && (
        // Messages of the line's cells, in column order, in a full-width row under the line (D-120).
        <tr className={rowClass} onClick={selectLine}>
          <td colSpan={COLUMN_CLASSES.length}>
            <div className="check-field">
              {withMessages.map((cell) => (
                <FieldMessage
                  key={cell.source}
                  messages={cell.messages}
                  fieldError={cell.fieldError}
                  onDismiss={(messageIndex) => {
                    dispatch({ type: 'messageDismissed', source: cell.source, index: messageIndex });
                    focusByName(name(cell.target));
                  }}
                />
              ))}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/** Shows invoice lines, editable in a draft and read-only when saved, with package-returned amounts. */
export default function InvoiceLinesGrid({ state, dispatch, onValidateLine, onRemoveLine }: InvoiceLinesGridProps) {
  const [pending, setPending] = useState<PendingValidation[]>([]);
  const [categoryTarget, setCategoryTarget] = useState<CatPickerTarget | null>(null);
  const addLineButton = useRef<HTMLButtonElement>(null);

  const fromSavedView = state.saved?.view != null;
  const lines = state.saved?.view?.lines ?? state.draft?.lines ?? [];
  const editable = state.draft !== null && state.saved === null && !state.readOnly;
  const previewLines = useMemo(() => previewLinesByClientId(state.preview), [state.preview]);

  useEffect(() => {
    if (pending.length === 0) {
      return;
    }
    setPending([]);
    for (const request of pending) {
      onValidateLine(request.index, request.target);
    }
  }, [pending, onValidateLine]);

  const queueValidation = (index: number, target: LineTarget) =>
    setPending((queued) => [...queued, { index, target }]);

  const removeLine = (index: number) => {
    onRemoveLine(index);
    addLineButton.current?.focus();
  };

  const pickCategory = (row: LovRow) => {
    if (categoryTarget === null) {
      return;
    }
    setLineField(dispatch, categoryTarget.index, 'catId', toNumberOrNull(lookup(row, 'CATID')));
    if (categoryTarget.clientId !== null && categoryTarget.clientId !== '') {
      const description = lookup(row, 'CATDESC');
      dispatch({
        type: 'displaySet',
        lineClientId: categoryTarget.clientId,
        values: { XCAT_NAMEX: description === null || description === undefined ? null : String(description) },
      });
    }
  };

  return (
    <>
      <div className="lines-grid-scroll">
        <table className="lines-grid" aria-label="Invoice lines">
          <colgroup>
            {COLUMN_CLASSES.map((sizeClass, position) => (
              <col key={position} className={sizeClass} />
            ))}
          </colgroup>
          <thead>
            <tr>
              <th scope="col" colSpan={2}>
                Catid
              </th>
              <th scope="col">Serviceid</th>
              <th scope="col">Servicedesc</th>
              <th scope="col">Qty</th>
              <th scope="col">Price</th>
              <th scope="col">Disc Type</th>
              <th scope="col">Disc</th>
              <th scope="col">My Disc</th>
              <th scope="col">The Pay</th>
              <th scope="col">The Comp</th>
              <th scope="col">VAT Pat</th>
              <th scope="col">VAT Co</th>
              <th scope="col">Net</th>
              <th scope="col" colSpan={2}>
                Fixpay / Rate
                <OpenItemNotice ids={NOT_SAVED_OPEN_ITEMS} />
              </th>
              <th scope="col" aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={line.clientId !== null && line.clientId !== '' ? line.clientId : `row-${index}`}
                state={state}
                dispatch={dispatch}
                line={line}
                index={index}
                editable={editable}
                fromSavedView={fromSavedView}
                previewLine={
                  fromSavedView || line.clientId === null || line.clientId === '' ? undefined : previewLines.get(line.clientId)
                }
                onValidateLine={onValidateLine}
                onQueueValidation={queueValidation}
                onRemoveLine={removeLine}
                onOpenCategory={setCategoryTarget}
              />
            ))}
          </tbody>
        </table>
      </div>
      {editable && (
        <div className="field-row">
          <button type="button" ref={addLineButton} onClick={() => dispatch({ type: 'lineAdded' })}>
            Add line
          </button>
        </div>
      )}
      {editable && categoryTarget !== null && (
        <LovPicker name="CAT" binds={{}} onPick={pickCategory} onClose={() => setCategoryTarget(null)} />
      )}
    </>
  );
}
