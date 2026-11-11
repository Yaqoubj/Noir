using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Noir;

static class PhoneProfileChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "W4-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new LibraryStore(dir);
        store.State.Roots = [];
        var item = new VideoItem
        {
            Id = "phone-edit",
            CollectionKey = "phone-group",
            Title = "Phone edit",
            IsMovie = true,
            Available = false,
        };
        store.State.Catalog[item.Id] = new() { Item = item };
        store.Save();
        var at = DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds();
        MobileOperation Op(string kind, string viewing = "") =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ItemId = item.Id,
                Kind = kind,
                At = ++at,
                ViewingId = viewing,
            };
        var viewing = Guid.NewGuid().ToString();
        var mark = Op("mark", viewing);
        mark.Completed = true;
        store.MergeMobile([mark]);
        var redated = Op("redate", viewing);
        redated.WatchedAt = at - 86400000;
        redated.Approximate = true;
        store.MergeMobile([redated]);
        check(
            store.ViewingHistory(item).Single() is { Approximate: true, DateUnknown: false } v
                && v.WatchedAt.ToUnixTimeMilliseconds() == redated.WatchedAt,
            "Phone redate changes the exact viewing and preserves approximate dates"
        );
        var snapshot = store.MobileSnapshot();
        check(
            snapshot.Items.Single().ViewingTimes.ContainsKey(viewing),
            "Snapshots carry viewing clocks for offline conflict resolution"
        );
        var remove = Op("deleteViewing", viewing);
        store.MergeMobile([remove]);
        var replay = Op("mark", viewing);
        replay.Completed = true;
        store.MergeMobile([replay]);
        check(
            store.ViewingHistory(item).Count == 0
                && !store.EverWatched(item)
                && store.MobileSnapshot().Items.Single().DeletedViewings.Contains(viewing),
            "Deleted viewing tombstones suppress later marks and completed progress"
        );
        var reopened = new LibraryStore(dir);
        reopened.MergeMobile([replay]);
        check(reopened.ViewingHistory(item).Count == 0, "Viewing deletion tombstones survive reopening the library");
        var pcViewing = Guid.NewGuid().ToString();
        var pcMark = Op("mark", pcViewing);
        pcMark.Completed = true;
        store.MergeMobile([pcMark]);
        store.RemoveViewing(item, pcViewing);
        var delayed = Op("mark", pcViewing);
        delayed.Completed = true;
        store.MergeMobile([delayed]);
        check(
            !store.EverWatched(item),
            "PC viewing deletion also prevents delayed phone replay from resurrecting history"
        );
        store.State.Collections["Old"] = [item.GroupKey];
        store.Save();
        var rename = Op("collectionRename");
        rename.Name = "Old";
        rename.Status = "New";
        store.MergeMobile([rename]);
        check(
            !store.State.Collections.ContainsKey("Old") && store.State.Collections["New"].Contains(item.GroupKey),
            "Phone collection rename preserves members"
        );
        var delete = Op("collectionDelete");
        delete.Name = "New";
        store.MergeMobile([delete]);
        var stale = Op("collection");
        stale.Name = "New";
        stale.Value = true;
        stale.At = delete.At - 1;
        store.MergeMobile([stale]);
        check(
            !store.State.Collections.ContainsKey("New"),
            "Collection delete tombstones prevent stale membership from restoring collections"
        );
        var list = Profiles.Load(dir);
        var second = Profiles.Create(list, "Other", "#7AA7FF", dir);
        second.PinHash = Profiles.HashPin(second, "1234");
        Profiles.Save(list, dir);
        var pcEdit = Profiles.Load(dir);
        var phoneEdit = Profiles.Load(dir);
        pcEdit.Profiles[0].Color = "#5FD0C9";
        phoneEdit.Profiles[0].Name = "Renamed remotely";
        Profiles.Save(phoneEdit, dir);
        Profiles.Save(pcEdit, dir);
        check(
            Profiles.Load(dir).Profiles[0] is { Name: "Renamed remotely", Color: "#5FD0C9" },
            "Concurrent PC and phone profile edits preserve independently changed fields"
        );
        var token = new string('B', 64);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        File.WriteAllText(
            Path.Combine(dir, "mobile-host.json"),
            JsonSerializer.Serialize(new MobileHostConfig { Peers = [new() { Name = "Migrated phone", Hash = hash }] })
        );
        await using var host = new MobileSyncServer(
            store,
            store.MobileSnapshot,
            store.MergeMobile,
            49279,
            ephemeralCertificate: true
        );
        check(host.Authorized(token), "Per-profile legacy peers migrate into shared pairing storage");
        var response = JsonSerializer.Serialize(MobileProfiles.List(dir));
        using var publicProfiles = JsonDocument.Parse(response);
        check(
            publicProfiles.RootElement.GetArrayLength() == 2
                && !response.Contains("PinHash", StringComparison.OrdinalIgnoreCase)
                && !response.Contains(second.PinHash)
                && publicProfiles.RootElement[1].GetProperty("locked").GetBoolean(),
            "GET profiles projection lists locked profiles without exposing PIN hashes"
        );
        var created = MobileProfiles.Update(dir, null, "Phone created", "#E5707A")!;
        var createdId = created.Id;
        check(
            Directory.Exists(Path.Combine(dir, "profiles", createdId)) && MobileProfiles.Active(dir).Id == "default",
            "POST profiles policy creates the folder without switching the active PC profile"
        );
        MobileProfiles.Update(dir, createdId, "Renamed", "#5FD0C9");
        check(
            Profiles.Load(dir).Profiles.Single(p => p.Id == createdId).Name == "Renamed",
            "Phone profile rename is persisted for the PC profiles UI"
        );
        check(
            host.UnlockProfile(token, second.Id, "1234") == 200,
            "Existing PBKDF2 profile PIN unlocks the profile for a paired phone"
        );
        for (int n = 0; n < 5; n++)
            check(
                host.UnlockProfile(token, second.Id, "wrong") == 403,
                "Incorrect profile PIN is rejected ? attempt " + (n + 1)
            );
        check(
            host.UnlockProfile(token, second.Id, "1234") == 429,
            "Profile unlock failures are rate limited before checking further PINs"
        );
        var wrong = Op("review");
        wrong.Rating = 5;
        var conflict = host.SyncProfile(second.Id, [wrong]);
        check(
            conflict.Status == 409
                && conflict.Library == null
                && conflict.Acknowledged.Count == 0
                && !store.State.MobileReceipts.Contains(wrong.Id)
                && !store.State.PersonalReviews.ContainsKey(item.Id),
            "Profile mismatch returns 409 and applies nothing, including receipts"
        );
        check(
            host.SyncProfile("default", [wrong], "another-installation").Status == 409
                && !store.State.MobileReceipts.Contains(wrong.Id),
            "A different PC host identity is rejected before applying phone changes"
        );
        check(
            host.SyncProfile("", []).Status == 200,
            "Old phones without a profile header still sync using the active PC profile"
        );
        var id = store.MobileSnapshot().HostId;
        var otherStore = new LibraryStore(Path.Combine(dir, "profiles", second.Id));
        await using var other = new MobileSyncServer(
            otherStore,
            otherStore.MobileSnapshot,
            otherStore.MergeMobile,
            49279,
            ephemeralCertificate: true
        );
        check(
            other.Pin == host.Pin && otherStore.MobileSnapshot().HostId == id,
            "PC profiles share one stable host identity and TLS certificate"
        );
        check(other.Authorized(token), "Migrated peer tokens remain authorized after changing PC profiles");
        other.Revoke();
        await using var revoked = new MobileSyncServer(
            store,
            store.MobileSnapshot,
            store.MergeMobile,
            49279,
            ephemeralCertificate: true
        );
        check(!revoked.Authorized(token), "One-time peer migration never restores a revoked legacy pairing");
    }
}
