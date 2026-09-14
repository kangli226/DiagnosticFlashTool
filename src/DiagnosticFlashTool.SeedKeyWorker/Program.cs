using System.Runtime.InteropServices;
using System.Text.Json;
using DiagnosticFlashTool.Core.Util;
using DiagnosticFlashTool.Infrastructure.Security;

namespace DiagnosticFlashTool.SeedKeyWorker;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task<int> Main()
    {
        try
        {
            var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                WriteResponse(new SeedKeyWorkerResponse { Success = false, Error = "Seed-key worker received no request." });
                return 2;
            }

            var request = JsonSerializer.Deserialize<SeedKeyWorkerRequest>(line, JsonOptions)
                ?? throw new InvalidDataException("Seed-key worker request is empty.");
            var key = GenerateKey(request);
            WriteResponse(new SeedKeyWorkerResponse
            {
                Success = true,
                KeyHex = HexUtil.ToHex(key)
            });
            return 0;
        }
        catch (Exception ex)
        {
            WriteResponse(new SeedKeyWorkerResponse
            {
                Success = false,
                Error = ex.Message
            });
            return 1;
        }
    }

    private static byte[] GenerateKey(SeedKeyWorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DllPath))
        {
            throw new ArgumentException("Seed-key DLL path is required.");
        }

        if (!File.Exists(request.DllPath))
        {
            throw new FileNotFoundException("Seed-key DLL was not found.", request.DllPath);
        }

        var seed = HexUtil.ParseBytes(request.SeedHex);
        if (seed.Length == 0)
        {
            throw new ArgumentException("Seed cannot be empty.");
        }

        var callingConvention = ParseCallingConvention(request.CallingConvention);
        var entryPoint = string.IsNullOrWhiteSpace(request.EntryPoint) ? "GenerateKeyEx" : request.EntryPoint.Trim();

        Environment.CurrentDirectory = Path.GetDirectoryName(Path.GetFullPath(request.DllPath))
            ?? Environment.CurrentDirectory;
        var library = NativeLibrary.Load(request.DllPath);
        try
        {
            var (export, usesOptions) = ResolveExport(library, entryPoint);
            var key = new byte[KeyCapacity];
            var actualSizePtr = Marshal.AllocHGlobal(sizeof(uint));

            try
            {
                Marshal.WriteInt32(actualSizePtr, 0);
                var status = usesOptions
                    ? InvokeGenerateKeyExOpt(
                        export,
                        callingConvention,
                        seed,
                        (uint)seed.Length,
                        request.SecurityLevel,
                        request.Variant,
                        request.Options,
                        key,
                        KeyCapacity,
                        actualSizePtr)
                    : InvokeGenerateKeyEx(
                        export,
                        callingConvention,
                        seed,
                        (uint)seed.Length,
                        request.SecurityLevel,
                        request.Variant,
                        key,
                        KeyCapacity,
                        actualSizePtr);

                if (status != 0)
                {
                    throw new InvalidOperationException($"Seed-key DLL returned {DescribeStatus(status)} ({status}).");
                }

                var actualSize = unchecked((uint)Marshal.ReadInt32(actualSizePtr));
                if (actualSize == 0 || actualSize > KeyCapacity)
                {
                    throw new InvalidDataException($"Seed-key DLL returned an invalid key size: {actualSize}.");
                }

                return key.AsSpan(0, checked((int)actualSize)).ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(actualSizePtr);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static int InvokeGenerateKeyEx(
        nint export,
        CallingConvention callingConvention,
        byte[] seed,
        uint seedSize,
        uint securityLevel,
        string variant,
        byte[] key,
        uint maxKeySize,
        nint actualKeySize)
    {
        return callingConvention == CallingConvention.StdCall
            ? Marshal.GetDelegateForFunctionPointer<GenerateKeyExStdCall>(export)(seed, seedSize, securityLevel, variant, key, maxKeySize, actualKeySize)
            : Marshal.GetDelegateForFunctionPointer<GenerateKeyExCdecl>(export)(seed, seedSize, securityLevel, variant, key, maxKeySize, actualKeySize);
    }

    private static int InvokeGenerateKeyExOpt(
        nint export,
        CallingConvention callingConvention,
        byte[] seed,
        uint seedSize,
        uint securityLevel,
        string variant,
        string options,
        byte[] key,
        uint maxKeySize,
        nint actualKeySize)
    {
        return callingConvention == CallingConvention.StdCall
            ? Marshal.GetDelegateForFunctionPointer<GenerateKeyExOptStdCall>(export)(seed, seedSize, securityLevel, variant, options, key, maxKeySize, actualKeySize)
            : Marshal.GetDelegateForFunctionPointer<GenerateKeyExOptCdecl>(export)(seed, seedSize, securityLevel, variant, options, key, maxKeySize, actualKeySize);
    }

    private static (nint Export, bool UsesOptions) ResolveExport(nint library, string entryPoint)
    {
        if (NativeLibrary.TryGetExport(library, entryPoint, out var export))
        {
            return (export, entryPoint.Contains("Opt", StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(entryPoint, "GenerateKeyEx", StringComparison.Ordinal)
            && NativeLibrary.TryGetExport(library, "GenerateKeyExOpt", out export))
        {
            return (export, true);
        }

        throw new EntryPointNotFoundException($"Seed-key DLL does not export {entryPoint} or GenerateKeyExOpt.");
    }

    private static string DescribeStatus(int status)
    {
        return status switch
        {
            1 => "buffer too small",
            2 => "invalid security level",
            3 => "invalid variant",
            4 => "unspecified error",
            _ => "error"
        };
    }

    private static CallingConvention ParseCallingConvention(string? value)
    {
        return string.Equals(value, "stdcall", StringComparison.OrdinalIgnoreCase)
            ? CallingConvention.StdCall
            : CallingConvention.Cdecl;
    }

    private static void WriteResponse(SeedKeyWorkerResponse response)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        Console.Out.Flush();
    }

    private const uint KeyCapacity = 1024;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GenerateKeyExCdecl(byte[] seed, uint seedSize, uint securityLevel, string variant, byte[] key, uint maxKeySize, nint actualKeySize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GenerateKeyExStdCall(byte[] seed, uint seedSize, uint securityLevel, string variant, byte[] key, uint maxKeySize, nint actualKeySize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GenerateKeyExOptCdecl(byte[] seed, uint seedSize, uint securityLevel, string variant, string options, byte[] key, uint maxKeySize, nint actualKeySize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GenerateKeyExOptStdCall(byte[] seed, uint seedSize, uint securityLevel, string variant, string options, byte[] key, uint maxKeySize, nint actualKeySize);
}
