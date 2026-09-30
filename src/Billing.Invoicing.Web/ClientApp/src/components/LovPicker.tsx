import { useEffect, useMemo, useState } from 'react';
import { ApiError, getLov } from '../api/client';
import type { LovBinds as ApiLovBinds, LovResponse } from '../api/types';
import FieldMessage from './FieldMessage';

/** The LOVs the picker can open. */
export type LovName = 'COMPANY1_2' | 'SUB_COMPANY' | 'THE_CLASS' | 'PAY_TYPE1' | 'PAY_TYPE2' | 'DOC' | 'RESERV_NO' | 'OFFERS' | 'CAT';

/** Bind values passed to getLov. */
export type LovBinds = ApiLovBinds;

type LovRow = LovResponse['rows'][number];

type LovPickerProps = {
  name: LovName;
  binds?: LovBinds;
  onPick: (row: LovRow) => void;
  onClose: () => void;
};

type VisibleRow = { row: LovRow; index: number };

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
const FOCUSABLE_SELECTOR = 'input, button, [tabindex="0"]';

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

/** Scrolls an active row into the visible part of the list. */
function scrollIntoViewNearest(element: HTMLTableRowElement | null): void {
  element?.scrollIntoView({ block: 'nearest' });
}

/** Focuses a sibling list row, if there is one. */
function focusRow(row: Element | null): void {
  if (row instanceof HTMLElement) {
    row.focus();
  }
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

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setResponse(null);
    setFailure(null);
    setActiveIndex(0);
    getLov(name, requestBinds).then(
      (result) => {
        if (!cancelled) {
          setResponse(result);
          setLoading(false);
        }
      },
      (reason: unknown) => {
        if (!cancelled) {
          setFailure({ reason });
          setLoading(false);
        }
      },
    );
    return () => {
      cancelled = true;
    };
  }, [name, requestBinds]);

  const columns = COLUMNS[name];
  const titleId = `lov-picker-title-${name}`;

  const visibleRows = useMemo<VisibleRow[]>(() => {
    if (response === null) {
      return [];
    }
    const needle = filter.trim().toLowerCase();
    const rows = response.rows.map((row, index) => ({ row, index }));
    if (needle === '') {
      return rows;
    }
    return rows.filter(({ row }) => columns.some((column) => cellText(row, column).toLowerCase().includes(needle)));
  }, [response, columns, filter]);

  const selectable = response !== null && !response.viewOnly;
  const currentIndex = Math.max(0, Math.min(activeIndex, visibleRows.length - 1));

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

  const table =
    response === null ? null : (
      <table className="lov-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column} scope="col">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {visibleRows.map(({ row, index }, position) => {
            const cells = columns.map((column) => <td key={column}>{cellText(row, column)}</td>);
            if (!selectable) {
              return (
                <tr key={index} className="lov-row">
                  {cells}
                </tr>
              );
            }
            const active = position === currentIndex;
            return (
              <tr
                key={index}
                ref={active ? scrollIntoViewNearest : undefined}
                className={active ? 'lov-row lov-row-selected' : 'lov-row'}
                tabIndex={0}
                onMouseEnter={() => setActiveIndex(position)}
                onFocus={() => setActiveIndex(position)}
                onClick={() => choose(row)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    choose(row);
                  } else if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    focusRow(event.currentTarget.nextElementSibling);
                  } else if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    focusRow(event.currentTarget.previousElementSibling);
                  }
                }}
              >
                {cells}
              </tr>
            );
          })}
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
            }}
            onKeyDown={(event) => {
              if (!selectable || visibleRows.length === 0) {
                return;
              }
              if (event.key === 'ArrowDown') {
                event.preventDefault();
                setActiveIndex(Math.min(currentIndex + 1, visibleRows.length - 1));
              } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                setActiveIndex(Math.max(currentIndex - 1, 0));
              } else if (event.key === 'Enter') {
                event.preventDefault();
                choose(visibleRows[currentIndex].row);
              }
            }}
          />
          {loading && <div role="status">Loading…</div>}
          {failure !== null && <LovError reason={failure.reason} />}
          {response !== null && <FieldMessage messages={response.messages} />}
          {response !== null && (response.viewOnly ? <div className="lov-view-only">{table}</div> : table)}
          {response !== null && visibleRows.length === 0 && <div role="status">No rows</div>}
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
