using System.Diagnostics;
using System.Text.Json;
using DiagnosticFlashTool.Core.Util;

namespace DiagnosticFlashTool.Infrastructure.Security;

/// <summary>
/// Invokes the architecture-selected SeedKeyWorker process and returns only
/// the generated key. A process boundary prevents a vendor DLL crash or
/// incompatible bitness from taking down the WPF host.
/// </summary>
public sealed class SeedKeyWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<byte[]> ComputeKeyAsync(
        SeedKeyDllOptions options,
        byte[] seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(seed);
        options.Validate();

        var workerPath = ResolveWorkerPath(options.WorkerPath);
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("Seed-key worker executable was not found.", workerPath);
        }

        EnsureArchitectureCompatible(options.DllPath, workerPath);

        var request = new SeedKeyWorkerRequest
        {
            DllPath = Path.GetFullPath(options.DllPath),
            EntryPoint = options.EntryPoint,
            CallingConvention = options.CallingConvention,
            SeedHex = HexUtil.ToHex(seed),
            SecurityLevel = options.SecurityLevel,
            Variant = options.Variant,
            Options = options.Options,
            TimeoutMs = options.TimeoutMs
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            WorkingDirectory = Path.GetDirectoryName(workerPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start SeedKeyWorker.");
        }

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.TimeoutMs);

        string responseJson;
        try
        {
            responseJson = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryTerminate(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Seed-key worker timeout after {options.TimeoutMs} ms.");
        }

        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "Seed-key worker returned no response."
                : $"Seed-key worker failed: {stderr.Trim()}");
        }

        SeedKeyWorkerResponse response;
        try
        {
            response = JsonSerializer.Deserialize<SeedKeyWorkerResponse>(responseJson, JsonOptions)
                ?? throw new InvalidDataException("Seed-key worker returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Seed-key worker returned invalid JSON.", ex);
        }

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "Seed-key DLL failed to generate a key.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Seed-key worker returned success but exited with code {process.ExitCode}.");
        }

        if (string.IsNullOrWhiteSpace(response.KeyHex))
        {
            throw new InvalidDataException("Seed-key worker response did not contain a key.");
        }

        return HexUtil.ParseBytes(response.KeyHex);
    }

    private static string ResolveWorkerPath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static void EnsureArchitectureCompatible(string dllPath, string workerPath)
    {
        var dllMachine = ReadPeMachine(dllPath);
        var workerMachine = ReadPeMachine(workerPath);
        if (dllMachine is null || workerMachine is null || dllMachine == workerMachine)
        {
            return;
        }

        throw new BadImageFormatException(
            $"Seed-key DLL architecture {DescribeMachine(dllMachine.Value)} does not match worker architecture {DescribeMachine(workerMachine.Value)}.",
            dllPath);
    }

    private static ushort? ReadPeMachine(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D || stream.Length < 0x40)
            {
                return null;
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset + 6 > stream.Length)
            {
                return null;
            }

            stream.Position = peOffset;
            return reader.ReadUInt32() == 0x00004550 ? reader.ReadUInt16() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string DescribeMachine(ushort machine)
    {
        return machine switch
        {
            0x014C => "x86",
            0x8664 => "x64",
            0xAA64 => "ARM64",
            _ => $"0x{machine:X4}"
        };
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The timeout/cancellation is the authoritative failure.
        }
    }
}
