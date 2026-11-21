// Standalone, reversible visual-brand patch for the user's local launcher copy.
// Compile with .NET Framework csc and dnlib.dll; the original executable is never modified.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.IO;
using dnlib.W32Resources;

internal static class PatchBrand
{
    const string Brand = "Cassell CL";
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--verify") return Verify(args[1], args[2]);
            if (args.Length != 5)
            {
                Console.Error.WriteLine("Usage: PatchBrand original.exe theme.dll icon.png icon.ico output.exe");
                Console.Error.WriteLine("       PatchBrand --verify original.exe output.exe");
                return 2;
            }
            string original = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[4]);
            if (String.Equals(original, output, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The output must be a separate copy of the original.");
            byte[] theme = File.ReadAllBytes(args[1]);
            byte[] png = File.ReadAllBytes(args[2]);
            byte[] ico = File.ReadAllBytes(args[3]);
            using (var themeModule = ModuleDefMD.Load(theme))
            {
                var themeType = themeModule.Find("InfiniteCL.Theme", false);
                if (themeType == null || !themeType.Methods.Any(m => m.Name == "Install" && m.IsPublic && m.IsStatic && m.MethodSig.Params.Count == 0))
                    throw new InvalidDataException("Theme DLL must expose public static InfiniteCL.Theme.Install().");
            }
            using (var module = ModuleDefMD.Load(original))
            {
                LauncherUpdatePolicy.Apply(module,true);
                InjectTheme(module, theme);
                SetAssemblyAttribute(module, "System.Reflection.AssemblyTitleAttribute", Brand);
                SetAssemblyAttribute(module, "System.Reflection.AssemblyProductAttribute", Brand);
                ReplaceManagedIcons(module, png, ico);
                ReplaceNativeIcons(module, ico);
                ReplaceVersionStrings(module);
                var options = new ModuleWriterOptions(module);
                options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                module.Write(output, options);
            }
            Console.WriteLine("OUTPUT " + output);
            Console.WriteLine("THEME_BYTES " + theme.Length);
            return Verify(original, output);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static void InjectTheme(ModuleDefMD module, byte[] theme)
    {
        var main = module.EntryPoint;
        if (main == null || !main.HasBody) throw new InvalidDataException("Managed entry point was not found.");
        var importer = new Importer(module);
        var code = new[] {
            Instruction.Create(OpCodes.Ldstr, Convert.ToBase64String(theme)),
            Instruction.Create(OpCodes.Call, importer.Import(typeof(Convert).GetMethod("FromBase64String", new[] { typeof(string) }))),
            Instruction.Create(OpCodes.Call, importer.Import(typeof(Assembly).GetMethod("Load", new[] { typeof(byte[]) }))),
            Instruction.Create(OpCodes.Ldstr, "InfiniteCL.Theme"),
            Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(Assembly).GetMethod("GetType", new[] { typeof(string) }))),
            Instruction.Create(OpCodes.Ldstr, "Install"),
            Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(Type).GetMethod("GetMethod", new[] { typeof(string) }))),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(MethodBase).GetMethod("Invoke", new[] { typeof(object), typeof(object[]) }))),
            Instruction.Create(OpCodes.Pop)
        };
        for (int i = 0; i < code.Length; i++) main.Body.Instructions.Insert(i, code[i]);
        // The old instruction objects are retained, including every original branch/EH target.
        main.Body.KeepOldMaxStack = false;
    }

    static void SetAssemblyAttribute(ModuleDefMD module, string typeName, string value)
    {
        var attribute = module.Assembly.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeName);
        if (attribute == null || attribute.ConstructorArguments.Count != 1)
            throw new InvalidDataException("Expected assembly attribute: " + typeName);
        attribute.ConstructorArguments[0] = new CAArgument(module.CorLibTypes.String, value);
    }

    static void ReplaceManagedIcons(ModuleDefMD module, byte[] png, byte[] ico)
    {
        int changed = 0;
        for (int i = 0; i < module.Resources.Count; i++)
        {
            var resource = module.Resources[i] as EmbeddedResource;
            if (resource == null || !resource.Name.String.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)) continue;
            using (var input = new MemoryStream(resource.CreateReader().ToArray()))
            using (var reader = new ResourceReader(input))
            using (var output = new MemoryStream())
            {
                bool touched = false;
                using (var writer = new ResourceWriter(output))
                {
                    foreach (DictionaryEntry entry in reader)
                    {
                        string name = (string)entry.Key, type;
                        byte[] data;
                        reader.GetResourceData(name, out type, out data);
                        byte[] replacement = null;
                        if (String.Equals(name, "images/icon.png", StringComparison.OrdinalIgnoreCase)) replacement = png;
                        if (String.Equals(name, "images/icon.ico", StringComparison.OrdinalIgnoreCase)) replacement = ico;
                        if (replacement != null)
                        {
                            // WPF .g.resources uses Stream; its serialized payload starts with its byte length.
                            if (type != "ResourceTypeCode.Stream" && type != "ResourceTypeCode.ByteArray")
                                throw new InvalidDataException("Unexpected managed icon resource type: " + type);
                            using (var payload = new MemoryStream())
                            using (var bw = new BinaryWriter(payload))
                            {
                                bw.Write(replacement.Length); bw.Write(replacement); data = payload.ToArray();
                            }
                            touched = true; changed++;
                        }
                        writer.AddResourceData(name, type, data);
                    }
                    writer.Generate();
                    if (touched) module.Resources[i] = new EmbeddedResource(resource.Name, output.ToArray(), resource.Attributes);
                }
            }
        }
        Console.WriteLine("MANAGED_ICON_ENTRIES_REPLACED " + changed);
    }

    sealed class IconFrame { public byte[] Header, Data; }
    static List<IconFrame> ReadIco(byte[] ico)
    {
        var frames = new List<IconFrame>();
        using (var reader = new BinaryReader(new MemoryStream(ico)))
        {
            if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) throw new InvalidDataException("Invalid ICO header.");
            int count = reader.ReadUInt16();
            if (count == 0 || 6 + 16 * count > ico.Length) throw new InvalidDataException("Invalid ICO frame count.");
            for (int i = 0; i < count; i++)
            {
                byte[] header = reader.ReadBytes(12);
                uint length = BitConverter.ToUInt32(header, 8), offset = reader.ReadUInt32();
                if (length == 0 || (ulong)offset + length > (ulong)ico.Length) throw new InvalidDataException("Invalid ICO frame bounds.");
                byte[] data = new byte[length]; Buffer.BlockCopy(ico, (int)offset, data, 0, (int)length);
                frames.Add(new IconFrame { Header = header, Data = data });
            }
        }
        return frames;
    }

    static ResourceData MakeData(ResourceData old, byte[] bytes)
    {
        return new ResourceData(old.Name, ByteArrayDataReaderFactory.Create(bytes, null), 0, (uint)bytes.Length, old.CodePage, old.Reserved);
    }
    static ResourceData MakeData(int language, byte[] bytes)
    {
        return new ResourceData(new ResourceName(language), ByteArrayDataReaderFactory.Create(bytes, null), 0, (uint)bytes.Length);
    }

    static void ReplaceNativeIcons(ModuleDefMD module, byte[] ico)
    {
        var root = module.Win32Resources == null ? null : module.Win32Resources.Root;
        if (root == null) throw new InvalidDataException("Native resources were not found.");
        var groups = root.FindDirectory(new ResourceName(14));
        if (groups == null || groups.Directories.Count == 0) throw new InvalidDataException("Native icon groups were not found.");
        var icons = root.FindDirectory(new ResourceName(3));
        if (icons == null) { icons = new ResourceDirectoryUser(new ResourceName(3)); root.Directories.Add(icons); }
        var frames = ReadIco(ico);
        int nextId = icons.Directories.Where(d => d.Name.HasId).Select(d => d.Name.Id).DefaultIfEmpty(0).Max() + 1;
        if (nextId + frames.Count > 65536) throw new InvalidDataException("No unused native icon IDs remain.");
        var languages = groups.Directories.SelectMany(g => g.Data).Select(d => d.Name.Id).Distinct().ToArray();
        if (groups.Directories.SelectMany(g => g.Data).Any(d => !d.Name.HasId)) throw new InvalidDataException("Unsupported native icon language name.");
        for (int i = 0; i < frames.Count; i++)
        {
            var frameDir = new ResourceDirectoryUser(new ResourceName(nextId + i));
            foreach (int lang in languages) frameDir.Data.Add(MakeData(lang, frames[i].Data));
            icons.Directories.Add(frameDir);
        }
        byte[] groupBytes;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
            for (int i = 0; i < frames.Count; i++) { writer.Write(frames[i].Header); writer.Write((ushort)(nextId + i)); }
            groupBytes = stream.ToArray();
        }
        int changed = 0;
        foreach (var group in groups.Directories)
            for (int i = 0; i < group.Data.Count; i++) { group.Data[i] = MakeData(group.Data[i], groupBytes); changed++; }
        Console.WriteLine("NATIVE_ICON_GROUPS_REPLACED " + changed);
        Console.WriteLine("NATIVE_ICON_FRAMES " + frames.Count);
    }

    sealed class VersionNode
    {
        public string Key;
        public ushort Type, ValueLength;
        public byte[] Value;
        public List<VersionNode> Children = new List<VersionNode>();
    }
    static int Align4(int value) { return (value + 3) & ~3; }
    static VersionNode ReadVersion(byte[] bytes, int start, int limit)
    {
        if (start + 6 > limit) throw new InvalidDataException("Truncated version node.");
        int length = BitConverter.ToUInt16(bytes, start), end = start + length;
        if (length < 6 || end > limit) throw new InvalidDataException("Invalid version node length.");
        var node = new VersionNode { ValueLength = BitConverter.ToUInt16(bytes, start + 2), Type = BitConverter.ToUInt16(bytes, start + 4) };
        int cursor = start + 6, keyStart = cursor;
        while (cursor + 2 <= end && BitConverter.ToUInt16(bytes, cursor) != 0) cursor += 2;
        if (cursor + 2 > end) throw new InvalidDataException("Unterminated version key.");
        node.Key = Encoding.Unicode.GetString(bytes, keyStart, cursor - keyStart);
        cursor = Align4(cursor + 2);
        int valueBytes = node.ValueLength * (node.Type == 1 ? 2 : 1);
        if (cursor + valueBytes > end) throw new InvalidDataException("Invalid version value length.");
        node.Value = new byte[valueBytes]; Buffer.BlockCopy(bytes, cursor, node.Value, 0, valueBytes);
        cursor = Align4(cursor + valueBytes);
        while (cursor + 6 <= end)
        {
            if (BitConverter.ToUInt16(bytes, cursor) == 0) break;
            var child = ReadVersion(bytes, cursor, end); node.Children.Add(child);
            cursor = Align4(cursor + BitConverter.ToUInt16(bytes, cursor));
        }
        return node;
    }
    static byte[] WriteVersion(VersionNode node)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write(node.ValueLength); writer.Write(node.Type);
            writer.Write(Encoding.Unicode.GetBytes(node.Key + "\0"));
            while ((stream.Position & 3) != 0) writer.Write((byte)0);
            writer.Write(node.Value);
            foreach (var child in node.Children)
            {
                while ((stream.Position & 3) != 0) writer.Write((byte)0);
                writer.Write(WriteVersion(child));
            }
            if (stream.Length > UInt16.MaxValue) throw new InvalidDataException("Version resource exceeds WORD length.");
            stream.Position = 0; writer.Write((ushort)stream.Length); return stream.ToArray();
        }
    }
    static int BrandVersion(VersionNode node)
    {
        int changed = 0;
        if (node.Key == "FileDescription" || node.Key == "ProductName" || node.Key == "OriginalFilename")
        {
            string value = Brand + (node.Key == "OriginalFilename" ? ".exe" : "");
            node.Value = Encoding.Unicode.GetBytes(value + "\0"); node.Type = 1; node.ValueLength = (ushort)(value.Length + 1); changed++;
        }
        foreach (var child in node.Children) changed += BrandVersion(child);
        return changed;
    }
    static void ReplaceVersionStrings(ModuleDefMD module)
    {
        var versions = module.Win32Resources.Root.FindDirectory(new ResourceName(16));
        if (versions == null) throw new InvalidDataException("Native version metadata was not found.");
        int changed = 0;
        foreach (var version in versions.Directories)
            for (int i = 0; i < version.Data.Count; i++)
            {
                var old = version.Data[i]; byte[] bytes = old.CreateReader().ToArray();
                var node = ReadVersion(bytes, 0, bytes.Length);
                if (node.Key != "VS_VERSION_INFO") throw new InvalidDataException("Unexpected version resource root.");
                changed += BrandVersion(node); version.Data[i] = MakeData(old, WriteVersion(node));
            }
        if (changed == 0) throw new InvalidDataException("No native branding strings were found.");
        Console.WriteLine("VERSION_STRINGS_REPLACED " + changed);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException("Verification failed: " + message);
        Console.WriteLine("PASS " + message);
    }
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    static string Operand(object operand, IList<Instruction> instructions)
    {
        if (operand == null) return "";
        var branch = operand as Instruction;
        if (branch != null) return "I:" + instructions.IndexOf(branch);
        var branches = operand as IList<Instruction>;
        if (branches != null) return "IS:" + String.Join(",", branches.Select(i => instructions.IndexOf(i).ToString()).ToArray());
        var variable = operand as Local;
        if (variable != null) return "L:" + variable.Index;
        var parameter = operand as Parameter;
        if (parameter != null) return "P:" + parameter.Index;
        var method = operand as IMethod;
        if (method != null) return "M:" + method.FullName;
        var field = operand as IField;
        if (field != null) return "F:" + field.FullName;
        var type = operand as IType;
        if (type != null) return "T:" + type.FullName;
        return operand.GetType().FullName + ":" + Convert.ToString(operand, System.Globalization.CultureInfo.InvariantCulture);
    }
    static string BodySignature(MethodDef method, int skip)
    {
        if (!method.HasBody) return "NO_BODY";
        var body = method.Body;
        var instructions = body.Instructions.Skip(skip).ToList();
        var text = new StringBuilder();
        text.Append("InitLocals:").Append(body.InitLocals).AppendLine();
        foreach (var variable in body.Variables) text.Append("Local:").Append(variable.Type.FullName).AppendLine();
        foreach (var instruction in instructions) text.Append(instruction.OpCode.Code).Append('|').Append(Operand(instruction.Operand, instructions)).AppendLine();
        foreach (var eh in body.ExceptionHandlers)
        {
            text.Append("EH:").Append(eh.HandlerType).Append('|').Append(instructions.IndexOf(eh.TryStart)).Append('|').Append(instructions.IndexOf(eh.TryEnd));
            text.Append('|').Append(instructions.IndexOf(eh.HandlerStart)).Append('|').Append(instructions.IndexOf(eh.HandlerEnd)).Append('|').Append(instructions.IndexOf(eh.FilterStart));
            text.Append('|').Append(eh.CatchType == null ? "" : eh.CatchType.FullName).AppendLine();
        }
        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }
    static Dictionary<string, string> ResourceEntries(byte[] bytes)
    {
        var result = new Dictionary<string, string>();
        using (var reader = new ResourceReader(new MemoryStream(bytes)))
            foreach (DictionaryEntry entry in reader)
            {
                string type; byte[] data; reader.GetResourceData((string)entry.Key, out type, out data);
                result.Add((string)entry.Key, type + ":" + Hash(data));
            }
        return result;
    }
    static void NativeEntries(ResourceDirectory directory, string path, Dictionary<string, string> result)
    {
        foreach (var data in directory.Data) result.Add(path + "/" + data.Name, data.CodePage + ":" + data.Reserved + ":" + Hash(data.CreateReader().ToArray()));
        foreach (var child in directory.Directories) NativeEntries(child, path + "/" + child.Name, result);
    }
    static Dictionary<string, string> VersionStrings(VersionNode node)
    {
        var result = new Dictionary<string, string>();
        CollectVersionStrings(node, "", result); return result;
    }
    static void CollectVersionStrings(VersionNode node, string path, Dictionary<string, string> result)
    {
        string key = path + "/" + node.Key;
        result[key] = node.Type + ":" + node.ValueLength + ":" + Hash(node.Value);
        foreach (var child in node.Children) CollectVersionStrings(child, key, result);
    }
    static string AttributeValue(object value)
    {
        if (value == null) return "<null>";
        var arguments = value as IList<CAArgument>;
        if (arguments != null) return "[" + String.Join(",", arguments.Select(a => a.Type.FullName + ":" + AttributeValue(a.Value)).ToArray()) + "]";
        return value.GetType().FullName + ":" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
    static string AttributeSignature(CustomAttribute attribute)
    {
        return attribute.Constructor.FullName + "(" + String.Join(",", attribute.ConstructorArguments.Select(a => a.Type.FullName + ":" + AttributeValue(a.Value)).ToArray()) + ")" +
            String.Join(",", attribute.NamedArguments.Select(a => a.IsField + ":" + a.Name + ":" + a.Type.FullName + ":" + AttributeValue(a.Argument.Value)).ToArray());
    }
    static int Verify(string original, string output)
    {
        using (var before = ModuleDefMD.Load(original))
        using (var after = ModuleDefMD.Load(output))
        using (var policyExpected = ModuleDefMD.Load(original))
        {
            LauncherUpdatePolicy.Apply(policyExpected,false);
            Require(before.Assembly.FullName == after.Assembly.FullName, "original assembly identity preserved: " + before.Assembly.FullName);
            var oldMethods = before.GetTypes().SelectMany(t => t.Methods).ToArray();
            var newMethods = after.GetTypes().SelectMany(t => t.Methods).ToArray();
            var expectedMethods = policyExpected.GetTypes().SelectMany(t => t.Methods).ToArray();
            Require(oldMethods.Length == newMethods.Length, "method count preserved: " + oldMethods.Length);
            int unchanged = 0, policyChanged = 0;
            for (int i = 0; i < oldMethods.Length; i++)
            {
                var old = oldMethods[i]; var current = newMethods[i];
                if (old.FullName != current.FullName) throw new InvalidDataException("Method identity changed at " + i);
                if (old == before.EntryPoint)
                {
                    if (current != after.EntryPoint || current.Body.Instructions.Count != old.Body.Instructions.Count + 11)
                        throw new InvalidDataException("Unexpected entry-point patch shape.");
                    if (current.Body.Instructions[3].Operand as string != "InfiniteCL.Theme" || current.Body.Instructions[5].Operand as string != "Install")
                        throw new InvalidDataException("Expected theme hook was not found.");
                    if (BodySignature(old, 0) != BodySignature(current, 11)) throw new InvalidDataException("Original entry-point instructions changed.");
                }
                else
                {
                    if (BodySignature(expectedMethods[i], 0) != BodySignature(current, 0)) throw new InvalidDataException("Unexpected method change outside verified update policy: " + old.FullName);
                    if (BodySignature(old, 0) == BodySignature(current, 0)) unchanged++; else policyChanged++;
                }
            }
            Require(policyChanged == LauncherUpdatePolicy.ChangedMethodCount, "only the 8 explicitly verified launcher update methods changed: " + policyChanged);
            Require(true, "all remaining non-entry method bodies unchanged: " + unchanged);
            Require(before.GetTypes().Count() == after.GetTypes().Count(), "type count preserved: " + before.GetTypes().Count());
            Require(before.Resources.Count == after.Resources.Count, "managed resource count preserved: " + before.Resources.Count);
            int sameResources = 0, sameEntries = 0, changedEntries = 0;
            for (int i = 0; i < before.Resources.Count; i++)
            {
                var old = before.Resources[i] as EmbeddedResource; var current = after.Resources[i] as EmbeddedResource;
                if (before.Resources[i].Name != after.Resources[i].Name) throw new InvalidDataException("Resource name changed.");
                if (old == null || current == null) throw new InvalidDataException("Unexpected non-embedded resource.");
                byte[] oldBytes = old.CreateReader().ToArray(), newBytes = current.CreateReader().ToArray();
                if (Hash(oldBytes) == Hash(newBytes)) { sameResources++; continue; }
                if (!old.Name.String.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Functional resource changed: " + old.Name);
                var oldEntries = ResourceEntries(oldBytes); var newEntries = ResourceEntries(newBytes);
                if (oldEntries.Count != newEntries.Count) throw new InvalidDataException("WPF resource entry count changed.");
                foreach (var entry in oldEntries)
                {
                    string value;
                    if (!newEntries.TryGetValue(entry.Key, out value)) throw new InvalidDataException("WPF resource missing: " + entry.Key);
                    if (entry.Value == value) { sameEntries++; continue; }
                    if (entry.Key != "images/icon.png" && entry.Key != "images/icon.ico") throw new InvalidDataException("Functional WPF resource changed: " + entry.Key);
                    changedEntries++;
                }
            }
            Require(true, "managed resources retained: " + sameResources + " exact blobs; " + sameEntries + " exact WPF entries; " + changedEntries + " branded icons");
            var nativeBefore = new Dictionary<string, string>(); var nativeAfter = new Dictionary<string, string>();
            NativeEntries(before.Win32Resources.Root, "", nativeBefore); NativeEntries(after.Win32Resources.Root, "", nativeAfter);
            int nativeUnchanged = 0;
            foreach (var entry in nativeBefore)
            {
                string value;
                if (!nativeAfter.TryGetValue(entry.Key, out value)) throw new InvalidDataException("Native resource missing: " + entry.Key);
                if (value == entry.Value) { nativeUnchanged++; continue; }
                if (!entry.Key.StartsWith("/14/") && !entry.Key.StartsWith("/16/")) throw new InvalidDataException("Other native resource changed: " + entry.Key);
            }
            if (nativeAfter.Keys.Except(nativeBefore.Keys).Any(k => !k.StartsWith("/3/")))
                throw new InvalidDataException("Unexpected new native resource.");
            Require(true, "native resources retained: " + nativeUnchanged + " exact entries; icon groups/version metadata branded");
            var versionBefore = before.Win32Resources.Root.FindDirectory(new ResourceName(16));
            var versionAfter = after.Win32Resources.Root.FindDirectory(new ResourceName(16));
            foreach (var dir in versionBefore.Directories)
                foreach (var data in dir.Data)
                {
                    byte[] oldBytes = data.CreateReader().ToArray();
                    byte[] newBytes = versionAfter.FindDirectory(dir.Name).FindData(data.Name).CreateReader().ToArray();
                    var oldStrings = VersionStrings(ReadVersion(oldBytes, 0, oldBytes.Length));
                    var newStrings = VersionStrings(ReadVersion(newBytes, 0, newBytes.Length));
                    if (oldStrings.Count != newStrings.Count) throw new InvalidDataException("Version metadata structure changed.");
                    foreach (var entry in oldStrings)
                    {
                        bool branded = entry.Key.EndsWith("/FileDescription") || entry.Key.EndsWith("/ProductName") || entry.Key.EndsWith("/OriginalFilename");
                        if (!branded && entry.Value != newStrings[entry.Key]) throw new InvalidDataException("Non-brand version field changed: " + entry.Key);
                        if (branded)
                        {
                            string expected = Brand + (entry.Key.EndsWith("/OriginalFilename") ? ".exe" : "");
                            string signature = "1:" + (expected.Length + 1) + ":" + Hash(Encoding.Unicode.GetBytes(expected + "\0"));
                            if (newStrings[entry.Key] != signature) throw new InvalidDataException("Native brand value is incorrect: " + entry.Key);
                        }
                    }
                }
            Require(true, "version numbers, copyright, company and all other version values preserved");
            foreach (string attribute in new[] { "System.Reflection.AssemblyTitleAttribute", "System.Reflection.AssemblyProductAttribute" })
                Require((string)(UTF8String)after.Assembly.CustomAttributes.First(a => a.AttributeType.FullName == attribute).ConstructorArguments[0].Value == Brand, attribute + " = " + Brand);
            var oldOther = before.Assembly.CustomAttributes.Where(a => a.AttributeType.FullName != "System.Reflection.AssemblyTitleAttribute" && a.AttributeType.FullName != "System.Reflection.AssemblyProductAttribute").ToArray();
            var newOther = after.Assembly.CustomAttributes.Where(a => a.AttributeType.FullName != "System.Reflection.AssemblyTitleAttribute" && a.AttributeType.FullName != "System.Reflection.AssemblyProductAttribute").ToArray();
            Require(oldOther.Length == newOther.Length && oldOther.Select(AttributeSignature).SequenceEqual(newOther.Select(AttributeSignature)), "all non-brand assembly attributes preserved");
            Console.WriteLine("ORIGINAL_SHA256 " + Hash(File.ReadAllBytes(original)));
            Console.WriteLine("OUTPUT_SHA256 " + Hash(File.ReadAllBytes(output)));
            Console.WriteLine("STRUCTURAL_VERIFICATION_COMPLETE");
            return 0;
        }
    }
}
