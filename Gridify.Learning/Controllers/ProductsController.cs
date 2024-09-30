using Gridify.Learning.Models;
using Microsoft.AspNetCore.Mvc;

namespace Gridify.Learning.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        public ProductsController(AppDbContext context) 
        {
            _context = context;
        }

        [HttpGet]
        public Paging<Product> GetPagingProducts([FromQuery]GridifyQuery query)
        {
            var queryable = _context.Products.AsQueryable();
            queryable.OrderByDescending(x => x.Sku).ThenByDescending(x => x.Sku);
            return queryable.Gridify(query);
        }
    }
}
