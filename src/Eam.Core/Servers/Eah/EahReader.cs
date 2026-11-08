using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers.Eah;

/// <summary>
/// Reads the data of EmpyrionAdminHelper (EAH), for the one-click import into EAM.
/// Interoperability only: EAH's file formats were studied, none of its code is used.
/// EAH keeps 7 SQLite files in &lt;EAH&gt;\Config\*.dat, encrypted with the legacy RC4 codec of System.Data.SQLite
/// (RC4 per page, key = first 16 bytes of SHA-1 of the password), and its settings in UTF-16 XML files next to them.
/// </summary>
public static class EahReader
{
    // EAH uses the same fixed password for every installation (it protects nothing, it only hides the files).
    private const string DbPassword = "sd*7kdi#we2§";
    private const string SecretPassword = "öerfig3h5js78d8fGFHukjiloSdfwerhliolJJHKeöih23457t.zumnödf89tu03";
    private static readonly byte[] SecretKeySalt = Convert.FromHexString("DAF4E2A9B703C2DDAFB2FFC34A2B");
    private static readonly byte[] SecretIvSalt = Convert.FromHexString("0322DDADB2FFDAF4E2A9B7CF4A3D");

    public static readonly string[] DbFiles = ["EAH_Ply.dat", "EAH_Log.dat", "EAH_Struct.dat", "EAH_Dat.dat", "EAH_Stat.dat", "EAH_AddOn.dat", "EAH_Item.dat"];

