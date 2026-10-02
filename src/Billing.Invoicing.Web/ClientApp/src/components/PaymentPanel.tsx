import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import type { Dispatch, KeyboardEvent } from 'react';
import { decimalText } from '../api/client';
import type { DecimalValue, DiscountLimitChoice, InvoiceHeaderDraft, MessageDto, ValidateTarget } from '../api/types';
import { entryErrorFor, fieldErrorFor, parseDecimalEntry } from '../state/invoiceDraft';
import type { InvoiceDraftAction, InvoiceDraftState } from '../state/invoiceDraft';
import FieldMessage, { fieldMessageRefs } from './FieldMessage';
import type { FieldMessageRefs } from './FieldMessage';
import LovPicker, { inertBackground, readOnlyTextProps } from './LovPicker';

type PaymentPanelProps = {
  state: InvoiceDraftState;
  dispatch: Dispatch<InvoiceDraftAction>;
  onValidate: (target: ValidateTarget) => void;
  /** Validation targets queued or in flight; their fields are marked pending. */
  busyTargets?: ReadonlySet<string>;
};

type PaymentLov = 'PAY_TYPE1' | 'PAY_TYPE2';

type NumericHeaderField = 'discT' | 'finalDiscPerc' | 'finalDisc' | 'subPayType' | 'subPayType2' | 'amount1' | 'amount2' | 'cashPayed';

type FieldFeedback = {
  messages: MessageDto[];
  sources: { source: string; index: number }[];
  fieldError: InvoiceDraftState['fieldErrors'][string] | null;
  rejected: boolean;
  invalid: boolean;
};

// Message sources rendered elsewhere: grid lines and the screen's import notices.
const LINE_SOURCE_PREFIX = 'LINE:';
const IMPORT_SOURCE = 'IMPORT';

const DISC_T_RATE = 1;
const DISC_T_VALUE = 0;

const DISCOUNT_LIMIT_TITLE = 'Maximum Discount';

const NO_TARGETS: ReadonlySet<string> = new Set();

// Method-row size classes: a narrow three-character code, then name and amount sharing the rest and wrapping when narrow (D-124).
const METHOD_CODE_CLASS = 'method-code';
const METHOD_FILL_CLASS = 'method-fill';

// Text shown for a value: '' for null or undefined, a number or text as returned (decimalText), otherwise the value as text.
function show(value: unknown): string {
  if (typeof value === 'number' || typeof value === 'string') {
    return decimalText(value);
  }
  return value == null ? '' : String(value);
}

// Display text of a picked or saved value, or null when absent.
function toText(value: unknown): string | null {
  return value == null ? null : String(value);
}

