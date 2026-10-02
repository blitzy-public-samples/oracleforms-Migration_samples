using CatalogRow = (string Package, int Number, string MessagePrefix, string? Kind, string? Field, string? LegacyText);

namespace Billing.Invoicing.Data.Errors;

/// <summary>Oracle application errors the target branches on or maps to a field, matched on package, number and message prefix.</summary>
public static class OracleErrorCatalog
{
    /// <summary>Package name of BIL_INVOICE_ENGINE.</summary>
    public const string EnginePackage = "BIL_INVOICE_ENGINE";

    /// <summary>Package name of BIL_INVOICE_API.</summary>
    public const string ApiPackage = "BIL_INVOICE_API";

    /// <summary>Package name of BIL_IMPORT.</summary>
    public const string ImportPackage = "BIL_IMPORT";

    /// <summary>Kind for requested service lines already invoiced or no longer available.</summary>
    public const string RequestLinesStaleKind = "RequestLinesStale";

    /// <summary>Kind for a package or offer definition changed after calculation.</summary>
    public const string DefinitionStaleKind = "DefinitionStale";

    /// <summary>Kind for a create request id that cannot be replayed for this invoice or patient.</summary>
    public const string IdempotencyConflictKind = "IdempotencyConflict";

    /// <summary>Kind for a missing application id, session or user in a BIL_IMPORT request call.</summary>
    public const string OperatorContextMissingKind = "OperatorContextMissing";

    /// <summary>Kind for a package service line that reached the engine without its package instance, an unexpanded package parent.</summary>
    public const string UnexpandedPackageParentKind = "UnexpandedPackageParent";

