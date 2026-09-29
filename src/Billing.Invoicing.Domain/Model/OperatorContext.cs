namespace Billing.Invoicing.Domain.Model;

/// <summary>Identity of the operator working on an invoice: user, information centre, machine and session.</summary>
public sealed record OperatorContext
{
    private const int MaxMachineNameLength = 15;

    private string _machineName = string.Empty;

    /// <summary>Operator user number, bound as the invoice header <c>user_no</c>.</summary>
    public int UserNo { get; init; }

    /// <summary>Operator user name, passed as the request-selection application user.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Operator information centre id, bound as the invoice header <c>info_center_id</c>.</summary>
    public string InfoCenterId { get; init; } = string.Empty;

    /// <summary>Operator machine name, at most 15 characters; a longer value keeps its first 15.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string MachineName
    {
        get => _machineName;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _machineName = value.Length > MaxMachineNameLength ? value[..MaxMachineNameLength] : value;
        }
    }

    /// <summary>Operator session id, passed as the request-selection application session.</summary>
    public string SessionId { get; init; } = string.Empty;
}
