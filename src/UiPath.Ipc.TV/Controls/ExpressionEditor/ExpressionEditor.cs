using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CSharp;
using System.CodeDom;
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UiPath.Ipc.TV;

using ElementHost = System.Windows.Forms.Integration.ElementHost;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

public partial class ExpressionEditor : UserControl
{
    private readonly record struct State(
        ElementHost ElementHost,
        TextEditor TextEditor,
        MyCompletionService CompletionService);

    private readonly Lazy<State> _state;
    private CompletionWindow? _completionWindow;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ReturnType { get; set; } = "Func<object, bool>";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowLineNumbers
    {
        get => TextEditor.ShowLineNumbers;
        set => TextEditor.ShowLineNumbers = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Code
    {
        get => TextEditor.Text;
        set => TextEditor.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Assembly> References
    {
        get => CompletionService.References;
        set => CompletionService.References = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Usings { get; set; } = ["System"];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public event EventHandler? CodeChanged
    {
        add => TextEditor.TextChanged += value;
        remove => TextEditor.TextChanged -= value;
    }

    private TextEditor TextEditor => _state.Value.TextEditor;
    private MyCompletionService CompletionService => _state.Value.CompletionService;

    public ExpressionEditor()
    {
        InitializeComponent();

        _state = new(CreateState);

        State CreateState()
        {
            var textEditor = new TextEditor()
            {
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"),
                FontFamily = new("Consolas"),
                FontSize = 14,
                ShowLineNumbers = true,
                WordWrap = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = @""
            };

            textEditor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
            textEditor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
            textEditor.TextArea.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Space && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                {
                    e.Handled = true;
                    _ = Pal();

                    async Task Pal()
                    {
                        try
                        {
                            await TriggerCompletion();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            };

            var elementHost = new ElementHost()
            {
                Parent = this,
                Dock = DockStyle.Fill,
                Child = textEditor
            };

            var completionService = new MyCompletionService();

            return new(elementHost, textEditor, completionService);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        if (!DesignMode)
        {
            _ = _state.Value;
        }
    }

    public async Task<T> Execute<T>()
    {
        var options = ScriptOptions.Default
            .WithReferences(References.Select(x => MetadataReference.CreateFromFile(x.Location)))
            .WithImports(Usings);
        return await CSharpScript.EvaluateAsync<T>(Pal(), options);

        string Pal()
        {
            return $$"""
                ({{ReturnType}})({{Code}})
                """;
        }
    }

    private string ComputeCode(out string prologue)
    {
        var usings = string.Join("\r\n", Usings.Select(x => $"using {x};"));

        prologue = $$"""
            {{usings}}

            namespace DynamicQueriesfe74fcf518094bf3a419f073b732e8e8;

            public static class Queryfe74fcf518094bf3a419f073b732e8e8
            {
                public static readonly {{ReturnType}} Resultfe74fcf518094bf3a419f073b732e8e8 = 
            """;

        return $$"""
            {{prologue}}{{TextEditor.Text}};
            }
            """;
    }

    private void textEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
    {
        _ = Pal();

        async Task Pal()
        {
            try
            {
                if (e.Text == ".")
                {
                    await TriggerCompletion();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async Task TriggerCompletion()
    {
        var code = ComputeCode(out var prologue);
        var results = await CompletionService.GetCompletions2(code, TextEditor.CaretOffset + prologue.Length);

        _completionWindow = new CompletionWindow(TextEditor.TextArea);
        IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;

        foreach (var result in results)
        {
            data.Add(new MyCompletionData(result.SymbolName, result.SymbolKind, result.InsertionText));
        }

        _completionWindow.Show();
        _completionWindow.Closed += delegate
        {
            _completionWindow = null;
        };
    }


    private void textEditor_TextArea_TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length > 0 && _completionWindow != null)
        {
            if (!char.IsLetterOrDigit(e.Text[0]))
            {
                // Whenever a non-letter is typed while the completion window is open,
                // insert the currently selected element.
                _completionWindow.CompletionList.RequestInsertion(e);
            }
        }
        // Do not set e.Handled=true.
        // We still want to insert the character that was typed.
    }

    private class MyCompletionData : ICompletionData
    {
        private readonly SymbolKind _symbolKind;

        public MyCompletionData(string text, SymbolKind symbolKind, string insertionText)
        {
            Text = text;
            _symbolKind = symbolKind;
            InsertionText = insertionText;
        }

        public ImageSource? Image => _symbolKind switch
        {
            SymbolKind.Property => new BitmapImage(new Uri("pack://application:,,,/Graphics/property-16.png")),
            SymbolKind.Method => new BitmapImage(new Uri("pack://application:,,,/Graphics/method-16.png")),
            SymbolKind.Field => new BitmapImage(new Uri("pack://application:,,,/Graphics/field-16.png")),
            _ => null
        };

        public string Text { get; }
        public string InsertionText { get; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => Text;

        public object Description => "Description for " + Text;

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }

        double ICompletionData.Priority => 0;
    }
}

public static class ExpressionEditorExtensions
{
    public static void EnsureAccessible(this ExpressionEditor expressionEditor, IEnumerable<Assembly> assemblies)
    {
        var set = assemblies.ToHashSet();
        set = [.. set, .. expressionEditor.References];

        expressionEditor.References = set.ToArray();
    }

    public static void EnsureAccessible(this ExpressionEditor expressionEditor, IEnumerable<Type> types)
    {
        var references = expressionEditor.References.ToHashSet();
        var usings = expressionEditor.Usings.ToHashSet();

        foreach (var type in types)
        {
            references.Add(type.Assembly);
            if (type.Namespace is { } @namespace)
            {
                usings.Add(@namespace);
            }
        }
       
        expressionEditor.References = references.ToArray();
        expressionEditor.Usings = usings.ToArray();
    }

    public static Func<Task<T>> ConfigureExecution<T>(this ExpressionEditor editor)
    {
        editor.ReturnType = typeof(T).ToCSharpSyntax();
        return editor.Execute<T>;
    }

    private static string ToCSharpSyntax(this Type type)
    {
        using (var provider = new CSharpCodeProvider())
        {
            var typeRef = new CodeTypeReference(type);
            return provider.GetTypeOutput(typeRef);
        }
    }
}
