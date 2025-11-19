using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Security.Cryptography;
using System.Text;

namespace InventorySystemApp.Models
{
    public class Block
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public long Index { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = string.Empty;
        public BsonDocument Payload { get; set; } = new();
        public string PrevHash { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;

        // ✅ Deterministic SHA-256 hashing using canonical JSON
        public static string ComputeHash(long index, DateTime timestamp, string eventType, BsonDocument payload, string prevHash)
{
    // Normalize BSON serialization for consistency
    var canonicalPayload = payload.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson });

    var raw = $"{index}-{timestamp:O}-{eventType}-{canonicalPayload}-{prevHash}";
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
    var hashBytes = sha256.ComputeHash(bytes);
    return BitConverter.ToString(hashBytes).Replace("-", "");
}


        public static string ComputeHmac(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(bytes).Replace("-", "").ToUpperInvariant();
        }
    }
}
