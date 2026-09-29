using System.Text;
using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;
using Treinos.Infrastructure;

namespace Treinos.Api;

public sealed class PasswordRecovery(AccountService accounts, Database database, IDataProtectionProvider protection, IConfiguration config)
{
    private readonly IDataProtector tokens = protection.CreateProtector("Treinos.PasswordReset.v1");
    private readonly IDataProtector messages = protection.CreateProtector("Treinos.EmailOutbox.v1");
    public bool Enabled => config.GetValue<bool>("Email:Enabled");
    public async Task Request(string email)
    {
        if (!Enabled) throw new InvalidOperationException("EMAIL_NOT_CONFIGURED");
        var user = await accounts.FindByEmail(email);
        if (user is null) return;
        await using var db = await database.Open();
        var recent = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM email_outbox WHERE user_id=@Id AND created_at_utc>DATE_SUB(UTC_TIMESTAMP(6),INTERVAL 1 HOUR)", new { user.Id });
        if (recent >= 3) return;
        var baseUrl = config["App:PublicBaseUrl"] ?? throw new InvalidOperationException("App:PublicBaseUrl ausente");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("URL pública HTTPS inválida.");
        var expires = DateTime.UtcNow.AddHours(1);
        var token = tokens.Protect($"{user.Id}|{user.SecurityStamp}|{expires:O}|{Guid.NewGuid()}");
        var url = $"{baseUrl.TrimEnd('/')}/reset-password?id={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        var ciphertext = Encoding.UTF8.GetBytes(messages.Protect(url));
        await db.ExecuteAsync("INSERT INTO email_outbox(id,user_id,recipient_email,template_key,payload_ciphertext,token_expires_at_utc) VALUES(@id,@userId,@email,'password_reset',@ciphertext,@expires)", new { id = Guid.NewGuid().ToString(), userId = user.Id, email = user.Email, ciphertext, expires });
    }
    public async Task<bool> Reset(string id, string token, string newPassword)
    {
        if (newPassword.Length is < 12 or > 128) return false;
        try
        {
            var parts = tokens.Unprotect(token).Split('|');
            if (parts.Length != 4 || parts[0] != id || DateTime.Parse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind) < DateTime.UtcNow) return false;
            return await accounts.ResetWithStamp(id, parts[1], newPassword);
        }
        catch { return false; }
    }
    public string Decrypt(byte[] ciphertext) => messages.Unprotect(Encoding.UTF8.GetString(ciphertext));
}

public sealed class EmailDispatcher(IServiceScopeFactory scopes, IConfiguration config, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Email:Enabled")) return;
        var host = config["Email:Host"] ?? throw new InvalidOperationException("Email:Host ausente");
        var from = config["Email:FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress ausente");
        var mode = config["Email:TlsMode"] ?? "StartTls";
        if (mode is not ("StartTls" or "SslOnConnect" or "None") || (mode == "None" && (config["ASPNETCORE_ENVIRONMENT"] != "Development" || host is not ("127.0.0.1" or "localhost")))) throw new InvalidOperationException("Email:TlsMode inválido");
        var port = config.GetValue<int>("Email:Port", 587);
        var user = config["Email:Username"];
        var passwordPath = config["Email:PasswordFile"];
        var password = passwordPath is null ? null : (await File.ReadAllTextAsync(passwordPath, stoppingToken)).TrimEnd('\r','\n');
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var dbFactory = scope.ServiceProvider.GetRequiredService<Database>(); var recovery = scope.ServiceProvider.GetRequiredService<PasswordRecovery>();
                await using var db = await dbFactory.Open(stoppingToken);
                await db.ExecuteAsync("UPDATE email_outbox SET status='expired',payload_ciphertext=X'' WHERE status IN ('pending','processing') AND token_expires_at_utc<=UTC_TIMESTAMP(6)");
                var row = await db.QuerySingleOrDefaultAsync<OutboxRow>("SELECT id,recipient_email,payload_ciphertext,attempts FROM email_outbox WHERE ((status='pending' AND available_at_utc<=UTC_TIMESTAMP(6)) OR (status='processing' AND lease_until_utc<UTC_TIMESTAMP(6))) AND token_expires_at_utc>UTC_TIMESTAMP(6) ORDER BY created_at_utc LIMIT 1");
                if (row is not null)
                {
                    var lease = Guid.NewGuid().ToString();
                    var claimed = await db.ExecuteAsync("UPDATE email_outbox SET status='processing',lease_token=@lease,lease_until_utc=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 2 MINUTE),attempts=attempts+1 WHERE id=@Id AND ((status='pending' AND available_at_utc<=UTC_TIMESTAMP(6)) OR (status='processing' AND lease_until_utc<UTC_TIMESTAMP(6)))", new { row.Id, lease });
                    if (claimed == 1)
                    {
                        try
                        {
                            var message = new MimeMessage(); message.From.Add(new MailboxAddress(config["Email:FromName"] ?? "Treinos", from)); message.To.Add(MailboxAddress.Parse(row.RecipientEmail)); message.Subject = "Redefinir senha do Treinos";
                            message.Body = new TextPart("plain") { Text = "Abra este link para definir uma nova senha. O link expira em uma hora:\n" + recovery.Decrypt(row.PayloadCiphertext) };
                            using var smtp = new SmtpClient(); await smtp.ConnectAsync(host, port, mode == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : mode == "None" ? SecureSocketOptions.None : SecureSocketOptions.StartTls, stoppingToken);
                            if (!string.IsNullOrEmpty(user)) await smtp.AuthenticateAsync(user, password ?? "", stoppingToken);
                            await smtp.SendAsync(message, stoppingToken); await smtp.DisconnectAsync(true, stoppingToken);
                            await db.ExecuteAsync("UPDATE email_outbox SET status='sent',sent_at_utc=UTC_TIMESTAMP(6),payload_ciphertext=X'',lease_token=NULL,lease_until_utc=NULL WHERE id=@Id AND lease_token=@lease", new { row.Id, lease });
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning("Falha ao enviar e-mail da fila: {ErrorType}", e.GetType().Name);
                            await db.ExecuteAsync("UPDATE email_outbox SET status=IF(attempts>=5,'failed','pending'),available_at_utc=DATE_ADD(UTC_TIMESTAMP(6),INTERVAL 5 MINUTE),lease_token=NULL,lease_until_utc=NULL,last_error_code=@code WHERE id=@Id AND lease_token=@lease", new { row.Id, lease, code = e.GetType().Name });
                        }
                    }
                }
            }
            catch (Exception e) { logger.LogWarning("Dispatcher de e-mail: {ErrorType}", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
    private sealed class OutboxRow { public string Id { get; set; } = ""; public string RecipientEmail { get; set; } = ""; public byte[] PayloadCiphertext { get; set; } = []; public int Attempts { get; set; } }
}
