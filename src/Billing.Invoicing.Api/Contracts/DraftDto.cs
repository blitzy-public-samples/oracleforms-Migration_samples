using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Api.Contracts;

/// <summary>Unsaved invoice draft exchanged between client and server.</summary>
public sealed record DraftDto : IValidatableObject
{
    private const int ValueMode = 0;
    private const int PercentMode = 1;
    private const string UnsupportedDiscountMode = "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)";
    private const string UndefinedDiscountLimitChoice =
        $"{nameof(DiscountLimitChoice)} must be {nameof(Billing.Invoicing.Domain.Model.DiscountLimitChoice.MaximumDiscount)} or {nameof(Billing.Invoicing.Domain.Model.DiscountLimitChoice.Cancel)}";
    private const string DiscTItem = "DISC_T";
    private const string FinalDiscPercItem = "FINALDISC_PERC";
    private const string FinalDiscItem = "FINALDISC";
    private const string Amount1Item = "AMOUNT_1";
    private const string Amount2Item = "AMOUNT_2";
    private const string CashPayedItem = "CASH_PAYED";

    private static readonly decimal MaxAmount = decimal.MaxValue / 4m;
    private static readonly string MaxAmountText = MaxAmount.ToString(CultureInfo.InvariantCulture);

    /// <summary>32-character upper-case hexadecimal request id, kept for the life of the draft.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Database time read when the draft was created.</summary>
    public DateTime DraftDate { get; init; }

    /// <summary>Seal of the request id and draft date issued with the draft.</summary>
    public string? DraftSeal { get; init; }

    /// <summary>The <c>T_INV</c> header; its JSON omits the pre-authorisation, <c>OFERID</c>, <c>DOCID1</c> and <c>SEQ_NO</c>.</summary>
    [JsonConverter(typeof(RequestHeaderContract))]
    public InvoiceHeaderDraft Header { get; init; } = new();

    /// <summary>The <c>D_INV</c> lines in grid order; a line's zero-based position is its line index; their JSON omits the display-only <c>CATID</c>, <c>FIXPAY</c>, <c>PAYRATE</c>, lens and <c>INS_EMP</c> members.</summary>
    [JsonConverter(typeof(RequestLinesContract))]
    public IReadOnlyList<InvoiceLineDraft> Lines { get; init; } = [];

    /// <summary>The Form entry parameters set by the calling module.</summary>
    public InvoiceEntryParameters Parameters { get; init; } = new();

    /// <summary>The operator's answer to the maximum-discount prompt; null when none was given.</summary>
    public Billing.Invoicing.Domain.Model.DiscountLimitChoice? DiscountLimitChoice { get; init; }

    /// <summary>Rejects an unsupported DISC_T or DiscountLimitChoice and an out-of-bound AMOUNT_1, AMOUNT_2 or CASH_PAYED, naming the legacy item.</summary>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        var header = Header;

        if (header?.DiscT is { } mode && mode is not (ValueMode or PercentMode))
        {
            yield return new ValidationResult(UnsupportedDiscountMode, [DiscTItem]);
        }

        if (DiscountLimitChoice is { } choice && !Enum.IsDefined(choice))
        {
            var discountItem = header?.DiscT == PercentMode ? FinalDiscPercItem : FinalDiscItem;
            yield return new ValidationResult(UndefinedDiscountLimitChoice, [discountItem]);
        }

        if (header is null)
        {
            yield break;
        }

        (decimal? Value, string Item)[] amounts =
        [
            (header.Amount1, Amount1Item),
            (header.Amount2, Amount2Item),
            (header.CashPayed, CashPayedItem),
        ];

        foreach (var (value, item) in amounts)
        {
            if (value is { } amount && Math.Abs(amount) > MaxAmount)
            {
                yield return new ValidationResult($"{item} must be between -{MaxAmountText} and {MaxAmountText}", [item]);
            }
        }
    }

    /// <summary>JSON contract of <typeparamref name="T"/> that drops the named members on read and leaves them out on write.</summary>
    /// <typeparam name="T">The header record, or the list of line records.</typeparam>
    /// <param name="omittedMembers">CLR names of the members left out of every JSON object, or of every object in a JSON array.</param>
    private abstract class OmittingContract<T>(params string[] omittedMembers) : JsonConverter<T>
    {
        /// <summary>Reads <typeparamref name="T"/> from the JSON value with the omitted members removed.</summary>
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteValue(document.RootElement, writer, options);
            }

            return JsonSerializer.Deserialize<T>(buffer.WrittenSpan, options);
        }

        /// <summary>Writes <paramref name="value"/> without the omitted members.</summary>
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            WriteValue(JsonSerializer.SerializeToElement(value, options), writer, options);

        /// <summary>Writes an object, or each element of an array, without the omitted members; any other value unchanged.</summary>
        private void WriteValue(JsonElement value, Utf8JsonWriter writer, JsonSerializerOptions options)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                WriteObject(value, writer, options);
                return;
            }

            writer.WriteStartArray();
            foreach (var element in value.EnumerateArray())
            {
                WriteObject(element, writer, options);
            }

            writer.WriteEndArray();
        }

        /// <summary>Writes an object without the omitted members; any other value unchanged.</summary>
        private void WriteObject(JsonElement value, Utf8JsonWriter writer, JsonSerializerOptions options)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value.WriteTo(writer);
                return;
            }

            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                if (!IsOmitted(property.Name, options))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>Whether <paramref name="name"/> is the JSON name of an omitted member, ignoring case.</summary>
        private bool IsOmitted(string name, JsonSerializerOptions options) =>
            omittedMembers.Any(member =>
                string.Equals(name, options.PropertyNamingPolicy?.ConvertName(member) ?? member, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>JSON contract of the header that omits the pre-authorisation and the display-only <c>OFERID</c>, <c>DOCID1</c> and <c>SEQ_NO</c>.</summary>
    private sealed class RequestHeaderContract() : OmittingContract<InvoiceHeaderDraft>(
        nameof(InvoiceHeaderDraft.PreAuthorization),
        nameof(InvoiceHeaderDraft.OferId),
        nameof(InvoiceHeaderDraft.DocId1),
        nameof(InvoiceHeaderDraft.SeqNo));

    /// <summary>JSON contract of the lines that omits the display-only <c>CATID</c>, <c>FIXPAY</c>, <c>PAYRATE</c>, lens and <c>INS_EMP</c> members.</summary>
    private sealed class RequestLinesContract() : OmittingContract<IReadOnlyList<InvoiceLineDraft>>(
        nameof(InvoiceLineDraft.CatId),
        nameof(InvoiceLineDraft.FixPay),
        nameof(InvoiceLineDraft.PayRate),
        nameof(InvoiceLineDraft.RegularLensesType),
        nameof(InvoiceLineDraft.LensSpecifications),
        nameof(InvoiceLineDraft.ContactLensesType),
        nameof(InvoiceLineDraft.FLIndicator),
        nameof(InvoiceLineDraft.NumberOfPairs),
        nameof(InvoiceLineDraft.InsEmp));
}
