using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;
using Treinos.Infrastructure;

var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Treinos") ?? throw new InvalidOperationException("ConnectionStrings__Treinos ausente");
var passwordFile = Environment.GetEnvironmentVariable("Database__PasswordFile");
if (!string.IsNullOrEmpty(passwordFile))
{
    var settings = new MySqlConnectionStringBuilder(connection) { Password = (await File.ReadAllTextAsync(passwordFile)).TrimEnd('\r', '\n') };
    connection = settings.ConnectionString;
}
var sqlRoot = Environment.GetEnvironmentVariable("TREINOS_SQL_ROOT") ?? "/opt/treinos/database/migrations";
var files = new[] { "001_schema_treinos.sql", "003_pwa_usuarios_email.sql" };
var database=new Database(connection);
MySqlConnection? ready=null;
for(var attempt=0; attempt<60; attempt++) {
    try { ready=await database.Open(); break; }
    catch(MySqlException) when(attempt<59) { await Task.Delay(1000); }
}
await using var db = ready ?? throw new InvalidOperationException("Banco indisponível.");
if (await db.ExecuteScalarAsync<int>("SELECT GET_LOCK('treinos_schema_migrations',30)") != 1) throw new Exception("Migração em uso.");
try
{
    await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS schema_migrations (version VARCHAR(80) PRIMARY KEY, checksum CHAR(64) NOT NULL, applied_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)) ENGINE=InnoDB");
    foreach (var file in files)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(sqlRoot, file));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        var existing = await db.ExecuteScalarAsync<string?>("SELECT checksum FROM schema_migrations WHERE version=@file", new { file });
        if (existing is not null) { if (existing != hash) throw new Exception($"Checksum divergente: {file}"); Console.WriteLine($"Já aplicado: {file}"); continue; }
        foreach (var statement in SplitSql(sql)) await db.ExecuteAsync(statement);
        await db.ExecuteAsync("INSERT INTO schema_migrations(version,checksum) VALUES(@file,@hash)", new { file, hash });
        Console.WriteLine($"Aplicado: {file}");
    }
}
finally { await db.ExecuteAsync("SELECT RELEASE_LOCK('treinos_schema_migrations')"); }
static IEnumerable<string> SplitSql(string sql)
{
    var buffer = new StringBuilder(); bool quote = false; bool lineComment = false; bool blockComment = false;
    for (var i = 0; i < sql.Length; i++)
    {
        var c = sql[i]; var n = i + 1 < sql.Length ? sql[i + 1] : '\0';
        if (lineComment) { if (c == '\n') { lineComment = false; buffer.Append(c); } continue; }
        if (blockComment) { if (c == '*' && n == '/') { blockComment = false; i++; } continue; }
        if (!quote && c == '-' && n == '-') { lineComment = true; i++; continue; }
        if (!quote && c == '/' && n == '*') { blockComment = true; i++; continue; }
        if (c == '\'' && (i == 0 || sql[i-1] != '\\')) quote = !quote;
        if (c == ';' && !quote) { var command = buffer.ToString().Trim(); if (command.Length > 0) yield return command; buffer.Clear(); }
        else buffer.Append(c);
    }
    if (buffer.ToString().Trim() is { Length: > 0 } tail) yield return tail;
}
