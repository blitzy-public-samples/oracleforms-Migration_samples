using Billing.Invoicing.Api.Composition;
using Billing.Invoicing.Data.Commands;
using Billing.Invoicing.Data.Oracle;
using Billing.Invoicing.Data.Plsql;
using Billing.Invoicing.Data.Ports;
using Billing.Invoicing.Data.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Invoicing.Tests.Api;

/// <summary>Configuration validation and service registration of <see cref="InvoicingDataRegistration.AddInvoicingData"/>, and the command-timeout check of the query classes it registers.</summary>
[Trait("Category", "Orchestration")]
public sealed class InvoicingDataRegistrationTests
{
    private const string ApplicationIdKey = "Invoicing:ApplicationId";
    private const string MaxOutputLinesKey = "Invoicing:MaxOutputLines";
    private const string CommandTimeoutSecondsKey = "Invoicing:CommandTimeoutSeconds";

    private const string ConnectionString = "Data Source=his-test/HIS;User Id=billing;Password=unused";
    private const string SealKey = "SW52b2ljaW5nRGF0YVJlZ2lzdHJhdGlvblRlc3RzIGRyYWZ0LXNlYWwga2V5";
    private const string RequestId = "7D3F2C1B9A8E4F6D8C2B1A0F9E8D7C6B";

    private static readonly DateTime DraftDate = new(2026, 9, 30, 23, 59, 59);

