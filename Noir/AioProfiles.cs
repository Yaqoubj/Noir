using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Noir;
public static class AioProfiles
{
    public static JsonObject DownloadConfig(string export)
    {
        var root=JsonNode.Parse(export) as JsonObject??throw new IOException("Choose an AIOStreams configuration export.");
        var config=(root["config"] as JsonObject??root["userData"] as JsonObject??root).DeepClone().AsObject();
        if(config["presets"] is not JsonArray presets||config["services"] is not JsonArray)throw new IOException("This file doesn't contain an AIOStreams profile.");
        config.Remove("uuid");config.Remove("trusted");
        foreach(var entry in presets.Where(p=>p?["type"]?.ToString()=="torbox-search").ToList())presets.Remove(entry);
        config["formatter"]=new JsonObject{["id"]="custom",["definitions"]=new JsonObject{["custom"]=new JsonObject{
            ["name"]="{stream.resolution} {stream.quality}",
            ["description"]="{stream.filename}\n[NOIR_HASH:{stream.infoHash}]\n[NOIR_SOURCE:{stream.indexer}]\n[NOIR_AUDIO:{stream.languages::join(' / ')}]"}}};
        return config;
    }
    public static async Task<string> Create(string manifestUrl,string export,CancellationToken ct)
    {
        var manifest=TorboxProvider.Manifest(manifestUrl);var config=DownloadConfig(export);
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(60)};
        var payload=new JsonObject{["config"]=config,["password"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))};
        using var body=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await http.PostAsync(new UriBuilder(manifest){Path="/api/v1/user",Query=""}.Uri,body,ct);
        if(!response.IsSuccessStatusCode)throw new IOException($"The AIOStreams host couldn't create the separate profile ({(int)response.StatusCode}). Your original profile is unchanged.");
        var root=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var uuid=root?["data"]?["uuid"]?.ToString();var password=root?["data"]?["encryptedPassword"]?.ToString();
        if(!Guid.TryParse(uuid,out _)||string.IsNullOrWhiteSpace(password))throw new IOException("The host didn't confirm the profile. Your original profile is unchanged.");
        return new UriBuilder(manifest){Path="/stremio/"+uuid+"/"+Uri.EscapeDataString(password)+"/manifest.json",Query=""}.Uri.AbsoluteUri;
    }
}
