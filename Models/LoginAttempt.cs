using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventorySystemApp.Models
{
    public class LoginAttempt
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string Username { get; set; } = null!;
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
        public string? IpAddress { get; set; }
    }
}
