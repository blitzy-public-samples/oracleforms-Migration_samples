import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { ApiError, newDraft } from './api/client';
import ConnectivityBanner from './components/ConnectivityBanner';
import InvoiceScreen from './screens/InvoiceScreen';
import MoreDetailsScreen from './screens/MoreDetailsScreen';
import { initialInvoiceDraftState, invoiceDraftReducer } from './state/invoiceDraft';
import type { InvoiceDraftAction } from './state/invoiceDraft';

/** Screen shown in the window: canvas CANVAS2 or canvas MORE. */
type Screen = 'invoice' | 'more';

/** Enabled controls the keyboard can reach. */
const FOCUSABLE =
  'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])';

/** Returns the error as an ApiError, wrapping any other thrown value. */
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

/** True when the action reports that Oracle is unavailable, as the draft reducer raises the banner for it. */
function reportsOutage(action: InvoiceDraftAction): boolean {
  if (action.type === 'connectivityLost') {
    return true;
  }
  if (action.type === 'errorReceived' || action.type === 'validationFailed') {
    return action.error.status === 503 || action.error.type === 'oracle-unavailable';
  }
  return false;
}

/** Root component: loads the draft once and switches between the Invoice and More details screens over it. */
export default function App() {
  const [state, dispatchDraft] = useReducer(invoiceDraftReducer, initialInvoiceDraftState);
  const outages = useRef(0);
  const [screen, setScreen] = useState<Screen>('invoice');
  const started = useRef(false);
  const shown = useRef<Screen>('invoice');
  const opener = useRef<HTMLElement | null>(null);
  const invoiceView = useRef<HTMLDivElement>(null);
  const moreView = useRef<HTMLDivElement>(null);

  /** Dispatches a draft action, counting the Oracle outages it reports. */
  const dispatch = useCallback((action: InvoiceDraftAction) => {
    if (reportsOutage(action)) {
      outages.current += 1;
    }
    dispatchDraft(action);
  }, []);

  /** Number of Oracle outages reported so far. */
  const outageCount = useCallback(() => outages.current, []);

  // Requests the first draft once, with the entry parameters of the page's query string.
  useEffect(() => {
    if (started.current) {
      return;
    }
    started.current = true;
    void (async () => {
      try {
        const response = await newDraft(window.location.search);
        if (response == null || response.draft == null) {
          throw new ApiError({
            status: 200,
            type: 'http-error',
            title: 'Invalid response',
            message: 'The new draft response carries no draft.',
          });
        }
        dispatch({ type: 'draftLoaded', response });
      } catch (error) {
        dispatch({ type: 'errorReceived', source: 'NEW', error: toApiError(error) });
      }
    })();
  }, []);

  // After a swap, focuses the control that opened MORE on return, else the first control of the shown screen.
  useEffect(() => {
    if (shown.current === screen) {
      return;
    }
    shown.current = screen;
    const back = screen === 'invoice' ? opener.current : null;
    const view = screen === 'invoice' ? invoiceView.current : moreView.current;
    const target =
      back !== null && back.isConnected && view?.contains(back) === true
        ? back
        : view?.querySelector<HTMLElement>(FOCUSABLE);
    target?.focus();
  }, [screen]);

  /** Shows the MORE screen, remembering the focused control to return to. */
  function showMore(): void {
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setScreen('more');
  }

  // Both screens stay mounted over the one draft; the inactive one is hidden (D-60).
  return (
    <div className="app">
      <ConnectivityBanner visible={state.connectivityDown} />
      <div ref={invoiceView} hidden={screen !== 'invoice'}>
        <InvoiceScreen state={state} dispatch={dispatch} onShowMore={showMore} />
      </div>
      <div ref={moreView} hidden={screen !== 'more'}>
        <MoreDetailsScreen
          state={state}
          dispatch={dispatch}
          active={screen === 'more'}
          outageCount={outageCount}
          onBack={() => setScreen('invoice')}
        />
      </div>
    </div>
  );
}
