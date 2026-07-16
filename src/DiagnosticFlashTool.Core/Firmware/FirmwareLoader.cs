namespace DiagnosticFlashTool.Core.Firmware;

public sealed class FirmwareLoader
{
    public FirmwareImage Load(string filePath, FirmwareImageKind kind, uint fallbackAddress = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Firmware file path is empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Firmware file not found.", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".s19" or ".srec" or ".mot" => new S19Parser().Load(filePath, kind),
            ".hex" => new HexParser().Load(filePath, kind),
            ".bin" => LoadBin(filePath, kind, fallbackAddress),
            _ => throw new NotSupportedException($"Unsupported firmware file extension: {extension}")
        };
    }

    private static FirmwareImage LoadBin(string filePath, FirmwareImageKind kind, uint fallbackAddress)
    {
        return new FirmwareImage
        {
            FilePath = filePath,
            Kind = kind,
            Blocks = [new FirmwareBlock(fallbackAddress, File.ReadAllBytes(filePath))]
        };
    }
}
