using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Health;

/// <summary>A method as written in metadata: owning type, name and parameter shape.</summary>
public sealed record MethodRef(string Type, string Name, string Parameters)
{
    public string Key => $"{Type}.{Name}({Parameters})";
    public override string ToString() => $"{Type}.{Name}({Parameters})";
}

/// <summary>
/// Reads compiled .NET metadata without loading or running any of it.
///
/// This is deliberately read-only inspection rather than reflection: loading a mod's
/// assembly would run its module initialisers, and a Unity game assembly will not load
/// outside the game anyway. Everything here comes from the metadata tables.
/// </summary>
public static class AssemblyIndex
{
    /// <summary>Every method an assembly DEFINES.</summary>
    public static IReadOnlyCollection<MethodRef> Defined(string path)
    {
        var found = new List<MethodRef>();
        Read(path, md =>
        {
            foreach (var handle in md.MethodDefinitions)
            {
                var m = md.GetMethodDefinition(handle);
                string type;
                try { type = md.GetString(md.GetTypeDefinition(m.GetDeclaringType()).Name); }
                catch { continue; }

                try
                {
                    var s = m.DecodeSignature(NameProvider.Instance, null);
                    found.Add(new MethodRef(type, md.GetString(m.Name),
                        string.Join(", ", s.ParameterTypes)));
                }
                catch { /* an undecodable signature is not worth failing a scan over */ }
            }
        });
        return found;
    }

    /// <summary>Every method an assembly CALLS on a type declared elsewhere.</summary>
    public static IReadOnlyCollection<MethodRef> Referenced(string path)
    {
        var found = new List<MethodRef>();
        Read(path, md =>
        {
            foreach (var handle in md.MemberReferences)
            {
                var r = md.GetMemberReference(handle);
                if (r.GetKind() != MemberReferenceKind.Method) continue;
                if (r.Parent.Kind != HandleKind.TypeReference) continue;

                try
                {
                    var type = md.GetString(md.GetTypeReference((TypeReferenceHandle)r.Parent).Name);
                    var s = r.DecodeMethodSignature(NameProvider.Instance, null);
                    found.Add(new MethodRef(type, md.GetString(r.Name),
                        string.Join(", ", s.ParameterTypes)));
                }
                catch { }
            }
        });
        return found;
    }

    /// <summary>
    /// The game methods an assembly patches, read from its [HarmonyPatch] attributes.
    ///
    /// Two mods patching the same method is how animation and combat mods break each
    /// other: both run, and whichever loses is not obvious from inside the game. Only
    /// the declarative attribute form is read - patches registered in code at runtime
    /// cannot be seen without running the mod, which this deliberately never does.
    /// </summary>
    /// <summary>
    /// The simple names of the assemblies this one is compiled against.
    ///
    /// Elin's own package.xml has no dependency element, so this is the only honest
    /// answer to "what does this mod need". A mod built against Custom Whatever Loader
    /// carries a reference to it whether or not anyone wrote that down.
    /// </summary>
    public static IReadOnlyCollection<string> AssemblyRefs(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Read(path, md =>
        {
            foreach (var handle in md.AssemblyReferences)
            {
                try
                {
                    var name = md.GetString(md.GetAssemblyReference(handle).Name);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                catch { }
            }
        });

        return names;
    }

    /// <summary>
    /// Assembly names that are always there and are never a mod's dependency: the
    /// runtime, Unity, and the loader stack every mod is built on.
    /// </summary>
    public static bool IsAmbientAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)
        || name is "mscorlib" or "netstandard" or "Elin" or "Assembly-CSharp"
                or "0Harmony" or "HarmonyLib" or "MonoMod" or "UnityEngine"
                or "PackageLoader" or "Steamworks.NET" or "com.rlabrecque.steamworks.net";

