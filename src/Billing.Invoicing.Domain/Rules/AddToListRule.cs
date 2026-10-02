using Billing.Invoicing.Domain.Model;

namespace Billing.Invoicing.Domain.Rules;

/// <summary>DR-23: derives the header <c>ADD_TO_LIST</c> flag from the draft's service queue flags (T029, T066).</summary>
public static class AddToListRule
{
    private const int PackageServiceLocation = 14;

    private const int Queued = 1;

    /// <summary>Returns the <c>ADD_TO_LIST</c> value for a draft whose lines have the given service profiles.</summary>
    /// <param name="profiles">Server-read profiles of the draft's line services; a package profile carries its component flags. Null entries are skipped.</param>
    /// <returns>1 when any service has <c>ADD_TO_QUE = 1</c>, or sits at <c>SERV_LOC_ID = 14</c> with a component whose <c>ADD_TO_QUE = 1</c>; otherwise 0.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is null.</exception>
    public static int Derive(IEnumerable<ServiceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                continue;
            }

            if (IsQueued(profile) || HasQueuedComponent(profile))
            {
                return 1;
            }
        }

        return 0;
    }

    private static bool IsQueued(ServiceProfile? profile) => (profile?.AddToQue ?? 0) == Queued;

    private static bool HasQueuedComponent(ServiceProfile profile)
    {
        if (profile.ServLocId != PackageServiceLocation || profile.Components is null)
        {
            return false;
        }

        foreach (var component in profile.Components)
        {
            if (IsQueued(component))
            {
                return true;
            }
        }

        return false;
    }
}
