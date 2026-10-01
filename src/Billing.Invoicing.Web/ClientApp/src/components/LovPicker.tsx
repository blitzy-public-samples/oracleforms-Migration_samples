import { createContext, useContext, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { InputHTMLAttributes, UIEvent } from 'react';
import { ApiError, getLov } from '../api/client';
import type { LovBinds as ApiLovBinds, LovResponse, MessageDto } from '../api/types';
import FieldMessage, { fieldMessageRefs } from './FieldMessage';
import OpenItemNotice from './OpenItemNotice';

/** The LOVs the picker can open. */
export type LovName = 'COMPANY1_2' | 'SUB_COMPANY' | 'THE_CLASS' | 'PAY_TYPE1' | 'PAY_TYPE2' | 'DOC' | 'RESERV_NO' | 'OFFERS' | 'CAT';

/** Bind values passed to getLov. */
export type LovBinds = ApiLovBinds;

/** Receives each LOV request's Oracle availability: false on a 503, true when its rows arrive. */
export const LovConnectivityContext = createContext<((available: boolean) => void) | null>(null);

/** Props of a read-only free-text input: the full value as title, automatic direction, the start shown on focus, and Home / End scrolling. */
export function readOnlyTextProps(
  value: string,
): Pick<InputHTMLAttributes<HTMLInputElement>, 'title' | 'dir' | 'onFocus' | 'onKeyDown'> {
  return {
    title: value === '' ? undefined : value,
    dir: 'auto',
    onFocus: (event) => {
      event.currentTarget.scrollLeft = 0;
    },
    onKeyDown: (event) => {
      if (event.ctrlKey || event.altKey || event.metaKey) {
        return;
      }
      const input = event.currentTarget;
      if (event.key === 'Home') {
        event.preventDefault();
        input.scrollLeft = 0;
      } else if (event.key === 'End') {
        event.preventDefault();
        input.scrollLeft = getComputedStyle(input).direction === 'rtl' ? -input.scrollWidth : input.scrollWidth;
      }
    },
  };
}

type LovRow = LovResponse['rows'][number];

type LovPickerProps = {
  name: LovName;
  binds?: LovBinds;
  onPick: (row: LovRow) => void;
  onClose: () => void;
};

/** A response row with its display text per column and the lower-cased text the filter matches. */
type PreparedRow = { row: LovRow; index: number; texts: string[]; needles: string[] };

/** The prepared rows of one response, and per column its longest texts, longest first, and every distinct text while none is longer than SHORT_VALUE_CHARS (null after). */
type PreparedRows = { rows: PreparedRow[]; samples: string[][]; shortTexts: (Set<string> | null)[] };

/** Per column, in pixels with padding and border: the minimum, the width that keeps the header whole, and the natural width. */
type ColumnLevels = { hard: number[]; soft: number[]; natural: number[] };

/** Column widths in pixels, and the table width when the minimums overflow the scroller. */
type ColumnLayout = { widths: number[]; tableWidth: number | null };

/** One rendered body row, or a gap standing in for unrendered rows. */
type WindowPart = { kind: 'row'; position: number } | { kind: 'gap'; rows: number; ordinal: number };

/** Rows rendered above and below the scroll viewport. */
const OVERSCAN = 6;

/** Rows rendered before the scroll viewport is measured. */
const INITIAL_ROW_COUNT = 25;

/** Row and header height in pixels until the rendered table is measured. */
const FALLBACK_ROW_HEIGHT = 30;

/** Characters of a value that count toward its column's natural width. */
const MAX_COLUMN_CHARS = 40;

/** Longest value, in characters, of a column whose values are never cut. */
const SHORT_VALUE_CHARS = 12;

/** Characters every column keeps visible. */
const MIN_COLUMN_CHARS = 4;

/** Longest texts per column measured for its width. */
const WIDTH_SAMPLES = 8;

/** Pixels added to each measured text width. */
const TEXT_SLACK_PX = 1;

/** Milliseconds after opening during which a backdrop click does not close the picker. */
const BACKDROP_CLOSE_DELAY_MS = 500;

/** Dialog titles from the Form; untitled payment LOVs use their LOV names. */
const TITLES: Record<LovName, string> = {
  COMPANY1_2: 'Select Company',
  SUB_COMPANY: 'Select Company',
  THE_CLASS: 'Select Class',
  PAY_TYPE1: 'PAY_TYPE1',
  PAY_TYPE2: 'PAY_TYPE2',
  DOC: 'Doctor',
  RESERV_NO: 'Select From List',
  OFFERS: 'Select',
  CAT: 'Select Category',
};

/** Displayed columns per LOV, in the Form's LOVColumnMapping order; RESERV_NO leaves out PATIENTNAME and the hidden row id (D-105). */
const COLUMNS: Record<LovName, readonly string[]> = {
  COMPANY1_2: ['COMP_CODE', 'CURR_CODE', 'COMP_NAME'],
  SUB_COMPANY: ['COMP_CODE', 'COMP_NAME'],
  THE_CLASS: ['CLASS_CODE', 'CLASS_NAME'],
  PAY_TYPE1: ['PAY_TYPE_ID', 'PAY_TYPE_NAME'],
  PAY_TYPE2: ['PAY_TYPE_ID', 'PAY_TYPE_NAME'],
  DOC: ['DOCID', 'DOC_NAME', 'CLINICID', 'CLINICNAME'],
  RESERV_NO: ['RESERV_NO', 'THE_TIME', 'PATAINTNO'],
  OFFERS: ['OFERID', 'OFFER_NAME'],
  CAT: ['CATID', 'CATDESC', 'STORE_ID'],
};

/** Elements the Tab key cycles through inside the dialog. */
const FOCUSABLE_SELECTOR = 'input:not([tabindex="-1"]), button:not([tabindex="-1"]), [tabindex="0"]';

/** Background elements left outside `inert`: scripts and the page-level live regions appended to the body. */
const INERT_EXEMPT_SELECTOR = 'script, body > [aria-live]';

/** One open modal: its backdrop, the release of its inert background while it is topmost, and its focused control while covered. */
type ModalLayer = { element: HTMLElement; release: (() => void) | null; focus: HTMLElement | null };

/** Open modals in opening order; only the last one keeps its background inert. */
const modalLayers: ModalLayer[] = [];

/** Makes every sibling on the path from `element` up to the body inert, including siblings added later; returns an idempotent release. */
function markBackground(element: HTMLElement): () => void {
  const marked: Element[] = [];
  const observers: MutationObserver[] = [];
  const mark = (candidate: Node, keep: Element) => {
    if (candidate instanceof Element && candidate !== keep && !candidate.hasAttribute('inert') && !candidate.matches(INERT_EXEMPT_SELECTOR)) {
      candidate.setAttribute('inert', '');
      marked.push(candidate);
    }
  };
  let node: Element = element;
  while (node !== document.body && node.parentElement !== null) {
    const parent: Element = node.parentElement;
    const keep = node;
    for (const sibling of Array.from(parent.children)) {
      mark(sibling, keep);
    }
    const observer = new MutationObserver((records) => {
      for (const record of records) {
        record.addedNodes.forEach((added) => mark(added, keep));
      }
    });
    observer.observe(parent, { childList: true });
    observers.push(observer);
    node = parent;
  }
  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    for (const observer of observers) {
      observer.disconnect();
    }
    for (const candidate of marked) {
      candidate.removeAttribute('inert');
    }
  };
}

