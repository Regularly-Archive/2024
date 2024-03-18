using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using PostgreSQL.Embedding.Common.Models.WebApi;

namespace PostgreSQL.Embedding.Common.Middlewares
{
    public class GlobalExceptionFilter : IActionFilter, IOrderedFilter
    {
        public int Order => int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception != null)
            {
                context.Result = ApiResult.Failure(context.Exception);
                context.ExceptionHandled = true;
            }
        }
    }
}
