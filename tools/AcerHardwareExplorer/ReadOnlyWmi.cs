using System.Management;
using System.Text.RegularExpressions;

namespace AcerHardwareExplorer;

/// <summary>
/// The ONLY door to WMI in this tool. It exposes enumeration, SELECT queries and class metadata.
/// There is deliberately no method-invocation, no property assignment, no delete.
/// (ExplorerReadOnlyGuardTests enforces this at the source level.)
/// </summary>
internal static class ReadOnlyWmi
{
    private static readonly Regex SafeIdentifier = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private static ManagementScope Connect(string ns)
    {
        var scope = new ManagementScope(ns, new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Timeout = TimeSpan.FromSeconds(15)
        });
        scope.Connect();
        return scope;
    }

    public static List<string> ChildNamespaces(string ns)
    {
        var scope = Connect(ns);
        var result = new List<string>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM __NAMESPACE"));
        foreach (ManagementBaseObject o in searcher.Get())
            using (o) result.Add($"{ns}\\{o["Name"]}");
        return result;
    }

    public static List<string> ClassNames(string ns)
    {
        var scope = Connect(ns);
        var names = new List<string>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM meta_class"));
        foreach (ManagementBaseObject o in searcher.Get())
            using (o) names.Add(o["__CLASS"]?.ToString() ?? "");
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>Reads class metadata only (schema, qualifiers, method signatures). Never calls anything.</summary>
    public static ClassInfoDto DescribeClass(string ns, string className)
    {
        var scope = Connect(ns);
        using var cls = new ManagementClass(scope, new ManagementPath(className),
            new ObjectGetOptions(null, TimeSpan.FromSeconds(15), true));
        cls.Get();

        string? guid = null, desc = null;
        foreach (QualifierData q in cls.Qualifiers)
        {
            if (q.Name.Equals("guid", StringComparison.OrdinalIgnoreCase)) guid = Format(q.Value)?.Trim('{', '}');
            else if (q.Name.Equals("Description", StringComparison.OrdinalIgnoreCase)) desc = Format(q.Value);
        }

        var props = new List<PropertyInfoDto>();
        foreach (PropertyData p in cls.Properties)
            props.Add(new PropertyInfoDto(p.Name, p.Type.ToString(), p.IsArray, QualifierText(p.Qualifiers, "Description")));

        var methods = new List<MethodInfoDto>();
        foreach (MethodData m in cls.Methods)
        {
            var q = new Dictionary<string, string>();
            foreach (QualifierData mq in m.Qualifiers) q[mq.Name] = Format(mq.Value) ?? "";
            methods.Add(new MethodInfoDto(m.Name, q.GetValueOrDefault("Description"),
                Params(m.InParameters), Params(m.OutParameters), q));
        }

        return new ClassInfoDto(ns, className, guid, desc, Array.Empty<string>(), props, methods);
    }

    public static List<Dictionary<string, string?>> Query(string ns, string wql, params string[] props)
    {
        if (!wql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only SELECT queries are permitted.");

        var scope = Connect(ns);
        var rows = new List<Dictionary<string, string?>>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        foreach (ManagementBaseObject o in searcher.Get())
            using (o)
            {
                var row = new Dictionary<string, string?>();
                if (props.Length == 0)
                    foreach (PropertyData p in o.Properties) row[p.Name] = Format(p.Value);
                else
                    foreach (var name in props) row[name] = Format(SafeGet(o, name));
                rows.Add(row);
            }
        return rows;
    }

    public static List<InstanceDto> ReadInstances(string ns, string className, int max = 8)
    {
        if (!SafeIdentifier.IsMatch(className)) throw new ArgumentException("Invalid class name", nameof(className));
        var scope = Connect(ns);
        var list = new List<InstanceDto>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}"));
        foreach (ManagementBaseObject o in searcher.Get())
        {
            using (o)
            {
                var values = new Dictionary<string, string?>();
                foreach (PropertyData p in o.Properties) values[p.Name] = Format(p.Value);
                list.Add(new InstanceDto($"{ns}:{className}", values));
            }
            if (list.Count >= max) break;
        }
        return list;
    }

    public static string? Format(object? v) => v switch
    {
        null => null,
        Array a => "[" + string.Join(", ", a.Cast<object?>().Take(64).Select(Format)) + (a.Length > 64 ? ", …" : "") + "]",
        _ => v.ToString()
    };

    private static object? SafeGet(ManagementBaseObject o, string name)
    {
        try { return o[name]; } catch (ManagementException) { return null; }
    }

    private static List<PropertyInfoDto> Params(ManagementBaseObject? o)
    {
        var l = new List<PropertyInfoDto>();
        if (o == null) return l;
        foreach (PropertyData p in o.Properties)
            l.Add(new PropertyInfoDto(p.Name, p.Type.ToString(), p.IsArray, QualifierText(p.Qualifiers, "Description")));
        return l;
    }

    private static string? QualifierText(QualifierDataCollection quals, string name)
    {
        foreach (QualifierData q in quals)
            if (q.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return Format(q.Value);
        return null;
    }
}
