import type { PreviewResponse } from '../api/types';
import type { InvoiceDraftState } from '../state/invoiceDraft';

type TotalsRow = {
  label: string;
  fromPreview: (preview: PreviewResponse) => unknown;
  savedKey: string | null;
  isStatus?: boolean;
  /** Announces the row politely when its value changes. */
  live?: boolean;
};

const ROWS: readonly TotalsRow[] = [
  { label: 'Total', fromPreview: (p) => p.totals?.totalGross, savedKey: null },
  { label: 'Discount', fromPreview: (p) => p.totals?.totalDiscount, savedKey: null },
  { label: 'Net', fromPreview: (p) => p.totals?.totalNet, savedKey: null },
  { label: 'Patient Share', fromPreview: (p) => p.totals?.patPay, savedKey: 'PAT_PAY' },
  { label: 'Credit', fromPreview: (p) => p.totals?.compPay, savedKey: 'COMP_PAY' },
  { label: 'VAT Pat.', fromPreview: (p) => p.totals?.vatTotalPat, savedKey: 'VAT_TOTAL_PAT' },
  { label: 'VAT Co.', fromPreview: (p) => p.totals?.vatTotalCo, savedKey: 'VAT_TOTAL_CO' },
  { label: 'Amount Due', fromPreview: (p) => p.totals?.cashCollected, savedKey: null, live: true },
  { label: 'Amount 1', fromPreview: (p) => p.totals?.amount1, savedKey: 'AMOUNT_1' },
  { label: 'Amount 2', fromPreview: (p) => p.totals?.amount2, savedKey: 'AMOUNT_2' },
  { label: 'Remaining', fromPreview: (p) => p.totals?.remainingAmount, savedKey: null },
  { label: 'Payment Status', fromPreview: (p) => p.totals?.paymentStatus, savedKey: null, isStatus: true, live: true },
  { label: 'Refund', fromPreview: (p) => p.refund, savedKey: 'REUND' },
  { label: 'Total Collected', fromPreview: (p) => p.totalCollected, savedKey: 'CASH_COLLECTED' },
];

/** Text of a returned value: empty for null or undefined, else the value as received. */
function show(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}

/** Saved-view values carried in the view's header rather than its display values. */
const SAVED_HEADER_FIELDS: Readonly<Partial<Record<string, 'amount1' | 'amount2'>>> = {
  AMOUNT_1: 'amount1',
  AMOUNT_2: 'amount2',
};

/** Shows the package-calculated invoice totals as returned. */
export default function TotalsPanel({ state }: { state: InvoiceDraftState }) {
  const saved = state.saved;
  const view = saved?.view ?? null;

  // A saved or queried invoice shows only its saved view; an unsaved draft shows its preview.
  const valueOf = (row: TotalsRow): string => {
    if (saved === null) {
      return state.preview !== null ? show(row.fromPreview(state.preview)) : '';
    }
    if (view === null || row.savedKey === null) {
      return '';
    }
    const savedDisplay = view.display ?? {};
    if (Object.hasOwn(savedDisplay, row.savedKey)) {
      return show(savedDisplay[row.savedKey]);
    }
    const headerField = SAVED_HEADER_FIELDS[row.savedKey];
    return headerField !== undefined ? show(view.header?.[headerField]) : '';
  };

  return (
    <div className="totals-panel" role="group" aria-label="Totals">
      <div className="panel-title">Totals</div>
      {ROWS.map((row) => (
        <div key={row.label} className="summary-row" role={row.live === true ? 'status' : undefined}>
          <span>{row.label}</span>
          <span className={row.isStatus === true ? 'payment-status' : 'read-only'}>{valueOf(row)}</span>
        </div>
      ))}
    </div>
  );
}
