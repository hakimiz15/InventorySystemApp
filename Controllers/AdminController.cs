using InventorySystemApp.Data;
using InventorySystemApp.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace InventorySystemApp.Controllers
{
    public class AdminController : Controller
    {
        private readonly MongoDBService _mongoService;

        public AdminController(MongoDBService mongoService)
        {
            _mongoService = mongoService;
        }

        // GET: Admin Dashboard
        public IActionResult Dashboard()
        {
            return View();
        }

        [HttpGet]
public IActionResult SearchItem(string itemCode)
{
    if (string.IsNullOrWhiteSpace(itemCode))
    {
        TempData["Message"] = "⚠️ Please enter an item code to search.";
        return RedirectToAction("Dashboard");
    }

    var cabinets = _mongoService.GetAllCabinets();
    var catalogItem = _mongoService.GetItemCatalogByCode(itemCode);

    if (catalogItem == null)
    {
        TempData["Message"] = $"❌ No item found with code '{itemCode}'.";
        return RedirectToAction("Dashboard");
    }

    // Find all cabinets that contain this item
    var results = cabinets
        .Where(c => c.Items != null && c.Items.Any(i => i.ItemCode == itemCode))
        .Select(c => new
        {
            ItemName = catalogItem.ItemName,
            CabinetNumber = c.CabinetNumber,
            Category = c.Category,
            Quantity = c.Items.First(i => i.ItemCode == itemCode).Quantity
        })
        .ToList<dynamic>();

    if (results.Any())
    {
        ViewData["SearchResults"] = results;
        ViewData["SearchCode"] = itemCode;
    }
    else
    {
        TempData["Message"] = $"🔍 No cabinet currently contains the item '{itemCode}'.";
    }

    return View("Dashboard");
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
    var currentUser = _mongoService.GetUserByUsername(User.Identity?.Name ?? "User");
    string executedBy = currentUser?.FirstName ?? (User.Identity?.Name ?? "User");

    // Call MongoDB service (returns success + cabinetNumber)
    var result = _mongoService.PlaceItemInCabinet(itemCode, quantity, executedBy);

    TempData["Message"] = result.success
        ? $"✅ Item '{itemCode}' placed automatically into Cabinet {result.cabinetNumber} by {executedBy}."
        : $"❌ Failed to add item '{itemCode}'. No suitable cabinet found.";

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
        var currentUser = _mongoService.GetUserByUsername(User.Identity?.Name ?? "Admin");
        string executedBy = currentUser?.FirstName ?? (User.Identity?.Name ?? "Admin");

bool result = _mongoService.RemoveItemFromCabinet(itemCode, cabinetNumber, quantity, executedBy);


    TempData["Message"] = result
        ? $"✅ Item '{itemCode}' successfully removed from Cabinet {cabinetNumber}!"
        : $"❌ Failed to remove item '{itemCode}'. It might not exist or insufficient quantity.";

    return RedirectToAction("RemoveItem");
}

        // GET: View Cabinets
        public IActionResult ViewCabinet()
        {
            var cabinets = _mongoService.GetAllCabinets();
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

                // ✅ View Blockchain Records
        public IActionResult ViewBlockchain()
        {
            var blocks = _mongoService.GetBlocks();
            var (ok, errorAt) = _mongoService.VerifyChain();

            ViewBag.IsValid = ok;
            ViewBag.ErrorAt = errorAt;

            return View(blocks);
    }

        // POST: Remove User  
        [HttpPost]
public IActionResult DeleteUser(string username)
{
    if (string.IsNullOrEmpty(username))
    {
        TempData["Message"] = "⚠️ Invalid username.";
        return RedirectToAction("ViewUsers");
    }

    // Delete user from MongoDB
    _mongoService.RemoveUser(username);

    TempData["Message"] = $"❌ User '{username}' has been removed successfully.";
    return RedirectToAction("ViewUsers");
}


        // ✅ VIEW ALL USERS
public IActionResult ViewUsers()
{
    var users = _mongoService.GetAllUsers();
    return View(users); // Views/Admin/ViewUsers.cshtml
}


        // GET: View Login Logs
        public IActionResult LoginLogs()
        {
            var logs = _mongoService.GetLoginLogs();
            return View(logs);
        }
    }
}
