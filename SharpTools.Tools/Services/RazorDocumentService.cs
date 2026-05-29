namespace SharpTools.Tools.Services;

public class RazorDocumentService : IRazorDocumentService {
    private readonly ISolutionManager _solutionManager;
    private readonly ILogger<RazorDocumentService> _logger;

    public RazorDocumentService(ISolutionManager solutionManager, ILogger<RazorDocumentService> logger) {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    public bool IsRazorFile(string filePath) {
        var ext = Path.GetExtension(filePath);
        return ext.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".razor", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<RazorDocumentResult> ParseAsync(string razorPath, CancellationToken cancellationToken) {
        if (!File.Exists(razorPath))
            throw new FileNotFoundException($"Razor file not found: {razorPath}");

        var cshtmlSource = await File.ReadAllTextAsync(razorPath, cancellationToken);

        var project = FindProjectContainingRazorFile(razorPath)
            ?? throw new InvalidOperationException(
                $"No loaded project includes '{razorPath}' as an additional document. " +
                "Ensure the solution is loaded and the file belongs to a web project.");

        _logger.LogInformation("Reading generated C# for {Path} from project {Project}",
            razorPath, project.Name);

        var (tree, generatedText) = await FindGeneratedTreeAsync(project, razorPath, cancellationToken);
        var mappings = ExtractMappings(tree, razorPath, cancellationToken);

        return new RazorDocumentResult {
            CshtmlPath = razorPath,
            CshtmlSource = cshtmlSource,
            GeneratedCSharp = generatedText.ToString(),
            SourceMappings = mappings
        };
    }

    private Project? FindProjectContainingRazorFile(string razorPath) {
        if (!_solutionManager.IsSolutionLoaded) return null;
        var normalized = Path.GetFullPath(razorPath);
        foreach (var project in _solutionManager.GetProjects()) {
            foreach (var add in project.AdditionalDocuments) {
                if (!string.IsNullOrEmpty(add.FilePath) &&
                    string.Equals(Path.GetFullPath(add.FilePath), normalized,
                        StringComparison.OrdinalIgnoreCase))
                    return project;
            }
        }
        return null;
    }

    private async Task<(SyntaxTree tree, SourceText text)> FindGeneratedTreeAsync(
        Project project, string razorPath, CancellationToken ct) {

        // In Roslyn 5.x this returns IEnumerable<SourceGeneratedDocument>.
        var generatedDocs = (await project.GetSourceGeneratedDocumentsAsync(ct)).ToList();

        _logger.LogDebug(
            "GetSourceGeneratedDocumentsAsync returned {Count} document(s) for project {Project}",
            generatedDocs.Count, project.Name);

        var doc = FindByName(generatedDocs, razorPath);

        if (doc is null) {
            var names = generatedDocs.Count > 0
                ? string.Join(", ", generatedDocs.Select(d => d.Name))
                : "none";
            throw new InvalidOperationException(
                $"No source-generated document found for '{razorPath}' in project '{project.Name}'. " +
                $"Available generated documents: {names}. " +
                "Ensure the project's Razor source generator is registered and the solution has been built.");
        }

        var tree = await doc.GetSyntaxTreeAsync(ct)
            ?? throw new InvalidOperationException(
                $"Could not get syntax tree for generated document of '{razorPath}'.");
        var text = await doc.GetTextAsync(ct);
        return (tree, text);
    }

    private static SourceGeneratedDocument? FindByName(
        List<SourceGeneratedDocument> docs, string razorPath) {

        var fileName = Path.GetFileNameWithoutExtension(razorPath);
        var ext = Path.GetExtension(razorPath).TrimStart('.');
        // Razor generator document names follow the pattern "{FileName}_{extension}.g.cs",
        // e.g. "Index_cshtml.g.cs" for "Index.cshtml".
        var hintFragment = $"{fileName}_{ext}";

        return docs.FirstOrDefault(doc =>
            doc.Name.Contains(hintFragment, StringComparison.OrdinalIgnoreCase));
    }

    private static List<RazorSourceMapping> ExtractMappings(
        SyntaxTree tree, string razorPath, CancellationToken ct) {

        var normalized = Path.GetFullPath(razorPath);
        var root = tree.GetRoot(ct);
        var text = tree.GetText(ct);
        var seen = new HashSet<(int line, int ch)>();
        var mappings = new List<RazorSourceMapping>();

        foreach (var token in root.DescendantTokens()) {
            if (token.IsMissing || token.Span.IsEmpty) continue;

            // GetMappedLineSpan honours #line directives — the same mechanism Roslyn
            // uses for diagnostics, so positions align with real compiler output.
            var mapped = tree.GetMappedLineSpan(token.Span);
            if (!mapped.IsValid || !mapped.HasMappedPath) continue;
            if (!string.Equals(Path.GetFullPath(mapped.Path), normalized,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var key = (mapped.StartLinePosition.Line, mapped.StartLinePosition.Character);
            if (!seen.Add(key)) continue;

            var genPos = text.Lines.GetLinePosition(token.SpanStart);
            mappings.Add(new RazorSourceMapping(
                CshtmlLine: mapped.StartLinePosition.Line + 1,
                CshtmlChar: mapped.StartLinePosition.Character + 1,
                GeneratedLine: genPos.Line + 1,
                GeneratedChar: genPos.Character + 1,
                Length: token.Span.Length));
        }

        return mappings.OrderBy(m => m.CshtmlLine).ThenBy(m => m.CshtmlChar).ToList();
    }
}
