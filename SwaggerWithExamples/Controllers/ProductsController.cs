using Microsoft.AspNetCore.Mvc;
using SwaggerWithExamples.Models;
using SwaggerWithExamples.Repositories;
using SwaggerWithExamples.Swagger;
using Swashbuckle.AspNetCore.Filters;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace SwaggerWithExamples.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly ProductsRepository _productsRepository;
        public ProductsController(ProductsRepository productRepository)
        {
            _productsRepository = productRepository;
        }


        // GET: api/<ProductsController>
        [HttpGet]
        public IEnumerable<Product> Get()
        {
            return _productsRepository.GetAll();
        }

        // GET api/<ProductsController>/5
        [HttpGet("{id}")]
        public Product Get(string id)
        {
            return _productsRepository.Get(id);
        }

        // POST api/<ProductsController>
        [HttpPost]
        [SwaggerRequestExample(typeof(Product), typeof(ParoductRequestExampleProvider))]
        [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ProductResponseExampleProvider))]
        public Product Post([FromBody] Product product)
        {
            _productsRepository.Add(product);
            return product;
        }

        // PUT api/<ProductsController>/5
        [HttpPut("{id}")]
        public void Put([FromBody] Product product)
        {
            _productsRepository.Update(product);
        }

        // DELETE api/<ProductsController>/5
        [HttpDelete("{id}")]
        public void Delete(string id)
        {
            _productsRepository.Delete(id);
        }
    }
}
