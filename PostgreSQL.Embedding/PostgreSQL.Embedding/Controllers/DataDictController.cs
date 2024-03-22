using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Converters;
using PostgreSQL.Embedding.Common.Models.WebApi;
using System.Reflection;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class DataDictController : ControllerBase
    {
        private readonly EnumValuesConverter _enumValuesConverter;
        public DataDictController(EnumValuesConverter enumValuesConverter)
        {
            _enumValuesConverter = enumValuesConverter;
        }

        [HttpGet("Enum/{typeName}")]
        public IActionResult GetEnumValues(string typeName)
        {
            var enumType = Type.GetType(typeName);
            if (enumType == null || !enumType.IsEnum)
                throw new ArgumentException($"The type '{typeName}' must be a Enum.");

            var enumValues = _enumValuesConverter.Convert(enumType).ToList();
            return ApiResult.Success(enumValues);
        }
    }
}
