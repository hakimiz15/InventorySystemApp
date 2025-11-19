using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace InventorySystemApp.Models
{
    public class Cabinet
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string CabinetNumber { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        public int Capacity { get; set; } = 100; // default max capacity
        public int UsedSpace { get; set; } = 0;  // amount used

        // 🟩 Fix: Add this to prevent FormatException
        public bool IsOccupied { get; set; } = false;

        // Optional: store current items
        public List<StoredItem> Items { get; set; } = new List<StoredItem>();

    }

    public class StoredItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public int Quantity { get; set; } = 0;
    }
}
