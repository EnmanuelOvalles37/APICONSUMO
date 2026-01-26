using Microsoft.Data.SqlClient;

namespace Consumo_App.Data.Sql
{
   
    public class SqlConnectionFactory
    {
        private readonly string _connectionString;

        public SqlConnectionFactory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' no encontrada en configuración.");

        }

       
        public SqlConnection Create() => new SqlConnection(_connectionString);

               
        public async Task<SqlConnection> CreateOpenAsync()
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }
}