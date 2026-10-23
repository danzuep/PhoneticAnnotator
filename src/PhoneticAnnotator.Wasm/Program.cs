namespace PhoneticAnnotator.Wasm;

internal static class Program
{
    private static async Task Main()
    {
        await Interop.InitializeAsync();
    }
}
