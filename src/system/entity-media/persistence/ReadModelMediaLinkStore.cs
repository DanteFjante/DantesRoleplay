using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Media;

public sealed class ReadModelMediaLinkStore(TimeProvider? clock = null) : IReadModelMediaLinkStore
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private readonly Dictionary<string, string> keys = [];
    private sealed record Entry(string Key, ReadModelMediaTicket Ticket, DateTimeOffset Expires);
    public static string Url(string token) => "/api/read-model-media/" + token + "/content";
    public static string Fingerprint(EntityMediaAttachment attachment) => Hash(JsonSerializer.Serialize(attachment));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string GetOrCreate(ReadModelMediaTicket ticket)
    {
        var key = Hash(JsonSerializer.Serialize(ticket));
        lock (gate)
        {
            if (keys.TryGetValue(key, out var found) && entries[found].Expires > time.GetUtcNow()) return Url(found);
            foreach (var token in entries.Where(pair => pair.Value.Expires <= time.GetUtcNow()).Select(pair => pair.Key).ToArray()) Remove(token);
            if (entries.Count >= 4096) Remove(entries.MinBy(pair => pair.Value.Expires).Key);
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            entries[id] = new(key, ticket with { Request = ticket.Request with
                { RoleBindings = ticket.Request.RoleBindings.ToDictionary() } }, time.GetUtcNow().AddMinutes(10));
            keys[key] = id;
            return Url(id);
        }
    }

    public ReadModelMediaTicket? Find(string token)
    {
        if (token.Length != 64 || token.Any(value => value is not (>= 'a' and <= 'f') and not (>= '0' and <= '9'))) return null;
        lock (gate)
        {
            if (!entries.TryGetValue(token, out var entry)) return null;
            if (entry.Expires <= time.GetUtcNow()) { Remove(token); return null; }
            return entry.Ticket;
        }
    }

    private void Remove(string token) { keys.Remove(entries[token].Key); entries.Remove(token); }
}
