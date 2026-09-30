import { createContext, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { UIEvent } from 'react';
import { ApiError, getLov } from '../api/client';
import type { LovBinds as ApiLovBinds, LovResponse } from '../api/types';
import FieldMessage from './FieldMessage';
import OpenItemNotice from './OpenItemNotice';

/** The LOVs the picker can open. */
export type LovName = 'COMPANY1_2' | 'SUB_COMPANY' | 'THE_CLASS' | 'PAY_TYPE1' | 'PAY_TYPE2' | 'DOC' | 'RESERV_NO' | 'OFFERS' | 'CAT';

/** Bind values passed to getLov. */
export type LovBinds = ApiLovBinds;

/** Receives each LOV request's Oracle availability: false on a 503, true when its rows arrive. */
export const LovConnectivityContext = createContext<((available: boolean) => void) | null>(null);

type LovRow = LovResponse['rows'][number];

type LovPickerProps = {
  name: LovName;
  binds?: LovBinds;
  onPick: (row: LovRow) => void;
  onClose: () => void;
};

/** A response row with its display text per column and the lower-cased text the filter matches. */
type PreparedRow = { row: LovRow; index: number; texts: string[]; needles: string[] };

/** The prepared rows of one response and each column's share of the table width in percent. */
type PreparedRows = { rows: PreparedRow[]; widths: number[] };

/** One rendered body row, or a gap standing in for unrendered rows. */
type WindowPart = { kind: 'row'; position: number } | { kind: 'gap'; rows: number; ordinal: number };

/** Rows rendered above and below the scroll viewport. */
const OVERSCAN = 6;

/** Rows rendered before the scroll viewport is measured. */
const INITIAL_ROW_COUNT = 25;

/** Row and header height in pixels until the rendered table is measured. */
const FALLBACK_ROW_HEIGHT = 30;

/** Widest column in characters, before padding. */
const MAX_COLUMN_CHARS = 40;

/** Characters added to each column width. */
const COLUMN_PADDING_CHARS = 2;

/** Dialog title per LOV, from the Form's LOV Title attribute. */
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

/** Displayed columns per LOV, in the Form's LOVColumnMapping order. */
const COLUMNS: Record<LovName, readonly string[]> = {
  COMPANY1_2: ['COMP_CODE', 'CURR_CODE', 'COMP_NAME'],
  SUB_COMPANY: ['COMP_CODE', 'COMP_NAME'],
  THE_CLASS: ['CLASS_CODE', 'CLASS_NAME'],
  PAY_TYPE1: ['PAY_TYPE_ID', 'PAY_TYPE_NAME'],
  PAY_TYPE2: ['PAY_TYPE_ID', 'PAY_TYPE_NAME'],
  DOC: ['DOCID', 'DOC_NAME', 'CLINICID', 'CLINICNAME'],
  RESERV_NO: ['RESERV_NO', 'THE_TIME', 'PATAINTNO', 'PATIENTNAME'],
  OFFERS: ['OFERID', 'OFFER_NAME'],
  CAT: ['CATID', 'CATDESC', 'STORE_ID'],
};

/** Elements the Tab key cycles through inside the dialog. */
const FOCUSABLE_SELECTOR = 'input:not([tabindex="-1"]), button:not([tabindex="-1"]), [tabindex="0"]';

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

/** Returns each response row's display and filter texts, and each column's share of the table width in percent. */
function prepareRows(response: LovResponse | null, columns: readonly string[]): PreparedRows {
  const longest = columns.map((column) => column.length);
  const rows = (response?.rows ?? []).map((row, index) => {
    const texts = columns.map((column) => cellText(row, column));
    texts.forEach((text, column) => {
      longest[column] = Math.max(longest[column], text.length);
    });
    return { row, index, texts, needles: texts.map((text) => text.toLowerCase()) };
  });
  const chars = longest.map((length) => Math.min(MAX_COLUMN_CHARS, length) + COLUMN_PADDING_CHARS);
  const totalChars = chars.reduce((sum, width) => sum + width, 0);
  return { rows, widths: chars.map((width) => (width / totalChars) * 100) };
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

/** Renders a failed getLov call as field-level messages. */
function LovError({ reason }: { reason: unknown }) {
  if (reason instanceof ApiError) {
    if (reason.type === 'field-validation' && reason.messages.length > 0) {
      return <FieldMessage messages={reason.messages} />;
    }
    return (
      <FieldMessage
        messages={[]}
        fieldError={{ text: reason.legacyText ?? reason.message, oracleErrorNumber: reason.oracleErrorNumber }}
      />
    );
  }
  return <FieldMessage messages={[]} fieldError={{ text: String(reason), oracleErrorNumber: null }} />;
}

/** Modal list of values for one served LOV; returns the chosen row to the host. */
export default function LovPicker({ name, binds = {}, onPick, onClose }: LovPickerProps) {
  const bindsKey = JSON.stringify(binds);
  const requestBinds = useMemo(() => JSON.parse(bindsKey) as LovBinds, [bindsKey]);

  const [opener] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null));
  const [loading, setLoading] = useState(true);
  const [response, setResponse] = useState<LovResponse | null>(null);
  const [failure, setFailure] = useState<{ reason: unknown } | null>(null);
  const [filter, setFilter] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [scroller, setScroller] = useState<HTMLDivElement | null>(null);
  const [firstVisible, setFirstVisible] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);
  const [rowHeight, setRowHeight] = useState(FALLBACK_ROW_HEIGHT);
  const [headerHeight, setHeaderHeight] = useState(FALLBACK_ROW_HEIGHT);
  const pendingFocus = useRef<number | null>(null);

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

  // Tracks the scroller's visible height.
  useLayoutEffect(() => {
    if (scroller === null) {
      return;
    }
    const measure = () => {
      const height = scroller.clientHeight;
      setViewportHeight((current) => (current === height ? current : height));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(scroller);
    return () => observer.disconnect();
  }, [scroller]);

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

  /** Makes the row at `position` active and scrolls it into view; `focus` moves focus to its select button. */
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

  const close = () => {
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
      <td key={columns[column]} title={text === '' ? undefined : text}>
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
        className={active ? 'lov-row lov-row-selected' : 'lov-row'}
        aria-rowindex={position + 2}
        onClick={() => choose(item.row)}
      >
        {cells}
      </tr>
    );
  };

  const table =
    response === null ? null : (
      <table className="lov-table" aria-rowcount={total + 1}>
        <colgroup>
          {columns.map((column, index) => (
            <col key={column} style={{ inlineSize: `${prepared.widths[index]}%` }} />
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
    <div className="modal-backdrop" onClick={close}>
      <div
        className="modal"
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
        <div className="modal-title" id={titleId}>
          {TITLES[name]}
        </div>
        <div className="modal-body">
          <input
            className="lov-filter"
            name="lov-filter"
            type="text"
            autoFocus
            autoComplete="off"
            placeholder="Filter"
            aria-label="Filter"
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
          {failure !== null && <LovError reason={failure.reason} />}
          {response !== null && <FieldMessage messages={response.messages} />}
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
            </div>
          )}
          {response !== null && total === 0 && <div role="status">No rows</div>}
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
