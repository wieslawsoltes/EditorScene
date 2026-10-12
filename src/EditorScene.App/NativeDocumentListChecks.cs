using EditorScene.Controls;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class NativeDocumentListChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Document menu: " + name); checks.Add(name); }
        using var menu = new EditorDocumentList();
        var a = new EditorDocumentListEntry(Guid.NewGuid(), "same.cs");
        var b = new EditorDocumentListEntry(Guid.NewGuid(), "same.cs", true);
        var c = new EditorDocumentListEntry(Guid.NewGuid(), "last.txt");
        var d = new EditorDocumentListEntry(Guid.NewGuid(), "extra.xaml");
        Check(menu.DocumentCount == 0 && menu.Items.Count == 0 && menu.SelectedDocumentId is null, "Native document menu starts empty without owning a document");
        menu.SetDocuments([a, b, c]);
        var first = (ToggleMenuFlyoutItem)menu.Items[0]; var second = (ToggleMenuFlyoutItem)menu.Items[1]; var third = menu.Items[2];
        Check(menu.DocumentCount == 3 && menu.ItemBuildCount == 3 && first.Text == "same.cs" && second.Text == "same.cs *", "Duplicate titles retain distinct identities and visible native dirty markers");
        menu.SelectedDocumentId = b.Id;
        Check(!first.IsChecked && second.IsChecked, "Selecting an identity checks only its native document menu item");
        var renamed = a with { Title = "renamed.cs", IsModified = true };
        Check(menu.UpdateDocument(renamed) && first.Text == "renamed.cs *" && ReferenceEquals(menu.Items[0], first) && menu.ItemBuildCount == 3, "Rename and dirty-state changes reuse the existing native menu control");
        Check(!menu.UpdateDocument(d) && menu.Items.Count == 3, "Updating a removed or unknown document cannot implicitly add an item");
        menu.SetDocuments([c, b, renamed]);
        Check(ReferenceEquals(menu.Items[0], third) && ReferenceEquals(menu.Items[1], second) && ReferenceEquals(menu.Items[2], first) && menu.ItemBuildCount == 3, "Reordering preserves every native menu item identity");
        menu.SetDocuments([c, b, renamed, d]);
        Check(menu.ItemBuildCount == 4 && ReferenceEquals(menu.Items[0], third), "Appending one document allocates exactly one new native item");
        menu.SetDocuments([c, renamed, d]);
        Check(menu.SelectedDocumentId is null && menu.DocumentCount == 3 && !menu.Items.Contains(second), "Removing the selected identity clears selection and detaches its menu item");
        Check(!menu.TryInvoke(b.Id), "A stale removed document identity cannot invoke activation");
        Guid? requested = null;
        EventHandler<EditorDocumentInvokedEventArgs> accept = (_, e) => { requested = e.Document.Id; menu.SelectedDocumentId = e.Document.Id; };
        menu.DocumentInvoked += accept; menu.TryInvoke(a.Id); menu.DocumentInvoked -= accept;
        Check(requested == a.Id && menu.SelectedDocumentId == a.Id && first.IsChecked, "The host receives the requested identity and explicitly accepts activation");
        menu.TryInvoke(c.Id);
        Check(menu.SelectedDocumentId == a.Id && first.IsChecked && !((ToggleMenuFlyoutItem)third).IsChecked, "A host-declined invocation leaves the original native check mark selected");
        EventHandler<EditorDocumentInvokedEventArgs> remove = (_, _) => menu.SetDocuments([d]);
        menu.DocumentInvoked += remove; menu.TryInvoke(a.Id); menu.DocumentInvoked -= remove;
        Check(menu.DocumentCount == 1 && menu.SelectedDocumentId is null && !menu.Items.Contains(first), "Activation callbacks can remove the requested document without restoring stale selection");
        var before = menu.Items[0]; var builds = menu.ItemBuildCount;
        var rejected = false;
        try { menu.SetDocuments([a, a]); } catch (ArgumentException) { rejected = true; }
        Check(rejected && menu.DocumentCount == 1 && ReferenceEquals(menu.Items[0], before), "Duplicate identities are rejected before changing the current native menu");
        rejected = false;
        try { menu.SetDocuments([a with { Id = Guid.Empty }]); } catch (ArgumentException) { rejected = true; }
        Check(rejected && menu.ItemBuildCount == builds, "Empty identities fail before native control construction");
        rejected = false;
        try { menu.SetDocuments([a with { Title = " " }]); } catch (ArgumentException) { rejected = true; }
        Check(rejected && ReferenceEquals(menu.Items[0], before), "Blank document titles leave prior native items intact");
        rejected = false;
        try { menu.SelectedDocumentId = a.Id; } catch (ArgumentException) { rejected = true; }
        Check(rejected && menu.SelectedDocumentId is null, "An unlisted selected identity is rejected without changing selection");
        rejected = false;
        try { menu.SetDocuments([a, null!]); } catch (ArgumentNullException) { rejected = true; }
        Check(rejected && menu.DocumentCount == 1 && menu.ItemBuildCount == builds, "Invalid later snapshot entries do not partially replace the native document list");
        for (var i = 0; i < 100; i++) menu.SetDocuments([d]);
        Check(menu.ItemBuildCount == builds && ReferenceEquals(menu.Items[0], before), "Repeated unchanged membership retains its native item instead of rebuilding it");
        menu.SetDocuments([]);
        Check(menu.Items.Count == 0 && menu.DocumentCount == 0 && !menu.TryInvoke(d.Id), "Clearing detaches all menu entries and rejects their stale activation requests");
        menu.Dispose(); rejected = false;
        try { menu.SetDocuments([a]); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected && menu.DocumentCount == 0 && a.Title == "same.cs", "Disposal rejects future mutation without modifying caller-owned document metadata");

        using var rebound = new EditorDocumentList(); rebound.SetDocuments([a, b]); rebound.SelectedDocumentId = a.Id;
        var old = (ToggleMenuFlyoutItem)rebound.Items[0];
        var token = old.RegisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, (_, _) =>
        { if (!old.IsChecked) rebound.SetDocuments([c]); });
        try { rebound.SelectedDocumentId = b.Id; }
        finally { old.UnregisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, token); }
        Check(rebound.DocumentCount == 1 && rebound.SelectedDocumentId is null && (Guid)rebound.Items[0].Tag == c.Id,
            "A native check-state callback can replace membership without stale selection writes");

        rebound.SetDocuments([a, b]); old = (ToggleMenuFlyoutItem)rebound.Items[0];
        token = old.RegisterPropertyChangedCallback(MenuFlyoutItem.TextProperty, (_, _) => rebound.SetDocuments([c]));
        try { rebound.SetDocuments([renamed, b]); }
        finally { old.UnregisterPropertyChangedCallback(MenuFlyoutItem.TextProperty, token); }
        Check(rebound.DocumentCount == 1 && rebound.Items.Count == 1 && (Guid)rebound.Items[0].Tag == c.Id,
            "A native caption callback can replace the menu without the prior traversal reintroducing removed items");

        using var disposed = new EditorDocumentList(); disposed.SetDocuments([a, b]); disposed.SelectedDocumentId = a.Id;
        old = (ToggleMenuFlyoutItem)disposed.Items[0];
        token = old.RegisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, (_, _) =>
        { if (!old.IsChecked) disposed.Dispose(); });
        try { disposed.SelectedDocumentId = b.Id; }
        finally { old.UnregisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, token); }
        Check(disposed.DocumentCount == 0 && disposed.Items.Count == 0 && disposed.SelectedDocumentId is null,
            "Disposal from a native selection callback safely terminates the remaining state update");

        using var invoked = new EditorDocumentList(); invoked.SetDocuments([a]);
        invoked.DocumentInvoked += (_, _) => invoked.Dispose();
        Check(invoked.TryInvoke(a.Id) && invoked.DocumentCount == 0 && invoked.Items.Count == 0,
            "Disposal from an activation callback does not revive the invoked item or its check mark");
    }
}
