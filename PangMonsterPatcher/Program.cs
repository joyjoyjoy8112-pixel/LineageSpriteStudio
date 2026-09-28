using System.Text;

namespace LineageSpriteStudio;

internal static class PangPatcherProgram
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(new PangPatcherForm());
    }
}
