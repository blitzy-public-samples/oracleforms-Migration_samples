using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Billing.Invoicing.Tests.Architecture;

/// <summary>Generates the reverse traceability-matrix key set.</summary>
public static class ReverseMatrixKeys
{
    private const string SolutionFileName = "SmallCashInvoice.sln";
    private const string KeyedNamespacePrefix = "Billing.Invoicing.";
    private const string NodeModulesDirectoryName = "node_modules";
    private const string DeclarationFileSuffix = ".d.ts";
    private const string HostEntryFileName = "Program.cs";

    private const BindingFlags DeclaredPublicMethods =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly[] KeyedAssemblies =
    [
        typeof(Billing.Invoicing.Domain.Model.Money).Assembly,
        typeof(Billing.Invoicing.Data.Plsql.PlsqlBlocks).Assembly,
        typeof(Billing.Invoicing.Api.Services.InvoiceWorkflowService).Assembly,
    ];

    private static readonly HashSet<string> ExcludedMethodNames = new(StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "ToString",
        "PrintMembers",
        "Deconstruct",
    };

    private static readonly HashSet<string> WebModuleExtensions = new(StringComparer.Ordinal) { ".ts", ".tsx" };

    private static readonly (string ProjectDirectory, string Key)[] HostEntryFiles =
    [
        ("Billing.Invoicing.Api", "Api/Program.cs"),
        ("Billing.Invoicing.Web", "Web/Program.cs"),
    ];

    private static readonly Regex GenericAritySuffix = new(@"`\d+", RegexOptions.CultureInvariant);

    /// <summary>Generates the reverse traceability-matrix key set.</summary>
    /// <returns>Every type, method, Web module and host-file key, sorted ordinally.</returns>
    public static IReadOnlySet<string> Generate()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in KeyedAssemblies)
        {
            AddAssemblyKeys(assembly, keys);
        }

        var root = FindRepositoryRoot();
        AddWebModuleKeys(root, keys);
        AddHostKeys(root, keys);

        return keys;
    }

    /// <summary>Adds the type key of each keyed exported type and the method keys of each keyed non-interface type.</summary>
    /// <param name="assembly">Assembly to reflect.</param>
    /// <param name="keys">Receives the keys.</param>
    private static void AddAssemblyKeys(Assembly assembly, ISet<string> keys)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (!IsKeyedType(type))
            {
                continue;
            }

            var typeKey = TypeKey(type);
            keys.Add(typeKey);

            if (type.IsInterface)
            {
                continue;
            }

            foreach (var method in type.GetMethods(DeclaredPublicMethods))
            {
                if (IsKeyedMethod(method))
                {
                    keys.Add(typeKey + "." + method.Name);
                }
            }
        }
    }

    /// <summary>Returns whether a type is source-declared and lives in a Billing.Invoicing namespace.</summary>
    /// <param name="type">Exported type.</param>
    private static bool IsKeyedType(Type type) =>
        !type.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && !type.Name.Contains('<')
        && type.Namespace is { } typeNamespace
        && typeNamespace.StartsWith(KeyedNamespacePrefix, StringComparison.Ordinal);

    /// <summary>Returns whether a declared public method is keyed: not an accessor, operator, compiler-generated or record-synthesized member.</summary>
    /// <param name="method">Declared public method.</param>
    private static bool IsKeyedMethod(MethodInfo method) =>
        !method.IsSpecialName
        && !method.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && !method.Name.Contains('<')
        && !ExcludedMethodNames.Contains(method.Name);

    /// <summary>Returns the dotted full name of a type, without generic arity suffixes.</summary>
    /// <param name="type">Keyed type.</param>
    private static string TypeKey(Type type)
    {
        var fullName = type.FullName ?? type.Namespace + "." + type.Name;
        return GenericAritySuffix.Replace(fullName.Replace('+', '.'), string.Empty);
    }

    /// <summary>Adds one key per TypeScript module below the Web client source directory, skipping declaration files and node_modules.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="keys">Receives the keys.</param>
    private static void AddWebModuleKeys(string root, ISet<string> keys)
    {
        var sourceDirectory = Path.Combine(root, "src", "Billing.Invoicing.Web", "ClientApp", "src");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new InvalidOperationException("Web client source directory not found: " + sourceDirectory);
        }

        var pending = new Stack<string>();
        pending.Push(sourceDirectory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (IsWebModuleFile(file))
                {
                    keys.Add(WebModuleKey(sourceDirectory, file));
                }
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (!string.Equals(Path.GetFileName(child), NodeModulesDirectoryName, StringComparison.Ordinal))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>Returns whether a file is a .ts or .tsx module other than a .d.ts declaration file.</summary>
    /// <param name="file">Full file path.</param>
    private static bool IsWebModuleFile(string file) =>
        WebModuleExtensions.Contains(Path.GetExtension(file))
        && !Path.GetFileName(file).EndsWith(DeclarationFileSuffix, StringComparison.Ordinal);

    /// <summary>Returns the module path relative to the source directory, without extension and with '/' separators.</summary>
    /// <param name="sourceDirectory">Web client source directory.</param>
    /// <param name="file">Full module path below it.</param>
    private static string WebModuleKey(string sourceDirectory, string file)
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, file);
        var withoutExtension = relativePath[..^Path.GetExtension(relativePath).Length];
        return withoutExtension
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>Adds the Api and Web host entry-file keys after confirming each file exists.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="keys">Receives the keys.</param>
    private static void AddHostKeys(string root, ISet<string> keys)
    {
        foreach (var (projectDirectory, key) in HostEntryFiles)
        {
            var path = Path.Combine(root, "src", projectDirectory, HostEntryFileName);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("Host entry file not found: " + path);
            }

            keys.Add(key);
        }
    }

    /// <summary>Returns the nearest directory at or above the test output directory that holds the solution file.</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(SolutionFileName + " not found above " + AppContext.BaseDirectory);
    }
}