// Numeric form of a picked code, or null when absent or not a number.
function toNumber(value: unknown): number | null {
  if (value == null || (typeof value === 'string' && value.trim() === '')) {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

// Space-separated class list without empty entries.
function classes(...names: (string | false)[]): string | undefined {
  const list = names.filter((name): name is string => name !== false && name !== '');
  return list.length > 0 ? list.join(' ') : undefined;
}

// Messages from every header-level source addressed to `target`, with their origin, and the field's rejected entry or Oracle error (D-123).
function feedbackFor(state: InvoiceDraftState, target: string): FieldFeedback {
  const messages: MessageDto[] = [];
  const sources: FieldFeedback['sources'] = [];
  const collect = (source: string) => {
    (state.messages[source] ?? []).forEach((message, index) => {
      if (message.field === target) {
        messages.push(message);
        sources.push({ source, index });
      }
    });
  };
  collect(target);
  for (const source of Object.keys(state.messages)) {
    if (source !== target && source !== IMPORT_SOURCE && !source.startsWith(LINE_SOURCE_PREFIX)) {
      collect(source);
    }
  }
  const fieldError = fieldErrorFor(state, target);
  const invalid = fieldError !== null || messages.some((message) => message.severity === 'Blocking');
  return { messages, sources, fieldError, rejected: entryErrorFor(state, target) !== null, invalid };
}

type FeedbackProps = {
  id: string;
  feedback: FieldFeedback;
  locked: boolean;
  dispatch: Dispatch<InvoiceDraftAction>;
};

// FieldMessage wired to dismiss each message at its originating source; no dismissal while locked.
function Feedback({ id, feedback, locked, dispatch }: FeedbackProps) {
  return (
    <FieldMessage
      id={id}
      messages={feedback.messages}
      fieldError={feedback.fieldError}
      onDismiss={
        locked
          ? undefined
          : (index) => {
              const origin = feedback.sources[index];
              if (origin !== undefined) {
                dispatch({ type: 'messageDismissed', source: origin.source, index: origin.index });
              }
            }
      }
    />
  );
}

type NumberInputProps = {
  id: string;
  ariaLabel?: string;
  value: DecimalValue | null;
  /** Shown while the record holds no value and no operator text is shown; never written to the record. */
  fallback?: unknown;
  locked: boolean;
  invalid: boolean;
  rejected: boolean;
  messageRefs?: FieldMessageRefs;
  pending?: boolean;
  sizeClass?: string;
  onEntry: (value: DecimalValue | null) => void;
  onRejected: (message: string) => void;
  onAccepted: () => void;
  onChangedBlur?: () => void;
};

// Decimal text input that keeps the operator's text while focused or rejected, keeps a non-decimal entry out of the record, rejects it on blur, reports leaving it with a changed value or while invalid, and is marked while its validation is pending.
function NumberInput({
  id,
  ariaLabel,
  value,
  fallback,
  locked,
  invalid,
  rejected,
  messageRefs,
  pending = false,
  sizeClass,
  onEntry,
  onRejected,
  onAccepted,
  onChangedBlur,
}: NumberInputProps) {
  const [text, setText] = useState<string | null>(null);
  const [focused, setFocused] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const focusValue = useRef<DecimalValue | null>(null);
  const editBase = useRef<{ value: DecimalValue | null } | null>(null);
  const shown = !locked && (focused || rejected) && text !== null ? text : show(value != null ? value : fallback);

  // A record value replaced while the field is focused and not yet typed into becomes the blur baseline and is selected for overtyping (D-145).
  useLayoutEffect(() => {
    const element = input.current;
    if (!focused || locked || text !== null || element === null || Object.is(focusValue.current, value)) {
      return;
    }
    focusValue.current = value;
    if (document.activeElement === element) {
      element.select();
    }
  }, [value, focused, locked, text]);

  return (
    <input
      ref={input}
      id={id}
      type="text"
      inputMode="decimal"
      autoComplete="off"
      aria-label={ariaLabel}
      aria-invalid={invalid || undefined}
      aria-describedby={messageRefs?.describedBy}
      aria-errormessage={invalid ? messageRefs?.errorMessage : undefined}
      aria-busy={pending || undefined}
      className={classes(sizeClass ?? false, locked && 'read-only', invalid && 'invalid', pending && 'is-pending')}
      readOnly={locked}
      tabIndex={locked ? -1 : undefined}
      title={shown !== '' ? shown : undefined}
      value={shown}
      onFocus={() => {
        focusValue.current = value;
        editBase.current = null;
        if (!locked) {
          setFocused(true);
          setText((current) => (rejected && current !== null ? current : null));
        }
      }}
      onChange={(event) => {
        if (locked) {
          return;
        }
        const raw = event.target.value;
        setText(raw);
        if (editBase.current === null) {
          editBase.current = { value };
        }
        const entry = parseDecimalEntry(raw);
        const next = entry.kind === 'value' ? entry.text : entry.kind === 'empty' ? null : editBase.current.value;
        if (!Object.is(next, value)) {
          onEntry(next);
        }
      }}
      onBlur={() => {
        setFocused(false);
        editBase.current = null;
        if (locked) {
          setText(null);
          return;
        }
        if (text !== null) {
          const entry = parseDecimalEntry(text);
          if (entry.kind === 'invalid') {
            onRejected(entry.message);
            return;
          }
        }
        setText(null);
        if (rejected) {
          onAccepted();
        }
        if (onChangedBlur !== undefined && (!Object.is(focusValue.current, value) || rejected || invalid)) {
          onChangedBlur();
        }
      }}
    />
  );
}

type ReadOnlyTextProps = {
  id?: string;
  ariaLabel?: string;
  value: unknown;
  invalid?: boolean;
  messageRefs?: FieldMessageRefs;
  pending?: boolean;
  sizeClass?: string;
  freeText?: boolean;
};

// Read-only display of a value as held by the record, marked while its validation is pending; a free-text value is a tab stop.
function ReadOnlyText({ id, ariaLabel, value, invalid = false, messageRefs, pending = false, sizeClass, freeText = false }: ReadOnlyTextProps) {
  const text = show(value);
  return (
    <input
      id={id}
      type="text"
      readOnly
      aria-label={ariaLabel}
      aria-invalid={invalid || undefined}
      aria-describedby={messageRefs?.describedBy}
      aria-errormessage={invalid ? messageRefs?.errorMessage : undefined}
      aria-busy={pending || undefined}
      className={classes(sizeClass ?? false, 'read-only', invalid && 'invalid', pending && 'is-pending')}
      value={text}
      {...(freeText ? readOnlyTextProps(text) : { tabIndex: -1, title: text === '' ? undefined : text })}
    />
  );
}

type DiscountPromptProps = {
  text: string;
  returnFocusId: string;
  onChoose: (choice: DiscountLimitChoice) => void;
};

// The DISC_ALERT alert: a required choice between the maximum discount and cancelling the entry.
function DiscountPrompt({ text, returnFocusId, onChoose }: DiscountPromptProps) {
  const titleId = useId();
  const bodyId = useId();
  const maximumRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const backdropRef = useRef<HTMLDivElement>(null);
  const releaseBackground = useRef<(() => void) | null>(null);
  const [opener] = useState(() =>
    document.activeElement instanceof HTMLElement && document.activeElement !== document.body
      ? document.activeElement
      : null,
  );

  // Makes the page behind the alert inert while it is open.
  useLayoutEffect(() => {
    if (backdropRef.current === null) {
      return undefined;
    }
    const release = inertBackground(backdropRef.current);
    releaseBackground.current = release;
    return release;
  }, []);

  useEffect(() => {
    maximumRef.current?.focus();
  }, []);

  // Returns focus to the discount field the alert concerns, else to the control focused before the alert.
  useEffect(
    () => () => {
      if (backdropRef.current === null || !backdropRef.current.isConnected) {
        releaseBackground.current?.();
      }
      const field = document.getElementById(returnFocusId);
      const target = field !== null ? field : opener !== null && opener.isConnected ? opener : null;
      target?.focus();
    },
    [opener, returnFocusId],
  );

  // Keeps keyboard focus on the two alert buttons.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab') {
      return;
    }
    const first = maximumRef.current;
    const last = cancelRef.current;
    if (first === null || last === null) {
      return;
    }
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return (
    <div
      ref={backdropRef}
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          event.preventDefault();
        }
      }}
    >
      <div
        className="modal discount-prompt"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        onKeyDown={onKeyDown}
      >
        <h2 className="modal-title" id={titleId}>
          {DISCOUNT_LIMIT_TITLE}
        </h2>
        <div className="modal-body" id={bodyId}>
          <span>{text}</span>
        </div>
        <div className="modal-actions">
          <button type="button" ref={maximumRef} onClick={() => onChoose('MaximumDiscount')}>
            Maximum Discount
          </button>
          <button type="button" ref={cancelRef} onClick={() => onChoose('Cancel')}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

