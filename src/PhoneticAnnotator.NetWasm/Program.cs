using System.Runtime.InteropServices.JavaScript;
using PhoneticAnnotator.Wasm;

namespace PhoneticAnnotator.NetWasm;

public static partial class Exports
{
    private static readonly AnnotationSession Session = new();

    [JSExport]
    public static async Task AnnotateToHtml(int requestId, string text, string optionsJson) =>
        Complete(requestId, await Session.AnnotateToHtml(text, optionsJson));

    [JSExport]
    public static async Task AnnotateToJson(int requestId, string text, string optionsJson) =>
        Complete(requestId, await Session.AnnotateToJson(text, optionsJson));

    [JSExport]
    public static Task LoadDictionaryChunk(byte[] dictionaryData) =>
        Session.LoadDictionaryChunk(dictionaryData);

    [JSImport("complete", "phonetic.results")]
    private static extern void Complete(int requestId, string result);
}
