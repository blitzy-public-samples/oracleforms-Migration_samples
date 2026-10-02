import { memo, useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type Dispatch, type FocusEvent, type KeyboardEvent, type UIEvent } from 'react';
import { flushSync } from 'react-dom';
import { decimalText } from '../api/client';
import type { EditablePreviewLine, InvoiceLineDraft, MessageDto, PreviewResponse, ValidateTarget } from '../api/types';
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

/** A line row and its message row, shown together by a scroll. */
type MessageRows = { lineRow: HTMLTableRowElement; messageRow: HTMLTableRowElement };

/** What a measured element's block size gives: the scroller's view, a line row or a line's message row. */
type MeasuredPart = 'viewport' | 'line' | 'messages';

/** Key, block-start offset from the body's start and ARIA row index of each line (the body's size at `tops[keys.length]`), the table's row count and the lines holding a rejected entry. */
type LinesLayout = {
  keys: string[];
  tops: number[];
  rowIndexes: number[];
  rowCount: number;
  rejected: number[];
};

/** A rendered line, or a spacer holding the size of the lines between two rendered ones. */
type WindowPart = { kind: 'line'; index: number } | { kind: 'gap'; size: number; ordinal: number };

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

/** Lines rendered beyond each edge of the scroller's view (D-181). */
const OVERSCAN = 12;

/** Line row block size in pixels until a rendered line row is measured (--grid-row-size). */
const FALLBACK_ROW_HEIGHT = 30;

/** Scroller view block size, in line rows, until the scroller is measured: the header and five lines. */
const FALLBACK_VIEWPORT_ROWS = 6;

/** Prefix of a line's message source keys, `LINE:<i>:<TARGET>`. */
const LINE_SOURCE_PREFIX = 'LINE:';

const NO_MESSAGES: readonly PlacedMessage[] = [];

