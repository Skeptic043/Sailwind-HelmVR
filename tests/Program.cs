using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HelmVR.Tests
{
    /// <summary>
    /// Offline checks against the installed game: every Harmony patch target and every member HelmVR
    /// looks up by name still exists in Sailwind's Assembly-CSharp, and the README only names real settings.
    /// </summary>
    internal static class Program
    {
        private static int passed;
        private static readonly List<string> failures = new List<string>();

        private static int Main()
        {
            string managed = Metadata("SailwindManaged");
            string plugin = Metadata("PluginOutput");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            var readerParams = new ReaderParameters { AssemblyResolver = resolver };

            // Game types resolve from the installed Managed folder (Assembly-CSharp, Assembly-CSharp-firstpass, Oculus.VR).
            using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(plugin, readerParams))
            {
                Section("Harmony patch targets in the installed game");
                PatchTargets(mod);

                Section("Members looked up by name");
                NamedLookups(mod);

                Section("README settings");
                ReadmeSettings(mod, Metadata("RepoRoot"));
            }

            Console.WriteLine();
            Console.WriteLine($"{passed} passed, {failures.Count} failed");
            foreach (string f in failures)
                Console.WriteLine("  FAILED: " + f);
            return failures.Count == 0 ? 0 : 1;
        }

        // ---- Harmony targets ---------------------------------------------------------

        private static void PatchTargets(AssemblyDefinition mod)
        {
            int count = 0;
            foreach (TypeDefinition type in mod.MainModule.GetTypes())
                Check($"{type.Name} has no class-level [HarmonyPatch] (these checks read method attributes only)",
                    type.CustomAttributes.All(a => a.AttributeType.Name != "HarmonyPatch"));

            foreach (TypeDefinition type in mod.MainModule.GetTypes())
            foreach (MethodDefinition method in type.Methods)
            foreach (CustomAttribute attr in method.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch"))
            {
                count++;
                var args = attr.ConstructorArguments;
                string label = $"{type.Name}.{method.Name}";
                if (args.Count != 2 || !(args[0].Value is TypeReference targetRef) || !(args[1].Value is string targetName))
                {
                    Check($"{label} uses [HarmonyPatch(typeof(T), name)]", false);
                    continue;
                }

                TypeDefinition target = SafeResolve(targetRef);
                if (target == null)
                {
                    Check($"{label}: type {targetRef.FullName} exists", false);
                    continue;
                }

                // Harmony resolves [HarmonyPatch(typeof(T), name)] with AccessTools.DeclaredMethod, so only methods declared on T count.
                List<MethodDefinition> candidates = target.Methods.Where(m => m.Name == targetName).ToList();
                Check($"{label} -> {target.Name}.{targetName} is declared", candidates.Count > 0);
                if (candidates.Count > 0)
                {
                    Check($"{label} -> {target.Name}.{targetName} is not overloaded", candidates.Count == 1);
                    CheckPatchParameters(label, method, candidates[0]);
                }
            }
            Check($"found Harmony patches ({count})", count > 0);
        }

        // Injected parameters (__instance, __result, named arguments) must match the target, or Harmony refuses the patch.
        private static void CheckPatchParameters(string label, MethodDefinition patch, MethodDefinition target)
        {
            foreach (ParameterDefinition p in patch.Parameters)
            {
                if (p.Name == "__instance")
                    Check($"{label}: __instance fits {target.DeclaringType.Name}", !target.IsStatic);
                else if (p.Name == "__result")
                    Check($"{label}: __result matches {target.ReturnType.Name}", StripByRef(p.ParameterType).FullName == target.ReturnType.FullName);
                else if (!p.Name.StartsWith("__"))
                    Check($"{label}: parameter '{p.Name}' exists on {target.Name}",
                        target.Parameters.Any(tp => tp.Name == p.Name && tp.ParameterType.FullName == StripByRef(p.ParameterType).FullName));
            }
        }

        // ---- AccessTools.Field / Method / Property ----------------------------------

        private static void NamedLookups(AssemblyDefinition mod)
        {
            int count = 0;
            foreach (TypeDefinition type in mod.MainModule.GetTypes())
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
            {
                IList<Instruction> ins = method.Body.Instructions;
                for (int i = 0; i < ins.Count; i++)
                {
                    if (!(ins[i].Operand is MethodReference call) || call.DeclaringType.Name != "AccessTools") continue;
                    if (call.Name != "Field" && call.Name != "Method" && call.Name != "Property") continue;

                    // Expected IL: ldtoken T; call Type.GetTypeFromHandle; ldstr "name"; [ldnull]; call AccessTools.X
                    Instruction nameLoad = Previous(ins, i, OpCodes.Ldstr);
                    Instruction typeLoad = nameLoad != null ? Previous(ins, ins.IndexOf(nameLoad), OpCodes.Ldtoken) : null;
                    if (nameLoad == null || !(typeLoad?.Operand is TypeReference targetRef))
                    {
                        Check($"{type.Name}.{method.Name}: AccessTools.{call.Name} uses typeof and a literal name", false);
                        continue;
                    }

                    count++;
                    string name = (string)nameLoad.Operand;
                    TypeDefinition target = SafeResolve(targetRef);
                    bool found = target != null && MemberExists(target, call.Name, name);
                    Check($"{type.Name}: AccessTools.{call.Name}({targetRef.Name}, \"{name}\") exists", found);
                }
            }
            Check($"found named lookups ({count})", count > 0);
        }

        private static bool MemberExists(TypeDefinition type, string kind, string name)
        {
            for (TypeDefinition t = type; t != null; t = SafeResolve(t.BaseType))
            {
                if (kind == "Field" && t.Fields.Any(f => f.Name == name)) return true;
                if (kind == "Method" && t.Methods.Any(m => m.Name == name)) return true;
                if (kind == "Property" && t.Properties.Any(p => p.Name == name)) return true;
            }
            return false;
        }

        // ---- README ------------------------------------------------------------------

        private static void ReadmeSettings(AssemblyDefinition mod, string repoRoot)
        {
            var boundKeys = new HashSet<string>();
            MethodDefinition awake = mod.MainModule.GetTypes().SelectMany(t => t.Methods).FirstOrDefault(m => m.DeclaringType.Name == "Plugin" && m.Name == "Awake");
            Check("Plugin.Awake found", awake != null);
            if (awake == null) return;
            foreach (Instruction i in awake.Body.Instructions)
                if (i.OpCode == OpCodes.Ldstr) boundKeys.Add((string)i.Operand);

            string readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
            int start = readme.IndexOf("## Configuration", StringComparison.Ordinal);
            Check("README has a Configuration section", start >= 0);
            if (start < 0) return;
            int end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            string section = end > 0 ? readme.Substring(start, end - start) : readme.Substring(start);

            var rows = Regex.Matches(section, @"^\|\s*`([A-Za-z0-9_]+)`\s*\|", RegexOptions.Multiline);
            Check("README lists settings", rows.Count > 0);
            foreach (Match row in rows)
                Check($"README setting `{row.Groups[1].Value}` is bound in Plugin.Awake", boundKeys.Contains(row.Groups[1].Value));
        }

        // ---- helpers -----------------------------------------------------------------

        private static TypeDefinition SafeResolve(TypeReference reference)
        {
            try { return reference?.Resolve(); }
            catch (AssemblyResolutionException) { return null; }
        }

        private static TypeReference StripByRef(TypeReference t) => t is ByReferenceType br ? br.ElementType : t;

        private static Instruction Previous(IList<Instruction> ins, int from, OpCode op)
        {
            for (int j = from - 1; j >= 0 && j >= from - 4; j--)
                if (ins[j].OpCode == op) return ins[j];
            return null;
        }

        private static string Metadata(string key) =>
            typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value;

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title);
        }

        private static void Check(string name, bool ok)
        {
            if (ok) passed++;
            else failures.Add(name);
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + name);
        }
    }
}
