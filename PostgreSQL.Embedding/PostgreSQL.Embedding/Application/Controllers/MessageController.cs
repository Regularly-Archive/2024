using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    public class MessageController : CrudBaseController<SystemMessage, SystemMessageQueryableFilter>
    {
        private readonly IUserInfoService _userInfoService;
        private readonly IRepository<SystemMessage> _messageRepository;
        public MessageController(CrudBaseService<SystemMessage> crudBaseService, IUserInfoService userInfoService) : base(crudBaseService)
        {
            _userInfoService = userInfoService;
            _messageRepository = crudBaseService.Repository;
        }

        [HttpPut("read")]
        public async Task<JsonResult> ReadAll()
        {
            var userId = (await _userInfoService.GetCurrentUserAsync()).UserName;
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