/** Captions of the line fields a message group names: the grid column labels, Catid and the MORE labels. */
export const FIELD_CAPTIONS: Readonly<Record<string, string>> = {
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

/** The line messages naming the cell's field, the line's rejected entry or Oracle field error, and the rejected and invalid flags of one cell of the line with `clientId`. */
function cellStatus(state: InvoiceDraftState, clientId: string | null, target: LineTarget, placed: readonly PlacedMessage[]): CellStatus {
  const messages = placed.filter((entry) => messageField(entry.message) === target);
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

/** Row key of a line: its client id, else its position. */
function lineKey(line: InvoiceLineDraft, index: number): string {
  return line.clientId !== null && line.clientId !== '' ? line.clientId : `row-${index}`;
}

/** Placed messages of each line that has any, as lineMessages gives them, from one pass that splits the `LINE:<i>:*` sources by line. */
function placedMessagesByLine(state: InvoiceDraftState): Map<number, PlacedMessage[]> {
  const sourcesByLine = new Map<number, Record<string, MessageDto[]>>();
  for (const [source, list] of Object.entries(state.messages)) {
    if (!source.startsWith(LINE_SOURCE_PREFIX)) {
      continue;
    }
    const lineIndex = Number(source.slice(LINE_SOURCE_PREFIX.length).split(':', 1)[0]);
    if (!Number.isInteger(lineIndex)) {
      continue;
    }
    const sources = sourcesByLine.get(lineIndex) ?? {};
    sources[source] = list;
    sourcesByLine.set(lineIndex, sources);
  }
  const placedByLine = new Map<number, PlacedMessage[]>();
  for (const [lineIndex, messages] of sourcesByLine) {
    const placed = lineMessages({ ...state, messages }, lineIndex);
    if (placed.length > 0) {
      placedByLine.set(lineIndex, placed);
    }
  }
  return placedByLine;
}

/** Layout of the lines: each line row `rowHeight` tall, followed by its message row at its measured size when the line has messages or a cell error. */
function linesLayout(
  state: InvoiceDraftState,
  lines: readonly InvoiceLineDraft[],
  placedByLine: ReadonlyMap<number, readonly PlacedMessage[]>,
  rowHeight: number,
  messageHeights: ReadonlyMap<string, number>,
): LinesLayout {
  const anyEntryError = Object.keys(state.entryErrors).length > 0;
  const anyCellError = anyEntryError || Object.keys(state.fieldErrors).length > 0;
  const keys: string[] = [];
  const tops: number[] = [0];
  const rowIndexes: number[] = [];
  const rejected: number[] = [];
  let rowIndex = 2;
  lines.forEach((line, index) => {
    const key = lineKey(line, index);
    const hasMessageRow =
      placedByLine.has(index) || (anyCellError && LINE_TARGETS.some((target) => fieldErrorFor(state, target, line.clientId) !== null));
    if (anyEntryError && LINE_TARGETS.some((target) => entryErrorFor(state, target, line.clientId) !== null)) {
      rejected.push(index);
    }
    keys.push(key);
    rowIndexes.push(rowIndex);
    rowIndex += hasMessageRow ? 2 : 1;
    tops.push(tops[index] + rowHeight + (hasMessageRow ? (messageHeights.get(key) ?? 0) : 0));
  });
  return { keys, tops, rowIndexes, rowCount: rowIndex - 1, rejected };
}

/** Index of the line whose rows span `offset` from the body's start: the last of `count` lines starting at or before it. */
function lineAt(tops: readonly number[], count: number, offset: number): number {
  let low = 0;
  let high = count - 1;
  while (low < high) {
    const middle = Math.ceil((low + high) / 2);
    if (tops[middle] <= offset) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }
  return low;
}

/** Body parts in order: the lines from `start` to `end` and each pinned line, with a spacer for every run of lines between them. */
function windowParts(tops: readonly number[], count: number, start: number, end: number, pinned: readonly number[]): WindowPart[] {
  const shown = new Set<number>(pinned.filter((index) => index >= 0 && index < count));
  for (let index = start; index < end; index += 1) {
    shown.add(index);
  }
  const parts: WindowPart[] = [];
  let next = 0;
  let gaps = 0;
  for (const index of Array.from(shown).sort((a, b) => a - b)) {
    if (index > next) {
      parts.push({ kind: 'gap', size: tops[index] - tops[next], ordinal: gaps });
      gaps += 1;
    }
    parts.push({ kind: 'line', index });
    next = index + 1;
  }
  if (count > next) {
    parts.push({ kind: 'gap', size: tops[count] - tops[next], ordinal: gaps });
  }
  return parts;
}

/** Scrolls the grid the least needed to show each message row under its visible line, in order, keeping the line below the sticky header. */
function revealMessageRows(scroller: HTMLElement, rows: readonly MessageRows[]): void {
  const clientTop = scroller.getBoundingClientRect().top + scroller.clientTop;
  const visibleTop = clientTop + (scroller.querySelector('thead')?.getBoundingClientRect().height ?? 0);
  const visibleBottom = clientTop + scroller.clientHeight;
  for (const { lineRow, messageRow } of rows) {
    if (!lineRow.isConnected || !messageRow.isConnected) {
      continue;
    }
    const line = lineRow.getBoundingClientRect();
    if (line.bottom <= visibleTop || line.top >= visibleBottom) {
      continue;
    }
    const shift = Math.min(Math.ceil(messageRow.getBoundingClientRect().bottom - visibleBottom), Math.floor(line.top - visibleTop));
    if (shift > 0) {
      scroller.scrollTop += shift;
    }
  }
}

/** Whole-pixel scroll change that brings the span from `start` to `end` inside the range from `rangeStart` to `rangeEnd`, aligning its start when it does not fit. */
function revealShift(start: number, end: number, rangeStart: number, rangeEnd: number): number {
  if (start < rangeStart || end - start > rangeEnd - rangeStart) {
    return Math.floor(start - rangeStart);
  }
  return end > rangeEnd ? Math.ceil(end - rangeEnd) : 0;
}

/** Scrolls only the grid, the least needed to show a focused control's border box inside its view and below the sticky header. */
function revealFocusedControl(scroller: HTMLElement, control: HTMLElement): void {
  const view = scroller.getBoundingClientRect();
  const clientLeft = view.left + scroller.clientLeft;
  const clientTop = view.top + scroller.clientTop;
  const visibleTop = clientTop + (scroller.querySelector('thead')?.getBoundingClientRect().height ?? 0);
  const box = control.getBoundingClientRect();
  const inlineShift = revealShift(box.left, box.right, clientLeft, clientLeft + scroller.clientWidth);
  const blockShift = revealShift(box.top, box.bottom, visibleTop, clientTop + scroller.clientHeight);
  if (inlineShift !== 0) {
    scroller.scrollLeft += inlineShift;
  }
  if (blockShift !== 0) {
    scroller.scrollTop += blockShift;
  }
}

/** True when `target` handles Home and End itself: a text entry, list or editable element. */
function handlesHomeEnd(target: EventTarget): boolean {
  return (
    target instanceof HTMLInputElement ||
    target instanceof HTMLSelectElement ||
    target instanceof HTMLTextAreaElement ||
    (target instanceof HTMLElement && target.isContentEditable)
  );
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
  disabled?: boolean;
};

/** Read-only display cell showing a value exactly as returned; a free-text cell is a tab stop, a disabled cell has the disabled look. */
function ReadOnlyCell({ name, label, value, freeText = false, disabled = false }: ReadOnlyCellProps) {
  const text = displayText(value);
  return (
    <td>
      <input
        type="text"
        name={name}
        className={disabled ? undefined : 'read-only'}
        aria-label={label}
        readOnly
        disabled={disabled}
        value={text}
        {...(freeText ? readOnlyTextProps(text) : { tabIndex: -1, title: text === '' ? undefined : text })}
      />
    </td>
  );
}

type LineRowProps = {
  /** Draft state, read for its entry and field errors only. */
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  line: InvoiceLineDraft;
  index: number;
  rowKey: string;
  /** ARIA row index of the line row; its message row follows it. */
  rowIndex: number;
  isCurrent: boolean;
  display: LineDisplay | undefined;
  /** Server judgement that PRICE is editable on the line's current service, patient and company. */
  priceAllowed: boolean;
  /** Every message of the line's `LINE:<index>:*` sources, placed by the field it names. */
  placed: readonly PlacedMessage[];
  editable: boolean;
  fromSavedView: boolean;
  previewLine: EditablePreviewLine | undefined;
  onValidateLine: (index: number, target: ValidateTarget) => void;
  onQueueValidation: (index: number, target: LineTarget) => void;
  onRemoveLine: (index: number) => void;
  onOpenCategory: (target: CatPickerTarget) => void;
  /** Asks the grid to show a changed message row; `mounted` is true when the row first renders with it. */
  onMessagesShown: (rowKey: string, mounted: boolean, rows: MessageRows) => void;
  /** Starts reporting an element's block size to the grid and returns the function that stops it. */
  onTrack: (element: Element, rowKey: string, part: MeasuredPart) => () => void;
};

/** One D_INV row: operator-entered fields, package-returned values, row actions and the line's messages. */
function LineRow({
  state,
  dispatch,
  line,
  index,
  rowKey,
  rowIndex,
  isCurrent,
  display,
  priceAllowed,
  placed,
  editable,
  fromSavedView,
  previewLine,
  onValidateLine,
  onQueueValidation,
  onRemoveLine,
  onOpenCategory,
  onMessagesShown,
  onTrack,
}: LineRowProps) {
  const rowBase = useId();
  const lineRowRef = useRef<HTMLTableRowElement>(null);
  const messageRowRef = useRef<HTMLTableRowElement>(null);
  const shownSignature = useRef<string | null>(null);
  const lineNo = index + 1;
  const label = (header: string) => `${header}, line ${lineNo}`;
  const name = (target: string) => `line-${index}-${target}`;
  const packageValue = (savedKey: string, previewValue: unknown) => (fromSavedView ? lookup(display, savedKey) : previewValue);

  const priceEditable =
    editable &&
    isBlank(line.packageServiceId) &&
    (line.offerId === null || line.offerId === undefined) &&
    priceAllowed;
  const priceValue = line.priceOverride !== null && line.priceOverride !== undefined ? line.priceOverride : (previewLine?.price ?? line.price);
  const discountType = line.discountType ?? '';
  const discountText = discountTexts(line, previewLine);
  const knownDiscountType = DISCOUNT_TYPES.some((option) => option.value === discountType);

  const status: Record<LineTarget, CellStatus> = {
    SERVICEID: cellStatus(state, line.clientId, 'SERVICEID', placed),
    QTY: cellStatus(state, line.clientId, 'QTY', placed),
    PRICE: cellStatus(state, line.clientId, 'PRICE', placed),
    LDISCT: cellStatus(state, line.clientId, 'LDISCT', placed),
    DISC: cellStatus(state, line.clientId, 'DISC', placed),
    MY_DISC: cellStatus(state, line.clientId, 'MY_DISC', placed),
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

  const hasMessageRow = groups.length > 0;

  /** Scrolls the grid the least needed to show the line's message row under its visible line, keeping the line below the sticky header. */
  const revealMessages = () => {
    const lineRow = lineRowRef.current;
    const messageRow = messageRowRef.current;
    const scroller = lineRow?.closest<HTMLElement>('.lines-grid-scroll') ?? null;
    if (lineRow !== null && messageRow !== null && scroller !== null) {
      revealMessageRows(scroller, [{ lineRow, messageRow }]);
    }
  };

  // Asks the grid to show a changed message row under its visible line.
  useLayoutEffect(() => {
    const previous = shownSignature.current;
    shownSignature.current = messageSignature;
    const lineRow = lineRowRef.current;
    const messageRow = messageRowRef.current;
    if (messageSignature !== '' && messageSignature !== previous && lineRow !== null && messageRow !== null) {
      onMessagesShown(rowKey, previous === null, { lineRow, messageRow });
    }
  }, [messageSignature, onMessagesShown, rowKey]);

  // Reports the line row's block size to the grid.
  useLayoutEffect(() => {
    const cell = lineRowRef.current?.cells.item(0) ?? null;
    return cell === null ? undefined : onTrack(cell, rowKey, 'line');
  }, [onTrack, rowKey]);

  // Reports the message row's block size to the grid while the line has one.
  useLayoutEffect(() => {
    const cell = messageRowRef.current?.cells.item(0) ?? null;
    return cell === null ? undefined : onTrack(cell, rowKey, 'messages');
  }, [onTrack, rowKey, hasMessageRow]);

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
        aria-rowindex={rowIndex}
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
        {/* OI-33 cells: empty and disabled until a saved view is loaded, then read-only with the persisted values. */}
        <ReadOnlyCell
          name={name('FIXPAY')}
          label={label('Fixpay')}
          value={fromSavedView ? line.fixPay : null}
          disabled={!fromSavedView}
        />
        <ReadOnlyCell
          name={name('PAYRATE')}
          label={label('Rate')}
          value={fromSavedView ? line.payRate : null}
          disabled={!fromSavedView}
        />
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
      {hasMessageRow && (
        // The line's messages, one captioned group per field they name, in a full-width row under the line (D-120).
        <tr ref={messageRowRef} className={rowClass} aria-rowindex={rowIndex + 1} onClick={selectLine}>
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

/** True when LineRow would render the same rows: every prop identical, with `state` compared by the entry and field errors LineRow reads. */
function sameLineRowProps(previous: LineRowProps, next: LineRowProps): boolean {
  return (Object.keys(next) as (keyof LineRowProps)[]).every((key) =>
    key === 'state'
      ? previous.state.entryErrors === next.state.entryErrors && previous.state.fieldErrors === next.state.fieldErrors
      : Object.is(previous[key], next[key]),
  );
}

/** LineRow, re-rendered only when its line's props change (D-181). */
const MemoLineRow = memo(LineRow, sameLineRowProps);

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
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);
  const [rowHeight, setRowHeight] = useState(FALLBACK_ROW_HEIGHT);
  const [messageHeights, setMessageHeights] = useState<ReadonlyMap<string, number>>(() => new Map());
  const measured = useRef(new Map<Element, { rowKey: string; part: MeasuredPart }>());
  const observer = useRef<ResizeObserver | null>(null);
  const revealQueue = useRef<MessageRows[]>([]);
  const committedKeys = useRef<{ keys: readonly string[]; known: Set<string> | null }>({ keys: [], known: null });
  const pointerPressed = useRef(false);

  const placedByLine = useMemo(() => placedMessagesByLine(state), [state.messages]);
  const layout = useMemo(
    () => linesLayout(state, lines, placedByLine, rowHeight, messageHeights),
    [lines, placedByLine, state.entryErrors, state.fieldErrors, rowHeight, messageHeights],
  );

  // Lines in and near the scroller's view, whose edges in body coordinates allow for the header, plus the current line, its neighbours and every line holding a rejected entry (D-181).
  const lineCount = lines.length;
  const viewport = viewportHeight > 0 ? viewportHeight : rowHeight * FALLBACK_VIEWPORT_ROWS;
  const start = Math.max(0, lineAt(layout.tops, lineCount, scrollTop - rowHeight) - OVERSCAN);
  const end = Math.min(lineCount, lineAt(layout.tops, lineCount, scrollTop + viewport) + 1 + OVERSCAN);
  const current = state.currentLineIndex;
  const parts = windowParts(layout.tops, lineCount, start, end, [current - 1, current, current + 1, ...layout.rejected]);

  /** Starts reporting an element's block size as the scroller's view, a line row or a message row; returns the function that stops it. */
  const track = useCallback((element: Element, rowKey: string, part: MeasuredPart): (() => void) => {
    if (typeof ResizeObserver === 'undefined') {
      return () => undefined;
    }
    observer.current ??= new ResizeObserver((entries) => {
      let viewportSize = 0;
      let lineSize = 0;
      const messageSizes = new Map<string, number>();
      for (const entry of entries) {
        const target = measured.current.get(entry.target);
        const size = entry.borderBoxSize?.[0]?.blockSize ?? entry.target.getBoundingClientRect().height;
        if (target === undefined || size <= 0) {
          continue;
        }
        if (target.part === 'viewport') {
          viewportSize = entry.target.clientHeight;
        } else if (target.part === 'line') {
          lineSize = size;
        } else {
          messageSizes.set(target.rowKey, size);
        }
      }
      if (viewportSize > 0) {
        setViewportHeight(viewportSize);
      }
      if (lineSize > 0) {
        setRowHeight((known) => (Math.abs(known - lineSize) < 0.5 ? known : lineSize));
      }
      if (messageSizes.size > 0) {
        setMessageHeights((known) => {
          let next: Map<string, number> | null = null;
          for (const [rowKey, size] of messageSizes) {
            if (Math.abs((known.get(rowKey) ?? -1) - size) >= 0.5) {
              next ??= new Map(known);
              next.set(rowKey, size);
            }
          }
          return next ?? known;
        });
      }
    });
    measured.current.set(element, { rowKey, part });
    observer.current.observe(element, { box: part === 'viewport' ? 'content-box' : 'border-box' });
    return () => {
      measured.current.delete(element);
      observer.current?.unobserve(element);
    };
  }, []);

  // Reports the scroller's view size.
  useLayoutEffect(() => (scroller.current === null ? undefined : track(scroller.current, '', 'viewport')), [track]);

  // Stops every size report when the grid unmounts.
  useEffect(
    () => () => {
      observer.current?.disconnect();
      observer.current = null;
    },
    [],
  );

  /** Queues a changed message row to show in this commit, unless the row first renders for a line the grid already held, scrolled into the window. */
  const onMessagesShown = useCallback((rowKey: string, mounted: boolean, rows: MessageRows) => {
    if (mounted) {
      const committed = committedKeys.current;
      committed.known ??= new Set(committed.keys);
      if (committed.known.has(rowKey)) {
        return;
      }
    }
    revealQueue.current.push(rows);
  }, []);

  // Shows the message rows queued in this commit and records the lines it rendered.
  useLayoutEffect(() => {
    committedKeys.current = { keys: layout.keys, known: null };
    const queued = revealQueue.current;
    revealQueue.current = [];
    if (queued.length > 0 && scroller.current !== null) {
      revealMessageRows(scroller.current, queued);
    }
  });

  /** Applies the scroller's position to the rendered lines synchronously. */
  const onScroll = (event: UIEvent<HTMLDivElement>) => {
    const top = event.currentTarget.scrollTop;
    flushSync(() => setScrollTop(top));
  };

  // Ends a pointer press that started in the grid (D-137).
  useEffect(() => {
    const release = () => {
      pointerPressed.current = false;
    };
    window.addEventListener('pointerup', release, true);
    window.addEventListener('pointercancel', release, true);
    window.addEventListener('blur', release);
    return () => {
      window.removeEventListener('pointerup', release, true);
      window.removeEventListener('pointercancel', release, true);
      window.removeEventListener('blur', release);
    };
  }, []);

  /** Shows a keyboard-focused grid control fully inside the scroller; pointer focus does not scroll (D-137). */
  const onFocus = (event: FocusEvent<HTMLDivElement>) => {
    const target = event.target;
    if (target instanceof HTMLElement && target !== event.currentTarget && !pointerPressed.current && target.matches(':focus-visible')) {
      revealFocusedControl(event.currentTarget, target);
    }
  };

  /** Jumps to the first or last line on Home or End and renders that window before the new position is painted (D-181). */
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (
      (event.key !== 'Home' && event.key !== 'End') ||
      event.altKey ||
      event.shiftKey ||
      event.metaKey ||
      event.defaultPrevented ||
      handlesHomeEnd(event.target)
    ) {
      return;
    }
    event.preventDefault();
    const element = event.currentTarget;
    const toEnd = event.key === 'End';
    element.scrollTop = toEnd ? element.scrollHeight - element.clientHeight : 0;
    const top = element.scrollTop;
    flushSync(() => setScrollTop(top));
    if (toEnd) {
      // Re-clamps to the end once the rendered lines are laid out.
      element.scrollTop = element.scrollHeight - element.clientHeight;
      const settled = element.scrollTop;
      if (settled !== top) {
        flushSync(() => setScrollTop(settled));
      }
    }
  };

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

  const queueValidation = useCallback(
    (index: number, target: LineTarget) => setPending((queued) => [...queued, { index, target }]),
    [],
  );

  const removeLine = useCallback(
    (index: number) => {
      onRemoveLine(index);
      addLineButton.current?.focus();
    },
    [onRemoveLine],
  );

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
      <div
        ref={scroller}
        className="lines-grid-scroll"
        role="region"
        aria-label="Scrollable invoice lines"
        tabIndex={0}
        onScroll={onScroll}
        onFocus={onFocus}
        onKeyDown={onKeyDown}
        onPointerDown={() => {
          pointerPressed.current = true;
        }}
      >
        <table className="lines-grid" aria-label="Invoice lines" aria-rowcount={lineCount === 0 ? 2 : layout.rowCount}>
          <colgroup>
            {COLUMN_CLASSES.map((sizeClass, position) => (
              <col key={position} className={sizeClass} />
            ))}
          </colgroup>
          <thead>
            <tr aria-rowindex={1}>
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
              <tr aria-rowindex={2}>
                <td className="lines-grid-empty" colSpan={COLUMN_CLASSES.length}>
                  <span>{editable ? 'No service lines. Use Add line or Import Request to add one.' : 'No service lines.'}</span>
                </td>
              </tr>
            )}
            {parts.map((part) => {
              if (part.kind === 'gap') {
                // Spacer holding the size of the lines not rendered (D-181).
                return (
                  <tr key={`gap-${part.ordinal}`} className="lines-spacer" aria-hidden="true">
                    <td colSpan={COLUMN_CLASSES.length} style={{ blockSize: `${part.size}px` }} />
                  </tr>
                );
              }
              const index = part.index;
              const line = lines[index];
              const clientId = line.clientId !== null && line.clientId !== '' ? line.clientId : null;
              return (
                <MemoLineRow
                  key={layout.keys[index]}
                  state={state}
                  dispatch={dispatch}
                  line={line}
                  index={index}
                  rowKey={layout.keys[index]}
                  rowIndex={layout.rowIndexes[index]}
                  isCurrent={index === current}
                  display={fromSavedView ? savedLineDisplay(state, index) : clientId === null ? undefined : state.lineDisplay[clientId]}
                  priceAllowed={serverAllowsPrice(state, line)}
                  placed={placedByLine.get(index) ?? NO_MESSAGES}
                  editable={editable}
                  fromSavedView={fromSavedView}
                  previewLine={fromSavedView || clientId === null ? undefined : previewLines.get(clientId)}
                  onValidateLine={onValidateLine}
                  onQueueValidation={queueValidation}
                  onRemoveLine={removeLine}
                  onOpenCategory={setCategoryTarget}
                  onMessagesShown={onMessagesShown}
                  onTrack={track}
                />
              );
            })}
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
