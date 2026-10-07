using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventorySystemApp.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string Email { get; set; } = null!;

        // ---------- Brute-force lockout tracking ----------
        /// <summary>Number of consecutive failed login attempts since the last success.</summary>
        [BsonDefaultValue(0)]
        public int FailedAttempts { get; set; } = 0;

        /// <summary>
        /// Until when this account is temporarily locked out (UTC).
        /// NULL means "not locked out right now".
        /// </summary>
        [BsonIgnoreIfNull]
        public DateTime? LockoutUntilUtc { get; set; }
    }
}
