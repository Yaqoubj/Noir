using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noir;

static class ProfileTransportChecks
{
    public static async Task Run(HttpClient client, LibraryStore store, Action<bool, string> check)
    {
        var profiles = await client.GetStringAsync("/v1/profiles");
        check(
            !profiles.Contains("pinHash", StringComparison.OrdinalIgnoreCase)
                && JsonDocument.Parse(profiles).RootElement.ValueKind == JsonValueKind.Array,
            "Profile HTTP list is an array without local PIN hashes"
        );
        var create = await client.PostAsJsonAsync(
            "/v1/profiles",
            new { name = "Phone profile fixture", color = "#7AA7FF" }
        );
        var p = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = p.GetProperty("id").GetString()!;
        check(
            create.IsSuccessStatusCode
                && Directory.Exists(Path.Combine(MobileProfiles.Root(store.DataDirectory), "profiles", id)),
            "Profile HTTP create writes a separate PC profile folder"
        );
        var rename = await client.PostAsJsonAsync(
            "/v1/profiles/" + id,
            new { name = "Renamed from phone", color = "#5FD0C9" }
        );
        check(
            rename.IsSuccessStatusCode
                && Profiles
                    .Load(MobileProfiles.Root(store.DataDirectory))
                    .Profiles.Any(p => p.Id == id && p.Name == "Renamed from phone"),
            "Profile HTTP rename reaches profiles.json"
        );
        var list = Profiles.Load(MobileProfiles.Root(store.DataDirectory));
        var locked = Profiles.Create(list, "Locked phone fixture", "#E5707A", MobileProfiles.Root(store.DataDirectory));
        locked.PinHash = Profiles.HashPin(locked, "2468");
        Profiles.Save(list, MobileProfiles.Root(store.DataDirectory));
        check(
            (
                await client.PostAsJsonAsync(
                    "/v1/profiles/" + locked.Id,
                    new { name = "Not allowed", color = "#7AA7FF" }
                )
            ).StatusCode == HttpStatusCode.Forbidden,
            "HTTP locked-profile rename requires an unlock"
        );
        check(
            (
                await client.PostAsJsonAsync("/v1/profiles/" + locked.Id + "/unlock", new { pin = "2468" })
            ).IsSuccessStatusCode,
            "HTTP unlock validates the PC profile PBKDF2 PIN"
        );
        for (int n = 0; n < 5; n++)
            await client.PostAsJsonAsync("/v1/profiles/" + locked.Id + "/unlock", new { pin = "incorrect" });
        check(
            (await client.PostAsJsonAsync("/v1/profiles/" + locked.Id + "/unlock", new { pin = "2468" })).StatusCode
                == HttpStatusCode.TooManyRequests,
            "HTTP unlock rate limit rejects further guesses"
        );
        client.DefaultRequestHeaders.Add("X-Noir-Profile", id);
        var before = store.State.MobileReceipts.Count;
        var operation = new MobileOperation
        {
            Id = Guid.NewGuid().ToString(),
            ItemId = store.Archive.First().Id,
            Kind = "review",
            Rating = 5,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        var mismatch = await client.PostAsJsonAsync("/v1/sync", new[] { operation });
        var detail = await mismatch.Content.ReadFromJsonAsync<JsonElement>();
        check(
            mismatch.StatusCode == HttpStatusCode.Conflict
                && detail.GetProperty("activeProfileId").GetString() == MobileProfiles.Active(store.DataDirectory).Id
                && store.State.MobileReceipts.Count == before,
            "HTTP profile mismatch returns active profile details and merges nothing"
        );
        client.DefaultRequestHeaders.Remove("X-Noir-Profile");
        check(
            (await client.PostAsJsonAsync("/v1/sync", Array.Empty<MobileOperation>())).IsSuccessStatusCode,
            "Legacy HTTP clients without a profile header continue syncing"
        );
    }
}
