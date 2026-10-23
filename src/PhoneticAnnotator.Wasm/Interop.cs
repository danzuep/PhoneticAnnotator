using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("browser")]

namespace PhoneticAnnotator.Wasm;

public static partial class Interop
{
    private static readonly AnnotationSession Session = new();

    internal static async Task InitializeAsync()
    {
        await Session.AnnotateToHtml(string.Empty, "{}");
        OnReady();
    }

    [JSImport("onReady", "phonetic-demo")]
    private static partial void OnReady();

    [JSExport]
    public static Task<string> AnnotateToHtml(string text, string optionsJson) =>
        Session.AnnotateToHtml(text, optionsJson);

    [JSExport]
    public static Task<string> AnnotateToJson(string text, string optionsJson) =>
        Session.AnnotateToJson(text, optionsJson);

    [JSExport]
    public static Task LoadDictionaryChunk(byte[] dictionaryData) =>
        Session.LoadDictionaryChunk(dictionaryData);
}
