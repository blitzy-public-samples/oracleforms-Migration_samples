using System.Collections.Frozen;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Billing.Invoicing.Data.Errors;

/// <summary>Translates Oracle errors and Data-layer exceptions into <see cref="DataFailure"/> values of the HTTP error contract.</summary>
public sealed partial class OracleFailureTranslator
{
    /// <summary>Exception data key holding the operator-facing text of a line value a binder refused to bind.</summary>
    public const string BindingRejectionKey = "Billing.Invoicing.Data.BindingRejection";

    /// <summary><see cref="Exception.Data"/> key whose value <c>true</c> marks a blank or malformed Oracle connection string.</summary>
    public const string ConfigurationFaultKey = "Billing.Invoicing.Data.ConfigurationFault";

    private const int ApplicationErrorFirst = -20999;
    private const int ApplicationErrorLast = -20000;

    private const int UnprocessableEntityStatus = 422;
    private const int InternalServerErrorStatus = 500;
    private const int NotImplementedStatus = 501;
    private const int ServiceUnavailableStatus = 503;

    private const string UnknownPackage = "UNKNOWN";

    private const string OracleErrorMessage = "The Oracle database returned an error.";
    private const string OracleUnavailableMessage = "Oracle database is unavailable.";
    private const string ConfigurationFaultMessage = "The Oracle connection string is not configured or is not well-formed.";

    /// <summary>ORA codes classified as connectivity or availability failures.</summary>
    private static readonly FrozenSet<int> ConnectivityNumbers = new[]
    {
        12154, 12170, 12505, 12514, 12528, 12537, 12541, 12543, 12545,
        3113, 3114, 3135,
        1033, 1034, 1089,
    }.ToFrozenSet();

    /// <summary>All three ingested packages; used for a null or unknown operation.</summary>
    private static readonly IReadOnlyCollection<string> AllPackages = Array.AsReadOnly(new[]
    {
        OracleErrorCatalog.ApiPackage,
        OracleErrorCatalog.EnginePackage,
        OracleErrorCatalog.ImportPackage,
    });

    /// <summary>Standalone database functions and procedures the legacy module and packages call, compared ignoring case.</summary>
    private static readonly FrozenSet<string> StandaloneRoutines = new[]
    {
        "GET_NEXT_INVOICE_NO",
        "VALIDATE_TOTAL_INV",
        "GET_ELLIGABILTY",
        "DAY_TO_DAYES",
        "GET_PAYID_VALUE",
        "GET_PRICE_PLAN",
        "GET_U_PREV20",
        "SEND_MESSAG",
        "GET_HTFN2",
        "FIND_PROMPT",
        "SILENT_COMMET00",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Packages reachable from each Data gateway operation, keyed ordinally by operation name.</summary>
    private static readonly FrozenDictionary<string, IReadOnlyCollection<string>> OperationPackages = BuildOperationPackages();

    /// <summary>Translates a parsed Oracle error into a business (422), connectivity (503) or generic Oracle (500) failure.</summary>
    /// <param name="error">The parsed Oracle error.</param>
    /// <returns>The failure to return over HTTP.</returns>
    public DataFailure Translate(OracleErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // Application errors -20999 … -20000 are business errors, also when raised while opening.
        if (error.Number is >= ApplicationErrorFirst and <= ApplicationErrorLast)
        {
            return TranslateApplicationError(error);
        }

        // Listed connectivity and availability codes are 503.
        if (ConnectivityNumbers.Contains(error.Number))
        {
            return Unavailable(error.Number);
        }

        // Every other code, including credential, privilege and account failures while opening, is 500.
        return OracleError(error.Number, InnermostFramePackage(error));
    }

    /// <summary>Translates a Data-layer exception into a failure, or returns null when the exception is not an Oracle, connection-string, open-item, transport or binding-rejection failure.</summary>
    /// <param name="exception">The exception raised by a Data member.</param>
    /// <returns>The failure to return over HTTP, or <see langword="null"/> when the exception is not translated.</returns>
    public DataFailure? Translate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        bool transport = HasTransportFailure(exception);

        if (OracleErrorParser.TryFromExceptionChain(exception, out OracleErrorInfo? oracleError, out int driverNumber))
        {
            // A driver number and message that cannot be parsed together are a 500 without a package.
            if (oracleError is null)
            {
                return OracleError(driverNumber, package: null);
            }

            // A generic Oracle error with a socket failure or timeout in the chain is 503.
            DataFailure failure = Translate(oracleError);
            return failure.Status == InternalServerErrorStatus && transport ? Unavailable(failure.Number) : failure;
        }

        // A blank or malformed connection string is a 500 without a number or package.
        if (HasConfigurationFault(exception))
        {
            return new DataFailure
            {
                Status = InternalServerErrorStatus,
                Type = DataFailure.OracleErrorType,
                Message = ConfigurationFaultMessage,
            };
        }

        // A blocked member names its open item at the start of the message.
        if (OpenItem(exception) is { } blocked)
        {
            return blocked;
        }

        // A socket failure or timeout without an Oracle error is 503 with no number.
        if (transport)
        {
            return Unavailable(null);
        }

        // A line value a binder refused is a form-level 422 carrying the binder's text.
        if (exception is ArgumentException && exception.Data[BindingRejectionKey] is string text)
        {
            return new DataFailure
            {
                Status = UnprocessableEntityStatus,
                Type = DataFailure.FieldValidationType,
                Message = text,
            };
        }

        // A blocked member wrapped by other exceptions is the first one along the inner-exception chain.
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (OpenItem(inner) is { } wrapped)
            {
                return wrapped;
            }
        }

