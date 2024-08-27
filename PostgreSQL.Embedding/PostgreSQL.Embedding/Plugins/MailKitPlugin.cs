using MailKit.Net.Smtp;
using Microsoft.SemanticKernel;
using MimeKit;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "MailKit插件")]
    public class MailKitPlugin
    {
        private const string MAIL_SENDER_NAME = "Wikit";
        private const string MAIL_SENDER_EMAIL = "qinyuanpei@126.com";

        public MailKitPlugin()
        {

        }

        [KernelFunction]
        [Description("使用 MailKit 发送邮件")]
        public string SendMailAsync([Description("主题")] string subject, [Description("正文")] string body, [Description("收件人")] string receiver)
        {
            var email = new MimeMessage();

            email.From.Add(new MailboxAddress(MAIL_SENDER_NAME, MAIL_SENDER_EMAIL));
            email.To.Add(new MailboxAddress(receiver, receiver));

            email.Subject = subject;
            email.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
            {
                Text = body
            };

            using var smtp = new SmtpClient();
            smtp.Connect("smtp.126.com", 587, true);
            smtp.Authenticate(MAIL_SENDER_EMAIL, "NIUCTOSAEHORYBDM");
            return smtp.Send(email);
        }
    }
}
