import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type Dispatch } from 'react';
import { decimalText } from '../api/client';
import type { EditablePreviewLine, InvoiceLineDraft, PreviewResponse, ValidateTarget } from '../api/types';
import { dismissalOrder, entryErrorFor, fieldErrorFor, lineMessages, messageField, parseDecimalEntry } from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState, PlacedMessage } from '../state/invoiceDraft';
import FieldMessage, { fieldMessageRefs } from './FieldMessage';
import type { FieldMessageRefs } from './FieldMessage';
import LovPicker, { readOnlyTextProps } from './LovPicker';
import OpenItemNotice from './OpenItemNotice';

type LineTarget = Extract<ValidateTarget, 'SERVICEID' | 'QTY' | 'PRICE' | 'LDISCT' | 'DISC' | 'MY_DISC'>;

type LineDisplay = Record<string, unknown>;

type LovRow = Record<string, unknown>;

type PendingValidation = { index: number; target: LineTarget };

type CatPickerTarget = { index: number; clientId: string | null };

type CellStatus = {
  target: LineTarget;
  messages: PlacedMessage[];
  fieldError: { text: string; oracleErrorNumber: number | null } | null;
  rejected: boolean;
  invalid: boolean;
};

/** One field's messages in a line's message row: its element id, caption, messages, field error and the grid cell it describes. */
type MessageGroup = {
  id: string;
  caption: string | null;
  messages: PlacedMessage[];
  fieldError: CellStatus['fieldError'];
  cell: LineTarget | null;
};

type InvoiceLinesGridProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onValidateLine: (index: number, target: ValidateTarget) => void;
  onRemoveLine: (index: number) => void;
};

/** Column size classes in grid order: CATID, XCAT_NAMEX, SERVICEID … MY_NET, FIXPAY, PAYRATE, THE_PAY … VAT_VAL_CO, actions (D-168); styles.css --lines-grid-min-size sums these column sizes. */
const COLUMN_CLASSES: readonly string[] = [
  'lines-col-code',
  'lines-col-name',
  'lines-col-code',
  'lines-col-desc',
  'lines-col-price',
  'lines-col-qty',
  'lines-col-choice',
  'lines-col-disc',
  'lines-col-disc',
  'lines-col-amount',
  'lines-col-rate',
  'lines-col-rate',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-amount',
  'lines-col-action',
];

/** LDISCT radio values and labels. */
const DISCOUNT_TYPES: readonly { value: string; label: string }[] = [
  { value: 'R', label: 'Rate' },
  { value: 'V', label: 'Value' },
];

const NOT_SAVED_OPEN_ITEMS: string[] = ['OI-33'];

/** Line validation targets in grid column order. */
const LINE_TARGETS: readonly LineTarget[] = ['SERVICEID', 'PRICE', 'QTY', 'LDISCT', 'DISC', 'MY_DISC'];

/** Captions of the line fields a message group names: the grid column labels, Catid and the MORE labels. */
const FIELD_CAPTIONS: Readonly<Record<string, string>> = {
  SERVICEID: 'Serviceid',
  QTY: 'Qty',
  PRICE: 'Price',
  LDISCT: 'Disc Type',
  DISC: 'Disc',
  MY_DISC: 'My Disc',
  CATID: 'Catid',
  TEETH_NO: 'Teeth No',
  TOOTH_SURFACE: 'Tooth Surface',
  APPROV_DATE: 'Approval Date',
  APPROV_VALIDITY: 'Approval Validity',
  APPROV_REF_NO: 'Approval Ref No',
};

