namespace SharpTools.Tools.Interfaces;

public record RazorSourceMapping(
    int CshtmlLine,     // 1-based original line
    int CshtmlChar,     // 1-based original column
    int GeneratedLine,  // 1-based generated line
    int GeneratedChar,  // 1-based generated column
    int Length);        // length of the mapped span in the original file

public class RazorDocumentResult {
    public string CshtmlPath { get; init; } = string.Empty;
    public string CshtmlSource { get; init; } = string.Empty;
    public string GeneratedCSharp { get; init; } = string.Empty;
    public IReadOnlyList<RazorSourceMapping> SourceMappings { get; init; } = [];
}

public interface IRazorDocumentService {
    bool IsRazorFile(string filePath);
    Task<RazorDocumentResult> ParseAsync(string razorPath, CancellationToken cancellationToken);
}
