using InventorySystemApp.Data;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace InventorySystemApp.Controllers
{
    public class UserController : Controller
    {
        private readonly MongoDBService _mongoService;

        public UserController(MongoDBService mongoService)
        {
            _mongoService = mongoService;
        }

        // 🏠 USER DASHBOARD
        public IActionResult Dashboard()
        {
            return View();
        }

        // 🔍 SEARCH ITEM
        [HttpGet]
        public IActionResult SearchItem(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                TempData["Message"] = "⚠️ Please enter an item code to search.";
                return RedirectToAction("Dashboard");
            }

            var cabinets = _mongoService.GetAllCabinets();
            var results = cabinets
                .Where(c => c.Items != null && c.Items.Any(i => i.ItemCode == itemCode))
                .SelectMany(c => c.Items
                    .Where(i => i.ItemCode == itemCode)
                    .Select(i => new
                    {
                        ItemName = _mongoService.GetItemCatalogByCode(itemCode)?.ItemName ?? "Unknown",
                        CabinetNumber = c.CabinetNumber,
                        Category = c.Category,
                        Quantity = i.Quantity
                    }))
                .ToList();

            if (results == null || !results.Any())
            {
                TempData["Message"] = "❌ No items found for the entered item code.";
                return RedirectToAction("Dashboard");
            }

            ViewData["SearchResults"] = results;
            ViewData["SearchCode"] = itemCode;

            return View("Dashboard");
        }

        // ➕ ADD ITEM
        [HttpGet]
        public IActionResult AddItem()
        {
            return View();
        }

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


        // ➖ REMOVE ITEM
        [HttpGet]
        public IActionResult RemoveItem()
        {
            return View();
        }

        [HttpPost]
        public IActionResult RemoveItem(string itemCode, string cabinetNumber, int quantity)
        {
            var currentUser = _mongoService.GetUserByUsername(User.Identity?.Name ?? "User");
            string executedBy = currentUser?.FirstName ?? (User.Identity?.Name ?? "User");

            bool success = _mongoService.RemoveItemFromCabinet(itemCode, cabinetNumber, quantity, executedBy);

            TempData["Message"] = success
                ? $"✅ Item '{itemCode}' removed successfully from Cabinet {cabinetNumber} by {executedBy}."
                : $"❌ Failed to remove item '{itemCode}'. Please check the details.";

            return RedirectToAction("RemoveItem");
        }

        // 🧾 VIEW CABINET
        public IActionResult ViewCabinet()
        {
            var cabinets = _mongoService.GetAllCabinets();
            return View(cabinets);
        }
    }
}
