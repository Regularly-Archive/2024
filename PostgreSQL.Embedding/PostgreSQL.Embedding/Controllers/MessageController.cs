using Azure.Storage.Blobs.Models;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.Services;

namespace PostgreSQL.Embedding.Controllers
{
    public class MessageController : CrudBaseController<SystemMessage>
    {
        private readonly IUserInfoService _userInfoService;
        private readonly IRepository<SystemMessage> _messageRepository;
        public MessageController(CrudBaseService<SystemMessage> crudBaseService, IUserInfoService userInfoService) : base(crudBaseService)
        {
            _userInfoService = userInfoService;
            _messageRepository = crudBaseService.Repository;
        }

        public override async Task<JsonResult> FindList()
        {
            var userId = _userInfoService.GetCurrentUser()?.Identity?.Name;
            var list = await _messageRepository.FindAsync(x => x.CreatedBy == userId && !x.IsRead);
            return ApiResult.Success(list);
        }

        [HttpPut("read")]
        public async Task<JsonResult> ReadAll()
        {
            var userId = _userInfoService.GetCurrentUser()?.Identity?.Name;
            var messages = await _messageRepository.FindAsync(x => x.CreatedBy == userId && !x.IsRead);

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
