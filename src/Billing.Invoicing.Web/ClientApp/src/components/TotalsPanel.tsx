import { decimalText } from '../api/client';
import type { PreviewResponse } from '../api/types';
import type { InvoiceDraftState } from '../state/invoiceDraft';

type TotalsRow = {
  label: string;
  fromPreview: (preview: PreviewResponse) => unknown;
  savedKey: string | null;
  /** Shown for an unsaved draft's preview only. */
  previewOnly?: boolean;
  isStatus?: boolean;
};

const ROWS: readonly TotalsRow[] = [
  { label: 'Total', fromPreview: (p) => p.totals?.totalGross, savedKey: 'TOTAL_GROSS' },
  { label: 'Discount', fromPreview: (p) => p.totals?.totalDiscount, savedKey: 'TOTAL_DISCOUNT' },
  { label: 'Net', fromPreview: (p) => p.totals?.totalNet, savedKey: 'TOTAL_NET' },
  { label: 'Patient Share', fromPreview: (p) => p.totals?.patPay, savedKey: 'PAT_PAY' },
  { label: 'Credit', fromPreview: (p) => p.totals?.compPay, savedKey: 'COMP_PAY' },
  { label: 'VAT Pat.', fromPreview: (p) => p.totals?.vatTotalPat, savedKey: 'VAT_TOTAL_PAT' },
  { label: 'VAT Co.', fromPreview: (p) => p.totals?.vatTotalCo, savedKey: 'VAT_TOTAL_CO' },
  { label: 'Amount Due', fromPreview: (p) => p.totals?.cashCollected, savedKey: 'CASH_COLLECTED' },
  { label: 'Amount 1', fromPreview: (p) => p.totals?.amount1, savedKey: 'AMOUNT_1' },
  { label: 'Amount 2', fromPreview: (p) => p.totals?.amount2, savedKey: 'AMOUNT_2' },
  { label: 'Remaining', fromPreview: (p) => p.totals?.remainingAmount, savedKey: null, previewOnly: true },
  { label: 'Payment Status', fromPreview: (p) => p.totals?.paymentStatus, savedKey: null, previewOnly: true, isStatus: true },
  { label: 'Refund', fromPreview: (p) => p.refund, savedKey: 'REUND' },
  { label: 'Total Collected', fromPreview: (p) => p.totalCollected, savedKey: 'TOTAL_COLLECTED' },
];

/** Text of a returned value: empty for null or undefined, a number or text as received (decimalText), else the value as text. */
function show(value: unknown): string {
  if (typeof value === 'number' || typeof value === 'string') {
    return decimalText(value);
  }
  return value === null || value === undefined ? '' : String(value);
}

/** Saved-view values carried in the view's header rather than its display values. */
const SAVED_HEADER_FIELDS: Readonly<Partial<Record<string, 'amount1' | 'amount2'>>> = {
  AMOUNT_1: 'amount1',
  AMOUNT_2: 'amount2',
};

/** Shows the package-calculated invoice totals as returned; `pending` marks them as being recalculated. */
export default function TotalsPanel({ state, pending = false }: { state: InvoiceDraftState; pending?: boolean }) {
  const saved = state.saved;
  const view = saved?.view ?? null;
  const rows = saved === null ? ROWS : ROWS.filter((row) => row.previewOnly !== true);

  // A saved or queried invoice shows each row's saved display value, else its header amount; an unsaved draft shows its preview.
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
    <div className={pending ? 'totals-panel totals-pending' : 'totals-panel'} role="group" aria-label="Totals" aria-busy={pending || undefined}>
      <div className="panel-title" role="heading" aria-level={2}>
        Totals
        {pending && <span className="pending-note">Recalculating…</span>}
      </div>
      {rows.map((row) => (
        <div key={row.label} className={row.isStatus === true ? 'summary-row summary-status' : 'summary-row'}>
          <span>{row.label}</span>
          <span className={row.isStatus === true ? 'payment-status' : 'read-only'}>{valueOf(row)}</span>
        </div>
      ))}
    </div>
  );
}
