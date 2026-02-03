using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    public class MessageController : CrudBaseController<SystemMessage, SystemMessageQueryableFilter>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IRepository<SystemMessage> _messageRepository;
        public MessageController(CrudBaseService<SystemMessage> crudBaseService, ICurrentUserService currentUserService) : base(crudBaseService)
        {
            _currentUserService = currentUserService;
            _messageRepository = crudBaseService.Repository;
        }

        [HttpPut("read")]
        public async Task<JsonResult> ReadAll()
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            var userId = user?.UserName;
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("用户未登录");
            }

            var messages = await _messageRepository.FindListAsync(x => x.CreatedBy == userId && !x.IsRead);

            foreach (var message in messages)
            {
                message.IsRead = true;
                await _messageRepository.UpdateAsync(message);
            }

            return ApiResult.Success<object>(null);
        }

        [HttpPut("read/{messageId}")]
        public async Task<JsonResult> Read(long messageId)
        {
            var message = await _messageRepository.GetAsync(messageId);
            message.IsRead = true;
            await _messageRepository.UpdateAsync(message);

            return ApiResult.Success<object>(null);
        }
    }
}
