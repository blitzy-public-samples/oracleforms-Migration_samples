import { useEffect, useRef, useState, type Dispatch } from 'react';
import type { InvoiceLineDraft, MessageDto, ValidateTarget } from '../api/types';
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
  invalid: boolean;
};

type InvoiceLinesGridProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onValidateLine: (index: number, target: ValidateTarget) => void;
  onRemoveLine: (index: number) => void;
};

/** Column widths in grid order: CATID, XCAT_NAMEX, SERVICEID … FIXPAY, PAYRATE, actions. */
const COLUMN_WIDTHS: readonly string[] = [
  '5.5rem',
  '7rem',
  '5.5rem',
  '12rem',
  '4.5rem',
  '6rem',
  '5.5rem',
  '5rem',
  '5rem',
  '5.5rem',
  '5.5rem',
  '5.5rem',
  '5.5rem',
  '5.5rem',
  '7rem',
  '7rem',
  '5.5rem',
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

/** Parses operator text: null when empty, the number when finite, undefined when unparsable. */
function parseNumber(text: string): number | null | undefined {
  const trimmed = text.trim();
  if (trimmed === '') {
    return null;
  }
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

/** Parses an LOV cell into a number, or null when it is empty or not numeric. */
function toNumberOrNull(value: unknown): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  return typeof value === 'string' ? (parseNumber(value) ?? null) : null;
}

/** True when a text field holds no value. */
function isBlank(value: string | null | undefined): boolean {
  return value === null || value === undefined || value.trim() === '';
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

/** Messages, current-line Oracle field error and invalid flag of one line cell. */
function cellStatus(state: InvoiceDraftState, index: number, target: LineTarget): CellStatus {
  const source = `LINE:${index}:${target}`;
  const messages = state.messages[source] ?? [];
  const mapped = index === state.currentLineIndex && Object.hasOwn(state.fieldErrors, target) ? state.fieldErrors[target] : undefined;
  const fieldError = mapped === undefined ? null : { text: mapped.text, oracleErrorNumber: mapped.oracleErrorNumber };
  return {
    target,
    source,
    messages,
    fieldError,
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
  onText: (raw: string) => void;
  onChanged: () => void;
};

/** Text input cell that keeps the raw text while focused and reports a changed value on blur. */
function EditableCell({ name, label, text, changeToken, numeric, readOnly, invalid, onText, onChanged }: EditableCellProps) {
  const [raw, setRaw] = useState<string | null>(null);
  const focusToken = useRef<string | null>(null);

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
        value={raw ?? text}
        title={text === '' ? undefined : text}
        onFocus={() => {
          if (!readOnly) {
            focusToken.current = changeToken;
            setRaw(text);
          }
        }}
        onChange={(event) => {
          if (readOnly) {
            return;
          }
          setRaw(event.target.value);
          onText(event.target.value);
        }}
        onBlur={() => {
          const before = focusToken.current;
          focusToken.current = null;
          setRaw(null);
          if (before !== null && before !== changeToken) {
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
  const previewLine = fromSavedView
    ? undefined
    : state.preview?.lines?.find((candidate) => candidate.clientId !== null && candidate.clientId === line.clientId);
  const packageValue = (savedKey: string, previewValue: unknown) => (fromSavedView ? lookup(display, savedKey) : previewValue);

  const priceEditable = editable && isBlank(line.packageServiceId) && (line.offerId === null || line.offerId === undefined);
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
    const parsed = parseNumber(raw);
    if (parsed !== undefined) {
      setLineField(dispatch, index, field, parsed);
    }
  };
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
              value={displayText(previewLine?.catId ?? line.catId)}
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
          onText={numericField('qty')}
          onChanged={() => onValidateLine(index, 'QTY')}
        />
        <EditableCell
          name={name('PRICE')}
          label={label('Price')}
          text={displayText(priceValue)}
          changeToken={displayText(line.priceOverride)}
          numeric
          readOnly={!priceEditable}
          invalid={status.PRICE.invalid}
          onText={(raw) => {
            const parsed = parseNumber(raw);
            if (parsed !== undefined) {
              setLineField(dispatch, index, 'priceOverride', parsed);
              setLineField(dispatch, index, 'usePriceOverride', parsed === null ? 'N' : 'Y');
            }
          }}
          onChanged={() => onValidateLine(index, 'PRICE')}
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
          onText={numericField('disc')}
          onChanged={() => onValidateLine(index, 'DISC')}
        />
        <EditableCell
          name={name('MY_DISC')}
          label={label('My Disc')}
          text={displayText(line.myDisc)}
          changeToken={displayText(line.myDisc)}
          numeric
          readOnly={!editable}
          invalid={status.MY_DISC.invalid}
          onText={numericField('myDisc')}
          onChanged={() => onValidateLine(index, 'MY_DISC')}
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
        // Messages of the line's cells, in column order, in a full-width row under the line.
        <tr className={rowClass} onClick={selectLine}>
          <td colSpan={COLUMN_WIDTHS.length}>
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

/** Editable grid of the invoice lines with the package-returned shares, VAT and net. */
export default function InvoiceLinesGrid({ state, dispatch, onValidateLine, onRemoveLine }: InvoiceLinesGridProps) {
  const [pending, setPending] = useState<PendingValidation[]>([]);
  const [categoryTarget, setCategoryTarget] = useState<CatPickerTarget | null>(null);
  const addLineButton = useRef<HTMLButtonElement>(null);

  const fromSavedView = state.saved?.view != null;
  const lines = state.saved?.view?.lines ?? state.draft?.lines ?? [];
  const editable = state.draft !== null && state.saved === null && !state.readOnly;

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
            {COLUMN_WIDTHS.map((width, position) => (
              <col key={position} style={{ inlineSize: width }} />
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
