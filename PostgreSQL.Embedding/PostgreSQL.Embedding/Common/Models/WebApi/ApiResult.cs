using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace PostgreSQL.Embedding.Common.Models.WebApi
{
    public class ApiResult
    {
        public object data { get; set; }
        public int code { get; set; }
        public string message { get; set; }

        public static JsonResult Success<T>(T data, string message = "")
        {
            var result = new ApiResult { code = (int)HttpStatusCode.OK, data = data, message = message };
            return new JsonResult(result);
        }

        public static JsonResult Failure(Exception ex)
        {
            var result = new ApiResult { code = (int)HttpStatusCode.InternalServerError, data = null, message = ex.Message };
            return new JsonResult(result);
        }
    }
}