/** Moves focus back into a resumed modal when it is not already there: to its last focused control, else to its dialog. */
function refocusResumed(layer: ModalLayer, focus: HTMLElement | null): void {
  window.setTimeout(() => {
    const active = document.activeElement;
    if (layer.release === null || !layer.element.isConnected || (active !== null && layer.element.contains(active))) {
      return;
    }
    const target =
      focus !== null && focus.isConnected && layer.element.contains(focus)
        ? focus
        : layer.element.querySelector<HTMLElement>('[role="dialog"], [role="alertdialog"]');
    target?.focus();
  }, 0);
}

/** Makes the page behind the modal at `element` inert, suspending an already open modal until this one is released; returns an idempotent release. */
export function inertBackground(element: HTMLElement): () => void {
  const covered = modalLayers.at(-1);
  if (covered !== undefined && covered.release !== null) {
    const active = document.activeElement;
    covered.focus = active instanceof HTMLElement && covered.element.contains(active) ? active : null;
    covered.release();
    covered.release = null;
  }
  const layer: ModalLayer = { element, release: markBackground(element), focus: null };
  modalLayers.push(layer);
  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    layer.release?.();
    layer.release = null;
    const position = modalLayers.indexOf(layer);
    if (position === -1) {
      return;
    }
    modalLayers.splice(position, 1);
    const resumed = modalLayers.at(-1);
    if (position !== modalLayers.length || resumed === undefined || resumed.release !== null || !resumed.element.isConnected) {
      return;
    }
    resumed.release = markBackground(resumed.element);
    const focus = resumed.focus;
    resumed.focus = null;
    refocusResumed(resumed, focus);
  };
}