        return null;
    }

    /// <summary>Builds the 422 failure of an application error from its catalogue row, or from its frames when uncatalogued.</summary>
    private static DataFailure TranslateApplicationError(OracleErrorInfo error)
    {
        var row = OracleErrorCatalog.Find(CandidatePackages(error.Operation), error.Number, error.Text);
        string package = row?.Package ?? InnermostFramePackage(error) ?? UnknownPackage;
        string? kind = row?.Kind;

        // Missing application id, session or user in a BIL_IMPORT call.
        if (string.Equals(kind, OracleErrorCatalog.OperatorContextMissingKind, StringComparison.Ordinal))
        {
            return new DataFailure
            {
                Status = UnprocessableEntityStatus,
                Type = DataFailure.OperatorContextMissingType,
                Kind = kind,
                Number = error.Number,
                Package = package,
                Message = error.Text,
            };
        }

        return new DataFailure
        {
            Status = UnprocessableEntityStatus,
            Type = DataFailure.OracleBusinessErrorType,
            Kind = kind,
            Number = error.Number,
            Package = package,
            Message = error.Text,
            Field = row?.Field,
            LegacyText = row?.LegacyText,
        };
    }

    /// <summary>Builds a 500 oracle-error failure with the fixed Oracle error message.</summary>
    /// <param name="number">Signed Oracle error number.</param>
    /// <param name="package">Attributed package, or null when none applies.</param>
    /// <returns>The 500 failure.</returns>
    private static DataFailure OracleError(int number, string? package) => new()
    {
        Status = InternalServerErrorStatus,
        Type = DataFailure.OracleErrorType,
        Number = number,
        Package = package,
        Message = OracleErrorMessage,
    };

    /// <summary>Builds a 503 oracle-unavailable failure with the fixed unavailability message.</summary>
    /// <param name="number">Signed Oracle error number, or null for a transport failure without one.</param>
    /// <returns>The 503 failure.</returns>
    private static DataFailure Unavailable(int? number) => new()
    {
        Status = ServiceUnavailableStatus,
        Type = DataFailure.OracleUnavailableType,
        Number = number,
        Message = OracleUnavailableMessage,
    };

    /// <summary>Builds the 501 open-item failure of a <see cref="NotImplementedException"/> whose message starts with an open-item id.</summary>
    /// <param name="exception">The exception or one of its inner exceptions.</param>
    /// <returns>The 501 failure carrying that id and message, or null when the exception is not a <see cref="NotImplementedException"/> naming an open item.</returns>
    private static DataFailure? OpenItem(Exception exception)
    {
        if (exception is not NotImplementedException)
        {
            return null;
        }

        Match openItem = OpenItemIdRegex().Match(exception.Message);
        return openItem.Success
            ? new DataFailure
            {
                Status = NotImplementedStatus,
                Type = DataFailure.OpenItemType,
                OpenItemId = openItem.Value,
                Message = exception.Message,
            }
            : null;
    }

    /// <summary>Returns the packages reachable from a gateway operation; all three when the operation is null or unknown.</summary>
    private static IReadOnlyCollection<string> CandidatePackages(string? operation) =>
        operation is not null && OperationPackages.TryGetValue(operation, out IReadOnlyCollection<string>? packages)
            ? packages
            : AllPackages;

    /// <summary>Returns the object named by the innermost ORA-06512 frame that is neither a standalone routine nor a trigger named by an ORA-04088 line, or null when none is.</summary>
    /// <param name="error">The parsed Oracle error.</param>
    /// <returns>The package name as the frame spells it, or null.</returns>
    private static string? InnermostFramePackage(OracleErrorInfo error)
    {
        if (error.Frames is not { Count: > 0 } frames)
        {
            return null;
        }

        IReadOnlyList<(string Schema, string Name)> triggers = OracleErrorParser.ParseTriggers(error.Message ?? string.Empty);
        foreach (var (schema, package, _) in frames)
        {
            if (!StandaloneRoutines.Contains(package) && !IsTrigger(triggers, schema, package))
            {
                return package;
            }
        }

        return null;
    }

    /// <summary>Returns whether a frame's object is one of the triggers named by ORA-04088 lines.</summary>
    /// <param name="triggers">Triggers named by the error's ORA-04088 lines.</param>
    /// <param name="schema">Schema of the frame, empty when unqualified.</param>
    /// <param name="name">Object name of the frame.</param>
    /// <returns>True when a trigger has the frame's name and, when the trigger is schema-qualified, its schema.</returns>
    private static bool IsTrigger(IReadOnlyList<(string Schema, string Name)> triggers, string schema, string name)
    {
        foreach ((string triggerSchema, string triggerName) in triggers)
        {
            if (string.Equals(triggerName, name, StringComparison.Ordinal)
                && (triggerSchema.Length == 0 || string.Equals(triggerSchema, schema, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether the exception or its inner-exception chain holds a <see cref="SocketException"/> or <see cref="TimeoutException"/>.</summary>
    /// <param name="exception">The exception raised by a Data member.</param>
    /// <returns>True when the chain holds a transport failure.</returns>
    private static bool HasTransportFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether the exception or its inner-exception chain is marked under <see cref="ConfigurationFaultKey"/>.</summary>
    /// <param name="exception">The exception raised by a Data member.</param>
    /// <returns>True when the chain holds a connection-string fault.</returns>
    private static bool HasConfigurationFault(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Data[ConfigurationFaultKey] is true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Maps each Data gateway operation name to the packages it reaches.</summary>
    private static FrozenDictionary<string, IReadOnlyCollection<string>> BuildOperationPackages()
    {
        IReadOnlyCollection<string> previewAndCreate = Array.AsReadOnly(new[]
        {
            OracleErrorCatalog.ApiPackage,
            OracleErrorCatalog.EnginePackage,
            OracleErrorCatalog.ImportPackage,
        });

        IReadOnlyCollection<string> bundledOffer = Array.AsReadOnly(new[]
        {
            OracleErrorCatalog.ApiPackage,
            OracleErrorCatalog.EnginePackage,
        });

        IReadOnlyCollection<string> packageLines = Array.AsReadOnly(new[]
        {
            OracleErrorCatalog.ApiPackage,
            OracleErrorCatalog.ImportPackage,
            OracleErrorCatalog.EnginePackage,
        });

        IReadOnlyCollection<string> import = Array.AsReadOnly(new[]
        {
            OracleErrorCatalog.ImportPackage,
            OracleErrorCatalog.EnginePackage,
        });

        var map = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["CalculatePreview"] = previewAndCreate,
            ["CreateFullInvoice"] = previewAndCreate,
            ["GetBundledOfferLines"] = bundledOffer,
            ["GetPackageLines"] = packageLines,
            ["ImportRequestLines"] = import,
            ["GetVisitLine"] = import,
        };

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>A two-digit open-item id such as OI-11 at the start of a message, not followed by a further digit.</summary>
    [GeneratedRegex("^OI-[0-9]{2}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex OpenItemIdRegex();
}
