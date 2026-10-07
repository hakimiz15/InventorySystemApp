using InventorySystemApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorySystemApp.Data
{
    /// <summary>Result of an <see cref="MongoDBService.AttemptLogin"/> call, includes lockout info.</summary>
    public class LoginAttemptResult
    {
        public User? User { get; set; }
        public bool IsLockedOut { get; set; }
        public bool IsSuccess => User != null && !IsLockedOut;
        public int FailedAttempts { get; set; }
        public DateTime? LockoutUntilUtc { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class MongoDBService
    {
        // ---------- Brute force lockout thresholds (easy to tune) ----------
        public const int MAX_FAILED_ATTEMPTS_BEFORE_LOCKOUT = 3;
        public static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(5);
        public const int REMAINING_ATTEMPTS_WARNING_AT = 1; // warn user once they have 1 attempt left

        private readonly IMongoCollection<User> _usersCollection;
        private readonly IMongoCollection<LoginLog> _loginLogsCollection;
        private readonly IMongoCollection<ItemCatalog> _itemCatalogCollection;
        private readonly IMongoCollection<Cabinet> _cabinetsCollection;
        private readonly IPasswordHasher<User> _passwordHasher;

        public MongoDBService(IConfiguration configuration, IPasswordHasher<User> passwordHasher)
        {
            var client = new MongoClient(configuration.GetValue<string>("MongoDB:ConnectionString"));
            var database = client.GetDatabase(configuration.GetValue<string>("MongoDB:DatabaseName"));

            _usersCollection = database.GetCollection<User>("User");
            _loginLogsCollection = database.GetCollection<LoginLog>("LoginLogs");
            _itemCatalogCollection = database.GetCollection<ItemCatalog>("ItemCatalog");
            _cabinetsCollection = database.GetCollection<Cabinet>("Cabinets");
            _passwordHasher = passwordHasher;
        }

        // -------------------- USER AUTH + LOCKOUT HELPERS --------------------

        public User? GetUserByUsername(string username) =>
            _usersCollection.Find(u => u.Username == username).FirstOrDefault();

        /// <summary>
        /// Returns true if the user is currently locked out, along with the expiry UTC.
        /// Also auto-clears an expired lockout so stale state never blocks logins.
        /// </summary>
        public (bool IsLocked, DateTime? LockoutUntil) GetLockoutStatus(string username)
        {
            var user = GetUserByUsername(username);
            if (user == null) return (false, null);
            return GetLockoutStatus(user);
        }

        private (bool IsLocked, DateTime? LockoutUntil) GetLockoutStatus(User user)
        {
            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return (true, user.LockoutUntilUtc.Value);
            }

            // Expired lockout — clear it now (and reset failed counter at the same time)
            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value <= DateTime.UtcNow)
            {
                user.LockoutUntilUtc = null;
                user.FailedAttempts = 0;
                _usersCollection.ReplaceOne(u => u.Id == user.Id, user);
            }
            else if (user.FailedAttempts >= MAX_FAILED_ATTEMPTS_BEFORE_LOCKOUT)
            {
                // Reached max attempts but LockoutUntil isn't set (stale legacy data)
                // — place a fresh lock now so the block is actually enforced.
                user.LockoutUntilUtc = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                _usersCollection.ReplaceOne(u => u.Id == user.Id, user);
                return (true, user.LockoutUntilUtc.Value);
            }

            return (false, null);
        }

        /// <summary>
        /// Main entry point: performs the full login workflow with lockout enforcement.
        /// Never actually verifies a password while the account is locked.
        /// </summary>
        public LoginAttemptResult AttemptLogin(string username, string password)
        {
            var log = new LoginLog
            {
                Username = username,
                Timestamp = DateTime.UtcNow,
                Success = false
            };

            try
            {
                var user = GetUserByUsername(username);

                // --- Case 1: User doesn't exist at all (prevent enumeration leaks, still
                // return a generic "invalid credentials" message). We don't increment
                // failed attempts for usernames that don't exist (avoids bloating the DB
                // and locking an account that literally doesn't exist).
                if (user == null)
                {
                    log.Success = false;
                    return new LoginAttemptResult
                    {
                        IsLockedOut = false,
                        FailedAttempts = 0,
                        Message = "Invalid username or password."
                    };
                }

                // --- Case 2: Account is currently locked out. Do NOT run the password
                // hasher / verifier here (saves CPU, prevents leak of info).
                var (isLocked, lockUntil) = GetLockoutStatus(user);
                if (isLocked)
                {
                    return new LoginAttemptResult
                    {
                        IsLockedOut = true,
                        FailedAttempts = user.FailedAttempts,
                        LockoutUntilUtc = lockUntil,
                        Message = "Account is temporarily locked due to too many failed attempts."
                    };
                }

                // --- Case 3: Account is unlocked — run the password verifier.
                var pwdOk = VerifyPassword(user, password, out var needsRehash);
                if (needsRehash && pwdOk)
                {
                    user.PasswordHash = _passwordHasher.HashPassword(user, password!);
                    _usersCollection.ReplaceOne(u => u.Id == user.Id, user);
                }

                if (pwdOk)
                {
                    // --- Case 3a: Success. Reset the consecutive-fail counter and any pending lock.
                    ResetFailedAttempts(user);
                    log.Success = true;
                    return new LoginAttemptResult
                    {
                        User = user,
                        IsLockedOut = false,
                        FailedAttempts = 0,
                        Message = "Login successful."
                    };
                }

                // --- Case 3b: Wrong password. Increment consecutive failures + maybe lock.
                var newAttempts = IncrementFailedAttempts(user);
                var nowLocked = newAttempts >= MAX_FAILED_ATTEMPTS_BEFORE_LOCKOUT;

                if (nowLocked)
                {
                    user.LockoutUntilUtc = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                    _usersCollection.ReplaceOne(u => u.Id == user.Id, user);

                    return new LoginAttemptResult
                    {
                        IsLockedOut = true,
                        FailedAttempts = newAttempts,
                        LockoutUntilUtc = user.LockoutUntilUtc,
                        Message = "Too many failed attempts — account temporarily locked."
                    };
                }

                return new LoginAttemptResult
                {
                    IsLockedOut = false,
                    FailedAttempts = newAttempts,
                    Message = "Invalid username or password."
                };
            }
            finally
            {
                // Always log the attempt (success/fail/locked-out) for audit trail.
                _loginLogsCollection.InsertOne(log);
            }
        }

        /// <summary>
        /// Original API preserved for back-compat with any existing callers.
        /// Simply delegates to AttemptLogin and returns the User on success, else null.
        /// </summary>
        public User? AuthenticateUser(string username, string password)
        {
            var result = AttemptLogin(username, password);
            return result.IsSuccess ? result.User : null;
        }

        private bool VerifyPassword(User user, string password, out bool needsRehash)
        {
            needsRehash = false;
            if (user == null) return false;

            PasswordVerificationResult result;
            try
            {
                result = _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash ?? string.Empty,
                    password ?? string.Empty);
            }
            catch (FormatException)
            {
                // --- Legacy plain-text fallback (catch #1) ---
                // ASP.NET Identity's VerifyHashedPassword tries to Base64-decode the
                // stored "hash". If the stored password is still plain text (a user you
                // manually inserted into MongoDB before password hashing was added),
                // that string is not valid Base64/PBKDF2 data, so VerifyHashedPassword
                // throws FormatException. Treat this case as "not a valid hash" and
                // fall through to the plain-text equality check below so legacy users
                // can still log in (and have their password auto-hashed immediately after).
                result = PasswordVerificationResult.Failed;
            }
            catch (Exception)
            {
                // Safety net: anything else Identity throws (bad format, crypto fault, etc.)
                // → treat as verify-failed. We never want a 500 on login.
                result = PasswordVerificationResult.Failed;
            }

            if (result == PasswordVerificationResult.Success) return true;
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                needsRehash = true;
                return true;
            }

            // Legacy plain-text fallback (auto-upgrade to hash on success)
            if (string.Equals(user.PasswordHash, password, StringComparison.Ordinal))
            {
                needsRehash = true;
                return true;
            }

            return false;
        }

        private int IncrementFailedAttempts(User user)
        {
            user.FailedAttempts = (user.FailedAttempts < 0 ? 0 : user.FailedAttempts) + 1;
            _usersCollection.ReplaceOne(u => u.Id == user.Id, user);
            return user.FailedAttempts;
        }

        private void ResetFailedAttempts(User user)
        {
            if (user.FailedAttempts == 0 && user.LockoutUntilUtc == null) return;
            user.FailedAttempts = 0;
            user.LockoutUntilUtc = null;
            _usersCollection.ReplaceOne(u => u.Id == user.Id, user);
        }

        public void AddUser(User user)
        {
            // Always hash the password before writing to the database.
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                user.PasswordHash = string.Empty;

            user.PasswordHash = _passwordHasher.HashPassword(user, user.PasswordHash);

            // New users start clean (explicit defaults for good measure)
            user.FailedAttempts = 0;
            user.LockoutUntilUtc = null;

            _usersCollection.InsertOne(user);
        }

        public void RemoveUser(string username) => _usersCollection.DeleteOne(u => u.Username == username);
        public List<User> GetAllUsers() => _usersCollection.Find(_ => true).ToList();
        public List<LoginLog> GetLoginLogs() => _loginLogsCollection.Find(_ => true).ToList();

        // -------------------- CATALOG --------------------
        public ItemCatalog? GetItemCatalogByCode(string itemCode)
        {
            return _itemCatalogCollection.Find(i => i.ItemCode == itemCode).FirstOrDefault();
        }

        public void SeedCatalog(List<ItemCatalog> items)
        {
            _itemCatalogCollection.InsertMany(items);
        }

        // -------------------- CABINETS --------------------
        public List<Cabinet> GetAllCabinets()
        {
            return _cabinetsCollection.Find(_ => true).ToList();
        }

        public List<Cabinet> SearchCabinetsByItem(string searchQuery)
        {
            var all = GetAllCabinets();

            if (string.IsNullOrWhiteSpace(searchQuery))
                return all;

            var q = searchQuery.Trim();

            return all
                .Where(c =>
                    // match on cabinet metadata
                    !string.IsNullOrEmpty(c.CabinetNumber) && c.CabinetNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrEmpty(c.Category) && c.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    // match on items inside the cabinet
                    c.Items != null && c.Items.Any(i =>
                        !string.IsNullOrEmpty(i.ItemCode) && i.ItemCode.Contains(q, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();
        }

        public int GetTotalItemQuantityAcrossCabinets(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode)) return 0;
            var q = itemCode.Trim();
            return GetAllCabinets()
                .SelectMany(c => c.Items ?? [])
                .Where(i => string.Equals(i.ItemCode, q, StringComparison.OrdinalIgnoreCase))
                .Sum(i => i.Quantity);
        }

        public void RecalculateCabinetUsage(string cabinetId)
        {
            var cabinet = _cabinetsCollection.Find(c => c.Id == cabinetId).FirstOrDefault();
            if (cabinet == null) return;

            cabinet.UsedSpace = cabinet.Items.Sum(i => i.Quantity);
            cabinet.IsOccupied = cabinet.UsedSpace > 0;

            _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);
        }

        // ✅ Multi-cabinet distribution logic
        // Utility: numeric sort for string cabinet numbers like "1","2","10"
