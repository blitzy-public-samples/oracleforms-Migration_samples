using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Data.Blocked;

/// <summary>Blocked member for the Form's package-consumption mode (PKG_INV); every call throws OI-31.</summary>
public sealed class PackageConsumptionGateway : IPackageConsumptionGateway
{
    private const string Message = $"{OpenItemIds.OI31}: package consumption (PKG_INV, PACKAGE_CONS_M / PACKAGE_CONS) is not available.";

    /// <summary>Stands in for PKG_INV package-consumption registration; throws <see cref="NotImplementedException"/> with OI-31.</summary>
    /// <param name="parameters">Module entry parameters carrying PKG_INV.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Never returns.</returns>
    public Task Begin(InvoiceEntryParameters parameters, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(Message);
}