    /// <summary>Catalogue rows in register order; each (Number, MessagePrefix) pair is unique.</summary>
    public static IReadOnlyList<(string Package, int Number, string MessagePrefix, string? Kind, string? Field, string? LegacyText)> Rows { get; } =
        Array.AsReadOnly(new CatalogRow[]
        {
            // BIL_INVOICE_ENGINE.sql:619 (c_request_unavailable_error, :176)
            (EnginePackage, -20931, "One or more requested services were already invoiced or are no longer available.", RequestLinesStaleKind, null, null),
            // BIL_INVOICE_ENGINE.sql:2958
            (EnginePackage, -20930, "Invoice create failed: request line ", RequestLinesStaleKind, null, null),
            // BIL_INVOICE_ENGINE.sql:1308
            (EnginePackage, -20969, "The package definition changed after the invoice was calculated. ", DefinitionStaleKind, null, null),
            // BIL_INVOICE_ENGINE.sql:1317
            (EnginePackage, -20970, "The offer changed after the invoice was calculated. ", DefinitionStaleKind, null, null),
            // BIL_INVOICE_ENGINE.sql:2496
            (EnginePackage, -20949, "Invoice create failed: package instance identity is required.", UnexpandedPackageParentKind, null, null),
            // BIL_INVOICE_ENGINE.sql:500
            (EnginePackage, -20900, "Invoice create failed: patient number is required.", null, "PATIENTNO", null),
            // BIL_INVOICE_ENGINE.sql:520
            (EnginePackage, -20923, "Invoice create failed: clinic is required when doctor is supplied.", null, "CLINICID", null),
            // BIL_INVOICE_ENGINE.sql:534
            (EnginePackage, -20924, "Invoice create failed: selected doctor does not belong to the selected clinic.", null, "DOCIDX", null),
            // BIL_INVOICE_ENGINE.sql:550
            (EnginePackage, -20903, "Invoice create failed: at least one service line is required.", null, null, "Invoice without Details"),
            // BIL_INVOICE_ENGINE.sql:569
            (EnginePackage, -20905, "Invoice create failed: quantity must be a positive whole number on line ", null, "QTY", null),
            // BIL_INVOICE_ENGINE.sql:1256
            (EnginePackage, -20914, "Invoice create failed: final discount cannot exceed patient share.", null, "FINALDISC", "discount is greater than cash payed amount"),
            // BIL_INVOICE_ENGINE.sql:3062
            (EnginePackage, -20916, "Invoice preview failed: cash collected cannot be negative.", null, null, null),
            // BIL_INVOICE_ENGINE.sql:3229
            (EnginePackage, -20916, "Invoice create failed: cash collected cannot be negative.", null, null, null),
            // BIL_INVOICE_ENGINE.sql:1065
            (EnginePackage, -20780, "Invoice line ", null, "PRICE", null),
            // BIL_INVOICE_ENGINE.sql:1072
            (EnginePackage, -20781, "Invoice line ", null, "PRICE", null),

            // BIL_INVOICE_API.sql:313
            (ApiPackage, -20871, "Bundled Offers are available only for Cash invoices.", null, "OFERID", null),
            // BIL_INVOICE_API.sql:1289, 1335, 1467
            (ApiPackage, -20848, "Invoice request ", IdempotencyConflictKind, null, null),
            // BIL_INVOICE_API.sql:1345
            (ApiPackage, -20849, "Invoice request ", IdempotencyConflictKind, null, null),

            // BIL_IMPORT.sql:519
            (ImportPackage, -20771, "Request import failed: selected request line ", null, null, null),
            // BIL_IMPORT.sql:709
            (ImportPackage, -20771, "Request package expansion failed: multiplied quantity exceeds the supported two-decimal quantity precision for component ", null, null, null),
            // BIL_IMPORT.sql:783
            (ImportPackage, -20772, "Request selection failed: patient number is required.", null, null, null),
            // BIL_IMPORT.sql:1062
            (ImportPackage, -20772, "Package import failed: package service ", null, null, null),
            // BIL_IMPORT.sql:699
            (ImportPackage, -20773, "Request package expansion failed: invalid component definition for package ", null, null, null),
            // BIL_IMPORT.sql:790
            (ImportPackage, -20773, "Request selection failed: visit unique is required.", null, null, null),
            // BIL_IMPORT.sql:1070
            (ImportPackage, -20773, "Package import failed: package service ", null, null, null),
            // BIL_IMPORT.sql:748
            (ImportPackage, -20774, "Request package expansion failed: package ", null, null, null),
            // BIL_IMPORT.sql:797
            (ImportPackage, -20774, "Request selection failed: request line row ID is required.", null, null, null),
            // BIL_IMPORT.sql:421
            (ImportPackage, -20778, "Request import failed: application ID is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:806
            (ImportPackage, -20778, "Request selection failed: application ID is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:918
            (ImportPackage, -20778, "Request selection clear failed: application ID is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:427
            (ImportPackage, -20779, "Request import failed: application session is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:813
            (ImportPackage, -20779, "Request selection failed: application session is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:925
            (ImportPackage, -20779, "Request selection clear failed: application session is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:433
            (ImportPackage, -20782, "Request import failed: application user is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:820
            (ImportPackage, -20782, "Request selection failed: application user is required.", OperatorContextMissingKind, null, null),
            // BIL_IMPORT.sql:932
            (ImportPackage, -20782, "Request selection clear failed: application user is required.", OperatorContextMissingKind, null, null),
        });

    /// <summary>Finds the catalogue row for an application error raised by one of the candidate packages.</summary>
    /// <param name="candidatePackages">Packages reachable from the failing operation, compared ordinally.</param>
    /// <param name="number">Signed Oracle error number, for example -20931.</param>
    /// <param name="text">Error text after the <c>ORA-2nnnn: </c> prefix.</param>
    /// <returns>The row whose package, number and message prefix all match, preferring the longest prefix; otherwise <see langword="null"/>.</returns>
    public static (string Package, int Number, string MessagePrefix, string? Kind, string? Field, string? LegacyText)? Find(
        IReadOnlyCollection<string> candidatePackages,
        int number,
        string text)
    {
        if (candidatePackages is null || candidatePackages.Count == 0 || text is null)
        {
            return null;
        }

        CatalogRow? match = null;
        foreach (var row in Rows)
        {
            if (row.Number != number
                || !text.StartsWith(row.MessagePrefix, StringComparison.Ordinal)
                || !ContainsOrdinal(candidatePackages, row.Package))
            {
                continue;
            }

            if (match is null || row.MessagePrefix.Length > match.Value.MessagePrefix.Length)
            {
                match = row;
            }
        }

        return match;
    }

    private static bool ContainsOrdinal(IReadOnlyCollection<string> packages, string package)
    {
        foreach (var candidate in packages)
        {
            if (string.Equals(candidate, package, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
