using ModelContextProtocol;

namespace SharpTools.Tools.Mcp.Tools;

public class RazorToolsLogCategory { }

[McpServerToolType]
public static class RazorTools {

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(ReadRazorDocument),
        Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false),
    Description("Reads a .cshtml or .razor file and returns its source with a summary of the code regions Razor identifies. " +
        "Use this to understand what parts of the file contain C# code and what directives are present.")]
    public static async Task<string> ReadRazorDocument(
        ISolutionManager solutionManager,
        IRazorDocumentService razorService,
        ILogger<RazorToolsLogCategory> logger,
        [Description("Absolute path to the .cshtml or .razor file.")] string filePath,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(ReadRazorDocument));

            if (!razorService.IsRazorFile(filePath))
                throw new McpException($"File is not a Razor file (.cshtml/.razor): {filePath}");

            logger.LogInformation("Reading Razor document: {FilePath}", filePath);

            var result = await razorService.ParseAsync(filePath, cancellationToken);

            var uniqueCshtmlLines = result.SourceMappings
                .Select(m => m.CshtmlLine).Distinct().Count();

            var header = $"[Razor Document: {filePath}]\n" +
                         $"Code regions: {result.SourceMappings.Count} mappings across {uniqueCshtmlLines} source lines\n" +
                         $"Generated C#: {result.GeneratedCSharp.Length} chars " +
                         $"({result.GeneratedCSharp.Split('\n').Length} lines)\n" +
                         $"Use SharpTool_GetRazorGeneratedCSharp to see the generated C#.\n" +
                         $"Use SharpTool_GetRazorSourceMappings to see the line mapping table.\n\n";

            return header + result.CshtmlSource;
        }, logger, nameof(ReadRazorDocument), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetRazorGeneratedCSharp),
        Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false),
    Description("Returns the full C# source that the Razor compiler generates from a .cshtml or .razor file. " +
        "This is the same generated code the project actually compiles. Useful for understanding runtime behaviour.")]
    public static async Task<string> GetRazorGeneratedCSharp(
        ISolutionManager solutionManager,
        IRazorDocumentService razorService,
        ILogger<RazorToolsLogCategory> logger,
        [Description("Absolute path to the .cshtml or .razor file.")] string filePath,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(GetRazorGeneratedCSharp));

            if (!razorService.IsRazorFile(filePath))
                throw new McpException($"File is not a Razor file (.cshtml/.razor): {filePath}");

            logger.LogInformation("Getting generated C# for Razor document: {FilePath}", filePath);

            var result = await razorService.ParseAsync(filePath, cancellationToken);
            return result.GeneratedCSharp;
        }, logger, nameof(GetRazorGeneratedCSharp), cancellationToken);
    }

    [McpServerTool(Name = ToolHelpers.SharpToolPrefix + nameof(GetRazorSourceMappings),
        Idempotent = true, ReadOnly = true, Destructive = false, OpenWorld = false),
    Description("Returns a JSON array mapping each code span in a .cshtml/.razor file to its position in the generated C#. " +
        "Each entry has cshtmlLine, cshtmlChar, generatedLine, generatedChar, and length (all 1-based). " +
        "Because the generated C# is the project's actual compiler output, these positions align with Roslyn diagnostics.")]
    public static async Task<string> GetRazorSourceMappings(
        ISolutionManager solutionManager,
        IRazorDocumentService razorService,
        ILogger<RazorToolsLogCategory> logger,
        [Description("Absolute path to the .cshtml or .razor file.")] string filePath,
        CancellationToken cancellationToken = default) {

        return await ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync(async () => {
            ErrorHandlingHelpers.ValidateStringParameter(filePath, "filePath", logger);
            ToolHelpers.EnsureSolutionLoadedWithDetails(solutionManager, logger, nameof(GetRazorSourceMappings));

            if (!razorService.IsRazorFile(filePath))
                throw new McpException($"File is not a Razor file (.cshtml/.razor): {filePath}");

            logger.LogInformation("Getting source mappings for Razor document: {FilePath}", filePath);

            var result = await razorService.ParseAsync(filePath, cancellationToken);
            return ToolHelpers.ToJson(result.SourceMappings);
        }, logger, nameof(GetRazorSourceMappings), cancellationToken);
    }
}
