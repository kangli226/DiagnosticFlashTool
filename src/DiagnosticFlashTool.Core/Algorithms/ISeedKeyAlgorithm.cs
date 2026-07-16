namespace DiagnosticFlashTool.Core.Algorithms;

public interface ISeedKeyAlgorithm
{
    string Name { get; }
    byte[] ComputeKey(byte[] seed, IReadOnlyList<string> parameters);
}
