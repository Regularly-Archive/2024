﻿using SwaggerWithExamples.Models;
using Swashbuckle.AspNetCore.Filters;
using System.Drawing;

namespace SwaggerWithExamples.Swagger
{
    public class ParoductRequestExampleProvider : IExamplesProvider<Product>
    {
        public Product GetExamples()
        {
            return new Product()
            {
                Id = "001",
                Sku = "S1",
                Size = "L",
                Model = "M1",
                Color = "Blue"
            };
        }
    }
}
