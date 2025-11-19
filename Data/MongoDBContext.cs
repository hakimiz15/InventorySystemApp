using MongoDB.Driver;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Data
{
    public class MongoDBContext
    {
        private readonly IMongoDatabase _database;
        private readonly IConfiguration _configuration;

        public MongoDBContext(IConfiguration configuration)
        {
            _configuration = configuration;
            var client = new MongoClient(_configuration["MongoDB:ConnectionString"]);
            _database = client.GetDatabase(_configuration["MongoDB:DatabaseName"]);
        }

        public IMongoDatabase Database => _database;
    }
}
