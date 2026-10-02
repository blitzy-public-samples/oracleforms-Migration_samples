using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Billing.Invoicing.Api.Contracts;
using Billing.Invoicing.Api.Services;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Ingress checks of <see cref="DraftDto"/> as data-annotations validation runs them on a bound request.</summary>
[Trait("Category", "Orchestration")]
public sealed class DraftDtoValidationTests
{
    private const string UnsupportedDiscountMode = "DISC_T must be 0 (Value Disc) or 1 (Rate Disc)";
    private const string UndefinedDiscountLimitChoice = "DiscountLimitChoice must be MaximumDiscount or Cancel";
    private const decimal MaxAmount = 19807040628566084398385987584m;
    private const string AmountRange = " must be between -19807040628566084398385987584 and 19807040628566084398385987584";
    private const string RequestIdText = "Request id must be 32 upper-case hexadecimal characters.";
    private const string WellFormedRequestId = "0123456789ABCDEF0123456789ABCDEF";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Action<JsonSerializerOptions, int> LimitLines = typeof(DraftDto)
        .GetMethod(nameof(LimitLines), BindingFlags.NonPublic | BindingFlags.Static)!
        .CreateDelegate<Action<JsonSerializerOptions, int>>();

    private static readonly Type RefusedLines =
        typeof(DraftDto).GetNestedType(nameof(RefusedLines), BindingFlags.NonPublic)!;

    private static readonly PropertyInfo LinesRefusal =
        typeof(DraftDto).GetProperty(nameof(LinesRefusal), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly OperatorContext Operator = new()
    {
        UserNo = 1,
        UserName = "dev",
        InfoCenterId = "1",
        MachineName = "clone28",
        SessionId = "00112233445566778899AABBCCDDEEFF",
    };

    private static List<ValidationResult> Validate(DraftDto dto)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        Assert.Equal(valid, results.Count == 0);
        return results;
    }

    private static void AssertRejected(DraftDto dto, string member, string text)
    {
        var result = Assert.Single(Validate(dto));
        Assert.Equal(text, result.ErrorMessage);
        Assert.Equal(new[] { member }, result.MemberNames);
    }

    private static DraftDto WithHeader(InvoiceHeaderDraft header) => new() { Header = header };

    private static DraftDto WithAmount(string item, decimal amount) => WithHeader(item switch
    {
        "AMOUNT_1" => new InvoiceHeaderDraft { Amount1 = amount },
        "AMOUNT_2" => new InvoiceHeaderDraft { Amount2 = amount },
        "CASH_PAYED" => new InvoiceHeaderDraft { CashPayed = amount },
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown amount item."),
    });

