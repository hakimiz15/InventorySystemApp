using InventorySystemApp.Data;
using InventorySystemApp.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace InventorySystemApp.Controllers
{
    public class AdminController(MongoDBService mongoService) : Controller
    {
        private readonly MongoDBService _mongoService = mongoService;

        // GET: Admin Dashboard
        public IActionResult Dashboard()
        {
            return View();
        }

        // GET: Add Item
        public IActionResult AddItem()
        {
            return View();
        }

        // POST: Add Item
        [HttpPost]
        public IActionResult AddItem(string itemCode, int quantity)
        {
            bool success = _mongoService.PlaceItemInCabinet(itemCode, quantity);

            if (success)
                TempData["Message"] = $"✅ Item '{itemCode}' successfully placed in an available cabinet.";
            else
                TempData["Message"] = $"⚠️ Failed to place item '{itemCode}'. No suitable cabinet found.";

            return RedirectToAction("AddItem");
        }

        // GET: Remove Item
        public IActionResult RemoveItem()
        {
            return View();
        }

        // POST: Remove Item
        [HttpPost]
public IActionResult RemoveItem(string itemCode, string cabinetNumber, int quantity)
{
    bool result = _mongoService.RemoveItemFromCabinet(itemCode, cabinetNumber, quantity);

    TempData["Message"] = result
        ? $"✅ Item '{itemCode}' successfully removed from Cabinet {cabinetNumber}!"
        : $"❌ Failed to remove item '{itemCode}'. It might not exist or insufficient quantity.";

    return RedirectToAction("RemoveItem");
}

        // GET: View Cabinets (with optional item search)
        public IActionResult ViewCabinet(string search)
        {
            List<Cabinet> cabinets;
            bool isSearch = !string.IsNullOrWhiteSpace(search);

            if (isSearch)
                cabinets = _mongoService.SearchCabinetsByItem(search!);
            else
                cabinets = _mongoService.GetAllCabinets();

            // Aggregate data for the UI summary
            ViewData["SearchQuery"] = search ?? string.Empty;
            ViewData["IsSearch"] = isSearch;

            if (isSearch)
            {
                ViewData["TotalCabinetsWithMatch"] = cabinets.Count;
                ViewData["TotalItemQuantity"] = _mongoService.GetTotalItemQuantityAcrossCabinets(search!);

                var catalogItem = _mongoService.GetItemCatalogByCode(search!.Trim());
                if (catalogItem != null)
                {
                    ViewData["CatalogName"] = catalogItem.ItemName;
                    ViewData["CatalogCategory"] = catalogItem.Category;
                }
            }

            return View(cabinets);
        }

        // GET: Add User
        public IActionResult AddUser()
        {
            return View();
        }

        [HttpPost]
        public IActionResult AddUser(string firstName, string lastName, string email, string username, string password, string role)
        {
            var user = new User
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Username = username,
                PasswordHash = password,
                Role = role
            };

            _mongoService.AddUser(user);
            TempData["Message"] = $"✅ User {username} added successfully!";
            return RedirectToAction("AddUser");
        }

        // GET: Remove User
        public IActionResult RemoveUser(string? username)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                var existing = _mongoService
                    .GetAllUsers()
                    .FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    ViewData["PrefillUsername"] = existing.Username;
                    ViewData["PrefillEmail"]    = existing.Email;
                }
            }

            return View();
        }

        [HttpPost]
        public IActionResult RemoveUser(string username, string email)
        {
            var user = _mongoService.GetAllUsers()
                                    .Find(u => u.Username == username && u.Email == email);

            if (user != null)
            {
                _mongoService.RemoveUser(username);
                TempData["Message"] = $"✅ User '{username}' has been removed successfully!";
            }
            else
            {
                TempData["Message"] = $"⚠️ User '{username}' not found or email mismatch.";
            }

            return RedirectToAction("RemoveUser");
        }

        // GET: View Users
        public IActionResult ViewUsers(string? search)
        {
            var allUsers = _mongoService.GetAllUsers();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                allUsers = allUsers
                    .Where(u =>
                        (!string.IsNullOrEmpty(u.Username) && u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.FirstName) && u.FirstName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.LastName) && u.LastName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.Email) && u.Email.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(u.Role) && u.Role.Contains(q, StringComparison.OrdinalIgnoreCase))
                    )
                    .ToList();
            }

            ViewData["SearchQuery"] = search;
            return View(allUsers);
        }

        // GET: View Login Logs
        public IActionResult LoginLogs()
        {
            var logs = _mongoService.GetLoginLogs();
            return View(logs);
        }
    }
}
