using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2) throw new ArgumentException("Pass baseline and candidate SDK DLLs.");
HashSet<string> before = ReadSurface(Path.GetFullPath(args[0]));
HashSet<string> after = ReadSurface(Path.GetFullPath(args[1]));
string[] missing = before.Except(after).Order().ToArray();
foreach (string signature in missing) Console.WriteLine("MISSING: " + signature);
Console.WriteLine($"Baseline public signatures: {before.Count}; removed: {missing.Length}; new: {after.Except(before).Count()}.");
if (missing.Length != 0) throw new InvalidOperationException("Existing SDK public signatures must be preserved.");

static HashSet<string> ReadSurface(string path)
{
    var context = new AssemblyLoadContext("SDK-surface-" + Guid.NewGuid(), isCollectible: true);
    try
    {
        Assembly assembly = context.LoadFromAssemblyPath(path);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in assembly.GetExportedTypes())
        {
            signatures.Add($"TYPE|{type.FullName}|{type.IsInterface}|{type.IsValueType}|{type.IsSealed}|{type.IsAbstract}|{type.BaseType}");
            foreach (Type contract in type.GetInterfaces()) signatures.Add($"IMPLEMENTS|{type.FullName}|{contract}");
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                signatures.Add($"MEMBER|{type.FullName}|{member.MemberType}|{member}");
            }
        }
        return signatures;
    }
    finally { context.Unload(); }
}
