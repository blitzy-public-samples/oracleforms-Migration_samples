using System.Collections.Frozen;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace Billing.Invoicing.Data.Errors;

/// <summary>Translates Oracle errors and Data-layer exceptions into <see cref="DataFailure"/> values of the HTTP error contract.</summary>
public sealed partial class OracleFailureTranslator
{
    private const int ApplicationErrorFirst = -20999;
    private const int ApplicationErrorLast = -20000;

    private const int UnprocessableEntityStatus = 422;
    private const int InternalServerErrorStatus = 500;
    private const int NotImplementedStatus = 501;
    private const int ServiceUnavailableStatus = 503;

    private const string UnknownPackage = "UNKNOWN";

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
            return Unavailable(error.Number, error.Text);
        }

        // Every other code, including credential, privilege and account failures while opening, is 500.
        return new DataFailure
        {
            Status = InternalServerErrorStatus,
            Type = DataFailure.OracleErrorType,
            Number = error.Number,
            Package = InnermostFramePackage(error),
            Message = error.Text,
        };
    }

    /// <summary>Translates a Data-layer exception into a failure, or returns null when the exception is not an Oracle, open-item or transport failure.</summary>
    /// <param name="exception">The exception raised by a Data member.</param>
    /// <returns>The failure to return over HTTP, or <see langword="null"/> when the exception is not translated.</returns>
    public DataFailure? Translate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        OracleException? oracleException = FindOracleException(exception);
        Exception? transportFailure = FindTransportFailure(exception);

        if (oracleException is not null)
        {
            return TranslateOracleException(oracleException, transportFailure);
        }

        // A blocked member names its open item at the start of the message.
        if (exception is NotImplementedException)
        {
            Match openItem = OpenItemIdRegex().Match(exception.Message);
            return new DataFailure
            {
                Status = NotImplementedStatus,
                Type = DataFailure.OpenItemType,
                OpenItemId = openItem.Success ? openItem.Value : null,
                Message = exception.Message,
            };
        }

        // A socket failure or timeout without an Oracle error is 503 with no number.
        if (transportFailure is not null)
        {
            return Unavailable(null, transportFailure.Message);
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

    /// <summary>Parses and translates a driver exception, turning a 500 into a 503 when the chain holds a transport failure.</summary>
    private DataFailure TranslateOracleException(OracleException oracleException, Exception? transportFailure)
    {
        OracleErrorInfo info;
        try
        {
            info = OracleErrorParser.FromException(oracleException);
        }
        catch (ArgumentException)
        {
            // Number and message disagree: report the driver values unparsed.
            return new DataFailure
            {
                Status = InternalServerErrorStatus,
                Type = DataFailure.OracleErrorType,
                Number = oracleException.Number,
                Message = oracleException.Message ?? string.Empty,
            };
        }

        DataFailure failure = Translate(info);
        if (failure.Status == InternalServerErrorStatus && transportFailure is not null)
        {
            return Unavailable(failure.Number, failure.Message);
        }

        return failure;
    }

    /// <summary>Builds a 503 oracle-unavailable failure.</summary>
    private static DataFailure Unavailable(int? number, string message) => new()
    {
        Status = ServiceUnavailableStatus,
        Type = DataFailure.OracleUnavailableType,
        Number = number,
        Message = message,
    };

    /// <summary>Returns the packages reachable from a gateway operation; all three when the operation is null or unknown.</summary>
    private static IReadOnlyCollection<string> CandidatePackages(string? operation) =>
        operation is not null && OperationPackages.TryGetValue(operation, out IReadOnlyCollection<string>? packages)
            ? packages
            : AllPackages;

    /// <summary>Returns the package named by the innermost ORA-06512 frame, or null when the error has no frame.</summary>
    private static string? InnermostFramePackage(OracleErrorInfo error) =>
        error.Frames is { Count: > 0 } frames ? frames[0].Package : null;

    /// <summary>Returns the first <see cref="OracleException"/> in the exception and its inner-exception chain.</summary>
    private static OracleException? FindOracleException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException)
            {
                return oracleException;
            }
        }

        return null;
    }

    /// <summary>Returns the first <see cref="SocketException"/> or <see cref="TimeoutException"/> in the exception and its inner-exception chain.</summary>
    private static Exception? FindTransportFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or TimeoutException)
            {
                return current;
            }
        }

        return null;
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

    /// <summary>An open-item id such as OI-11 at the start of a message.</summary>
    [GeneratedRegex("^OI-[0-9]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenItemIdRegex();
}
