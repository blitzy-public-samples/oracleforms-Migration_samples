using System.Reflection;
using System.Xml.Linq;

namespace Billing.Invoicing.Tests.Architecture;

/// <summary>Isolation checks for the Domain project file and assembly.</summary>
[Trait("Category", "Compliance")]
public sealed class DomainIsolationTests
{
    private const string SolutionFileName = "SmallCashInvoice.sln";

    private static readonly string[] ForbiddenAssemblyPrefixes = ["Oracle.", "Dapper", "Microsoft.AspNetCore"];

    private static readonly string[] ForbiddenSiblingAssemblies =
        ["Billing.Invoicing.Data", "Billing.Invoicing.Api", "Billing.Invoicing.Web"];

    private static readonly string[] ForbiddenReferenceElements = ["PackageReference", "ProjectReference"];

    /// <summary>Asserts the Domain assembly references no Oracle, Dapper, ASP.NET Core or sibling project assembly.</summary>
    [Fact]
    public void DomainAssembly_ReferencesNoOracleDapperAspNetOrSiblingProject()
    {
        AssemblyName[] referenced = typeof(Billing.Invoicing.Domain.Model.Money).Assembly.GetReferencedAssemblies();

        var offending = referenced
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbiddenAssemblyName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(
            offending.Count == 0,
            "Billing.Invoicing.Domain references forbidden assemblies: " + string.Join(", ", offending));
    }

    /// <summary>Asserts the Domain project file declares no package or project reference.</summary>
    [Fact]
    public void DomainProject_DeclaresNoPackageOrProjectReference()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(), "src", "Billing.Invoicing.Domain", "Billing.Invoicing.Domain.csproj");
        Assert.True(File.Exists(projectPath), $"Domain project file not found: {projectPath}");

        var declared = XDocument.Load(projectPath)
            .Descendants()
            .Where(element => ForbiddenReferenceElements.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(DescribeReference)
            .ToList();

        Assert.True(
            declared.Count == 0,
            "Billing.Invoicing.Domain.csproj declares references: " + string.Join("; ", declared));
    }

    private static bool IsForbiddenAssemblyName(string name) =>
        ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || ForbiddenSiblingAssemblies.Any(sibling => string.Equals(name, sibling, StringComparison.OrdinalIgnoreCase));

    private static string DescribeReference(XElement element)
    {
        var target = (string?)element.Attribute("Include")
            ?? (string?)element.Attribute("Update")
            ?? "(no Include)";
        return $"{element.Name.LocalName} {target}";
    }

    /// <summary>Returns the nearest directory at or above the test output folder that holds the solution file.</summary>
    /// <returns>The repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        var start = AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory containing {SolutionFileName} was found at or above {start}.");
    }
}
