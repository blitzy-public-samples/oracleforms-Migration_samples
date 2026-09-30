const OPEN_ITEM_TITLES: Record<string, string> = {
  'OI-10': 'Store transfer (BIL_STOCK_POSTING)',
  'OI-11': 'Invoice print (BIL_REPORTS_PRINT)',
  'OI-12': 'Invoice SMS (BIL_MESSAGE)',
  'OI-20': 'Invoice total validation (VALIDATE_TOTAL_INV) not performed',
  'OI-21': 'Pre-authorisation (GET_ELLIGABILTY) not derived',
  'OI-22': 'Clinic age check (DAY_TO_DAYES)',
  'OI-23': 'Prior claim payments and deductible (GET_PAYID_VALUE)',
  'OI-24': 'Price plan (GET_PRICE_PLAN)',
  'OI-26': 'Legacy SMS transport (SEND_MESSAG)',
  'OI-31': 'Package consumption',
  'OI-32': 'Service admission sub-rules (OKA / CHK_ADV_CLASS)',
  'OI-33': 'Not saved in this build',
  'OI-42': 'Reservation time display (GET_HTFN2)',
  'OI-44': 'Store-transfer commit (SILENT_COMMET00)',
  'OI-45': 'Cash invoice report (inv_small_cash.jsp)',
  'OI-46': 'Detailed invoice report (inv_form2.jsp)',
  'OI-47': 'Patient card report (PAT_CARD_INV.jsp)',
  'OI-48': 'Iqama check report (iqama_check.jsp)',
  'OI-49': 'List print report (LIST1111.jsp)',
  'OI-56': 'Editing a saved invoice',
};

function ownText(source: Record<string, string> | undefined, id: string): string | undefined {
  if (source === undefined || !Object.hasOwn(source, id)) {
    return undefined;
  }
  const value: unknown = source[id];
  return typeof value === 'string' ? value : undefined;
}

/** Lists the open items an action or response reports as not available in this build. */
export default function OpenItemNotice({
  ids,
  serverMessages,
}: {
  ids: string[];
  serverMessages?: Record<string, string>;
}) {
  const uniqueIds = Array.from(new Set(ids));
  if (uniqueIds.length === 0) {
    return null;
  }

  return (
    <ul className="open-item-notice" role="status">
      {uniqueIds.map((id) => {
        const title = ownText(OPEN_ITEM_TITLES, id) ?? ownText(serverMessages, id);
        return (
          <li key={id}>
            {title === undefined
              ? `Not available in this build — open item ${id}`
              : `Not available in this build — open item ${id}: ${title}`}
          </li>
        );
      })}
    </ul>
  );
}
