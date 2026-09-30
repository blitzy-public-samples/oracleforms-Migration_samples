using System.Globalization;
using Billing.Invoicing.Data.Blocked;
using Billing.Invoicing.Data.Commands;
using Billing.Invoicing.Data.Errors;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Data.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Invoicing.Api.Composition;

/// <summary>Registers the Billing.Invoicing.Data services.</summary>
public static class InvoicingDataRegistration
{
    /// <summary>Binds <see cref="InvoicingDataOptions"/> from configuration and registers every Data class as a singleton by its port.</summary>
    /// <param name="services">Service collection of the host.</param>
    /// <param name="configuration">Configuration holding <c>ConnectionStrings:HisOracle</c> and the <c>Invoicing:*</c> keys.</param>
    /// <returns>The same <paramref name="services"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An <c>Invoicing:*</c> value is present but is not an integer, <c>Invoicing:MaxOutputLines</c> or <c>Invoicing:CommandTimeoutSeconds</c> is below 1, <c>Invoicing:CommandTimeoutSeconds</c> is above <see cref="InvoicingDataOptions.MaxCommandTimeoutSeconds"/>, or <c>Invoicing:DraftSealKey</c> is malformed or is absent while <c>ConnectionStrings:HisOracle</c> is set.</exception>
    public static IServiceCollection AddInvoicingData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("HisOracle") ?? string.Empty;
        var options = new InvoicingDataOptions
        {
            ConnectionString = connectionString,
            ApplicationId = ReadInt(configuration, "Invoicing:ApplicationId", 48, int.MinValue, int.MaxValue),
            MaxOutputLines = ReadInt(configuration, "Invoicing:MaxOutputLines", 1000, 1, int.MaxValue),
            CommandTimeoutSeconds = ReadInt(configuration, "Invoicing:CommandTimeoutSeconds", 30, 1, InvoicingDataOptions.MaxCommandTimeoutSeconds),
            DraftSealKey = ReadDraftSealKey(configuration, connectionString),
        };

        services.AddSingleton(options);

        services.AddSingleton<IOracleSessionFactory, OracleSessionFactory>();
        services.AddSingleton<ILovQueries, LovQueries>();
        services.AddSingleton<ILookupQueries, LookupQueries>();
        services.AddSingleton<IInvoiceQueries, InvoiceQueries>();
        services.AddSingleton<IBilInvoiceApiGateway, BilInvoiceApiGateway>();
        services.AddSingleton<IBilImportGateway, BilImportGateway>();
        services.AddSingleton<IPatientTransferCommand, PatientTransferCommand>();
        services.AddSingleton<ILegacyExternalCalls, LegacyExternalCalls>();
        services.AddSingleton<IPackageConsumptionGateway, PackageConsumptionGateway>();
        services.AddSingleton<OracleFailureTranslator>();

        return services;
    }

    /// <summary>Reads an integer configuration value, or the default when the key is absent or blank.</summary>
    /// <param name="configuration">Configuration to read.</param>
    /// <param name="key">Configuration key.</param>
    /// <param name="defaultValue">Value returned when the key is absent or blank.</param>
    /// <param name="minimum">Smallest value accepted when the key is present.</param>
    /// <param name="maximum">Largest value accepted when the key is present.</param>
    /// <returns>The parsed value or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="InvalidOperationException">The value is present but is not an integer, or is outside <paramref name="minimum"/> to <paramref name="maximum"/>.</exception>
    private static int ReadInt(IConfiguration configuration, string key, int defaultValue, int minimum, int maximum)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be an integer.");
        }

        if (n < minimum)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Configuration value '{key}' must be at least {minimum}."));
        }

        if (n > maximum)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Configuration value '{key}' must be at most {maximum}."));
        }

        return n;
    }

    /// <summary>Reads <c>Invoicing:DraftSealKey</c>, required once an Oracle connection string is set; blank returns empty.</summary>
    /// <param name="configuration">Configuration to read.</param>
    /// <param name="connectionString">Configured <c>ConnectionStrings:HisOracle</c>.</param>
    /// <returns>The trimmed base64 key, or empty when neither the key nor a connection string is set.</returns>
    /// <exception cref="InvalidOperationException">The key is absent while a connection string is set, or is not base64 of at least <see cref="BilInvoiceApiGateway.MinDraftSealKeyBytes"/> bytes.</exception>
    private static string ReadDraftSealKey(IConfiguration configuration, string connectionString)
    {
        const string key = "Invoicing:DraftSealKey";
        var value = configuration[key]?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.IsNullOrWhiteSpace(connectionString)
                ? string.Empty
                : throw new InvalidOperationException($"Configuration value '{key}' must be set when 'ConnectionStrings:HisOracle' is set.");
        }

        if (!Convert.TryFromBase64String(value, new byte[value.Length], out var length) || length < BilInvoiceApiGateway.MinDraftSealKeyBytes)
        {
            throw new InvalidOperationException(
                $"Configuration value '{key}' must be base64 of at least {BilInvoiceApiGateway.MinDraftSealKeyBytes} bytes.");
        }

        return value;
    }
}
