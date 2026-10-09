using Microsoft.UI.Xaml;

namespace EditorScene.App;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "EditorScene — Syntax Editor" };
        MainWindow.Content = new EditorPage();
        MainWindow.Activate();
    }
}
