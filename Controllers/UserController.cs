using InventorySystemApp.Data;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystemApp.Controllers
{
    public class UserController(MongoDBService mongoService) : Controller
    {
        private readonly MongoDBService _mongoService = mongoService;

        // ✅ USER DASHBOARD
        public IActionResult Dashboard()
        {
            return View(); // Views/User/Dashboard.cshtml
        }

        // ✅ ADD ITEM
        [HttpGet]
        public IActionResult AddItem()
        {
            return View(); // Views/User/AddItem.cshtml
        }

        [HttpPost]
        public IActionResult AddItem(string itemCode, int quantity)
        {
            var success = _mongoService.PlaceItemInCabinet(itemCode, quantity);

            TempData["Message"] = success
                ? $"✅ Item {itemCode} added successfully!"
                : $"❌ Failed to add item {itemCode}. No suitable cabinet found.";
            
            return RedirectToAction("AddItem");
        }

        // ✅ REMOVE ITEM
        [HttpGet]
        public IActionResult RemoveItem()
        {
            return View(); // Views/User/RemoveItem.cshtml
        }

        [HttpPost]
        public IActionResult RemoveItem(string itemCode, string cabinetNumber, int quantity)
        {
            var success = _mongoService.RemoveItemFromCabinet(itemCode, cabinetNumber, quantity);

            TempData["Message"] = success
                ? $"✅ Item {itemCode} removed successfully from Cabinet {cabinetNumber}."
                : $"❌ Failed to remove item {itemCode}. Please check the details.";

            return RedirectToAction("RemoveItem");
        }

        // ✅ VIEW CABINET (original layout — no filtering, no search results)
        public IActionResult ViewCabinet()
        {
            var cabinets = _mongoService.GetAllCabinets();
            return View(cabinets);
        }

        // ✅ SEARCH CABINET (separate page for search results only)
        public IActionResult SearchCabinet(string search)
        {
            List<Models.Cabinet> cabinets;
            bool isSearch = !string.IsNullOrWhiteSpace(search);

            if (isSearch)
                cabinets = _mongoService.SearchCabinetsByItem(search!);
            else
                cabinets = [];

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
    }
}
