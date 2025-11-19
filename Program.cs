using InventorySystem.Data;
using InventorySystemApp.Data;

var builder = WebApplication.CreateBuilder(args);

// Add MongoDB services
builder.Services.AddSingleton<MongoDBService>();
builder.Services.AddSingleton<MongoDBContext>();

// Add MVC services
builder.Services.AddControllersWithViews();

// Add session and distributed memory cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add IHttpContextAccessor so Razor views can access session safely
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var mongoService = scope.ServiceProvider.GetRequiredService<MongoDBService>();
    Console.WriteLine("✅ MongoDB Service initialized and blockchain verified.");
}


// Error handling & HTTPS
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable session middleware
app.UseSession();
app.UseAuthorization();

// Set default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