    [Theory]
    [InlineData(CommandTimeoutSecondsKey, "0")]
    [InlineData(CommandTimeoutSecondsKey, "-1")]
    [InlineData(MaxOutputLinesKey, "0")]
    [InlineData(MaxOutputLinesKey, "-1")]
    public void ValueBelowOne_ThrowsNamingTheKeyBeforeRegistering(string key, string value)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = Configuration(new Dictionary<string, string?> { [key] = value });

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddInvoicingData(configuration));

        Assert.Equal($"Configuration value '{key}' must be at least 1.", exception.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void CommandTimeoutAboveTheMaximum_ThrowsNamingTheKeyBeforeRegistering()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            [CommandTimeoutSecondsKey] = "4294968",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddInvoicingData(configuration));

        Assert.Equal($"Configuration value '{CommandTimeoutSecondsKey}' must be at most 4294967.", exception.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void CommandTimeoutAtTheMaximum_IsBound()
    {
        InvoicingDataOptions options = RegisteredOptions(new Dictionary<string, string?>
        {
            [CommandTimeoutSecondsKey] = "4294967",
        });

        Assert.Equal(InvoicingDataOptions.MaxCommandTimeoutSeconds, options.CommandTimeoutSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentOrBlankValues_YieldTheDefaults(string? value)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings[ApplicationIdKey] = value;
            settings[MaxOutputLinesKey] = value;
            settings[CommandTimeoutSecondsKey] = value;
        }

        InvoicingDataOptions options = RegisteredOptions(settings);

        Assert.Equal(48, options.ApplicationId);
        Assert.Equal(1000, options.MaxOutputLines);
        Assert.Equal(30, options.CommandTimeoutSeconds);
    }

    [Theory]
    [InlineData(ApplicationIdKey)]
    [InlineData(MaxOutputLinesKey)]
    [InlineData(CommandTimeoutSecondsKey)]
    public void NonIntegerValue_ThrowsTheIntegerMessage(string key)
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?> { [key] = "abc" });

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInvoicingData(configuration));

        Assert.Equal($"Configuration value '{key}' must be an integer.", exception.Message);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-1", -1)]
    public void ApplicationId_BelowOne_IsAccepted(string value, int expected)
    {
        InvoicingDataOptions options = RegisteredOptions(new Dictionary<string, string?> { [ApplicationIdKey] = value });

        Assert.Equal(expected, options.ApplicationId);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("17", 17)]
    public void ValuesAtOrAboveOne_AreBound(string value, int expected)
    {
        InvoicingDataOptions options = RegisteredOptions(new Dictionary<string, string?>
        {
            [MaxOutputLinesKey] = value,
            [CommandTimeoutSecondsKey] = value,
        });

        Assert.Equal(expected, options.MaxOutputLines);
        Assert.Equal(expected, options.CommandTimeoutSeconds);
    }

    [Fact]
    public void ValidConfiguration_ResolvesTheDataPortsWithoutOpeningAConnection()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            [ApplicationIdKey] = "48",
            [MaxOutputLinesKey] = "1000",
            [CommandTimeoutSecondsKey] = "30",
        });

        using ServiceProvider provider = new ServiceCollection()
            .AddInvoicingData(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<PatientTransferCommand>(provider.GetRequiredService<IPatientTransferCommand>());
        Assert.IsType<LovQueries>(provider.GetRequiredService<ILovQueries>());
        Assert.IsType<LookupQueries>(provider.GetRequiredService<ILookupQueries>());
        Assert.IsType<InvoiceQueries>(provider.GetRequiredService<IInvoiceQueries>());
        Assert.IsType<OracleSessionFactory>(provider.GetRequiredService<IOracleSessionFactory>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void QueryClasses_RejectCommandTimeoutBelowOne(int commandTimeoutSeconds)
    {
        var options = new InvoicingDataOptions { CommandTimeoutSeconds = commandTimeoutSeconds };

        Action[] constructors =
        [
            () => _ = new LovQueries(options),
            () => _ = new LookupQueries(options),
            () => _ = new InvoiceQueries(options),
        ];

        foreach (Action construct in constructors)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(construct);

            Assert.Equal("options", exception.ParamName);
            Assert.Equal(commandTimeoutSeconds, exception.ActualValue);
            Assert.Contains(CommandTimeoutSecondsKey, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public void AddInvoicingData_OracleConfiguredWithoutDraftSealKey_IsRefusedAtStartup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(ConnectionString, null));

        Assert.Equal(
            "Configuration value 'Invoicing:DraftSealKey' must be set when 'ConnectionStrings:HisOracle' is set.",
            error.Message);
    }

    [Theory]
    [Trait("Decision", "D-39")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")]
    public void AddInvoicingData_MalformedOrShortDraftSealKey_IsRefusedAtStartup(string draftSealKey)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(null, draftSealKey));

        Assert.Equal("Configuration value 'Invoicing:DraftSealKey' must be base64 of at least 32 bytes.", error.Message);
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public void AddInvoicingData_ConfiguredKey_SealsAlikeInTwoHosts()
    {
        using var first = Register(ConnectionString, $" {SealKey} ");
        using var restarted = Register(ConnectionString, SealKey);

        Assert.Equal(SealKey, first.GetRequiredService<InvoicingDataOptions>().DraftSealKey);
        Assert.Equal(
            first.GetRequiredService<IBilInvoiceApiGateway>().SealDraftDate(RequestId, DraftDate),
            restarted.GetRequiredService<IBilInvoiceApiGateway>().SealDraftDate(RequestId, DraftDate));
    }

    [Fact]
    [Trait("Decision", "D-39")]
    public void AddInvoicingData_NeitherOracleNorKeyConfigured_SealsUnderAKeyOfThisHost()
    {
        using var first = Register(null, null);
        using var restarted = Register(null, null);

        Assert.Equal(string.Empty, first.GetRequiredService<InvoicingDataOptions>().DraftSealKey);
        Assert.NotEqual(
            first.GetRequiredService<IBilInvoiceApiGateway>().SealDraftDate(RequestId, DraftDate),
            restarted.GetRequiredService<IBilInvoiceApiGateway>().SealDraftDate(RequestId, DraftDate));
    }

    /// <summary>Configuration holding exactly the given settings.</summary>
    /// <param name="settings">Keys and values; a null value is an absent key.</param>
    private static IConfiguration Configuration(IDictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>Registers the Data services over the given settings and returns the bound options.</summary>
    /// <param name="settings">Configuration keys and values.</param>
    /// <returns>The registered <see cref="InvoicingDataOptions"/>.</returns>
    private static InvoicingDataOptions RegisteredOptions(IDictionary<string, string?> settings)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddInvoicingData(Configuration(settings))
            .BuildServiceProvider();

        return provider.GetRequiredService<InvoicingDataOptions>();
    }

    /// <summary>Registers the Data layer from the given connection string and draft-seal key, as one host start does.</summary>
    private static ServiceProvider Register(string? connectionString, string? draftSealKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HisOracle"] = connectionString,
                ["Invoicing:DraftSealKey"] = draftSealKey,
            })
            .Build();

        return new ServiceCollection().AddInvoicingData(configuration).BuildServiceProvider();
    }
}
