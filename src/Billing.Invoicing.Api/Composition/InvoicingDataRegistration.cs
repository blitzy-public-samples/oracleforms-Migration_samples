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
    /// <exception cref="InvalidOperationException">An <c>Invoicing:*</c> value is present but is not an integer.</exception>
    public static IServiceCollection AddInvoicingData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new InvoicingDataOptions
        {
            ConnectionString = configuration.GetConnectionString("HisOracle") ?? string.Empty,
            ApplicationId = ReadInt(configuration, "Invoicing:ApplicationId", 48),
            MaxOutputLines = ReadInt(configuration, "Invoicing:MaxOutputLines", 1000),
            CommandTimeoutSeconds = ReadInt(configuration, "Invoicing:CommandTimeoutSeconds", 30),
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
    /// <returns>The parsed value or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="InvalidOperationException">The value is present but is not an integer.</exception>
    private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
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

        return n;
    }
}