    public static IReadOnlyCollection<string> PatchTargets(string path)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        Read(path, md =>
        {
            foreach (var handle in md.CustomAttributes)
            {
                var attr = md.GetCustomAttribute(handle);

                // Harmony can be referenced or merged into the mod, so the attribute's
                // constructor is a MemberReference in one case and a MethodDefinition in
                // the other. Both forms appear across a real mod folder.
                string? attrName = null;
                try
                {
                    if (attr.Constructor.Kind == HandleKind.MemberReference)
                    {
                        var ctor = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                        if (ctor.Parent.Kind == HandleKind.TypeReference)
                            attrName = md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name);
                    }
                    else if (attr.Constructor.Kind == HandleKind.MethodDefinition)
                    {
                        var ctor = md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                        attrName = md.GetString(md.GetTypeDefinition(ctor.GetDeclaringType()).Name);
                    }
                }
                catch { }

                if (!string.Equals(attrName, "HarmonyPatch", StringComparison.Ordinal)) continue;

                // Only the (Type) and (Type, string) overloads say what is patched in a
                // form worth reading. Checking the constructor's shape first matters for
                // speed as much as correctness: decoding the other overloads throws, and
                // throwing thousands of times across a mod folder is slower than the
                // entire rest of the scan put together.
                // Only overloads that start with the patched type say what is patched in
                // a readable form; Harmony's common one is (Type, string, Type[]).
                // Checking the shape first matters for speed as much as correctness:
                // decoding the other overloads throws, and throwing thousands of times
                // across a mod folder took longer than the entire rest of the scan.
                ImmutableArray<string> shape;
                try
                {
                    if (attr.Constructor.Kind != HandleKind.MemberReference) continue;

                    var ctorRef = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    shape = ctorRef.DecodeMethodSignature(NameProvider.Instance, null).ParameterTypes;

                    // Only overloads that start with the patched type name what is
                    // patched in a readable form; Harmony's common one is
                    // (Type, string, Type[]).
                    if (shape.Length is < 1 or > 4) continue;
                    if (!shape[0].EndsWith("Type", StringComparison.Ordinal)) continue;
                }
                catch { continue; }

                // The blob is read directly rather than through DecodeValue. Both give
                // the same answer, but DecodeValue on these attributes was slower than
                // every other part of the scan combined - it took over two minutes across
                // one mod folder, against milliseconds for this.
                //
                // Layout is ECMA-335 II.23.3: a 0x0001 prolog, then each fixed argument
                // in constructor order. typeof(X) and string are both stored as a
                // length-prefixed UTF-8 string, which is all this needs to read.
                try
                {
                    var blob = md.GetBlobReader(attr.Value);
                    if (blob.RemainingBytes < 2 || blob.ReadUInt16() != 1) continue;

                    var typeName = blob.ReadSerializedString();
                    if (string.IsNullOrWhiteSpace(typeName)) continue;

                    string? methodName = null;
                    if (shape.Length >= 2 && shape[1] == "string" && blob.RemainingBytes > 0)
                        methodName = blob.ReadSerializedString();

                    // The stored name carries the assembly; only the type matters here.
                    var type = Short(typeName.Split(',')[0].Trim());
                    if (type.Length == 0) continue;

                    found.Add(string.IsNullOrWhiteSpace(methodName) ? type : $"{type}.{methodName}");
                }
                catch { }
            }
        });

        return found;
    }

    private static string Short(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot >= 0 && dot < typeName.Length - 1 ? typeName[(dot + 1)..] : typeName;
    }

    /// <summary>True when the file is a managed assembly this can read at all.</summary>
    public static bool IsManaged(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            return pe.HasMetadata;
        }
        catch { return false; }
    }

    private static void Read(string path, Action<MetadataReader> body)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return;
            body(pe.GetMetadataReader());
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Could not read metadata from {path}: {ex.Message}");
        }
    }

    /// <summary>Decodes custom attribute arguments; typeof(X) arrives as a type name.</summary>
    private sealed class AttributeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly AttributeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode c) => NameProvider.Instance.GetPrimitiveType(c);
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string e) => e + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte b)
            => NameProvider.Instance.GetTypeFromDefinition(r, h, b);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte b)
            => NameProvider.Instance.GetTypeFromReference(r, h, b);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }

    /// <summary>
    /// Decodes signatures to type NAMES only. Full resolution would need every referenced
    /// assembly on hand; names are enough to tell "HealHP(int)" from "HealHP(int64)",
    /// which is the shape of every version-mismatch crash.
    /// </summary>
    internal sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public static readonly NameProvider Instance = new();

        public string GetArrayType(string e, ArrayShape s) => e + "[]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetFunctionPointerType(MethodSignature<string> si) => "fnptr";
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => $"{g}<{string.Join(",", a)}>";
        public string GetGenericMethodParameter(object? _, int i) => "!!" + i;
        public string GetGenericTypeParameter(object? _, int i) => "!" + i;
        public string GetModifiedType(string m, string u, bool r) => u;
        public string GetPinnedType(string e) => e;
        public string GetPointerType(string e) => e + "*";
        public string GetSZArrayType(string e) => e + "[]";

        public string GetPrimitiveType(PrimitiveTypeCode c) => c switch
        {
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            _ => c.ToString().ToLowerInvariant(),
        };

        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte _)
            => r.GetString(r.GetTypeDefinition(h).Name);

        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte _)
            => r.GetString(r.GetTypeReference(h).Name);

        public string GetTypeFromSpecification(MetadataReader r, object? g, TypeSpecificationHandle h, byte b)
            => r.GetTypeSpecification(h).DecodeSignature(this, g);
    }
}