/** Returns a row's value for an upper-case column key as text, matching the key case-insensitively. */
function cellText(row: LovRow, column: string): string {
  let value: unknown = Object.hasOwn(row, column) ? row[column] : undefined;
  if (value === undefined) {
    const key = Object.keys(row).find((candidate) => candidate.toUpperCase() === column);
    value = key === undefined ? undefined : row[key];
  }
  return value === null || value === undefined ? '' : String(value);
}

/** Keeps Tab focus inside the container, wrapping at either end; returns whether it moved focus. */
function wrapFocus(container: HTMLElement, backwards: boolean): boolean {
  const focusable = Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR));
  if (focusable.length === 0) {
    return false;
  }
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (document.activeElement === container) {
    (backwards ? last : first).focus();
    return true;
  }
  if (backwards && document.activeElement === first) {
    last.focus();
    return true;
  }
  if (!backwards && document.activeElement === last) {
    first.focus();
    return true;
  }
  return false;
}

/** Inserts `text` into `samples`, which keeps the WIDTH_SAMPLES longest distinct texts, longest first. */
function addSample(samples: string[], text: string) {
  if (text === '' || (samples.length === WIDTH_SAMPLES && text.length <= samples[WIDTH_SAMPLES - 1].length)) {
    return;
  }
  if (samples.includes(text)) {
    return;
  }
  const at = samples.findIndex((sample) => sample.length < text.length);
  samples.splice(at === -1 ? samples.length : at, 0, text);
  if (samples.length > WIDTH_SAMPLES) {
    samples.pop();
  }
}

/** Returns each response row's display and filter texts, and each column's longest texts and, while all are short, its distinct texts. */
function prepareRows(response: LovResponse | null, columns: readonly string[]): PreparedRows {
  const samples = columns.map((): string[] => []);
  const shortTexts = columns.map((): Set<string> | null => new Set<string>());
  const rows = (response?.rows ?? []).map((row, index) => {
    const texts = columns.map((column) => cellText(row, column));
    texts.forEach((text, column) => {
      addSample(samples[column], text);
      const short = shortTexts[column];
      if (short !== null) {
        if (text.length > SHORT_VALUE_CHARS) {
          shortTexts[column] = null;
        } else if (text !== '') {
          short.add(text);
        }
      }
    });
    return { row, index, texts, needles: texts.map((text) => text.toLowerCase()) };
  });
  return { rows, samples, shortTexts };
}

/** The 2D canvas context text is measured with: undefined until first needed, null where canvas is unavailable. */
let measuringContext: CanvasRenderingContext2D | null | undefined;

