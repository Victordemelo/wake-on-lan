using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);
if (builder.Configuration["settings"] is { Length: > 0 } settings)
    builder.Configuration.AddJsonFile(Path.GetFullPath(settings), optional: false, reloadOnChange: false).AddEnvironmentVariables();
builder.Services.AddWindowsService(options => options.ServiceName =
    builder.Configuration["REMOTE_WAKE_MODE"]?.ToLowerInvariant() == "gateway" ? "RemoteWakeGateway" : "RemoteWakeAgent");
builder.Services.AddSystemd();
builder.Services.AddHostedService<RemoteWorker>();
await builder.Build().RunAsync();

public sealed record RemoteJob(Guid Id, Guid MachineId, string Action, string? MacAddress,
    string? BroadcastAddress, int WolPort, DateTimeOffset ExpiresAt);
public sealed record RemoteJobResult(bool Succeeded, string Message);

public sealed class RemoteWorker(ILogger<RemoteWorker> logger, IConfiguration configuration, IHostApplicationLifetime lifetime) : BackgroundService
{
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, DateTimeOffset> completed = new();
    private string? Setting(string name) => configuration[name];
    private void Stop() { Environment.ExitCode = 1; lifetime.StopApplication(); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = Setting("REMOTE_WAKE_MODE")?.ToLowerInvariant();
        var apiUrl = Setting("REMOTE_WAKE_API_URL");
        var key = Setting("REMOTE_WAKE_KEY");
        if (mode is not ("gateway" or "agent") || !Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(key))
        {
            logger.LogError("Configure REMOTE_WAKE_MODE, REMOTE_WAKE_API_URL and REMOTE_WAKE_KEY.");
            Stop();
            return;
        }

        var machineId = Guid.Empty;
        if (mode == "agent" && !Guid.TryParse(Setting("REMOTE_WAKE_MACHINE_ID"), out machineId))
        {
            logger.LogError("Configure REMOTE_WAKE_MACHINE_ID for the agent.");
            Stop();
            return;
        }

        using var client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("X-Remote-Wake-Key", key);
        var basePath = mode == "gateway" ? "api/gateway" : $"api/agent/{machineId}";
        logger.LogInformation("Remote Wake {Mode} started. Polling {ApiUrl}.", mode, uri);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync($"{basePath}/poll", stoppingToken);
                if (response.StatusCode == HttpStatusCode.NoContent) continue;
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    logger.LogError("Worker key rejected. Check API and worker configuration.");
                    Stop();
                    return;
                }
                response.EnsureSuccessStatusCode();
                var job = await response.Content.ReadFromJsonAsync<RemoteJob>(json, stoppingToken);
                if (job is null) continue;
                foreach (var expired in completed.Where(item => item.Value < DateTimeOffset.UtcNow).Select(item => item.Key).ToArray()) completed.Remove(expired);
                RemoteJobResult result;
                if (job.ExpiresAt <= DateTimeOffset.UtcNow || completed.ContainsKey(job.Id))
                    result = new(false, "Comando expirado ou já processado.");
                else
                {
                    completed[job.Id] = job.ExpiresAt;
                    try { result = mode == "gateway" ? await WakeAsync(job, stoppingToken) : RunAction(job, machineId); }
                    catch (SocketException) { result = new(false, "Falha de rede ao enviar o Magic Packet."); }
                }
                using var complete = await client.PostAsJsonAsync(
                    $"{basePath}/jobs/{job.Id}/complete?machineId={job.MachineId}", result, json, stoppingToken);
                if (!complete.IsSuccessStatusCode)
                    logger.LogWarning("Could not acknowledge job {JobId}: HTTP {StatusCode}.", job.Id, complete.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Worker connection failed; retrying in five seconds.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<RemoteJobResult> WakeAsync(RemoteJob job, CancellationToken cancellationToken)
    {
        if (job.Action != "wake" || job.MacAddress is null || job.BroadcastAddress is null)
            return new(false, "Pedido de wake inválido.");
        var allowed = (Setting("REMOTE_WAKE_ALLOWED_BROADCASTS") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!allowed.Contains(job.BroadcastAddress, StringComparer.OrdinalIgnoreCase)
            || !IPAddress.TryParse(job.BroadcastAddress, out var destination)
            || destination.AddressFamily != AddressFamily.InterNetwork || job.WolPort is < 1 or > 65535)
            return new(false, "Destino não está na lista permitida do gateway.");

        var normalized = new string(job.MacAddress.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length != 12) return new(false, "Endereço MAC inválido.");
        var mac = Convert.FromHexString(normalized);
        var packet = new byte[102];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var offset = 6; offset < packet.Length; offset += 6) mac.CopyTo(packet, offset);
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        await udp.SendAsync(packet, new IPEndPoint(destination, job.WolPort), cancellationToken);
        return new(true, "Magic Packet enviado pelo gateway residencial.");
    }

    private RemoteJobResult RunAction(RemoteJob job, Guid machineId)
    {
        if (job.MachineId != machineId || job.Action is not ("shutdown" or "restart"))
            return new(false, "Ação não permitida para este agente.");
        if (Setting("REMOTE_WAKE_DRY_RUN") == "true")
            return new(true, $"Simulação: {job.Action} recebido; nenhuma ação executada.");
        if (Setting("REMOTE_WAKE_POWER_ACTIONS_ENABLED") != "true")
            return new(false, "Ações de energia desativadas neste agente.");

        try
        {
            var windows = OperatingSystem.IsWindows();
            var start = new ProcessStartInfo(windows ? "shutdown.exe" : "shutdown")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (windows)
            {
                start.ArgumentList.Add(job.Action == "shutdown" ? "/s" : "/r");
                start.ArgumentList.Add("/t");
                start.ArgumentList.Add("30");
            }
            else
            {
                start.ArgumentList.Add(job.Action == "shutdown" ? "-h" : "-r");
                start.ArgumentList.Add("+1");
            }
            using var process = Process.Start(start);
            if (process is null) return new(false, "Não foi possível iniciar o comando do sistema.");
            if (!process.WaitForExit(5000))
                return new(false, "O comando do sistema não respondeu em cinco segundos.");
            return process.ExitCode == 0
                ? new(true, job.Action == "shutdown" ? "Desligamento agendado." : "Reinicialização agendada.")
                : new(false, $"Comando do sistema retornou código {process.ExitCode}.");
        }
        catch (Exception exception)
        {
            return new(false, $"Falha ao agendar a ação: {exception.Message}");
        }
    }
}
