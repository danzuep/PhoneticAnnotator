namespace PhoneticAnnotator.Core;

/// <summary>
/// An immutable annotation configuration over already-loaded dictionaries.
/// Custom filters and aligners must be immutable (and thread-safe when the engine is shared).
/// Stream loading remains explicit through the dictionary LoadAsync methods; streams stay caller-owned.
/// </summary>
public sealed class AnnotationEngine
{
    private readonly WordReadingLexicon[] lexicons;
    private readonly UnihanReadingDictionary? unihan;
    private readonly ISpanPhoneticAligner? aligner;
    private readonly IGlossFilter[] filters;

    internal AnnotationEngine(
        LanguageCode language,
        ReadingSystem readingSystem,
        bool pickFirstReading,
        WordReadingLexicon[] lexicons,
        UnihanReadingDictionary? unihan,
        ISpanPhoneticAligner? aligner,
        IGlossFilter[] filters)
    {
        Language = language;
        ReadingSystem = readingSystem;
        PickFirstReading = pickFirstReading;
        this.lexicons = lexicons;
        this.unihan = unihan;
        this.aligner = aligner;
        this.filters = filters;
    }

    public LanguageCode Language { get; }
    public ReadingSystem ReadingSystem { get; }
    public bool PickFirstReading { get; }

    /// <summary>Configures and builds an independent engine snapshot.</summary>
    public static AnnotationEngine Create(Action<AnnotationEngineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AnnotationEngineBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Creates an owned document directly from borrowed input without intermediate heap tokens.
    /// Filters run in registration order; a custom filter replacing the document root with a
    /// non-document node has its result wrapped in a document before the next filter runs.
    /// Null filter results are rejected. Cancellation is checked before and after each filter.
    /// </summary>
    public DocumentNode Annotate(ReadOnlySpan<char> text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = GlossDocumentAssembler.Assemble(
            new TokenEnumerator(text, lexicons, Language, ReadingSystem, unihan, cancellationToken),
            PickFirstReading, aligner, cancellationToken);
        foreach (var filter in filters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = filter.Apply(document)
                ?? throw new InvalidOperationException("A gloss filter returned null.");
            document = result as DocumentNode ?? new DocumentNode(Array.AsReadOnly(new[] { result }));
            cancellationToken.ThrowIfCancellationRequested();
        }
        return document;
    }

    /// <summary>
    /// Renders a previously annotated tree. The factory is invoked once per call and must
    /// return a fresh visitor; rendering never re-runs tokenization or filters.
    /// </summary>
    public string Render(GlossAstNode document, Func<IGlossAstVisitor> visitorFactory)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(visitorFactory);
        var visitor = visitorFactory()
            ?? throw new InvalidOperationException("The visitor factory returned null.");
        document.Accept(visitor);
        return visitor.GetResult();
    }

    public string ToHtml(GlossAstNode document) => Render(document, static () => new HtmlRubyVisitor());
    public string ToJson(GlossAstNode document) => Render(document, static () => new JsonAstVisitor());
    public string ToAnki(GlossAstNode document) => Render(document, static () => new AnkiSyntaxVisitor());
}
