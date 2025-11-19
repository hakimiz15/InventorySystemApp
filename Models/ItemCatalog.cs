using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventorySystemApp.Models
{
    public class ItemCatalog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("ItemCode")]
        public string ItemCode { get; set; } = string.Empty;

        [BsonElement("ItemName")]
        public string ItemName { get; set; } = string.Empty;

        [BsonElement("Category")]
        public string Category { get; set; } = string.Empty;
    }
}
