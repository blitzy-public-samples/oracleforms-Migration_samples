import type { MessageDto } from '../api/types';

type FieldMessageProps = {
  messages: MessageDto[];
  fieldError?: { text: string; oracleErrorNumber: number | null } | null;
  onDismiss?: (index: number) => void;
};

/** Renders one field's blocking and warning messages and its mapped Oracle error. */
export default function FieldMessage({ messages, fieldError, onDismiss }: FieldMessageProps) {
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
          <div key={index} className="msg msg-warning">
            <span>{message.text}</span>
            {onDismiss !== undefined && (
              <button
                type="button"
                className="msg-dismiss"
                aria-label="Dismiss"
                onClick={() => onDismiss(index)}
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
