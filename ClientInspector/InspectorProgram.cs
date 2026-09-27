namespace LineageSpriteStudio;

internal static class InspectorProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ClientInspectorForm());
    }
}
