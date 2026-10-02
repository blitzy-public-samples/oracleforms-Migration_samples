using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Workflow;

/// <summary>Detects the open items whose triggering condition a draft meets.</summary>
public static class OpenItemGate
{
    /// <summary>Returns the open-item ids the draft needs, from draft context and server-read lookups.</summary>
    /// <param name="header">Draft header, as carried by the draft; only <see cref="InvoiceHeaderDraft.PayType"/> and <see cref="InvoiceHeaderDraft.SubCompCode"/> are read.</param>
    /// <param name="services">Server-read service profiles of every draft line; package components are inspected recursively.</param>
    /// <param name="cardId">Server-read patient card id (<c>PATIENT.CARD_ID</c> or the claim preload's card id); null when none.</param>
    /// <param name="maxDeductable">Server-read <c>MAX_DEDUCTABLE</c> of the coverage snapshot or claim preload.</param>
    /// <param name="useAdvanced">Server-read <c>DISC_CLASSES.USE_ADVANCED</c> of the draft's class; null when no class.</param>
    /// <param name="parameters">Entry parameters, as carried by the draft; only <see cref="InvoiceEntryParameters.PkgInv"/> is read.</param>
    /// <returns>Distinct open-item ids in ordinal order; empty when no condition holds.</returns>
    public static IReadOnlyList<string> Evaluate(
        InvoiceHeaderDraft header,
        IEnumerable<ServiceProfile> services,
        int? cardId,
        decimal? maxDeductable,
        int? useAdvanced,
        InvoiceEntryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(parameters);

        var hasSubCompany = !string.IsNullOrEmpty(header.SubCompCode);
        var ids = new SortedSet<string>(StringComparer.Ordinal);

        // Queued service with a sub-company, claim or revisit limit, advance-instalment package, or a patient card.
        if (cardId is not null
            || Flatten(services ?? []).Any(profile =>
                (profile.AddToQue == 1 && hasSubCompany)
                || profile.ConsRev is 1 or 2
                || (profile.IsPackage == 1 && profile.PkgType == 3)))
        {
            ids.Add(OpenItemIds.OI32);
        }

        // Package-consumption mode.
        if (parameters.PkgInv is not null)
        {
            ids.Add(OpenItemIds.OI31);
        }

        // Credit draft with a deductible or an advanced class.
        if (header.PayType == 2 && (maxDeductable > 0 || useAdvanced == 2))
        {
            ids.Add(OpenItemIds.OI23);
        }

        return ids.ToArray();
    }

    /// <summary>Yields each non-null profile and its nested components, depth-first, each instance once.</summary>
    private static IEnumerable<ServiceProfile> Flatten(IEnumerable<ServiceProfile?> roots)
    {
        var visited = new HashSet<ServiceProfile>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<ServiceProfile?>(roots.Reverse());

        while (pending.TryPop(out var profile))
        {
            if (profile is null || !visited.Add(profile))
            {
                continue;
            }

            yield return profile;

            if (profile.Components is { Count: > 0 } components)
            {
                for (var index = components.Count - 1; index >= 0; index--)
                {
                    pending.Push(components[index]);
                }
            }
        }
    }
}