    [Fact]
    public void DefaultDraft_IsValid()
    {
        Assert.Empty(Validate(new DraftDto()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public void DiscT_ValueOrRateModeIsValid(int? discT)
    {
        Assert.Empty(Validate(WithHeader(new InvoiceHeaderDraft { DiscT = discT })));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void DiscT_OtherModeIsRejected(int discT)
    {
        AssertRejected(WithHeader(new InvoiceHeaderDraft { DiscT = discT }), "DISC_T", UnsupportedDiscountMode);
    }

    [Theory]
    [InlineData(DiscountLimitChoice.MaximumDiscount)]
    [InlineData(DiscountLimitChoice.Cancel)]
    [InlineData(null)]
    public void DiscountLimitChoice_AlertButtonOrNoneIsValid(DiscountLimitChoice? choice)
    {
        Assert.Empty(Validate(new DraftDto { DiscountLimitChoice = choice }));
    }

    [Fact]
    public void DiscountLimitChoice_UndefinedValueIsRejected()
    {
        AssertRejected(new DraftDto { DiscountLimitChoice = (DiscountLimitChoice)99 }, "FINALDISC", UndefinedDiscountLimitChoice);
        AssertRejected(
            new DraftDto { Header = new InvoiceHeaderDraft { DiscT = 1 }, DiscountLimitChoice = (DiscountLimitChoice)(-1) },
            "FINALDISC_PERC",
            UndefinedDiscountLimitChoice);

        var numeric = JsonSerializer.Deserialize<DraftDto>("""{"discountLimitChoice":99}""", Json);
        Assert.NotNull(numeric);
        Assert.Equal((DiscountLimitChoice)99, numeric.DiscountLimitChoice);
        AssertRejected(numeric, "FINALDISC", UndefinedDiscountLimitChoice);

        var noHeader = JsonSerializer.Deserialize<DraftDto>("""{"header":null,"discountLimitChoice":99}""", Json);
        Assert.NotNull(noHeader);
        AssertRejected(noHeader, "FINALDISC", UndefinedDiscountLimitChoice);

        var named = JsonSerializer.Deserialize<DraftDto>("""{"discountLimitChoice":"Cancel"}""", Json);
        Assert.NotNull(named);
        Assert.Equal(DiscountLimitChoice.Cancel, named.DiscountLimitChoice);
        Assert.Empty(Validate(named));
    }

    [Theory]
    [InlineData("AMOUNT_1")]
    [InlineData("AMOUNT_2")]
    [InlineData("CASH_PAYED")]
    public void Amount_WithinBoundIsValid(string item)
    {
        Assert.Equal(decimal.MaxValue / 4m, MaxAmount);

        foreach (var amount in new[] { MaxAmount, -MaxAmount, -10m, 0m, 150.25m })
        {
            Assert.Empty(Validate(WithAmount(item, amount)));
        }
    }

    [Theory]
    [InlineData("AMOUNT_1")]
    [InlineData("AMOUNT_2")]
    [InlineData("CASH_PAYED")]
    public void Amount_BeyondBoundIsRejected(string item)
    {
        foreach (var amount in new[] { MaxAmount + 1m, -MaxAmount - 1m, decimal.MaxValue, decimal.MinValue })
        {
            AssertRejected(WithAmount(item, amount), item, item + AmountRange);
        }
    }

    [Fact]
    public void EveryRejectionIsReportedInOrder()
    {
        var dto = new DraftDto
        {
            Header = new InvoiceHeaderDraft { DiscT = 2, Amount1 = decimal.MaxValue, Amount2 = decimal.MinValue, CashPayed = decimal.MaxValue },
            DiscountLimitChoice = (DiscountLimitChoice)99,
        };

        Assert.Equal(
            new[] { "DISC_T", "FINALDISC", "AMOUNT_1", "AMOUNT_2", "CASH_PAYED" },
            Validate(dto).Select(r => Assert.Single(r.MemberNames)));
    }

    [Fact]
    public void NullHeader_DoesNotThrow()
    {
        Assert.Empty(Validate(new DraftDto { Header = null! }));

        var bound = JsonSerializer.Deserialize<DraftDto>("""{"header":null}""", Json);
        Assert.NotNull(bound);
        Assert.Null(bound.Header);
        Assert.Empty(Validate(bound));
    }

    public static TheoryData<Type> RequestTypes => new()
    {
        typeof(CreateInvoiceRequest),
        typeof(ValidateDraftRequest),
        typeof(ImportRequestsRequest),
        typeof(PackageImportRequest),
        typeof(VisitLineRequest),
        typeof(BundledOfferRequest),
    };

    [Theory]
    [MemberData(nameof(RequestTypes))]
    public void RequestBody_DropsPreAuthorizationAndDisplayOnlyMembers(Type requestType)
    {
        const string body = """
            {"draft":{"requestId":"0123456789ABCDEF0123456789ABCDEF",
              "header":{"patientNo":"1001","subCompCode":"10",
                "preAuthorization":"PA-1","PreAuthorization":"PA-2","PREAUTHORIZATION":"PA-3","pre\u0041uthorization":"PA-4",
                "oferId":"abc","OferId":7,"docId1":12,"DOCID1":{"x":1},"seqNo":5,"SeqNo":"x"},
              "lines":[
                {"serviceId":"S1","catId":3,"priceOverride":12.5,"clientId":"C1","fixPay":"bad","FixPay":1,"payRate":0.5,"PAYRATE":{"x":1},
                  "regularLensesType":"R","lensSpecifications":"L","ContactLensesType":"C","flIndicator":"F","NUMBEROFPAIRS":"2","insEmp":"abc"},
                {"serviceId":"S2","clientId":"C2","fixPay":2,"CATID":"x","insEmp":44}]}}
            """;

        var request = JsonSerializer.Deserialize(body, requestType, Json);

        Assert.NotNull(request);
        var draft = Assert.IsType<DraftDto>(requestType.GetProperty(nameof(CreateInvoiceRequest.Draft))!.GetValue(request));
        Assert.Equal(WellFormedRequestId, draft.RequestId);
        Assert.Equal(new InvoiceHeaderDraft { PatientNo = "1001", SubCompCode = "10" }, draft.Header);
        Assert.Equal(
            new[]
            {
                new InvoiceLineDraft { ServiceId = "S1", PriceOverride = 12.5m, ClientId = "C1" },
                new InvoiceLineDraft { ServiceId = "S2", ClientId = "C2" },
            },
            draft.Lines);
    }

    [Fact]
    public void Serialize_OmitsPreAuthorizationAndDisplayOnlyMembers_AndRoundTrips()
    {
        var draft = new DraftDto
        {
            RequestId = WellFormedRequestId,
            Header = new InvoiceHeaderDraft { PatientNo = "1001", SubCompCode = "10", PreAuthorization = "PA-1", OferId = 7, DocId1 = 12, SeqNo = 5 },
            Lines =
            [
                new InvoiceLineDraft
                {
                    ServiceId = "S1",
                    PriceOverride = 12.5m,
                    ClientId = "C1",
                    CatId = 3,
                    FixPay = 1m,
                    PayRate = 0.5m,
                    RegularLensesType = "R",
                    LensSpecifications = "L",
                    ContactLensesType = "C",
                    FLIndicator = "F",
                    NumberOfPairs = "2",
                    InsEmp = 44,
                },
            ],
        };
        var expectedHeader = new InvoiceHeaderDraft { PatientNo = "1001", SubCompCode = "10" };
        var expectedLine = new InvoiceLineDraft { ServiceId = "S1", PriceOverride = 12.5m, ClientId = "C1" };

        var draftJson = JsonSerializer.Serialize(draft, Json);
        using (var document = JsonDocument.Parse(draftJson))
        {
            AssertOmitted(document.RootElement);
        }

        var back = JsonSerializer.Deserialize<DraftDto>(draftJson, Json);
        Assert.NotNull(back);
        Assert.Equal(WellFormedRequestId, back.RequestId);
        Assert.Equal(expectedHeader, back.Header);
        Assert.Equal(new[] { expectedLine }, back.Lines);

        var responseJson = JsonSerializer.Serialize(new NewDraftResponse { Draft = draft }, Json);
        using (var document = JsonDocument.Parse(responseJson))
        {
            AssertOmitted(document.RootElement.GetProperty("draft"));
        }

        var response = JsonSerializer.Deserialize<NewDraftResponse>(responseJson, Json);
        Assert.NotNull(response);
        Assert.Equal(expectedHeader, response.Draft.Header);
        Assert.Equal(new[] { expectedLine }, response.Draft.Lines);
    }

    [Fact]
    public void NullHeaderAndNullLines_KeepTheirBinding()
    {
        var request = JsonSerializer.Deserialize<CreateInvoiceRequest>("""{"draft":{"header":null,"lines":null}}""", Json);
        Assert.NotNull(request);
        Assert.Null(request.Draft.Header);
        Assert.Null(request.Draft.Lines);

        var withNullLine = JsonSerializer.Deserialize<DraftDto>("""{"header":null,"lines":[null,{"serviceId":"S1","fixPay":1}]}""", Json);
        Assert.NotNull(withNullLine);
        Assert.Null(withNullLine.Header);
        Assert.Collection(
            withNullLine.Lines,
            line => Assert.Null(line),
            line => Assert.Equal(new InvoiceLineDraft { ServiceId = "S1" }, line));

        var written = JsonSerializer.Serialize(new DraftDto { Header = null!, Lines = [null!] }, Json);
        using var document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("header").ValueKind);
        Assert.Equal(JsonValueKind.Null, Assert.Single(document.RootElement.GetProperty("lines").EnumerateArray()).ValueKind);
    }

    [Theory]
    [InlineData("""{"header":5}""")]
    [InlineData("""{"header":"1001"}""")]
    [InlineData("""{"header":[{"patientNo":"1001"}]}""")]
    [InlineData("""{"header":{"patientNo":1001}}""")]
    [InlineData("""{"lines":{"serviceId":"S1"}}""")]
    [InlineData("""{"lines":[5]}""")]
    [InlineData("""{"lines":[{"qty":"abc"}]}""")]
    [InlineData("""{"header":{"patientNo":"\uD800"}}""")]
    [InlineData("""{"lines":[{"serviceId":"\uDC00"}]}""")]
    [InlineData("""{"header":{"preAuthorization":"\uD800","patientNo":"\uD800"}}""")]
    public void MalformedHeaderOrLines_StillFailAsJson(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<DraftDto>(json, Json));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<CreateInvoiceRequest>($$"""{"draft":{{json}}}""", Json));
    }

    [Fact]
    public async Task LimitedLines_UpToTheLimit_BindWithoutTheOmittedMembers()
    {
        const string draftJson = """
            {"header":{"patientNo":"1001","preAuthorization":"PA-1","OFERID":{"x":1}},
              "lines":[{"serviceId":"S1","catId":"x","fixPay":"bad"},null,{"serviceId":"S3","insEmp":{"x":1}}]}
            """;
        var options = LimitedJson(3);
        var expectedLines = new[] { new InvoiceLineDraft { ServiceId = "S1" }, null!, new InvoiceLineDraft { ServiceId = "S3" } };

        foreach (var draft in new[]
        {
            JsonSerializer.Deserialize<DraftDto>(draftJson, options),
            JsonSerializer.Deserialize<CreateInvoiceRequest>($$"""{"draft":{{draftJson}}}""", options)?.Draft,
            await DeserializeAsync<DraftDto>(draftJson, options),
            (await DeserializeAsync<CreateInvoiceRequest>($$"""{"draft":{{draftJson}}}""", options))?.Draft,
        })
        {
            Assert.NotNull(draft);
            Assert.Equal(new InvoiceHeaderDraft { PatientNo = "1001" }, draft.Header);
            Assert.Equal(expectedLines, draft.Lines);
            Assert.IsNotType(RefusedLines, draft.Lines);
            Assert.Null(LinesRefusal.GetValue(draft));
        }
    }

    [Theory]
    [InlineData("""[{"serviceId":"S1"},{"serviceId":"S2"},{"serviceId":"S3"},{"serviceId":"S4"}]""", 4)]
    [InlineData("""[null,null,null,null]""", 4)]
    [InlineData("""[{"serviceId":"S1"},null,{"serviceId":"S3"},5]""", 4)]
    [InlineData("""[{"serviceId":"S1"},{"serviceId":"S2"},{"serviceId":"S3"},5,"x",[1],{"qty":"abc"}]""", 7)]
    [InlineData("""[5,{"serviceId":1},{"qty":"abc"},{"serviceId":"\uDC00"}]""", 4)]
    public async Task LimitedLines_BeyondTheLimit_BindAsRefusedLinesWithTheLineCountAndTheRestOfTheDraft(string lines, int count)
    {
        var options = LimitedJson(3);
        var draftJson = $$$"""
            {"requestId":"{{{WellFormedRequestId}}}","header":{"patientNo":"1001","preAuthorization":"PA-1"},"lines":{{{lines}}},
              "parameters":{"visitUnique":"V1"},"discountLimitChoice":"Cancel"}
            """;
        var requestJson = $$"""{"draft":{{draftJson}}}""";

        foreach (var draft in new[]
        {
            JsonSerializer.Deserialize<DraftDto>(draftJson, options),
            JsonSerializer.Deserialize<CreateInvoiceRequest>(requestJson, options)?.Draft,
            await DeserializeAsync<DraftDto>(draftJson, options),
            (await DeserializeAsync<CreateInvoiceRequest>(requestJson, options))?.Draft,
        })
        {
            AssertRefusedLines(draft, count, 3);
            Assert.Equal(WellFormedRequestId, draft!.RequestId);
            Assert.Equal(new InvoiceHeaderDraft { PatientNo = "1001" }, draft.Header);
            Assert.Equal(new InvoiceEntryParameters { VisitUnique = "V1" }, draft.Parameters);
            Assert.Equal(DiscountLimitChoice.Cancel, draft.DiscountLimitChoice);
        }
    }

    [Fact]
    public void LimitedLines_AtALimitOfOne_KeepEmptyAndNullLinesAndBindTwoAsRefusedLines()
    {
        var options = LimitedJson(1);

        Assert.Empty(JsonSerializer.Deserialize<DraftDto>("""{"lines":[]}""", options)!.Lines);
        Assert.Null(JsonSerializer.Deserialize<DraftDto>("""{"lines":null}""", options)!.Lines);
        Assert.Equal(new[] { new InvoiceLineDraft { ServiceId = "S1" } }, JsonSerializer.Deserialize<DraftDto>("""{"lines":[{"serviceId":"S1"}]}""", options)!.Lines);
        Assert.Null(LinesRefusal.GetValue(JsonSerializer.Deserialize<DraftDto>("""{"lines":[null]}""", options)));
        AssertRefusedLines(JsonSerializer.Deserialize<DraftDto>("""{"lines":[null,null]}""", options), 2, 1);
    }

    [Theory]
    [InlineData("""{"lines":[null,null],"header":{"patientNo":1001}}""", "$.patientNo", "$.patientNo")]
    [InlineData("""{"lines":[1,2],"parameters":{"visitUnique":5}}""", "$.parameters.visitUnique", "$.draft.parameters.visitUnique")]
    [InlineData("""{"lines":[{"serviceId":"S1"},{"serviceId":"S2"}],"draftDate":"2026-09-29T10:00:00Z"}""", "$.draftDate", "$.draft.draftDate")]
    public void LimitedLines_BeyondTheLimit_LeaveTheFollowingMembersFailingAtTheirOwnPath(string json, string draftPath, string requestPath)
    {
        var options = LimitedJson(1);

        Assert.Equal(draftPath, Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DraftDto>(json, options)).Path);
        Assert.Equal(
            requestPath,
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateInvoiceRequest>($$"""{"draft":{{json}}}""", options)).Path);
    }

    [Fact]
    public void RefusedLines_AreWrittenAsAnEmptyArray()
    {
        var refused = JsonSerializer.Deserialize<DraftDto>("""{"requestId":"0123456789ABCDEF0123456789ABCDEF","lines":[null,null]}""", LimitedJson(1));
        AssertRefusedLines(refused, 2, 1);

        foreach (var options in new[] { Json, LimitedJson(1) })
        {
            var written = JsonSerializer.Serialize(refused, options);

            using var document = JsonDocument.Parse(written);
            Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("lines").ValueKind);
            Assert.Equal(0, document.RootElement.GetProperty("lines").GetArrayLength());
            var back = JsonSerializer.Deserialize<DraftDto>(written, options);
            Assert.NotNull(back);
            Assert.Empty(back.Lines);
            Assert.Null(LinesRefusal.GetValue(back));
        }
    }

    [Theory]
    [InlineData("""{"lines":[{"serviceId":5}]}""", "$[0].serviceId")]
    [InlineData("""{"lines":[{"serviceId":"S1"},{"priceOverride":"abc"}]}""", "$[1].priceOverride")]
    [InlineData("""{"lines":[null,5]}""", "$[1]")]
    [InlineData("""{"lines":{"serviceId":"S1"}}""", "$")]
    [InlineData("""{"header":{"patientNo":1001}}""", "$.patientNo")]
    public void MalformedHeaderOrLinesWithinTheLimit_FailAsJsonAtTheirOwnPath(string json, string path)
    {
        foreach (var options in new[] { Json, LimitedJson(3) })
        {
            Assert.Equal(path, Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DraftDto>(json, options)).Path);
            Assert.Equal(path, Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateInvoiceRequest>($$"""{"draft":{{json}}}""", options)).Path);
        }
    }

    [Fact]
    public void UnlimitedLines_BindEveryLine()
    {
        var lines = string.Join(',', Enumerable.Range(0, 5000).Select(i => $$"""{"clientId":"L{{i}}","serviceId":"1001","qty":1,"catId":"x"}"""));

        foreach (var options in new[] { Json, LimitedJson(5000) })
        {
            var draft = JsonSerializer.Deserialize<DraftDto>($$"""{"lines":[{{lines}}]}""", options);
            var request = JsonSerializer.Deserialize<CreateInvoiceRequest>($$$"""{"draft":{"lines":[{{{lines}}}]}}""", options);

            Assert.NotNull(draft);
            Assert.Equal(5000, draft.Lines.Count);
            Assert.Equal(new InvoiceLineDraft { ClientId = "L4999", ServiceId = "1001", Qty = 1m }, draft.Lines[^1]);
            Assert.NotNull(request);
            Assert.Equal(draft.Lines, request.Draft.Lines);
        }
    }

    [Fact]
    public void LimitedLines_AreWrittenInFull()
    {
        var draft = new DraftDto
        {
            Lines = Enumerable.Range(0, 5).Select(i => new InvoiceLineDraft { ClientId = "L" + i, CatId = 3, InsEmp = 44 }).ToArray(),
        };

        var written = JsonSerializer.Serialize(draft, LimitedJson(3));

        Assert.Equal(JsonSerializer.Serialize(draft, Json), written);
        using var document = JsonDocument.Parse(written);
        var lines = document.RootElement.GetProperty("lines");
        Assert.Equal(5, lines.GetArrayLength());
        Assert.All(lines.EnumerateArray(), line =>
        {
            Assert.False(line.TryGetProperty("catId", out _));
            Assert.False(line.TryGetProperty("insEmp", out _));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void LimitLines_RefusesNoOptionsOrALimitBelowOne(int maxLines)
    {
        var options = new JsonSerializerOptions(Json);

        Assert.Equal("maxLines", Assert.Throws<ArgumentOutOfRangeException>(() => LimitLines(options, maxLines)).ParamName);
        Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => LimitLines(null!, 1)).ParamName);
    }

    /// <summary>Copy of the test options with the draft lines limited to <paramref name="maxLines"/>.</summary>
    private static JsonSerializerOptions LimitedJson(int maxLines)
    {
        var options = new JsonSerializerOptions(Json);
        LimitLines(options, maxLines);
        return options;
    }

    /// <summary>Deserializes <paramref name="json"/> from a stream read one byte per buffer fill.</summary>
    private static async Task<T?> DeserializeAsync<T>(string json, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions(options) { DefaultBufferSize = 1 });
    }

    /// <summary>Asserts that <paramref name="draft"/> bound its lines as refused lines holding no line, carrying the line count, the cap and their refusal text.</summary>
    private static void AssertRefusedLines(DraftDto? draft, int count, int maxLines)
    {
        Assert.NotNull(draft);
        var lines = draft.Lines;
        Assert.IsType(RefusedLines, lines);
        Assert.Empty(lines);
        Assert.Equal(0, RefusedLines.GetProperty(nameof(lines.Count))!.GetValue(lines));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines[0]);
        var text = $"The draft has {count} lines; an invoice can be created with at most {maxLines} lines.";
        Assert.Equal(count, RefusedLines.GetProperty("LineCount")!.GetValue(lines));
        Assert.Equal(maxLines, RefusedLines.GetProperty("MaxLines")!.GetValue(lines));
        Assert.Equal(text, RefusedLines.GetProperty("Text")!.GetValue(lines));
        Assert.Equal(text, LinesRefusal.GetValue(draft));
    }

    public static TheoryData<string, DateTime> WallClockDraftDates => new()
    {
        { "2026-09-29T23:59:59", new DateTime(2026, 9, 29, 23, 59, 59) },
        { "2026-09-29T23:59:59.1234567", new DateTime(2026, 9, 29, 23, 59, 59).AddTicks(1234567) },
        { "2026-03-31", new DateTime(2026, 3, 31) },
    };

    [Theory]
    [MemberData(nameof(WallClockDraftDates))]
    public void DraftDate_WithoutTimeZoneDesignator_IsReadAsItsWallClock(string text, DateTime expected)
    {
        var draft = JsonSerializer.Deserialize<DraftDto>($$"""{"draftDate":"{{text}}"}""", Json);
        var request = JsonSerializer.Deserialize<CreateInvoiceRequest>($$$"""{"draft":{"draftDate":"{{{text}}}"}}""", Json);

        Assert.NotNull(draft);
        Assert.Equal(expected.Ticks, draft.DraftDate.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, draft.DraftDate.Kind);
        Assert.NotNull(request);
        Assert.Equal(expected.Ticks, request.Draft.DraftDate.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, request.Draft.DraftDate.Kind);
    }

    [Theory]
    [InlineData("\"2026-09-29T23:59:59+03:00\"")]
    [InlineData("\"2026-09-29T23:59:59-05:00\"")]
    [InlineData("\"2026-09-29T23:59:59+00:00\"")]
    [InlineData("\"2026-09-29T23:59:59Z\"")]
    [InlineData("\"2026-09-29T23:59:59.1234567Z\"")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"29/09/2026\"")]
    [InlineData("1790000000")]
    public void DraftDate_WithATimeZoneDesignatorOrNoIsoText_FailsAsJsonOnDraftDate(string value)
    {
        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DraftDto>($$"""{"draftDate":{{value}}}""", Json));
        var nested = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ValidateDraftRequest>($$$"""{"draft":{"draftDate":{{{value}}}}}""", Json));

        Assert.Equal("$.draftDate", error.Path);
        Assert.Equal("$.draft.draftDate", nested.Path);
    }

    [Theory]
    [MemberData(nameof(WallClockDraftDates))]
    public void DraftDate_IsWrittenAsTheDefaultIsoTextAndRoundTrips(string text, DateTime value)
    {
        var draft = new DraftDto { DraftDate = value };

        var written = JsonSerializer.SerializeToElement(draft, Json).GetProperty("draftDate");
        var response = JsonSerializer.SerializeToElement(new NewDraftResponse { Draft = draft }, Json);
        var back = JsonSerializer.Deserialize<DraftDto>(JsonSerializer.Serialize(draft, Json), Json);

        Assert.Equal(JsonSerializer.Serialize(value, Json), written.GetRawText());
        Assert.StartsWith(text, written.GetString(), StringComparison.Ordinal);
        Assert.Equal(written.GetRawText(), response.GetProperty("draft").GetProperty("draftDate").GetRawText());
        Assert.NotNull(back);
        Assert.Equal(value.Ticks, back.DraftDate.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, back.DraftDate.Kind);
    }

    [Fact]
    public void DraftDate_WallClockIsWrittenWithoutADesignator()
    {
        var written = JsonSerializer.SerializeToElement(new DraftDto { DraftDate = new DateTime(2026, 9, 29, 23, 59, 59) }, Json);

        Assert.Equal("2026-09-29T23:59:59", written.GetProperty("draftDate").GetString());
    }

    private static void AssertOmitted(JsonElement draft)
    {
        string[] omittedHeader = ["preAuthorization", "oferId", "docId1", "seqNo"];
        string[] omittedLine =
            ["catId", "fixPay", "payRate", "regularLensesType", "lensSpecifications", "contactLensesType", "flIndicator", "numberOfPairs", "insEmp"];

        var header = draft.GetProperty("header").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(typeof(InvoiceHeaderDraft).GetProperties().Length - omittedHeader.Length, header.Length);
        Assert.DoesNotContain(header, name => omittedHeader.Contains(name, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("patientNo", header);
        Assert.Contains("subCompCode", header);

        var line = Assert.Single(draft.GetProperty("lines").EnumerateArray()).EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(typeof(InvoiceLineDraft).GetProperties().Length - omittedLine.Length, line.Length);
        Assert.DoesNotContain(line, name => omittedLine.Contains(name, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("serviceId", line);
        Assert.Contains("priceOverride", line);
        Assert.Contains("clientId", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789ABCDEF0123456789ABCDE")]
    [InlineData("0123456789ABCDEF0123456789ABCDEFA")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789ABCDEF")]
    [InlineData("0123456789ABCDEG0123456789ABCDEF")]
    [InlineData(" 0123456789ABCDEF0123456789ABCDE")]
    [InlineData("0123456789ABCDEF0123456789ABCDE ")]
    [InlineData(" 0123456789ABCDEF0123456789ABCDEF ")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF\n")]
    [InlineData("0123456789ABCDEF0123456789ABCDE\uFF21")]
    [InlineData("0123456789ABCDEF-0123456789ABCDE")]
    public async Task Create_MalformedRequestId_IsRejectedBeforeAnyPortCall(string? requestId)
    {
        await AssertRequestIdRejected(new CreateInvoiceRequest { Draft = new DraftDto { RequestId = requestId! } });
        await AssertRequestIdRejected(new CreateInvoiceRequest
        {
            Draft = new DraftDto { RequestId = requestId!, Header = null!, Lines = [null!], Parameters = null! },
        });
    }

    [Fact]
    public async Task Create_NullDraft_IsRejectedBeforeAnyPortCall()
    {
        await AssertRequestIdRejected(new CreateInvoiceRequest { Draft = null! });

        var bound = JsonSerializer.Deserialize<CreateInvoiceRequest>("""{"draft":null}""", Json);
        Assert.NotNull(bound);
        await AssertRequestIdRejected(bound);
    }

    [Theory]
    [InlineData(WellFormedRequestId)]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public async Task Create_WellFormedRequestId_ReachesTheReplayLookupFirst(string requestId)
    {
        await AssertReplayLookupFirst(requestId);
    }

    [Fact]
    public async Task Create_IssuedRequestIdShape_ReachesTheReplayLookupFirst()
    {
        await AssertReplayLookupFirst(Guid.NewGuid().ToString("N").ToUpperInvariant());
    }

    private static InvoiceWorkflowService Service(List<PortCall> calls) => new(
        RecordingPort.Create<IOracleSessionFactory>(calls),
        RecordingPort.Create<ILookupQueries>(calls),
        RecordingPort.Create<IInvoiceQueries>(calls),
        RecordingPort.Create<ILovQueries>(calls),
        RecordingPort.Create<IBilInvoiceApiGateway>(calls),
        RecordingPort.Create<IBilImportGateway>(calls),
        RecordingPort.Create<IPatientTransferCommand>(calls),
        RecordingPort.Create<ILegacyExternalCalls>(calls),
        RecordingPort.Create<IPackageConsumptionGateway>(calls));

    private static async Task AssertRequestIdRejected(CreateInvoiceRequest request)
    {
        var calls = new List<PortCall>();

        var response = await Service(calls).Create(request, Operator);

        Assert.Null(response.InvNo);
        Assert.Null(response.Message);
        var message = Assert.Single(response.Messages);
        Assert.Equal("REQUEST_ID", message.Field);
        Assert.Equal(RequestIdText, message.Text);
        Assert.Equal(ValidationMessage.Blocking, message.Severity);
        Assert.Null(message.Rule);
        Assert.Empty(response.OpenItems);
        Assert.Empty(calls);
    }

    private static async Task AssertReplayLookupFirst(string requestId)
    {
        var calls = new List<PortCall>();
        var request = new CreateInvoiceRequest { Draft = new DraftDto { RequestId = requestId } };

        var failure = await Assert.ThrowsAsync<PortCalledException>(() => Service(calls).Create(request, Operator));

        var call = Assert.Single(calls);
        Assert.Equal(typeof(IInvoiceQueries), call.Port);
        Assert.Equal(nameof(IInvoiceQueries.GetCreateRequest), call.Method);
        Assert.Equal(requestId, call.Arguments[0]);
        Assert.Equal(nameof(IInvoiceQueries.GetCreateRequest), failure.Message);
    }

    /// <summary>One call a <see cref="RecordingPort"/> received.</summary>
    private sealed record PortCall(Type Port, string Method, object?[] Arguments);

    /// <summary>Thrown by every <see cref="RecordingPort"/> call, carrying the called method name.</summary>
    private sealed class PortCalledException(string method) : Exception(method);

    /// <summary>Port stand-in that records each call into a shared list and then throws <see cref="PortCalledException"/>.</summary>
    private class RecordingPort : DispatchProxy
    {
        private List<PortCall> _calls = [];

        /// <summary>Returns a <typeparamref name="TPort"/> whose every call is recorded into <paramref name="calls"/>.</summary>
        public static TPort Create<TPort>(List<PortCall> calls)
            where TPort : class
        {
            var port = Create<TPort, RecordingPort>();
            ((RecordingPort)(object)port)._calls = calls;
            return port;
        }

        /// <summary>Records the call and throws <see cref="PortCalledException"/>.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            _calls.Add(new PortCall(targetMethod.DeclaringType!, targetMethod.Name, args ?? []));
            throw new PortCalledException(targetMethod.Name);
        }
    }
}
