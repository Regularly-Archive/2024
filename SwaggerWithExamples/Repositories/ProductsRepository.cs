using SwaggerWithExamples.Models;

namespace SwaggerWithExamples.Repositories
{
    public class ProductsRepository
    {
        private readonly List<Product> _products;
        public ProductsRepository()
        {
            _products = new List<Product>()
            {
                new Product(){Id="001", Sku="S1", Size = "L", Model = "M1", Color = "Black"}
            };
        }

        public IEnumerable<Product> GetAll()
        {
            return _products;
        }

        public Product Get(string id)
        {
            return _products.FirstOrDefault(x => x.Id == id);
        }

        public void Delete(string id)
        {
            var product = _products.FirstOrDefault(x => x.Id == id);
            if (product != null)
                _products.Remove(product);

        }

        public void Update(Product product)
        {
            var toUpdated = _products.FirstOrDefault(x => x.Id == product.Id);
            if (toUpdated != null)
            {
                toUpdated.Sku = product.Sku;
                toUpdated.Size = product.Size;
                toUpdated.Color = product.Color;
                toUpdated.Model = product.Model;
            }
        }

        public void Add(Product product)
        {
            var maxId = 0;

            if (_products.Any())
                maxId = _products.Select(x => int.Parse(x.Id)).Max();

            var id = (maxId + 1).ToString("000");
            product.Id = id;
            _products.Add(product);
        }
    }
}
