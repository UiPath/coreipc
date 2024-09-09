using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using System.Reflection;

namespace UiPath.Ipc.TV;

public class MyCompletionService
{
    private readonly MefHostServices _host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
    private Lazy<(AdhocWorkspace workspace, Project project)> _state;

    private IReadOnlyList<Assembly> _references = [];
    public IReadOnlyList<Assembly> References
    {
        get => _references;
        set
        {
            _references = value;
            _state = Reset();
        }
    }

    public MyCompletionService()
    {
        _state = Reset();
    }

    private Lazy<(AdhocWorkspace workspace, Project project)> Reset() => new(CreateState);
    private (AdhocWorkspace workspace, Project project) CreateState()
    {        
        var workspace = new AdhocWorkspace(_host);

        var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "IpcRoslynCompletionService", "IpcRoslynCompletionService", LanguageNames.CSharp).
            WithMetadataReferences(_references.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));

        var project = workspace.AddProject(projectInfo);

        return (workspace, project);
    }

    public async Task<IReadOnlyList<MyCompletion>> GetCompletions2(string code, int caretPosition)
    {
        var (workspace, project) = _state.Value;
        var document = workspace.AddDocument(project.Id, "MyFile.cs", SourceText.From(code));

        try
        {
            var completionService = CompletionService.GetService(document);
            if (completionService is null)
            {
                return [];
            }

            var results = await completionService.GetCompletionsAsync(document, caretPosition);
            var myResults = Enumerate(results).ToArray();
            return myResults;
        }
        finally
        {
        }

        static IEnumerable<MyCompletion> Enumerate(CompletionList completionList)
        {
            foreach (var completion in completionList.ItemsList)
            {
                if (!completion.Properties.TryGetValue("InsertionText", out var insertionText) ||
                    !completion.Properties.TryGetValue("SymbolName", out var symbolName) ||
                    !completion.Properties.TryGetValue("SymbolKind", out var rawSymbolKind))
                {
                    continue;
                }

                _ = completion.Properties.TryGetValue("ShouldProvideParenthesisCompletion", out var rawShouldProvideParenthesisCompletion);
                if (!bool.TryParse(rawShouldProvideParenthesisCompletion, out var shouldProvideParenthesisCompletion))
                {
                    shouldProvideParenthesisCompletion = false;
                }

                var symbolKind = (SymbolKind)int.Parse(rawSymbolKind);

                yield return new MyCompletion(symbolName, symbolKind, insertionText, shouldProvideParenthesisCompletion);
            }
        }
    }

    public readonly record struct MyCompletion(
        string SymbolName,
        SymbolKind SymbolKind,
        string InsertionText,
        bool ShouldProvideParenthesisCompletion);
}
