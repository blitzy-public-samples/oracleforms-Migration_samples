namespace Billing.Invoicing.Domain.Model;

/// <summary>Server-read <c>SERVICES</c> flags of one service and, for a package, of its <c>PACKAGE_DTL</c> components.</summary>
public sealed record ServiceProfile
{
    /// <summary>Service id, <c>SERVICES.SERVICEID</c>.</summary>
    public string? ServiceId { get; init; }

    /// <summary><c>SHOW_QTY</c>; 1 limits the line quantity to 1.</summary>
    public int? ShowQty { get; init; }

    /// <summary><c>BEGIN_OF_CLAIM</c>; 0 means the service needs a doctor request on an insured claim.</summary>
    public int? BeginOfClaim { get; init; }

    /// <summary><c>ADD_TO_QUE</c>; 1 marks a service that is added to the visit queue.</summary>
    public int? AddToQue { get; init; }

    /// <summary><c>SERV_LOC_ID</c>; 14 is the package service location.</summary>
    public int? ServLocId { get; init; }

    /// <summary><c>CONS_REV</c>; 1 and 2 mark claim and revisit limits.</summary>
    public int? ConsRev { get; init; }

    /// <summary><c>IS_PACKAGE</c>; 1 marks a package service.</summary>
    public int? IsPackage { get; init; }

    /// <summary><c>PKG_TYPE</c>; 3 marks an advance-instalment package.</summary>
    public int? PkgType { get; init; }

    /// <summary><c>PRICE_IS_FIXED</c>, 'Y' or 'N'; 'N' lets a cash line's price be edited.</summary>
    public string? PriceIsFixed { get; init; }

    /// <summary><c>REQ_NEED_A</c>; non-zero means the service needs approval.</summary>
    public int? ReqNeedA { get; init; }

    /// <summary>Flags of the package's <c>PACKAGE_DTL</c> component services; empty for a non-package service.</summary>
    public IReadOnlyList<ServiceProfile> Components { get; init; } = Array.Empty<ServiceProfile>();
}
