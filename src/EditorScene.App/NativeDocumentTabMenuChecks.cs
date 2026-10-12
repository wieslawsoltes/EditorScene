using EditorScene.Controls;
using EditorScene.Text;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class NativeDocumentTabMenuChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Document tab menu: " + name); checks.Add(name); }
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var menu = new EditorDocumentTabMenu();
        var items = menu.Items.ToArray();
        Check(menu.ItemBuildCount == 4 && items.Length == 4 && menu.TargetDocumentId is null && items.Cast<MenuFlyoutItem>().All(i => !i.IsEnabled),
            "Native tab menu starts with four retained disabled commands and no document target");
        menu.Configure(a, 0, 1);
        Check(menu.CanInvoke(DocumentCloseScope.Current) && menu.CanInvoke(DocumentCloseScope.All) && !menu.CanInvoke(DocumentCloseScope.Others) && !menu.CanInvoke(DocumentCloseScope.ToRight),
            "One native tab enables Close and Close All but disables empty bulk scopes");
        menu.Configure(a, 1, 3);
        Check(Enum.GetValues<DocumentCloseScope>().All(menu.CanInvoke) && menu.TargetDocumentId == a,
            "Middle-tab context enables each populated scope without retaining a live document");
        EditorDocumentCloseRequestedEventArgs? request = null;
        EventHandler<EditorDocumentCloseRequestedEventArgs> handler = (_, value) => { request = value; menu.Configure(b, 0, 1); };
        menu.CloseRequested += handler; var invoked = menu.TryInvoke(DocumentCloseScope.Others); menu.CloseRequested -= handler;
        Check(invoked && request?.DocumentId == a && request.Scope == DocumentCloseScope.Others && menu.TargetDocumentId == b,
            "A close request freezes its original identity and scope even when the host changes menu context");
        menu.Configure(a, 1, 3, false);
        Check(!menu.TryInvoke(DocumentCloseScope.All) && items.Cast<MenuFlyoutItem>().All(i => !i.IsEnabled),
            "Busy or host-disabled native tab menus cannot issue close requests");
        var rejected = 0;
        try { menu.Configure(Guid.Empty, 0, 1); } catch (ArgumentException) { rejected++; }
        try { menu.Configure(b, -1, 1); } catch (ArgumentOutOfRangeException) { rejected++; }
        try { menu.Configure(b, 1, 1); } catch (ArgumentOutOfRangeException) { rejected++; }
        try { menu.Configure(b, 0, 0); } catch (ArgumentOutOfRangeException) { rejected++; }
        Check(rejected == 4 && menu.TargetDocumentId == a && !menu.CanInvoke(DocumentCloseScope.Current),
            "Invalid native tab-menu targets and positions leave the preceding context unchanged");
        for (var i = 0; i < 200; i++) menu.Configure(i % 2 == 0 ? a : b, 0, 2);
        Check(menu.ItemBuildCount == 4 && menu.Items.SequenceEqual(items),
            "Two hundred native context changes reuse all four original menu controls");
        menu.ClearContext();
        Check(menu.TargetDocumentId is null && !menu.TryInvoke(DocumentCloseScope.Current) && items.Cast<MenuFlyoutItem>().All(i => !i.IsEnabled),
            "Clearing tab context rejects stale commands and disables retained native items");
        menu.Configure(a, 0, 2);
        Check(!menu.TryInvoke((DocumentCloseScope)999), "Unknown close scopes never reach host callbacks");
        using var rebound = new EditorDocumentTabMenu();
        var first = (MenuFlyoutItem)rebound.Items[0];
        var token = first.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) =>
        { if (first.IsEnabled) rebound.Configure(b, 0, 1, false); });
        try { rebound.Configure(a, 0, 3); }
        finally { first.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, token); }
        Check(rebound.TargetDocumentId == b && rebound.Items.Cast<MenuFlyoutItem>().All(i => !i.IsEnabled),
            "Native enabled-state callbacks can reconfigure context without later stale enabled writes");
        using var disposed = new EditorDocumentTabMenu();
        first = (MenuFlyoutItem)disposed.Items[0];
        token = first.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) => { if (first.IsEnabled) disposed.Dispose(); });
        try { disposed.Configure(a, 0, 2); }
        finally { first.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, token); }
        Check(disposed.Items.Count == 0 && disposed.TargetDocumentId is null,
            "Disposal during a native menu-property callback safely terminates reconciliation");
        menu.Dispose(); var throws = false;
        try { menu.Configure(a, 0, 1); } catch (ObjectDisposedException) { throws = true; }
        Check(throws && menu.Items.Count == 0 && menu.TargetDocumentId is null,
            "Disposed tab menus reject mutation and detach their native commands");
    }
}
