# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

SharpTools is a Roslyn-powered MCP (Model Context Protocol) server for C# code analysis and modification. It exposes tools to MCP clients (Claude, GitHub Copilot) via two transports: HTTP SSE (`SharpTools.SseServer`) and Stdio (`SharpTools.StdioServer`). The core library lives in `SharpTools.Tools`.

## Build & Run

```bash
# Build everything
dotnet build SharpTools.sln

# Run SSE server (HTTP transport)
cd SharpTools.SseServer
dotnet run -- --port 3005 --log-file ./logs/mcp-sse-server.log --log-level Debug --load-solution /path/to/solution.sln

# Run Stdio server (for MCP client config)
cd SharpTools.StdioServer
dotnet run -- --log-directory /var/log/sharptools/ --log-level Debug --load-solution /path/to/solution.sln
```

Both servers accept: `--load-solution`, `--build-configuration` (default Release), `--disable-git`, `--log-level`.

There are no automated tests in this repository.

## Architecture

```
MCP Client (Claude / Copilot)
    ↓ SSE or Stdio transport
Tool Classes  (SharpTools.Tools/Mcp/Tools/)
    ↓ constructor-injected services
Service Layer (SharpTools.Tools/Services/)
    ↓
Roslyn: MSBuildWorkspace / Compilation / SemanticModel / ISymbol
```

### Tool Layer (`Mcp/Tools/`)

Each tool class is decorated with `[McpServerToolType]`; individual methods use `[McpServerTool(Name = "SharpTool_*")]`. All tools:
- Receive dependencies via constructor injection
- Delegate to services for logic
- Wrap execution in `ErrorHandlingHelpers.ExecuteWithErrorHandlingAsync()`
- Return JSON via `ToolHelpers.ToJson()`

Tool files: `SolutionTools`, `AnalysisTools`, `ModificationTools`, `DocumentTools`, `PackageTools`, `MiscTools`, `RazorTools`.

### Service Layer (`Services/`)

| Interface | Implementation | Role |
|---|---|---|
| `ISolutionManager` | `SolutionManager` | Roslyn MSBuildWorkspace lifecycle; symbol lookup via Roslyn + Reflection |
| `ICodeAnalysisService` | `CodeAnalysisService` | Implementations, overrides, references, derived types |
| `ICodeModificationService` | `CodeModificationService` | Add/remove/rename members, format, apply changes |
| `IFuzzyFqnLookupService` | `FuzzyFqnLookupService` | Resolves partial/fuzzy FQNs to exact Roslyn `ISymbol` |
| `ISourceResolutionService` | `SourceResolutionService` | Retrieves source from local files, SourceLink, embedded PDB, or decompilation |
| `IComplexityAnalysisService` | `ComplexityAnalysisService` | Cyclomatic / cognitive complexity |
| `ISemanticSimilarityService` | `SemanticSimilarityService` | Duplicate / similar code detection |
| `IGitService` | `GitService` / `NoOpGitService` | Commits changes to `sharptools/*` branches; disabled via `--disable-git` |
| `IDocumentOperationsService` | `DocumentOperationsService` | File I/O, path validation |
| `IRazorDocumentService` | `RazorDocumentService` | Razor page parsing — generated C# and source mappings for `.cshtml`/`.razor` files |

All services are registered as singletons in `Extensions/ServiceCollectionExtensions.cs` via `WithSharpToolsServices(enableGit, buildConfiguration)`.

### Key Design Points

- **MSBuild bootstrap**: `MsBuildLocatorBootstrapper` must run before any Roslyn workspace is opened. Both `Program.cs` files call this during startup.
- **FQN resolution**: `FuzzyFqnLookupService` is the entry point for turning user-supplied type/member names into Roslyn symbols — it handles partial names, overloads, and ambiguity.
- **Source retrieval order**: local file → SourceLink → embedded PDB → ICSharpCode.Decompiler fallback. SourceLink HTTP fetches are restricted to `https` only and blocked for loopback, link-local, and RFC-1918 addresses (`IsSourceLinkUrlAllowed` in `SourceResolutionService`) to prevent SSRF via malicious PDB content.
- **Git integration**: when enabled, modifications create/commit to a `sharptools/<branch>` git branch using LibGit2Sharp.
- **Razor parsing**: `RazorDocumentService` surfaces the C# that the Razor source generator produces for a `.cshtml`/`.razor` file via `Project.GetSourceGeneratedDocumentsAsync`. It locates the generated document by matching the generator's hint name (`{FileName}_{ext}`), then extracts source mappings with `SyntaxTree.GetMappedLineSpan` — the same mechanism Roslyn uses for diagnostics, so positions align with real compiler output. Falls back to driving `CSharpGeneratorDriver` explicitly if the workspace returns no generated documents. `.cshtml` files are found as `AdditionalDocuments` on the owning Roslyn `Project`.

## Code Style

Enforced via `.editorconfig` and `.github/copilot-instructions.md`:
- 4-space indentation, Egyptian braces
- Nullable enabled, implicit usings (see `GlobalUsings.cs`)
- Early returns over nested ifs
- Local functions preferred for helpers scoped to one method
- No XML doc comments
- No magic strings; descriptive names; strong typing
- Modern C# features: pattern matching, records, functional composition