/** Text of a displayed value: empty for null or undefined, a number or text as returned (decimalText), else the value as text. */
function displayText(value: unknown): string {
  if (typeof value === 'number' || typeof value === 'string') {
    return decimalText(value);
  }
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

/** Disc and My Disc cell texts: the preview's computed value where the line's discount type derives that field and holds no non-zero entry, else the entry (D-148). */
function discountTexts(line: InvoiceLineDraft, previewLine: EditablePreviewLine | undefined): { disc: string; myDisc: string } {
  const entered = (value: unknown) => {
    const text = displayText(value).trim();
    return text !== '' && Number(text) !== 0;
  };
  const typed = trimmed(line.discountType).toUpperCase();
  const type = typed === '' ? 'R' : typed;
  const computedDisc = previewLine !== undefined && (type === 'V' || type === 'N') && !entered(line.disc);
  const computedMyDisc = previewLine !== undefined && type !== 'V' && !entered(line.myDisc);
  return {
    disc: displayText(computedDisc ? (previewLine?.disc ?? line.disc) : line.disc),
    myDisc: displayText(computedMyDisc ? (previewLine?.myDisc ?? line.myDisc) : line.myDisc),
  };
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

/** The line messages naming the cell's field, the line's rejected entry or Oracle field error, and the rejected and invalid flags of one line cell. */
function cellStatus(state: InvoiceDraftState, index: number, target: LineTarget, placed: readonly PlacedMessage[]): CellStatus {
  const messages = placed.filter((entry) => messageField(entry.message) === target);
  const clientId = (state.saved?.view?.lines ?? state.draft?.lines ?? [])[index]?.clientId ?? null;
  const mapped = fieldErrorFor(state, target, clientId);
  const fieldError = mapped === null ? null : { text: mapped.text, oracleErrorNumber: mapped.oracleErrorNumber };
  return {
    target,
    messages,
    fieldError,
    rejected: entryErrorFor(state, target, clientId) !== null,
    invalid: fieldError !== null || messages.some((entry) => entry.message.severity === 'Blocking'),
  };
}

/** Message groups of a line in row order: each cell with messages or a field error in column order, each other named field in first-seen order, then the messages naming no field. */
function messageGroups(index: number, status: Readonly<Record<LineTarget, CellStatus>>, placed: readonly PlacedMessage[]): MessageGroup[] {
  const groupId = (field: string) => `line-${index}-${encodeURIComponent(field)}-messages`;
  const groups: MessageGroup[] = LINE_TARGETS.map((target) => status[target])
    .filter((cell) => cell.messages.length > 0 || cell.fieldError !== null)
    .map((cell) => ({
      id: groupId(cell.target),
      caption: FIELD_CAPTIONS[cell.target],
      messages: cell.messages,
      fieldError: cell.fieldError,
      cell: cell.target,
    }));
  const cells: ReadonlySet<string> = new Set(LINE_TARGETS);
  const others = new Map<string, PlacedMessage[]>();
  const unnamed: PlacedMessage[] = [];
  for (const entry of placed) {
    const field = messageField(entry.message);
    if (field === null) {
      unnamed.push(entry);
    } else if (!cells.has(field)) {
      others.set(field, [...(others.get(field) ?? []), entry]);
    }
  }
  for (const [field, messages] of others) {
    groups.push({
      id: groupId(field),
      caption: Object.hasOwn(FIELD_CAPTIONS, field) ? FIELD_CAPTIONS[field] : field,
      messages,
      fieldError: null,
      cell: null,
    });
  }
  if (unnamed.length > 0) {
    groups.push({ id: `line-${index}-messages`, caption: null, messages: unnamed, fieldError: null, cell: null });
  }
  return groups;
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
  messageRefs: FieldMessageRefs;
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
  messageRefs,
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
        aria-describedby={messageRefs.describedBy}
        aria-errormessage={invalid ? messageRefs.errorMessage : undefined}
        autoComplete="off"
        readOnly={readOnly}
        tabIndex={readOnly ? -1 : undefined}
        value={shown}
        title={text === '' ? undefined : text}
        onFocus={() => {
          if (!readOnly) {
            focusToken.current = changeToken;
            setFocused(true);
            setRaw((current) => (rejected && current !== null ? current : null));
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

type ReadOnlyCellProps = {
  name: string;
  label: string;
  value: unknown;
  freeText?: boolean;
};

/** Read-only display cell showing a value exactly as returned; a free-text cell is a tab stop. */
function ReadOnlyCell({ name, label, value, freeText = false }: ReadOnlyCellProps) {
  const text = displayText(value);
  return (
    <td>
      <input
        type="text"
        name={name}
        className="read-only"
        aria-label={label}
        readOnly
        value={text}
        {...(freeText ? readOnlyTextProps(text) : { tabIndex: -1, title: text === '' ? undefined : text })}
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
  const rowBase = useId();
  const lineRowRef = useRef<HTMLTableRowElement>(null);
  const messageRowRef = useRef<HTMLTableRowElement>(null);
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
  const discountText = discountTexts(line, previewLine);
  const knownDiscountType = DISCOUNT_TYPES.some((option) => option.value === discountType);

  // Every message of the line's `LINE:<index>:*` sources, placed by the field it names.
  const placed = lineMessages(state, index);
  const status: Record<LineTarget, CellStatus> = {
    SERVICEID: cellStatus(state, index, 'SERVICEID', placed),
    QTY: cellStatus(state, index, 'QTY', placed),
    PRICE: cellStatus(state, index, 'PRICE', placed),
    LDISCT: cellStatus(state, index, 'LDISCT', placed),
    DISC: cellStatus(state, index, 'DISC', placed),
    MY_DISC: cellStatus(state, index, 'MY_DISC', placed),
  };
  const groups = messageGroups(index, status, placed);
  const dismiss = (group: MessageGroup, messageIndex: number) => {
    for (const ref of dismissalOrder(group.messages[messageIndex]?.refs ?? [])) {
      dispatch({ type: 'messageDismissed', source: ref.source, index: ref.index });
    }
    focusByName(name(group.cell ?? 'SERVICEID'));
  };
  /** Id of a message group's FieldMessage in the line's message row. */
  const messageId = (group: MessageGroup) => `${rowBase}-${group.id}-msg`;
  const messageRefs = (target: LineTarget): FieldMessageRefs => {
    const group = groups.find((candidate) => candidate.cell === target);
    return group === undefined
      ? { describedBy: undefined, errorMessage: undefined }
      : fieldMessageRefs(messageId(group), group.messages.map((entry) => entry.message), group.fieldError);
  };
  const messageSignature = groups
    .map((group) =>
      [
        group.id,
        ...group.messages.map((entry) => `${entry.message.severity}:${entry.message.text}`),
        group.fieldError?.text ?? '',
      ].join('\u0000'),
    )
    .join('\u0001');

  /** Scrolls the grid the least needed to show the line's message row under its visible line, keeping the line below the sticky header. */
  const revealMessages = () => {
    const lineRow = lineRowRef.current;
    const messageRow = messageRowRef.current;
    const scroller = lineRow?.closest<HTMLElement>('.lines-grid-scroll') ?? null;
    if (lineRow === null || messageRow === null || scroller === null) {
      return;
    }
    const clientTop = scroller.getBoundingClientRect().top + scroller.clientTop;
    const visibleTop = clientTop + (scroller.querySelector('thead')?.getBoundingClientRect().height ?? 0);
    const visibleBottom = clientTop + scroller.clientHeight;
    const line = lineRow.getBoundingClientRect();
    if (line.bottom <= visibleTop || line.top >= visibleBottom) {
      return;
    }
    const shift = Math.min(Math.ceil(messageRow.getBoundingClientRect().bottom - visibleBottom), Math.floor(line.top - visibleTop));
    if (shift > 0) {
      scroller.scrollTop += shift;
    }
  };

  // Shows a changed message row under its visible line.
  useLayoutEffect(() => {
    if (messageSignature !== '') {
      revealMessages();
    }
  }, [messageSignature]);

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
      <tr
        ref={lineRowRef}
        className={rowClass}
        aria-current={isCurrent ? 'true' : undefined}
        onClick={selectLine}
        onFocus={(event) => {
          selectLine();
          if (event.target instanceof HTMLElement && event.target.getAttribute('aria-invalid') === 'true') {
            revealMessages();
          }
        }}
      >
        <td>
          <div className="field-row">
            <input
              type="text"
              name={name('CATID')}
              className="read-only"
              aria-label={label('Catid')}
              readOnly
              tabIndex={-1}
              title={displayText(line.catId) === '' ? undefined : displayText(line.catId)}
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
        <ReadOnlyCell
          name={name('XCAT_NAMEX')}
          label={label('Category name')}
          value={lookup(display, 'XCAT_NAMEX')}
          freeText
        />
        <EditableCell
          name={name('SERVICEID')}
          label={label('Serviceid')}
          text={displayText(line.serviceId)}
          changeToken={displayText(line.serviceId)}
          numeric={false}
          readOnly={!editable}
          invalid={status.SERVICEID.invalid}
          rejected={false}
          messageRefs={messageRefs('SERVICEID')}
          onText={(raw) => setLineField(dispatch, index, 'serviceId', raw.trim() === '' ? null : raw.trim())}
          onChanged={() => onValidateLine(index, 'SERVICEID')}
        />
        <ReadOnlyCell
          name={name('SERVICEDESC')}
          label={label('Servicedesc')}
          value={previewLine?.serviceDesc ?? lookup(display, 'SERVICEDESC')}
          freeText
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
          messageRefs={messageRefs('PRICE')}
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
        <EditableCell
          name={name('QTY')}
          label={label('Qty')}
          text={displayText(line.qty)}
          changeToken={displayText(line.qty)}
          numeric
          readOnly={!editable}
          invalid={status.QTY.invalid}
          rejected={status.QTY.rejected}
          messageRefs={messageRefs('QTY')}
          onText={numericField('qty')}
          onChanged={() => onValidateLine(index, 'QTY')}
          onRejected={rejectEntry('QTY')}
          onAccepted={acceptEntry('QTY')}
        />
        <td>
          <select
            name={name('LDISCT')}
            aria-label={label('Disc Type')}
            aria-invalid={status.LDISCT.invalid || undefined}
            aria-describedby={messageRefs('LDISCT').describedBy}
            aria-errormessage={status.LDISCT.invalid ? messageRefs('LDISCT').errorMessage : undefined}
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
          text={discountText.disc}
          changeToken={displayText(line.disc)}
          numeric
          readOnly={!editable}
          invalid={status.DISC.invalid}
          rejected={status.DISC.rejected}
          messageRefs={messageRefs('DISC')}
          onText={numericField('disc')}
          onChanged={() => onValidateLine(index, 'DISC')}
          onRejected={rejectEntry('DISC')}
          onAccepted={acceptEntry('DISC')}
        />
        <EditableCell
          name={name('MY_DISC')}
          label={label('My Disc')}
          text={discountText.myDisc}
          changeToken={displayText(line.myDisc)}
          numeric
          readOnly={!editable}
          invalid={status.MY_DISC.invalid}
          rejected={status.MY_DISC.rejected}
          messageRefs={messageRefs('MY_DISC')}
          onText={numericField('myDisc')}
          onChanged={() => onValidateLine(index, 'MY_DISC')}
          onRejected={rejectEntry('MY_DISC')}
          onAccepted={acceptEntry('MY_DISC')}
        />
        <ReadOnlyCell name={name('MY_NET')} label={label('Net')} value={packageValue('MY_NET', previewLine?.myNet)} />
        <ReadOnlyCell name={name('FIXPAY')} label={label('Fixpay')} value={line.fixPay} />
        <ReadOnlyCell name={name('PAYRATE')} label={label('Rate')} value={line.payRate} />
        <ReadOnlyCell name={name('THE_PAY')} label={label('The Pay')} value={packageValue('THE_PAY', previewLine?.thePay)} />
        <ReadOnlyCell name={name('THE_COMP')} label={label('The Comp')} value={packageValue('THE_COMP', previewLine?.theComp)} />
        <ReadOnlyCell
          name={name('VAT_VAL_PAT')}
          label={label('VAT Pat')}
          value={packageValue('VAT_VAL_PAT', previewLine?.vatValPat)}
        />
        <ReadOnlyCell name={name('VAT_VAL_CO')} label={label('VAT Co')} value={packageValue('VAT_VAL_CO', previewLine?.vatValCo)} />
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
      {groups.length > 0 && (
        // The line's messages, one captioned group per field they name, in a full-width row under the line (D-120).
        <tr ref={messageRowRef} className={rowClass} onClick={selectLine}>
          <td colSpan={COLUMN_CLASSES.length} className="line-messages-cell">
            <div className="line-messages">
              {groups.map((group) => (
                <div key={group.id} id={group.id} className={group.caption === null ? undefined : 'field'}>
                  {group.caption !== null && <span className="field-caption">{group.caption}</span>}
                  <FieldMessage
                    id={messageId(group)}
                    messages={group.messages.map((entry) => entry.message)}
                    fieldError={group.fieldError}
                    onDismiss={(messageIndex) => dismiss(group, messageIndex)}
                  />
                </div>
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
  const scroller = useRef<HTMLDivElement>(null);
  const addedLineCount = useRef<number | null>(null);
  const noteId = useId();

  const fromSavedView = state.saved?.view != null;
  const lines = state.saved?.view?.lines ?? state.draft?.lines ?? [];
  const editable = state.draft !== null && state.saved === null && !state.readOnly;
  const previewLines = useMemo(() => previewLinesByClientId(state.preview), [state.preview]);
  const draftRequestId = state.draft?.requestId ?? null;

  // Returns the scroller to its start when the grid has no lines, including a new draft without lines (D-137).
  useEffect(() => {
    if (lines.length === 0 && scroller.current !== null) {
      scroller.current.scrollLeft = 0;
      scroller.current.scrollTop = 0;
    }
  }, [lines.length, draftRequestId]);

  // After Add line, focuses the new line's Serviceid and scrolls its row fully into the grid's view.
  useEffect(() => {
    const expected = addedLineCount.current;
    addedLineCount.current = null;
    if (expected === null || expected !== lines.length) {
      return;
    }
    const name = `line-${lines.length - 1}-SERVICEID`;
    focusByName(name);
    document.getElementsByName(name)[0]?.closest('tr')?.scrollIntoView({ block: 'nearest' });
  }, [lines.length]);

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
      {/* Named keyboard-scrollable region around the grid (D-137). */}
      <div ref={scroller} className="lines-grid-scroll" role="region" aria-label="Scrollable invoice lines" tabIndex={0}>
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
              <th scope="col">Price</th>
              <th scope="col">Qty</th>
              <th scope="col">Disc Type</th>
              <th scope="col">Disc</th>
              <th scope="col">My Disc</th>
              <th scope="col">Net</th>
              <th scope="col" colSpan={2} aria-describedby={noteId}>Fixpay / Rate</th>
              <th scope="col">The Pay</th>
              <th scope="col">The Comp</th>
              <th scope="col">VAT Pat</th>
              <th scope="col">VAT Co</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {lines.length === 0 && (
              // Empty-state row shown while the grid has no lines (D-137).
              <tr>
                <td className="lines-grid-empty" colSpan={COLUMN_CLASSES.length}>
                  <span>{editable ? 'No service lines. Use Add line or Import Request to add one.' : 'No service lines.'}</span>
                </td>
              </tr>
            )}
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
      <div className="grid-note" id={noteId}>
        <span>Fixpay / Rate</span>
        <OpenItemNotice ids={NOT_SAVED_OPEN_ITEMS} />
      </div>
      {editable && (
        <div className="field-row">
          <button
            type="button"
            ref={addLineButton}
            onClick={() => {
              addedLineCount.current = lines.length + 1;
              dispatch({ type: 'lineAdded' });
            }}
          >
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
