namespace DiagnosticFlashTool.Core.Algorithms;

public interface ICrcAlgorithm
{
    string Name { get; }

    int WidthBits { get; }

    uint Compute(ReadOnlySpan<byte> data);
}
