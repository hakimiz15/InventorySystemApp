using InventorySystemApp.Models;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorySystemApp.Data
{
    public class MongoDBService
    {
        private readonly IMongoCollection<User> _usersCollection;
        private readonly IMongoCollection<LoginLog> _loginLogsCollection;
        private readonly IMongoCollection<ItemCatalog> _itemCatalogCollection;
        private readonly IMongoCollection<Cabinet> _cabinetsCollection;
        private readonly IMongoCollection<Block> _blocksCollection;
        private readonly string _hmacSecret = "change-me-super-secret"; // move to appsettings.json in production

        public MongoDBService(IConfiguration configuration)
        {
            var client = new MongoClient(configuration.GetValue<string>("MongoDB:ConnectionString"));
            var database = client.GetDatabase(configuration.GetValue<string>("MongoDB:DatabaseName"));

            _usersCollection = database.GetCollection<User>("User");
            _loginLogsCollection = database.GetCollection<LoginLog>("LoginLogs");
            _itemCatalogCollection = database.GetCollection<ItemCatalog>("ItemCatalog");
            _cabinetsCollection = database.GetCollection<Cabinet>("Cabinets");
            _blocksCollection = database.GetCollection<Block>("Blocks");

            EnsureGenesisBlock();
        }

        // ✅ Ensure genesis block exists
        private void EnsureGenesisBlock()
        {
            var existing = _blocksCollection.Find(FilterDefinition<Block>.Empty).FirstOrDefault();
            if (existing != null) return;

            var payload = new BsonDocument { { "message", "Genesis Block - Blockchain initialized" } };
            var now = DateTime.UtcNow;

            var hash = Block.ComputeHash(0L, now, "GENESIS", payload, string.Empty);

            var genesis = new Block
            {
                Index = 0,
                TimestampUtc = now,
                EventType = "GENESIS",
                Payload = payload,
                PrevHash = string.Empty,
                Hash = hash,
                Signature = Block.ComputeHmac(hash, _hmacSecret)
            };

            _blocksCollection.InsertOne(genesis);
        }

        // -------------------- USER MANAGEMENT --------------------
        public void AddUser(User user) => _usersCollection.InsertOne(user);
        public void RemoveUser(string username) => _usersCollection.DeleteOne(u => u.Username == username);
        public User? GetUserByUsername(string username) => _usersCollection.Find(u => u.Username == username).FirstOrDefault();
        public List<User> GetAllUsers() => _usersCollection.Find(_ => true).ToList();
        public List<LoginLog> GetLoginLogs() => _loginLogsCollection.Find(_ => true).ToList();

        // ✅ User Authentication
        public User? AuthenticateUser(string username, string password)
        {
            var user = _usersCollection.Find(u => u.Username == username).FirstOrDefault();
            if (user != null && user.PasswordHash == password)
            {
                _loginLogsCollection.InsertOne(new LoginLog
                {
                    Username = username,
                    Timestamp = DateTime.UtcNow,
                    Success = true
                });
                return user;
            }

            _loginLogsCollection.InsertOne(new LoginLog
            {
                Username = username,
                Timestamp = DateTime.UtcNow,
                Success = false
            });

            return null;
        }

        // -------------------- ITEM CATALOG --------------------
        public ItemCatalog? GetItemCatalogByCode(string itemCode)
            => _itemCatalogCollection.Find(i => i.ItemCode == itemCode).FirstOrDefault();

        public void SeedCatalog(List<ItemCatalog> items)
            => _itemCatalogCollection.InsertMany(items);

        // -------------------- CABINETS --------------------
        public List<Cabinet> GetAllCabinets() => _cabinetsCollection.Find(_ => true).ToList();

        public void RecalculateCabinetUsage(string cabinetId)
        {
            var cabinet = _cabinetsCollection.Find(c => c.Id == cabinetId).FirstOrDefault();
            if (cabinet == null) return;

            cabinet.UsedSpace = cabinet.Items.Sum(i => i.Quantity);
            cabinet.IsOccupied = cabinet.UsedSpace > 0;
            _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);
        }

        private static int ParseCabNum(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return int.MaxValue;
            return int.TryParse(s, out var n) ? n : int.MaxValue;
        }

        private void SaveCabinet(Cabinet cabinet)
        {
            cabinet.IsOccupied = cabinet.UsedSpace > 0;
            _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);
            if (!string.IsNullOrEmpty(cabinet.Id))
                RecalculateCabinetUsage(cabinet.Id);
        }

        // ✅ Place Item with user tracking
        public (bool success, string cabinetNumber) PlaceItemInCabinet(string itemCode, int quantity, string executedBy)

        {
            var catalogItem = GetItemCatalogByCode(itemCode);
            if (catalogItem == null) return (false, "");

            var targetCategory = catalogItem.Category;
            var remaining = quantity;

            var cabinetPriority = new List<string>
            {
                "Running", "Mountain", "Fitness",
                "Watersport", "Kids education",
                "Wheelsport", "Teamsport"
            };

            // Step 1: Same-category with item
            var sameCatWithItem = _cabinetsCollection
                .Find(c => c.Category == targetCategory && (c.Capacity - c.UsedSpace) > 0 && c.Items.Any(i => i.ItemCode == itemCode))
                .ToList()
                .OrderBy(c => ParseCabNum(c.CabinetNumber));

            foreach (var cab in sameCatWithItem)
            {
                var free = cab.Capacity - cab.UsedSpace;
                if (free <= 0) continue;
                var toAdd = Math.Min(remaining, free);

                var existing = cab.Items.First(i => i.ItemCode == itemCode);
                existing.Quantity += toAdd;
                cab.UsedSpace += toAdd;
                SaveCabinet(cab);

                remaining -= toAdd;
                if (remaining <= 0)
                {
                    AppendBlock("AddItem", new BsonDocument
                    {
                        { "itemCode", itemCode },
                        { "quantity", quantity },
                        { "cabinetNumber", cab.CabinetNumber },
                        { "category", cab.Category },
                        { "action", "added" }
                    }, executedBy);
                    return (true, cab.CabinetNumber);
                }
            }

            // Step 2: Same-category new item
            var sameCatNoItem = _cabinetsCollection
                .Find(c => c.Category == targetCategory && (c.Capacity - c.UsedSpace) > 0 && !c.Items.Any(i => i.ItemCode == itemCode))
                .ToList()
                .OrderBy(c => ParseCabNum(c.CabinetNumber));

            foreach (var cab in sameCatNoItem)
            {
                var free = cab.Capacity - cab.UsedSpace;
                if (free <= 0) continue;
                var toAdd = Math.Min(remaining, free);

                cab.Items.Add(new StoredItem { ItemCode = itemCode, Quantity = toAdd });
                cab.UsedSpace += toAdd;
                SaveCabinet(cab);
                remaining -= toAdd;

                AppendBlock("AddItem", new BsonDocument
                {
                    { "itemCode", itemCode },
                    { "quantity", toAdd },
                    { "cabinetNumber", cab.CabinetNumber },
                    { "category", cab.Category },
                    { "action", "added" }
                }, executedBy);

                if (remaining <= 0) return (true, cab.CabinetNumber);
            }

            // Step 3: Overflow
            foreach (var cat in cabinetPriority.Where(c => c != targetCategory))
            {
                if (remaining <= 0) break;

                var otherCatCabs = _cabinetsCollection
                    .Find(c => c.Category == cat && (c.Capacity - c.UsedSpace) > 0)
                    .ToList()
                    .OrderBy(c => ParseCabNum(c.CabinetNumber));

                foreach (var cab in otherCatCabs)
                {
                    var free = cab.Capacity - cab.UsedSpace;
                    if (free <= 0) continue;
                    var toAdd = Math.Min(remaining, free);

                    var existing = cab.Items.FirstOrDefault(i => i.ItemCode == itemCode);
                    if (existing != null) existing.Quantity += toAdd;
                    else cab.Items.Add(new StoredItem { ItemCode = itemCode, Quantity = toAdd });

                    cab.UsedSpace += toAdd;
                    SaveCabinet(cab);

                    AppendBlock("AddItem", new BsonDocument
                    {
                        { "itemCode", itemCode },
                        { "quantity", toAdd },
                        { "cabinetNumber", cab.CabinetNumber },
                        { "category", cab.Category },
                        { "action", "added-overflow" }
                    }, executedBy);

                    remaining -= toAdd;
                    if (remaining <= 0) return (true, cab.CabinetNumber);
                }
            }

            if (remaining < quantity)
            {
                AppendBlock("AddItem", new BsonDocument
                {
                    { "itemCode", itemCode },
                    { "quantityPlaced", quantity - remaining },
                    { "quantityUnplaced", remaining },
                    { "status", "partial" }
                }, executedBy);
            }

            return (remaining < quantity, "");   // because we cannot guarantee the last cabinet for partial

        }

        // ✅ Remove Item with user tracking
        public bool RemoveItemFromCabinet(string itemCode, string cabinetNumber, int quantity, string executedBy)
        {
            var cabinet = _cabinetsCollection.Find(c => c.CabinetNumber == cabinetNumber).FirstOrDefault();
            if (cabinet == null || cabinet.Items == null || !cabinet.Items.Any()) return false;

            var existingItem = cabinet.Items.FirstOrDefault(i => i.ItemCode == itemCode);
            if (existingItem == null) return false;

            int removedQuantity = Math.Min(existingItem.Quantity, quantity);

            if (removedQuantity == existingItem.Quantity)
                cabinet.Items.Remove(existingItem);
            else
                existingItem.Quantity -= removedQuantity;

            cabinet.UsedSpace -= removedQuantity;
            cabinet.IsOccupied = cabinet.UsedSpace > 0;
            _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);
            if (!string.IsNullOrEmpty(cabinet.Id))
                RecalculateCabinetUsage(cabinet.Id);

            AppendBlock("RemoveItem", new BsonDocument
            {
                { "itemCode", itemCode },
                { "cabinetNumber", cabinetNumber },
                { "quantityRemoved", removedQuantity },
                { "remainingInCabinet", cabinet.UsedSpace },
                { "category", cabinet.Category },
                { "action", "removed" }
            }, executedBy);

            return true;
        }

        // -------------------- BLOCKCHAIN CORE --------------------
        public Block? GetLastBlock()
        {
            return _blocksCollection.Find(FilterDefinition<Block>.Empty)
                .SortByDescending(b => b.Index)
                .Limit(1)
                .FirstOrDefault();
        }

        public Block AppendBlock(string eventType, BsonDocument payload, string executedBy)
        {
            var last = GetLastBlock();
            var index = (last?.Index ?? -1) + 1;
            var prevHash = last?.Hash ?? string.Empty;
            var now = DateTime.UtcNow;

            if (!payload.Contains("executedBy"))
                payload.Add("executedBy", executedBy);

            var hash = Block.ComputeHash(index, now, eventType, payload, prevHash);

            var block = new Block
            {
                Index = index,
                TimestampUtc = now,
                EventType = eventType,
                Payload = payload,
                PrevHash = prevHash,
                Hash = hash,
                Signature = Block.ComputeHmac(hash, _hmacSecret)
            };

            _blocksCollection.InsertOne(block);
            return block;
        }

        public (bool ok, string? errorAt) VerifyChain()
        {
            var all = _blocksCollection.Find(FilterDefinition<Block>.Empty)
                                       .SortBy(b => b.Index)
                                       .ToList();
            if (all.Count == 0) return (true, null);

            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                var expected = Block.ComputeHash(b.Index, b.TimestampUtc, b.EventType, b.Payload, b.PrevHash);

                if (!string.Equals(expected, b.Hash, StringComparison.OrdinalIgnoreCase))
                    return (false, $"Hash mismatch at block index {b.Index}");

                if (i > 0 && all[i - 1].Hash != b.PrevHash)
                    return (false, $"Previous hash mismatch at block index {b.Index}");

                var sig = Block.ComputeHmac(b.Hash, _hmacSecret);
                if (!string.Equals(sig, b.Signature, StringComparison.OrdinalIgnoreCase))
                    return (false, $"Signature mismatch at block index {b.Index}");
            }

            return (true, null);
        }

        public List<Block> GetBlocks()
        {
            return _blocksCollection.Find(FilterDefinition<Block>.Empty)
                                    .SortByDescending(b => b.Index)
                                    .ToList();
        }
    }

    // -------------------- MODELS --------------------
    public class LoginLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string Username { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
    }
}