private static int ParseCabNum(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return int.MaxValue;
    return int.TryParse(s, out var n) ? n : int.MaxValue;
}

// Utility: persist + keep derived fields correct
private void SaveCabinet(Cabinet cabinet)
{
    cabinet.IsOccupied = cabinet.UsedSpace > 0;
    _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);
    if (!string.IsNullOrEmpty(cabinet.Id))
        RecalculateCabinetUsage(cabinet.Id);
}

public bool PlaceItemInCabinet(string itemCode, int quantity)
{
    var catalogItem = GetItemCatalogByCode(itemCode);
    if (catalogItem == null) return false;

    var targetCategory = catalogItem.Category;
    var remaining = quantity;

    // Global priority if overflow is allowed
    var cabinetPriority = new[]
    {
        "Running", "Mountain", "Fitness",
        "Watersport", "Kids education",
        "Wheelsport", "Teamsport"
    };

    // ---------- 1) Same-category cabinets that ALREADY contain this item ----------
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
        if (remaining <= 0) return true;
    }

    // ---------- 2) Same-category cabinets that DON'T yet contain this item ----------
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
        if (remaining <= 0) return true;
    }
    // ---------- 3) (Optional) Overflow into other categories by priority ----------
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

            remaining -= toAdd;
            if (remaining <= 0) return true;
        }
    }
    // Return true if at least partially placed
    return remaining < quantity;
}

        // ✅ Remove item from cabinet (used by AdminController)
public bool RemoveItemFromCabinet(string itemCode, string cabinetNumber, int quantity)
{
    var cabinet = _cabinetsCollection.Find(c => c.CabinetNumber == cabinetNumber).FirstOrDefault();
    if (cabinet == null || cabinet.Items == null || cabinet.Items.Count == 0)
        return false; // cabinet not found or empty

    var existingItem = cabinet.Items.FirstOrDefault(i => i.ItemCode == itemCode);
    if (existingItem == null)
        return false; // item not found in this cabinet

    if (quantity >= existingItem.Quantity)
    {
        // Remove entire item entry if quantity fully used up
        cabinet.Items.Remove(existingItem);
        cabinet.UsedSpace -= existingItem.Quantity;
    }
    else
    {
        // Reduce only part of it
        existingItem.Quantity -= quantity;
        cabinet.UsedSpace -= quantity;
    }

    // Update occupancy
    cabinet.IsOccupied = cabinet.UsedSpace > 0;

            // Save changes
            _cabinetsCollection.ReplaceOne(c => c.Id == cabinet.Id, cabinet);

    // Recalculate just to be safe
    if (!string.IsNullOrEmpty(cabinet.Id))
    RecalculateCabinetUsage(cabinet.Id);

    return true;
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
