using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Texto 100% ASCII de proposito: evita mojibake em qualquer codepage.
if (args.Length == 0) { Usage(); return 1; }
try
{
    switch (args[0].ToLowerInvariant())
    {
        case "new-key": return NewKey(GetOpt(args, "--dir") ?? ".", HasFlag(args, "--force"));
        case "sign": return Sign(GetOpt(args, "--key") ?? "", GetOpt(args, "--req") ?? "", int.Parse(GetOpt(args, "--dias") ?? "30"), GetOpt(args, "--saida") ?? "");
        case "verify": return Verify(GetOpt(args, "--key") ?? "", GetOpt(args, "--license") ?? "");
        default: Usage(); return 1;
    }
}
catch (Exception ex) { Console.Error.WriteLine("ERRO: " + ex.Message); return 2; }

static void Usage()
{
    Console.WriteLine("LicenseTool new-key --dir <pasta> [--force]");
    Console.WriteLine("LicenseTool sign --key private.pem --req SPBREQ... --dias 30 [--saida chave.txt]");
    Console.WriteLine("LicenseTool verify --key public.pem --license SPB1...");
}

static string? GetOpt(string[] a, string name)
{
    for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
    return null;
}

static bool HasFlag(string[] a, string name) => a.Any(x => x == name);

static string Pem(string label, byte[] der)
{
    var b64 = Convert.ToBase64String(der);
    var sb = new StringBuilder();
    sb.Append("-----BEGIN ").Append(label).Append("-----\n");
    for (int i = 0; i < b64.Length; i += 64) sb.Append(b64.Substring(i, Math.Min(64, b64.Length - i))).Append('\n');
    sb.Append("-----END ").Append(label).Append("-----\n");
    return sb.ToString();
}

static int NewKey(string dir, bool force)
{
    Directory.CreateDirectory(dir);
    var priv = Path.Combine(dir, "private.pem");
    var pub = Path.Combine(dir, "public.pem");
    if ((File.Exists(priv) || File.Exists(pub)) && !force) { Console.WriteLine("Chaves ja existem em " + dir + " (use --force para regerar)."); return 0; }
    using var rsa = RSA.Create(2048);
    File.WriteAllText(priv, Pem("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey()));
    File.WriteAllText(pub, Pem("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
    Console.WriteLine("Gerado em " + dir + " (GUARDE private.pem - ele assina as chaves).");
    return 0;
}

static string B64U(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static byte[] B64D(string s)
{
    var t = s.Replace('-', '+').Replace('_', '/');
    switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
    return Convert.FromBase64String(t);
}

static int Sign(string keyPath, string req, int dias, string saida)
{
    if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(req)) throw new Exception("Informe --key e --req.");
    req = req.Trim();
    if (!req.StartsWith("SPBREQ.")) throw new Exception("Pedido invalido (esperado SPBREQ...).");
    using var doc = JsonDocument.Parse(B64D(req.Substring("SPBREQ.".Length)));
    var g = doc.RootElement.GetProperty("g").GetString() ?? "";
    var f = doc.RootElement.GetProperty("f").GetString() ?? "";
    if (g == "" || f == "") throw new Exception("Pedido sem maquina.");
    var exp = DateTime.UtcNow.AddDays(dias).ToString("yyyy-MM-dd");
    var payload = Encoding.UTF8.GetBytes("{\"g\":\"" + g + "\",\"f\":\"" + f + "\",\"exp\":\"" + exp + "T00:00:00Z\"}");
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(keyPath));
    var sig = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var token = "SPB1." + B64U(payload) + "." + B64U(sig);
    if (!string.IsNullOrWhiteSpace(saida)) File.WriteAllText(saida, token);
    Console.WriteLine(token);
    Console.WriteLine("Valida ate " + exp + " (UTC).");
    return 0;
}

static int Verify(string keyPath, string token)
{
    token = token.Trim();
    var parts = token.Split('.');
    if (parts.Length != 3 || parts[0] != "SPB1") throw new Exception("Formato invalido.");
    var payload = B64D(parts[1]);
    var sig = B64D(parts[2]);
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(keyPath));
    var ok = rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    Console.WriteLine(ok ? "ASSINATURA OK" : "ASSINATURA INVALIDA");
    if (ok) Console.WriteLine(Encoding.UTF8.GetString(payload));
    return ok ? 0 : 3;
}
