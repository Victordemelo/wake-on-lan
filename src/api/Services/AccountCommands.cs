using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Services;

// Account recovery for self-hosted installations without e-mail:
//   docker compose exec api dotnet RemoteWake.Api.dll reset-password voce@exemplo.com [--disable-2fa]
//   docker compose exec api dotnet RemoteWake.Api.dll create-user pessoa@exemplo.com Nome da pessoa
public static class AccountCommands
{
    private const string Usage = """
        Comandos de administração do Remote Wake:
          reset-password <e-mail> [--disable-2fa]  Gera uma senha temporária e encerra todas as sessões.
          create-user <e-mail> <nome>              Cria uma conta com senha temporária.
        """;

    // Returns null when the arguments are not a command, so the API starts normally.
    public static async Task<int?> TryRunAsync(string[] args, IServiceProvider services)
    {
        if (args.Length == 0 || args[0].StartsWith('-') || args[0].Contains('=')) return null;

        var database = services.GetRequiredService<AppDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var sessions = services.GetRequiredService<SessionService>();
        var log = services.GetRequiredService<SecurityLog>();
        var time = services.GetRequiredService<TimeProvider>();

        switch (args)
        {
            case ["reset-password", var rawEmail, .. var options]:
            {
                var email = AccountRules.NormalizeEmail(rawEmail);
                var user = await database.Users.SingleOrDefaultAsync(item => item.Email == email);
                if (user is null)
                {
                    Console.Error.WriteLine($"Nenhuma conta com o e-mail {email}.");
                    return 1;
                }
                var password = TemporaryPassword();
                user.PasswordHash = hasher.HashPassword(user, password);
                user.PasswordChangedAt = time.GetUtcNow();
                var disableTwoFactor = options.Contains("--disable-2fa");
                if (disableTwoFactor) await TwoFactor.DisableAsync(user, database);
                log.Add(user.Id, SecurityEventTypes.PasswordReset, context: null);
                await database.SaveChangesAsync();
                await sessions.RevokeAllAsync(user.Id);
                Console.WriteLine($"Senha temporária de {user.Email}: {password}");
                Console.WriteLine(disableTwoFactor
                    ? "A verificação em duas etapas foi desativada e todas as sessões foram encerradas."
                    : "Todas as sessões foram encerradas.");
                Console.WriteLine("Entre com essa senha e troque-a em Minha conta.");
                return 0;
            }
            case ["create-user", var rawEmail, .. var nameParts] when nameParts.Length > 0:
            {
                var email = AccountRules.NormalizeEmail(rawEmail);
                var name = string.Join(' ', nameParts).Trim();
                if (!AccountRules.IsEmail(email) || name.Length is < 2 or > 100)
                {
                    Console.Error.WriteLine("Informe um e-mail válido e um nome entre 2 e 100 caracteres.");
                    return 1;
                }
                if (await database.Users.AnyAsync(item => item.Email == email))
                {
                    Console.Error.WriteLine($"Já existe uma conta com o e-mail {email}.");
                    return 1;
                }
                var password = TemporaryPassword();
                var user = new User { Name = name, Email = email, PasswordHash = string.Empty, CreatedAt = time.GetUtcNow() };
                user.PasswordHash = hasher.HashPassword(user, password);
                database.Users.Add(user);
                log.Add(user.Id, SecurityEventTypes.Registered, context: null);
                await database.SaveChangesAsync();
                Console.WriteLine($"Conta criada para {email}. Senha temporária: {password}");
                Console.WriteLine("Peça para a pessoa trocar a senha em Minha conta após entrar.");
                return 0;
            }
            case ["help"]:
                Console.WriteLine(Usage);
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    // 80 random bits in four groups, for example "k7pq-3mzx-9dha-t2wr".
    private static string TemporaryPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Join('-', Enumerable.Range(0, 4).Select(_ =>
            new string(Enumerable.Range(0, 4).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray())));
    }
}
