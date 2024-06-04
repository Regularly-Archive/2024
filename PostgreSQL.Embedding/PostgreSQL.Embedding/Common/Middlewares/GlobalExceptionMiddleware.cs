using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using PostgreSQL.Embedding.Common.Models.WebApi;
using Microsoft.Extensions.Logging;

namespace PostgreSQL.Embedding.Common.Middlewares
{
    public class GlobalExceptionFilter : IActionFilter, IOrderedFilter
    {
        ILogger<GlobalExceptionFilter> _logger;
        public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
        {
            _logger = logger;
        }

        public int Order => int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception != null)
            {
                _logger.LogError(context.Exception, string.Empty);
                context.Result = ApiResult.Failure(context.Exception);
                context.ExceptionHandled = true;
            }
        }
    }
}