/** Returns the canvas font string of an element's computed font. */
function canvasFont(style: CSSStyleDeclaration): string {
  return `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
}

/** Returns an element's computed horizontal padding and border in pixels. */
function inlineSpacing(style: CSSStyleDeclaration): number {
  return [style.paddingInlineStart, style.paddingInlineEnd, style.borderInlineStartWidth, style.borderInlineEndWidth]
    .map((value) => Number.parseFloat(value))
    .reduce((sum, value) => sum + (Number.isFinite(value) ? value : 0), 0);
}

/** Returns each column's width levels from the header cells' fonts and spacing and the body font, or null without a 2D canvas. */
function measureColumns(
  headers: readonly HTMLTableCellElement[],
  body: Element,
  columns: readonly string[],
  prepared: PreparedRows,
): ColumnLevels | null {
  if (measuringContext === undefined) {
    measuringContext = document.createElement('canvas').getContext('2d');
  }
  const context = measuringContext;
  if (context === null) {
    return null;
  }
  const measure = (text: string) => Math.ceil(context.measureText(text).width) + TEXT_SLACK_PX;
  context.font = canvasFont(getComputedStyle(body));
  const floor = measure('0'.repeat(MIN_COLUMN_CHARS));
  const widest = prepared.samples.map((texts, column) => {
    let width = 0;
    for (const text of prepared.shortTexts[column] ?? texts) {
      width = Math.max(width, measure(text.slice(0, MAX_COLUMN_CHARS)));
    }
    return width;
  });
  const levels: ColumnLevels = { hard: [], soft: [], natural: [] };
  headers.forEach((header, column) => {
    const style = getComputedStyle(header);
    const spacing = inlineSpacing(style);
    context.font = canvasFont(style);
    const hard = spacing + Math.max(floor, prepared.shortTexts[column] !== null ? widest[column] : 0);
    const soft = Math.max(hard, spacing + measure(columns[column]));
    levels.hard.push(hard);
    levels.soft.push(soft);
    levels.natural.push(Math.max(soft, spacing + widest[column]));
  });
  return levels;
}

/** Returns column widths that fill `available` pixels, growing every column from its minimum toward its natural width. */
function layoutColumns(levels: ColumnLevels, available: number): ColumnLayout {
  const steps = [levels.hard, levels.soft, levels.natural];
  const totals = steps.map((widths) => widths.reduce((sum, width) => sum + width, 0));
  if (totals[0] >= available) {
    return { widths: levels.hard, tableWidth: totals[0] > available ? totals[0] : null };
  }
  if (totals[2] <= available) {
    return { widths: levels.natural.map((width) => (width * available) / totals[2]), tableWidth: null };
  }
  const step = totals[1] <= available ? 1 : 0;
  const share = (available - totals[step]) / (totals[step + 1] - totals[step]);
  return {
    widths: steps[step].map((width, column) => width + share * (steps[step + 1][column] - width)),
    tableWidth: null,
  };
}

/** Returns the rendered positions in order, with a gap for each run of unrendered rows. */
function windowParts(total: number, start: number, end: number, pinned: number | null): WindowPart[] {
  const positions: number[] = [];
  if (pinned !== null && pinned < start) {
    positions.push(pinned);
  }
  for (let position = start; position < end; position += 1) {
    positions.push(position);
  }
  if (pinned !== null && pinned >= end) {
    positions.push(pinned);
  }
  const parts: WindowPart[] = [];
  let next = 0;
  let gaps = 0;
  for (const position of positions) {
    if (position > next) {
      parts.push({ kind: 'gap', rows: position - next, ordinal: gaps });
      gaps += 1;
    }
    parts.push({ kind: 'row', position });
    next = position + 1;
  }
  if (total > next) {
    parts.push({ kind: 'gap', rows: total - next, ordinal: gaps });
  }
  return parts;
}

/** Returns the accessible name of a row's select button from its non-empty cell texts. */
function pickLabel(texts: readonly string[], position: number): string {
  const shown = texts.filter((text) => text.trim() !== '');
  return shown.length === 0 ? `Select row ${position + 1}` : `Select ${shown.join(', ')}`;
}

/** Returns whether a failed getLov call reports Oracle as unavailable. */
function isOracleUnavailable(reason: unknown): boolean {
  return reason instanceof ApiError && (reason.status === 503 || reason.type === 'oracle-unavailable');
}

/** Messages and mapped Oracle error that a failed getLov call renders. */
type LovFailureParts = { messages: MessageDto[]; fieldError: { text: string; oracleErrorNumber: number | null } | null };

/** Returns the field-level messages, else the mapped error, of a failed getLov call. */
function lovFailureParts(reason: unknown): LovFailureParts {
  if (reason instanceof ApiError) {
    if (reason.type === 'field-validation' && reason.messages.length > 0) {
      return { messages: reason.messages, fieldError: null };
    }
    return { messages: [], fieldError: { text: reason.legacyText ?? reason.message, oracleErrorNumber: reason.oracleErrorNumber } };
  }
  return { messages: [], fieldError: { text: String(reason), oracleErrorNumber: null } };
}

/** Modal list of values for one served LOV; returns the chosen row to the host. */
export default function LovPicker({ name, binds = {}, onPick, onClose }: LovPickerProps) {
  const bindsKey = JSON.stringify(binds);
  const requestBinds = useMemo(() => JSON.parse(bindsKey) as LovBinds, [bindsKey]);

  const [opener] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null));
  const [openedAt] = useState(() => performance.now());
  const backdropPressed = useRef(false);
  const [loading, setLoading] = useState(true);
  const [response, setResponse] = useState<LovResponse | null>(null);
  const [failure, setFailure] = useState<{ reason: unknown } | null>(null);
  const [filter, setFilter] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [scroller, setScroller] = useState<HTMLDivElement | null>(null);
  const [firstVisible, setFirstVisible] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);
  const [viewportWidth, setViewportWidth] = useState(0);
  const [columnLevels, setColumnLevels] = useState<ColumnLevels | null>(null);
  const [rowHeight, setRowHeight] = useState(FALLBACK_ROW_HEIGHT);
  const [headerHeight, setHeaderHeight] = useState(FALLBACK_ROW_HEIGHT);
  const pendingFocus = useRef<number | null>(null);
  const listId = useId();
  const backdropRef = useRef<HTMLDivElement>(null);
  const releaseBackground = useRef<(() => void) | null>(null);

  // Makes the page behind the dialog inert while it is open.
  useLayoutEffect(() => {
    if (backdropRef.current === null) {
      return undefined;
    }
    const release = inertBackground(backdropRef.current);
    releaseBackground.current = release;
    return release;
  }, []);

  const reportConnectivity = useContext(LovConnectivityContext);
  const latestReportConnectivity = useRef(reportConnectivity);
  latestReportConnectivity.current = reportConnectivity;

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    setLoading(true);
    setResponse(null);
    setFailure(null);
    setActiveIndex(0);
    setFirstVisible(0);
    getLov(name, requestBinds, controller.signal).then(
      (result) => {
        if (!cancelled) {
          setResponse(result);
          setLoading(false);
          latestReportConnectivity.current?.(true);
        }
      },
      (reason: unknown) => {
        if (!cancelled) {
          setFailure({ reason });
          setLoading(false);
          if (isOracleUnavailable(reason)) {
            latestReportConnectivity.current?.(false);
          }
        }
      },
    );
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [name, requestBinds]);

  const columns = COLUMNS[name];
  const titleId = `lov-picker-title-${name}`;

  const prepared = useMemo(() => prepareRows(response, columns), [response, columns]);

  const filtered = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (needle === '') {
      return prepared.rows;
    }
    return prepared.rows.filter((item) => item.needles.some((text) => text.includes(needle)));
  }, [prepared, filter]);

  const selectable = response !== null && !response.viewOnly;
  const total = filtered.length;
  const currentIndex = Math.max(0, Math.min(activeIndex, total - 1));
  const start = Math.min(total, Math.max(0, firstVisible - OVERSCAN));
  const end =
    viewportHeight > 0
      ? Math.min(total, firstVisible + Math.ceil(viewportHeight / rowHeight) + 1 + OVERSCAN)
      : Math.min(total, start + INITIAL_ROW_COUNT);
  const pinned = selectable && total > 0 && (currentIndex < start || currentIndex >= end) ? currentIndex : null;
  const pageSize = Math.max(1, Math.floor((viewportHeight - headerHeight) / rowHeight));

  // Tracks the scroller's visible height and width.
  useLayoutEffect(() => {
    if (scroller === null) {
      return;
    }
    const measure = () => {
      const height = scroller.clientHeight;
      const width = scroller.clientWidth;
      setViewportHeight((current) => (current === height ? current : height));
      setViewportWidth((current) => (current === width ? current : width));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(scroller);
    return () => observer.disconnect();
  }, [scroller]);

  // Measures each column's width levels once per response, from the rendered header and body cells.
  useLayoutEffect(() => {
    const headers = Array.from(scroller?.querySelectorAll<HTMLTableCellElement>('thead th') ?? []);
    const body = scroller?.querySelector('tbody > tr.lov-row > td') ?? scroller?.querySelector('tbody') ?? null;
    setColumnLevels(
      body !== null && headers.length === columns.length ? measureColumns(headers, body, columns, prepared) : null,
    );
  }, [scroller, prepared, columns]);

  const columnLayout = useMemo(
    () => (columnLevels === null || viewportWidth <= 0 ? null : layoutColumns(columnLevels, viewportWidth)),
    [columnLevels, viewportWidth],
  );

  // Measures the rendered header and body-row heights.
  useLayoutEffect(() => {
    if (scroller === null) {
      return;
    }
    const header = scroller.querySelector('thead')?.getBoundingClientRect().height ?? 0;
    if (header > 0 && Math.abs(header - headerHeight) >= 0.5) {
      setHeaderHeight(header);
    }
    const row = scroller.querySelector('tbody > tr.lov-row')?.getBoundingClientRect().height ?? 0;
    if (row > 0 && Math.abs(row - rowHeight) >= 0.5) {
      setRowHeight(row);
    }
  });

  // Focuses the select button a keyboard move targeted once its row is rendered.
  useLayoutEffect(() => {
    const target = pendingFocus.current;
    if (target === null || scroller === null) {
      return;
    }
    pendingFocus.current = null;
    scroller.querySelector<HTMLElement>(`tr[aria-rowindex="${target + 2}"] .lov-pick`)?.focus({ preventScroll: true });
  });

  /** Scrolls the list so the row at `position` shows below the sticky header. */
  const reveal = (position: number) => {
    if (scroller === null) {
      return;
    }
    const top = position * rowHeight;
    const bottom = headerHeight + (position + 1) * rowHeight;
    if (top < scroller.scrollTop) {
      scroller.scrollTop = top;
    } else if (bottom > scroller.scrollTop + scroller.clientHeight) {
      scroller.scrollTop = bottom - scroller.clientHeight;
    }
    setFirstVisible(Math.floor(scroller.scrollTop / rowHeight));
  };

  /** Moves to a row, optionally focusing its select button when the active row changes. */
  const moveTo = (position: number, focus: boolean) => {
    if (total === 0) {
      return;
    }
    const next = Math.max(0, Math.min(position, total - 1));
    reveal(next);
    if (next !== currentIndex) {
      setActiveIndex(next);
      if (focus) {
        pendingFocus.current = next;
      }
    }
  };

  /** Returns the row position a navigation key moves to from `position`, or null for other keys. */
  const keyTarget = (key: string, position: number): number | null => {
    switch (key) {
      case 'ArrowDown':
        return position + 1;
      case 'ArrowUp':
        return position - 1;
      case 'Home':
        return 0;
      case 'End':
        return total - 1;
      case 'PageDown':
        return position + pageSize;
      case 'PageUp':
        return position - pageSize;
      default:
        return null;
    }
  };

  const onScroll = (event: UIEvent<HTMLDivElement>) => {
    setFirstVisible(Math.floor(event.currentTarget.scrollTop / rowHeight));
  };

  const tableId = `${listId}-grid`;
  const failureMessageId = `${listId}-failure-msg`;
  const responseMessageId = `${listId}-msg`;
  const failureParts = failure === null ? null : lovFailureParts(failure.reason);
  const filterDescribedBy =
    [
      failureParts === null ? undefined : fieldMessageRefs(failureMessageId, failureParts.messages, failureParts.fieldError).describedBy,
      response === null ? undefined : fieldMessageRefs(responseMessageId, response.messages).describedBy,
    ]
      .filter((ids): ids is string => ids !== undefined)
      .join(' ') || undefined;

  /** Id of the rendered body row of a response row. */
  const rowId = (item: PreparedRow): string => `${listId}-row-${item.index}`;

  const close = () => {
    releaseBackground.current?.();
    onClose();
    if (opener !== null && opener.isConnected) {
      opener.focus();
    }
  };

  const choose = (row: LovRow) => {
    onPick(row);
    close();
  };

  /** Renders the body row at `position` of the filtered rows. */
  const renderRow = (position: number) => {
    const item = filtered[position];
    const active = selectable && position === currentIndex;
    const cells = item.texts.map((text, column) => (
      <td key={columns[column]} dir="auto" title={text === '' ? undefined : text}>
        {selectable && column === 0 ? (
          <button
            type="button"
            className="lov-pick"
            tabIndex={active ? 0 : -1}
            aria-label={pickLabel(item.texts, position)}
            onFocus={() => setActiveIndex(position)}
            onKeyDown={(event) => {
              const target = keyTarget(event.key, position);
              if (target !== null) {
                event.preventDefault();
                moveTo(target, true);
              }
            }}
          >
            {text}
          </button>
        ) : (
          text
        )}
      </td>
    ));
    if (!selectable) {
      return (
        <tr key={item.index} className="lov-row" aria-rowindex={position + 2}>
          {cells}
        </tr>
      );
    }
    return (
      <tr
        key={item.index}
        id={rowId(item)}
        className={active ? 'lov-row lov-row-selected' : 'lov-row'}
        aria-rowindex={position + 2}
        aria-selected={active}
        onClick={() => choose(item.row)}
      >
        {cells}
      </tr>
    );
  };

  const table =
    response === null ? null : (
      <table
        id={tableId}
        className="lov-table"
        role={selectable ? 'grid' : undefined}
        aria-labelledby={titleId}
        aria-rowcount={total + 1}
        style={
          columnLayout === null || columnLayout.tableWidth === null
            ? undefined
            : { inlineSize: `${columnLayout.tableWidth}px` }
        }
      >
        <colgroup>
          {columns.map((column, index) => (
            <col
              key={column}
              style={columnLayout === null ? undefined : { inlineSize: `${columnLayout.widths[index]}px` }}
            />
          ))}
        </colgroup>
        <thead>
          <tr aria-rowindex={1}>
            {columns.map((column) => (
              <th key={column} scope="col" title={column}>
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {windowParts(total, start, end, pinned).map((part) =>
            part.kind === 'row' ? (
              renderRow(part.position)
            ) : (
              <tr key={`gap-${part.ordinal}`} className="lov-spacer" aria-hidden="true">
                <td colSpan={columns.length} style={{ blockSize: `${part.rows * rowHeight}px` }} />
              </tr>
            ),
          )}
        </tbody>
      </table>
    );

  return (
    <div
      ref={backdropRef}
      className="modal-backdrop"
      onMouseDown={(event) => {
        backdropPressed.current = event.target === event.currentTarget;
        if (backdropPressed.current) {
          event.preventDefault();
        }
      }}
      onClick={(event) => {
        const pressed = backdropPressed.current;
        backdropPressed.current = false;
        if (
          pressed &&
          event.target === event.currentTarget &&
          performance.now() - openedAt >= BACKDROP_CLOSE_DELAY_MS
        ) {
          close();
        }
      }}
    >
      <div
        className="modal lov-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            close();
          } else if (event.key === 'Tab' && wrapFocus(event.currentTarget, event.shiftKey)) {
            event.preventDefault();
          }
        }}
      >
        <h2 className="modal-title" id={titleId}>
          {TITLES[name]}
        </h2>
        <div className="modal-body">
          <input
            className="lov-filter"
            name="lov-filter"
            type="text"
            autoFocus
            autoComplete="off"
            placeholder="Filter"
            aria-label="Filter"
            role={selectable ? 'combobox' : undefined}
            aria-expanded={selectable ? true : undefined}
            aria-autocomplete={selectable ? 'list' : undefined}
            aria-haspopup={selectable ? 'grid' : undefined}
            aria-controls={selectable ? tableId : undefined}
            aria-activedescendant={selectable && total > 0 ? rowId(filtered[currentIndex]) : undefined}
            aria-describedby={filterDescribedBy}
            value={filter}
            onChange={(event) => {
              setFilter(event.target.value);
              setActiveIndex(0);
              setFirstVisible(0);
              if (scroller !== null) {
                scroller.scrollTop = 0;
              }
            }}
            onKeyDown={(event) => {
              if (!selectable || total === 0) {
                return;
              }
              if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveTo(currentIndex + 1, false);
              } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                moveTo(currentIndex - 1, false);
              } else if (event.key === 'Enter') {
                event.preventDefault();
                choose(filtered[currentIndex].row);
              }
            }}
          />
          {loading && <div role="status">Loading…</div>}
          {failureParts !== null && (
            <FieldMessage id={failureMessageId} messages={failureParts.messages} fieldError={failureParts.fieldError} />
          )}
          {response !== null && <FieldMessage id={responseMessageId} messages={response.messages} />}
          {response !== null && <OpenItemNotice ids={response.openItems ?? []} />}
          {response !== null && (
            <div
              ref={setScroller}
              className={response.viewOnly ? 'lov-scroll lov-view-only' : 'lov-scroll'}
              tabIndex={response.viewOnly ? 0 : undefined}
              role={response.viewOnly ? 'region' : undefined}
              aria-label={response.viewOnly ? TITLES[name] : undefined}
              onScroll={onScroll}
            >
              {table}
              {total === 0 && (
                <div className="lov-empty" role="status">
                  No rows
                </div>
              )}
            </div>
          )}
        </div>
        <div className="modal-actions">
          <button type="button" onClick={close}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