/** Payment methods, amounts, final discount and the discount-limit prompt of the invoice. */
export default function PaymentPanel({ state, dispatch, onValidate, busyTargets = NO_TARGETS }: PaymentPanelProps) {
  const baseId = useId();
  const [lov, setLov] = useState<PaymentLov | null>(null);
  const [pending, setPending] = useState<ValidateTarget[]>([]);

  const view = state.saved?.view ?? null;
  const header: InvoiceHeaderDraft | null = view?.header ?? state.draft?.header ?? null;
  const names: Record<string, unknown> = view !== null ? (view.display ?? {}) : state.display;
  const locked = state.readOnly || state.draft === null;

  // Fires validations queued by a dispatch once the render carrying the dispatched values has committed.
  useEffect(() => {
    if (pending.length === 0) {
      return;
    }
    setPending([]);
    for (const target of pending) {
      onValidate(target);
    }
  }, [pending, onValidate]);

  useEffect(() => {
    if (locked) {
      setLov(null);
    }
  }, [locked]);

  const queueValidation = (target: ValidateTarget) => {
    setPending((current) => [...current, target]);
  };

  const setField = <K extends NumericHeaderField>(field: K, value: InvoiceHeaderDraft[K]) => {
    if (!locked) {
      dispatch({ type: 'headerFieldChanged', field, value });
    }
  };

  // Sets the discount mode and validates the final-discount field that governs in it (D-64).
  const changeDiscountMode = (mode: typeof DISC_T_RATE | typeof DISC_T_VALUE) => {
    if (locked || header?.discT === mode) {
      return;
    }
    setField('discT', mode);
    queueValidation(mode === DISC_T_RATE ? 'FINALDISC_PERC' : 'FINALDISC');
  };

  const rejectEntry = (field: string) => (text: string) => dispatch({ type: 'entryRejected', field, lineIndex: null, text });
  const acceptEntry = (field: string) => () => dispatch({ type: 'entryAccepted', field, lineIndex: null });

  const pickPayType = (row: Record<string, unknown>) => {
    if (locked) {
      return;
    }
    if (lov === 'PAY_TYPE1') {
      setField('subPayType', toNumber(row.PAY_TYPE_ID));
      dispatch({ type: 'displaySet', values: { SUB_PAYTYPE_NAME: toText(row.PAY_TYPE_NAME) } });
      queueValidation('SUB_PAYTYPE');
    } else if (lov === 'PAY_TYPE2') {
      setField('subPayType2', toNumber(row.PAY_TYPE_ID));
      dispatch({ type: 'displaySet', values: { SUB_PAYTYPE2_NAME: toText(row.PAY_TYPE_NAME) } });
    }
  };

  const prompt = locked ? null : state.discountPrompt;

  const chooseDiscountLimit = (choice: DiscountLimitChoice) => {
    if (prompt === null) {
      return;
    }
    dispatch({ type: 'discountChoiceMade', choice });
    queueValidation(prompt.target);
  };

  // A saved or queried invoice shows only its saved view's refund; an unsaved draft shows its preview's refund, else the validated one.
  const refund =
    state.saved !== null
      ? view !== null
        ? names.REUND
        : undefined
      : state.preview !== null
        ? state.preview.refund
        : names.REUND;

  // Amounts an unsaved draft's preview defaulted, shown while the operator has entered none (D-148).
  const previewTotals = state.saved === null && state.preview !== null ? state.preview.totals : null;

  const discTFeedback = feedbackFor(state, 'DISC_T');
  const finalDiscPercFeedback = feedbackFor(state, 'FINALDISC_PERC');
  const finalDiscFeedback = feedbackFor(state, 'FINALDISC');
  const subPayTypeFeedback = feedbackFor(state, 'SUB_PAYTYPE');
  const amount1Feedback = feedbackFor(state, 'AMOUNT_1');
  const subPayType2Feedback = feedbackFor(state, 'SUB_PAYTYPE2');
  const amount2Feedback = feedbackFor(state, 'AMOUNT_2');
  const cashPayedFeedback = feedbackFor(state, 'CASH_PAYED');
  const refundFeedback = feedbackFor(state, 'REUND');

  const ids = {
    title: `${baseId}-title`,
    discT: `${baseId}-disc-t`,
    finalDiscPerc: `${baseId}-finaldisc-perc`,
    finalDisc: `${baseId}-finaldisc`,
    subPayType: `${baseId}-sub-paytype`,
    subPayTypeName: `${baseId}-sub-paytype-name`,
    amount1: `${baseId}-amount-1`,
    subPayType2: `${baseId}-sub-paytype2`,
    subPayType2Name: `${baseId}-sub-paytype2-name`,
    amount2: `${baseId}-amount-2`,
    cashPayed: `${baseId}-cash-payed`,
    refund: `${baseId}-reund`,
  };

  /** Base id of the messages shown for the field with id `fieldId`. */
  const messageId = (fieldId: string): string => `${fieldId}-msg`;

  /** Message ids the field with id `fieldId` references for `feedback`. */
  const refsOf = (feedback: FieldFeedback, fieldId: string): FieldMessageRefs =>
    fieldMessageRefs(messageId(fieldId), feedback.messages, feedback.fieldError);

  return (
    <div className="payment-panel" role="group" aria-labelledby={ids.title}>
      <div className="panel-title" role="heading" aria-level={2} id={ids.title}>
        Payment
      </div>

      <div className="field">
        <span id={ids.discT} className="field-caption">Discount Type</span>
        <div
          className="field-row choice-group"
          role="radiogroup"
          aria-labelledby={ids.discT}
          aria-describedby={refsOf(discTFeedback, ids.discT).describedBy}
        >
          <label>
            <input
              type="radio"
              name="DISC_T"
              value={DISC_T_RATE}
              checked={header?.discT === DISC_T_RATE}
              disabled={locked}
              className={classes(locked && 'read-only')}
              onChange={() => changeDiscountMode(DISC_T_RATE)}
            />{' '}
            Rate Disc
          </label>
          <label>
            <input
              type="radio"
              name="DISC_T"
              value={DISC_T_VALUE}
              checked={header?.discT === DISC_T_VALUE}
              disabled={locked}
              className={classes(locked && 'read-only')}
              onChange={() => changeDiscountMode(DISC_T_VALUE)}
            />{' '}
            Value Disc
          </label>
        </div>
        <Feedback id={messageId(ids.discT)} feedback={discTFeedback} locked={locked} dispatch={dispatch} />
      </div>

      <div className="field">
        <label htmlFor={ids.finalDiscPerc}>Deduct</label>
        <div className="field-row">
          <NumberInput
            id={ids.finalDiscPerc}
            ariaLabel="Deduct percent"
            value={header?.finalDiscPerc ?? null}
            locked={locked}
            invalid={finalDiscPercFeedback.invalid}
            rejected={finalDiscPercFeedback.rejected}
            messageRefs={refsOf(finalDiscPercFeedback, ids.finalDiscPerc)}
            pending={busyTargets.has('FINALDISC_PERC')}
            onEntry={(value) => setField('finalDiscPerc', value)}
            onRejected={rejectEntry('FINALDISC_PERC')}
            onAccepted={acceptEntry('FINALDISC_PERC')}
            onChangedBlur={() => onValidate('FINALDISC_PERC')}
          />
          <NumberInput
            id={ids.finalDisc}
            ariaLabel="Deduct amount"
            value={header?.finalDisc ?? null}
            locked={locked}
            invalid={finalDiscFeedback.invalid}
            rejected={finalDiscFeedback.rejected}
            messageRefs={refsOf(finalDiscFeedback, ids.finalDisc)}
            pending={busyTargets.has('FINALDISC')}
            onEntry={(value) => setField('finalDisc', value)}
            onRejected={rejectEntry('FINALDISC')}
            onAccepted={acceptEntry('FINALDISC')}
            onChangedBlur={() => onValidate('FINALDISC')}
          />
        </div>
        <Feedback id={messageId(ids.finalDiscPerc)} feedback={finalDiscPercFeedback} locked={locked} dispatch={dispatch} />
        <Feedback id={messageId(ids.finalDisc)} feedback={finalDiscFeedback} locked={locked} dispatch={dispatch} />
      </div>

      <div className="field">
        <label htmlFor={ids.subPayType}>Method 1</label>
        <div className="field-row">
          <ReadOnlyText
            id={ids.subPayType}
            value={header?.subPayType}
            invalid={subPayTypeFeedback.invalid}
            messageRefs={refsOf(subPayTypeFeedback, ids.subPayType)}
            pending={busyTargets.has('SUB_PAYTYPE')}
            sizeClass={METHOD_CODE_CLASS}
          />
          <ReadOnlyText
            id={ids.subPayTypeName}
            ariaLabel="Method 1 name"
            value={names.SUB_PAYTYPE_NAME}
            sizeClass={METHOD_FILL_CLASS}
            freeText
          />
          <button
            type="button"
            aria-label="Choose method 1"
            aria-haspopup="dialog"
            disabled={locked}
            onClick={() => setLov('PAY_TYPE1')}
          >
            …
          </button>
          <NumberInput
            id={ids.amount1}
            ariaLabel="Amount 1"
            value={header?.amount1 ?? null}
            fallback={previewTotals?.amount1}
            locked={locked}
            invalid={amount1Feedback.invalid}
            rejected={amount1Feedback.rejected}
            messageRefs={refsOf(amount1Feedback, ids.amount1)}
            pending={busyTargets.has('AMOUNT_1')}
            sizeClass={METHOD_FILL_CLASS}
            onEntry={(value) => setField('amount1', value)}
            onRejected={rejectEntry('AMOUNT_1')}
            onAccepted={acceptEntry('AMOUNT_1')}
            onChangedBlur={() => onValidate('AMOUNT_1')}
          />
        </div>
        <Feedback id={messageId(ids.subPayType)} feedback={subPayTypeFeedback} locked={locked} dispatch={dispatch} />
        <Feedback id={messageId(ids.amount1)} feedback={amount1Feedback} locked={locked} dispatch={dispatch} />
      </div>

      <div className="field">
        <label htmlFor={ids.subPayType2}>Method 2</label>
        <div className="field-row">
          <ReadOnlyText
            id={ids.subPayType2}
            value={header?.subPayType2}
            invalid={subPayType2Feedback.invalid}
            messageRefs={refsOf(subPayType2Feedback, ids.subPayType2)}
            sizeClass={METHOD_CODE_CLASS}
          />
          <ReadOnlyText
            id={ids.subPayType2Name}
            ariaLabel="Method 2 name"
            value={names.SUB_PAYTYPE2_NAME}
            sizeClass={METHOD_FILL_CLASS}
            freeText
          />
          <button
            type="button"
            aria-label="Choose method 2"
            aria-haspopup="dialog"
            disabled={locked}
            onClick={() => setLov('PAY_TYPE2')}
          >
            …
          </button>
          <NumberInput
            id={ids.amount2}
            ariaLabel="Amount 2"
            value={header?.amount2 ?? null}
            fallback={previewTotals?.amount2}
            locked={locked}
            invalid={amount2Feedback.invalid}
            rejected={amount2Feedback.rejected}
            messageRefs={refsOf(amount2Feedback, ids.amount2)}
            pending={busyTargets.has('AMOUNT_2')}
            sizeClass={METHOD_FILL_CLASS}
            onEntry={(value) => setField('amount2', value)}
            onRejected={rejectEntry('AMOUNT_2')}
            onAccepted={acceptEntry('AMOUNT_2')}
            onChangedBlur={() => onValidate('AMOUNT_2')}
          />
        </div>
        <Feedback id={messageId(ids.subPayType2)} feedback={subPayType2Feedback} locked={locked} dispatch={dispatch} />
        <Feedback id={messageId(ids.amount2)} feedback={amount2Feedback} locked={locked} dispatch={dispatch} />
      </div>

      <div className="field">
        <label htmlFor={ids.cashPayed}>Cash Payed</label>
        <NumberInput
          id={ids.cashPayed}
          value={header?.cashPayed ?? null}
          locked={locked}
          invalid={cashPayedFeedback.invalid}
          rejected={cashPayedFeedback.rejected}
          messageRefs={refsOf(cashPayedFeedback, ids.cashPayed)}
          onEntry={(value) => setField('cashPayed', value)}
          onRejected={rejectEntry('CASH_PAYED')}
          onAccepted={acceptEntry('CASH_PAYED')}
        />
        <Feedback id={messageId(ids.cashPayed)} feedback={cashPayedFeedback} locked={locked} dispatch={dispatch} />
      </div>

      <div className="field">
        <label htmlFor={ids.refund}>Refund</label>
        <ReadOnlyText
          id={ids.refund}
          value={refund}
          invalid={refundFeedback.invalid}
          messageRefs={refsOf(refundFeedback, ids.refund)}
        />
        <Feedback id={messageId(ids.refund)} feedback={refundFeedback} locked={locked} dispatch={dispatch} />
      </div>

      {lov !== null && !locked && (
        <LovPicker name={lov} binds={{}} onPick={pickPayType} onClose={() => setLov(null)} />
      )}

      {prompt !== null && (
        <DiscountPrompt
          text={prompt.text}
          returnFocusId={prompt.target === 'FINALDISC_PERC' ? ids.finalDiscPerc : ids.finalDisc}
          onChoose={chooseDiscountLimit}
        />
      )}
    </div>
  );
}