    /// <summary>
    /// Finds EAH's Config folder from what the admin typed: the Config folder itself, the EAH folder,
    /// the Empyrion server folder (DedicatedServer\EmpyrionAdminHelper) or a parent folder (searched 4 levels deep).
    /// </summary>
    public static string? FindConfig(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Trim('"');
        if (!Directory.Exists(path)) return null;
        static bool IsConfig(string d) => File.Exists(Path.Combine(d, "EAH_Ply.dat")) || File.Exists(Path.Combine(d, "Settings.XML"));
        foreach (var c in new[] { path, Path.Combine(path, "Config"), Path.Combine(path, "EmpyrionAdminHelper", "Config"),
                     Path.Combine(path, "DedicatedServer", "EmpyrionAdminHelper", "Config") })
            if (Directory.Exists(c) && IsConfig(c)) return c;
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((path, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > 4) continue;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); } catch { continue; }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (name.Equals("Saves", StringComparison.OrdinalIgnoreCase) || name.Equals("Content", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Config", StringComparison.OrdinalIgnoreCase) && IsConfig(s)) return s;
                queue.Enqueue((s, depth + 1));
            }
        }
        return null;
    }

    /// <summary>The Empyrion server folder above EAH's Config folder (the one with EmpyrionDedicated.exe), if any.</summary>
    public static string? FindGameDir(string configDir)
    {
        var d = new DirectoryInfo(configDir);
        for (var i = 0; i < 5 && d is not null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "EmpyrionLauncher.exe")) || File.Exists(Path.Combine(d.FullName, "DedicatedServer", "EmpyrionDedicated.exe")))
                return d.FullName;
        return null;
    }

    // ------------------------------------------------------------------ databases

    /// <summary>Decrypts an EAH database (System.Data.SQLite legacy RC4). Plain SQLite files are returned unchanged.</summary>
    public static byte[] Decrypt(byte[] file)
    {
        if (file.Length < 512 || IsPlain(file)) return file;
        var key = SHA1.HashData(Encoding.UTF8.GetBytes(DbPassword))[..16];
        var first = file[..512].ToArray();
        Rc4(key, first);
        if (!IsPlain(first)) throw new InvalidDataException("not_eah_database");
        var pageSize = first[16] << 8 | first[17];
        if (pageSize == 1) pageSize = 65536;
        if (pageSize < 512) throw new InvalidDataException("not_eah_database");
        var output = new byte[file.Length];
        for (var off = 0; off < file.Length; off += pageSize)
        {
            var page = file.AsSpan(off, Math.Min(pageSize, file.Length - off)).ToArray();
            Rc4(key, page);
            page.CopyTo(output, off);
        }
        // The plain file must not ask for a reserved area that only made sense with the codec.
        return output;
    }

    private static bool IsPlain(byte[] b) => b.Length >= 16 && Encoding.ASCII.GetString(b, 0, 15) == "SQLite format 3";

    internal static void Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256];
        for (var i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
        }
        for (int k = 0, i = 0, j = 0; k < data.Length; k++)
        {
            i = (i + 1) & 255;
            j = (j + s[i]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
            data[k] ^= s[(s[i] + s[j]) & 255];
        }
    }

    /// <summary>Encrypts a plain SQLite file the way EAH does (used by the tests).</summary>
    public static byte[] EncryptForTests(byte[] plain)
    {
        var key = SHA1.HashData(Encoding.UTF8.GetBytes(DbPassword))[..16];
        var pageSize = plain[16] << 8 | plain[17];
        if (pageSize == 1) pageSize = 65536;
        var output = new byte[plain.Length];
        for (var off = 0; off < plain.Length; off += pageSize)
        {
            var page = plain.AsSpan(off, Math.Min(pageSize, plain.Length - off)).ToArray();
            Rc4(key, page);
            page.CopyTo(output, off);
        }
        return output;
    }

    /// <summary>Copies one EAH database, decrypted, into <paramref name="workDir"/>. Null when the file does not exist.</summary>
    public static string? PlainCopy(string configDir, string file, string workDir)
    {
        var src = Path.Combine(configDir, file);
        if (!File.Exists(src)) return null;
        Directory.CreateDirectory(workDir);
        byte[] bytes;
        // EAH may be running: read with sharing.
        using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        var target = Path.Combine(workDir, Path.ChangeExtension(file, ".db"));
        File.WriteAllBytes(target, Decrypt(bytes));
        return target;
    }

    public static SqliteConnection OpenReadOnly(string plainFile)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = plainFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    /// <summary>Name of the table holding an EAH entity (EF pluralises the class name: clsChat → clsChats).</summary>
    public static string? Table(SqliteConnection c, params string[] candidates)
    {
        var names = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
        }
        foreach (var cand in candidates)
        {
            var hit = names.FirstOrDefault(n => n.Equals(cand, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Rows of a table as case-insensitive dictionaries, streamed. Optional filter on a date column.</summary>
    public static IEnumerable<Row> Rows(SqliteConnection c, string table, string? orderBy = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\"" + (orderBy is null ? "" : $" ORDER BY \"{orderBy}\"") + ";";
        using var r = cmd.ExecuteReader();
        var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        while (r.Read())
        {
            var row = new Row();
            for (var i = 0; i < cols.Length; i++) row[cols[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
            yield return row;
        }
    }

    public static long Count(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM \"{table.Replace("\"", "\"\"")}\";";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>A database row; missing columns read as null so that older EAH versions import too.</summary>
    public sealed class Row : Dictionary<string, object?>
    {
        public Row() : base(StringComparer.OrdinalIgnoreCase) { }

        public object? Get(string col) => TryGetValue(col, out var v) ? v : null;
        public string? Str(string col) => Get(col) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        public long Long(string col, long def = 0)
        {
            var v = Get(col);
            return v switch
            {
                null => def,
                long l => l,
                int i => i,
                double d => (long)d,
                bool b => b ? 1 : 0,
                string s when long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
                string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2) => (long)d2,
                string s when bool.TryParse(s, out var b2) => b2 ? 1 : 0,
                _ => def,
            };
        }

        public double Double(string col, double def = 0)
        {
            var v = Get(col);
            return v switch
            {
                null => def,
                double d => d,
                long l => l,
                int i => i,
                string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
                _ => def,
            };
        }

        public bool Bool(string col) => Long(col) != 0;

        /// <summary>EAH stores local times (System.Data.SQLite: ISO-8601 text by default, ticks or Julian days on some setups).</summary>
        public DateTime? Date(string col) => ToUtc(Get(col));
    }

    public static DateTime? ToUtc(object? v)
    {
        switch (v)
        {
            case null: return null;
            case string s:
                if (s.Length == 0) return null;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var d))
                    return d.Year < 2000 ? null : d;
                if (long.TryParse(s, out var l)) return ToUtc(l);
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl)) return ToUtc(dbl);
                return null;
            case long ticks when ticks > 100_000_000_000_000:
                return Valid(new DateTime(ticks, DateTimeKind.Local));
            case long unix when unix > 0:
                return Valid(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);
            case double julian when julian > 2_400_000:
                return Valid(DateTime.SpecifyKind(DateTime.FromOADate(julian - 2415018.5), DateTimeKind.Local));
            case DateTime dt:
                return Valid(dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Local));
            default: return null;
        }

        static DateTime? Valid(DateTime d) => d.Year < 2000 || d.Year > 2100 ? null : d.ToUniversalTime();
    }

    // ------------------------------------------------------------------ inventories

    /// <summary>One item of an inventory, in EAM's format (same as the game API: slot index in the toolbelt or the bag).</summary>
    public sealed record Stack(int Id, int Count, int Slot, int Ammo, int Decay);

    /// <summary>
    /// Decodes an EAH inventory: Base64 of a protobuf list of {1 slot, 2 item id, 3 count, 4 ammo, 5 decay}.
    /// EAH slots 1-9 are the toolbelt, 10 and above the bag.
    /// </summary>
    public static (List<Stack> Toolbelt, List<Stack> Bag) DecodeInventory(string? base64)
    {
        var toolbelt = new List<Stack>();
        var bag = new List<Stack>();
        if (string.IsNullOrWhiteSpace(base64)) return (toolbelt, bag);
        byte[] data;
        try { data = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { return (toolbelt, bag); }
        var pos = 0;
        while (pos < data.Length)
        {
            var tag = ReadVarint(data, ref pos);
            var field = (int)(tag >> 3);
            var wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                var len = (int)ReadVarint(data, ref pos);
                if (len < 0 || pos + len > data.Length) break;
                var msg = data.AsSpan(pos, len).ToArray();
                pos += len;
                var f = new long[6];
                var p = 0;
                while (p < msg.Length)
                {
                    var t = ReadVarint(msg, ref p);
                    var fn = (int)(t >> 3);
                    var w = (int)(t & 7);
                    long value;
                    if (w == 0) value = ReadVarint(msg, ref p);
                    else if (w == 5) { value = BitConverter.ToInt32(msg, p); p += 4; }
                    else if (w == 1) { value = BitConverter.ToInt64(msg, p); p += 8; }
                    else if (w == 2) { var l = (int)ReadVarint(msg, ref p); p += l; continue; }
                    else break;
                    if (fn is >= 1 and <= 5) f[fn] = value;
                }
                var slot = (int)f[1];
                var id = (int)f[2];
                var count = (int)f[3];
                if (id <= 0 || count <= 0) continue;
                if (slot is >= 1 and <= 9) toolbelt.Add(new Stack(id, count, slot - 1, (int)f[4], (int)f[5]));
                else if (slot >= 10) bag.Add(new Stack(id, count, slot - 10, (int)f[4], (int)f[5]));
            }
            else if (!Skip(data, ref pos, wire)) break;
        }
        return (toolbelt, bag);
    }

    public static string ToJson(IEnumerable<Stack> stacks) =>
        JsonSerializer.Serialize(stacks.Select(s => new { id = s.Id, count = s.Count, slot = s.Slot, ammo = s.Ammo, decay = s.Decay }));

    private static long ReadVarint(byte[] b, ref int pos)
    {
        long result = 0;
        for (var shift = 0; shift < 64 && pos < b.Length; shift += 7)
        {
            var x = b[pos++];
            result |= (long)(x & 0x7f) << shift;
            if ((x & 0x80) == 0) return result;
        }
        return result;
    }

    private static bool Skip(byte[] b, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(b, ref pos); return true;
            case 1: pos += 8; return pos <= b.Length;
            case 2: var l = (int)ReadVarint(b, ref pos); pos += l; return l >= 0 && pos <= b.Length;
            case 5: pos += 4; return pos <= b.Length;
            default: return false;
        }
    }

    /// <summary>Encodes an inventory like EAH (used by the tests).</summary>
    public static string EncodeInventoryForTests(IEnumerable<(int Slot, int Id, int Count)> items)
    {
        var outer = new List<byte>();
        foreach (var (slot, id, count) in items)
        {
            var inner = new List<byte>();
            void Field(int n, long v) { Varint(inner, (n << 3) | 0); Varint(inner, v); }
            Field(1, slot); Field(2, id); Field(3, count);
            Varint(outer, (1 << 3) | 2);
            Varint(outer, inner.Count);
            outer.AddRange(inner);
        }
        return Convert.ToBase64String(outer.ToArray());

        static void Varint(List<byte> o, long v)
        {
            var u = (ulong)v;
            while (u >= 0x80) { o.Add((byte)(u | 0x80)); u >>= 7; }
            o.Add((byte)u);
        }
    }

    // ------------------------------------------------------------------ XML settings and secrets

    /// <summary>Loads an EAH XML file (UTF-16, or whatever its declaration says).</summary>
    public static XDocument? LoadXml(string file)
    {
        if (!File.Exists(file)) return null;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return XDocument.Load(fs);
        }
        catch (Exception e) { LastXmlError = e.Message; return null; }
    }

    public static string? LastXmlError { get; private set; }

    /// <summary>The settings of Settings.XML as name → value (the first &lt;Einstellung&gt; block, or any leaf element).</summary>
    public static Dictionary<string, string> Settings(string configDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = LoadXml(Path.Combine(configDir, "Settings.XML"));
        if (doc?.Root is null) return map;
        var block = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Einstellung") ?? doc.Root;
        foreach (var e in block.Descendants().Where(e => !e.HasElements))
            map.TryAdd(e.Name.LocalName, e.Value);
        foreach (var a in block.Attributes()) map.TryAdd(a.Name.LocalName, a.Value);
        return map;
    }

    /// <summary>Decrypts a secret of Settings.XML (Telnet password, Discord token...). Null when it cannot be read.</summary>
    public static string? DecryptSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromHexString(value.Replace("-", "").Trim());
            using var keyGen = new Rfc2898DeriveBytes(SecretPassword, SecretKeySalt, 10, HashAlgorithmName.SHA1);
            using var ivGen = new Rfc2898DeriveBytes(SecretPassword, SecretIvSalt, 10, HashAlgorithmName.SHA1);
            using var aes = Aes.Create();
            aes.Key = keyGen.GetBytes(32);
            aes.IV = ivGen.GetBytes(16);
            var plain = aes.DecryptCbc(bytes, aes.IV, PaddingMode.PKCS7);
            return Encoding.ASCII.GetString(plain).Replace("\0", "");
        }
        catch { return null; }
    }

    /// <summary>Encrypts a secret like EAH (used by the tests).</summary>
    public static string EncryptSecretForTests(string plain)
    {
        using var keyGen = new Rfc2898DeriveBytes(SecretPassword, SecretKeySalt, 10, HashAlgorithmName.SHA1);
        using var ivGen = new Rfc2898DeriveBytes(SecretPassword, SecretIvSalt, 10, HashAlgorithmName.SHA1);
        using var aes = Aes.Create();
        aes.Key = keyGen.GetBytes(32);
        aes.IV = ivGen.GetBytes(16);
        return BitConverter.ToString(aes.EncryptCbc(Encoding.ASCII.GetBytes(plain), aes.IV, PaddingMode.PKCS7));
    }

    /// <summary>Splits a list typed in EAH (one per line, or separated by commas or semicolons).</summary>
    public static List<string> SplitList(string? value) =>
        (value ?? "").Split(['\r', '\n', ',', ';', '|', '#'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>An EAH inventory template (Inventory Templates\&lt;name&gt;.xml): &lt;Inv P=slot ID=item M=count A=ammo H=decay/&gt;.</summary>
    public static (List<Stack> Toolbelt, List<Stack> Bag) LoadTemplate(string file)
    {
        var toolbelt = new List<Stack>();
        var bag = new List<Stack>();
        var doc = LoadXml(file);
        if (doc is null) return (toolbelt, bag);
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "Inv"))
        {
            int A(string n) => int.TryParse((string?)e.Attribute(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            var slot = A("P");
            var id = A("ID");
            var count = A("M");
            if (id <= 0 || count <= 0) continue;
            if (slot is >= 1 and <= 9) toolbelt.Add(new Stack(id, count, slot - 1, A("A"), A("H")));
            else if (slot >= 10) bag.Add(new Stack(id, count, slot - 10, A("A"), A("H")));
            else bag.Add(new Stack(id, count, bag.Count, A("A"), A("H")));
        }
        return (toolbelt, bag);
    }
}
