import { useEffect, useRef } from 'react';
import type { MessageDto } from '../api/types';

type FieldMessageProps = {
  messages: MessageDto[];
  fieldError?: { text: string; oracleErrorNumber: number | null } | null;
  onDismiss?: (index: number) => void;
};

const ANNOUNCE_DELAY_MS = 100;
const ANNOUNCE_CLEAR_MS = 7000;

/** Enabled entry controls a dismissal can return focus to. */
const CONTROL = 'input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled)';

/** The page-level polite region that announces warnings. */
let politeRegion: HTMLDivElement | null = null;

/** Warning messages already announced by any rendered copy. */
const announcedMessages = new WeakSet<MessageDto>();

/** Returns the visually hidden polite region, appending it to the body when absent. */
function liveRegion(): HTMLDivElement {
  if (politeRegion === null || !politeRegion.isConnected) {
    const region = document.createElement('div');
    region.setAttribute('role', 'status');
    region.setAttribute('aria-live', 'polite');
    region.setAttribute('aria-atomic', 'false');
    region.style.cssText =
      'position:absolute;inline-size:1px;block-size:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip-path:inset(50%);white-space:nowrap;';
    document.body.append(region);
    politeRegion = region;
  }
  return politeRegion;
}

/** Announces a warning message once, whichever copy of it is shown first. */
function announceOnce(message: MessageDto): void {
  if (!announcedMessages.has(message)) {
    announcedMessages.add(message);
    announce(message.text);
  }
}

/** Adds the text to the polite region after a short delay and removes it again after a few seconds. */
function announce(text: string): void {
  const region = liveRegion();
  window.setTimeout(() => {
    for (const child of Array.from(region.children)) {
      if (child.textContent === text) {
        child.remove();
      }
    }
    const entry = document.createElement('div');
    entry.textContent = text;
    region.append(entry);
    window.setTimeout(() => entry.remove(), ANNOUNCE_CLEAR_MS);
  }, ANNOUNCE_DELAY_MS);
}

/** The labelled, else the first, enabled control of the `.field` enclosing the element; null outside a field. */
function owningControl(element: HTMLElement): HTMLElement | null {
  const field = element.closest('.field');
  if (field === null) {
    return null;
  }
  const label = field.querySelector(':scope > label');
  if (label instanceof HTMLLabelElement && label.control instanceof HTMLElement && label.control.matches(CONTROL)) {
    return label.control;
  }
  return field.querySelector<HTMLElement>(CONTROL);
}

/** When a dismissal leaves focus on the body, focuses the owning field's control, else the nearest remaining Dismiss button. */
function returnFocusAfterDismiss(button: HTMLElement): void {
  const owner = owningControl(button);
  const container = button.closest('.msg')?.parentElement ?? null;
  const position = container === null ? -1 : Array.from(container.querySelectorAll('.msg-dismiss')).indexOf(button);
  window.setTimeout(() => {
    const active = document.activeElement;
    if (active !== null && active !== document.body) {
      return;
    }
    if (owner !== null && owner.isConnected && owner.matches(CONTROL)) {
      owner.focus();
      return;
    }
    if (container !== null && container.isConnected) {
      const remaining = container.querySelectorAll<HTMLElement>('.msg-dismiss');
      if (remaining.length > 0) {
        remaining[Math.min(Math.max(position, 0), remaining.length - 1)].focus();
      }
    }
  }, 0);
}

/** Renders one field's blocking and warning messages and its mapped Oracle error. */
export default function FieldMessage({ messages, fieldError, onDismiss }: FieldMessageProps) {
  const seen = useRef(new Set<string>());
  const warningNodes = useRef(new Map<number, HTMLDivElement>());

  useEffect(() => {
    liveRegion();
  }, []);

  // Announces each warning once, when it is first shown on a visible screen; blocking messages stay alerts.
  useEffect(() => {
    const announced = new Set<string>();
    const waiting: { key: string; message: MessageDto; node: HTMLDivElement }[] = [];
    const occurrences = new Map<string, number>();
    messages.forEach((message, index) => {
      if (message.severity === 'Blocking') {
        return;
      }
      const occurrence = occurrences.get(message.text) ?? 0;
      occurrences.set(message.text, occurrence + 1);
      const key = `${message.text}\u0000${occurrence}`;
      const node = warningNodes.current.get(index);
      if (seen.current.has(key)) {
        announced.add(key);
      } else if (node !== undefined && node.getClientRects().length > 0) {
        announceOnce(message);
        announced.add(key);
      } else if (node !== undefined) {
        waiting.push({ key, message, node });
      }
    });
    seen.current = announced;
    if (waiting.length === 0) {
      return undefined;
    }
    // Warnings on a hidden screen are announced when that screen is shown.
    const observer = new ResizeObserver(() => {
      const shown = waiting.filter((entry) => entry.node.getClientRects().length > 0);
      for (const entry of shown) {
        if (!seen.current.has(entry.key)) {
          announceOnce(entry.message);
          seen.current.add(entry.key);
        }
        waiting.splice(waiting.indexOf(entry), 1);
        observer.unobserve(entry.node);
      }
      if (waiting.length === 0) {
        observer.disconnect();
      }
    });
    for (const entry of waiting) {
      observer.observe(entry.node);
    }
    return () => observer.disconnect();
  }, [messages]);

  if (messages.length === 0 && fieldError == null) {
    return null;
  }

  return (
    <>
      {messages.map((message, index) =>
        message.severity === 'Blocking' ? (
          <div key={index} className="msg msg-blocking" role="alert">
            <span>{message.text}</span>
          </div>
        ) : (
          <div
            key={index}
            className="msg msg-warning"
            ref={(node) => {
              if (node === null) {
                warningNodes.current.delete(index);
              } else {
                warningNodes.current.set(index, node);
              }
            }}
          >
            <span>{message.text}</span>
            {onDismiss !== undefined && (
              <button
                type="button"
                className="msg-dismiss"
                aria-label="Dismiss"
                onClick={(event) => {
                  returnFocusAfterDismiss(event.currentTarget);
                  onDismiss(index);
                }}
              >
                ×
              </button>
            )}
          </div>
        ),
      )}
      {fieldError != null && (
        <div className="msg msg-blocking" role="alert">
          {fieldError.oracleErrorNumber != null && (
            <span className="oracle-number">
              {`ORA-${String(Math.abs(fieldError.oracleErrorNumber)).padStart(5, '0')}`}
            </span>
          )}
          <span>{fieldError.text}</span>
        </div>
      )}
    </>
  );
}
