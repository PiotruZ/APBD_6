using Microsoft.AspNetCore.Mvc;
using System.Data;
using Microsoft.Data.SqlClient;

namespace APBD_6.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WarehouseController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost;Database=LocalDB;Trusted_Connection=True;Encrypt=False;";

        public class WarehouseRequest
        {
            public int ProductId { get; set; }
            public int WarehouseId { get; set; }
            public int Amount { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        [HttpPost("add-product")]
        public IActionResult AddProduct(WarehouseRequest request)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Check if product exists
                        var productCmd = new SqlCommand("SELECT COUNT(*) FROM Product WHERE IdProduct = @ProductId", connection, transaction);
                        productCmd.Parameters.AddWithValue("@ProductId", request.ProductId);
                        var productExists = (int)productCmd.ExecuteScalar() > 0;
                        if (!productExists)
                            return NotFound("Product not found");

                        // Check if warehouse exists
                        var warehouseCmd = new SqlCommand("SELECT COUNT(*) FROM Warehouse WHERE IdWarehouse = @WarehouseId", connection, transaction);
                        warehouseCmd.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                        var warehouseExists = (int)warehouseCmd.ExecuteScalar() > 0;
                        if (!warehouseExists)
                            return NotFound("Warehouse not found");

                        // Validate amount
                        if (request.Amount <= 0)
                            return BadRequest("Amount must be greater than 0");

                        // Check for corresponding order
                        var orderCmd = new SqlCommand("SELECT TOP 1 IdOrder, Price FROM [Order] o LEFT JOIN Product_Warehouse pw ON o.IdOrder = pw.IdOrder WHERE o.IdProduct = @ProductId AND o.Amount = @Amount AND pw.IdProductWarehouse IS NULL AND o.CreatedAt < @CreatedAt", connection, transaction);
                        orderCmd.Parameters.AddWithValue("@ProductId", request.ProductId);
                        orderCmd.Parameters.AddWithValue("@Amount", request.Amount);
                        orderCmd.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);
                        using (var orderReader = orderCmd.ExecuteReader())
                        {
                            if (!orderReader.Read())
                                return BadRequest("No matching order found");

                            var orderId = (int)orderReader["IdOrder"];
                            var price = (decimal)orderReader["Price"];

                            // Update the order's FulfilledAt
                            var updateCmd = new SqlCommand("UPDATE [Order] SET FulfilledAt = @Now WHERE IdOrder = @OrderId", connection, transaction);
                            updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                            updateCmd.Parameters.AddWithValue("@OrderId", orderId);
                            updateCmd.ExecuteNonQuery();

                            // Insert into Product_Warehouse
                            var insertCmd = new SqlCommand("INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) OUTPUT INSERTED.IdProductWarehouse VALUES (@WarehouseId, @ProductId, @OrderId, @Amount, @Price * @Amount, @Now)", connection, transaction);
                            insertCmd.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                            insertCmd.Parameters.AddWithValue("@ProductId", request.ProductId);
                            insertCmd.Parameters.AddWithValue("@OrderId", orderId);
                            insertCmd.Parameters.AddWithValue("@Amount", request.Amount);
                            insertCmd.Parameters.AddWithValue("@Price", price);
                            insertCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                            var insertedId = (int)insertCmd.ExecuteScalar();

                            transaction.Commit();
                            return Ok(insertedId);
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return StatusCode(500, ex.Message);
                    }
                }
            }
        }

        [HttpPost("add-product-procedure")]
        public IActionResult AddProductProcedure(WarehouseRequest request)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("AddProductToWarehouse", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IdProduct", request.ProductId);
                    command.Parameters.AddWithValue("@IdWarehouse", request.WarehouseId);
                    command.Parameters.AddWithValue("@Amount", request.Amount);
                    command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

                    try
                    {
                        var result = command.ExecuteScalar();
                        return Ok(result);
                    }
                    catch (SqlException sqlEx)
                    {
                        return StatusCode(500, sqlEx.Message);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, ex.Message);
                    }
                }
            }
        }
    }
}
