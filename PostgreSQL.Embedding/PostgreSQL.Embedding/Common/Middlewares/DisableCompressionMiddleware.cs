namespace PostgreSQL.Embedding.Common.Middlewares
{
    public class DisableCompressionMiddleware
    {
        private readonly RequestDelegate _next;

        public DisableCompressionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api/Conversation") && context.Request.Method == "POST")
            {
                context.Response.Headers.Remove("Content-Encoding");
                context.Response.Headers["Content-Encoding"] = "identity";
            }

            await _next(context);
        }
    }
}
