using MailKit.Net.Smtp;
using Microsoft.SemanticKernel;
using MimeKit;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "MailKit 插件")]
    public class MailKitPlugin : BasePlugin
    {
        [PluginParameter(Description = "发件人姓名")]
        private string MAIL_SENDER_NAME { get; set; } = "Wikit";

        [PluginParameter(Description = "发件人邮箱")]
        private string MAIL_SENDER_EMAIL { get; set; } = "qinyuanpei@126.com";

        [PluginParameter(Description = "发件人密码", Required = true)]
        private string MAIL_SENDER_PASS { get; set; }

        [PluginParameter(Description = "SMTP 主机")]
        private string SMTP_HOST { get; set; } = "smtp.126.com";

        [PluginParameter(Description = "SMTP 端口号")]
        private int SMTP_PORT { get; set; } = 857;

        [PluginParameter(Description = "是否启用 SSL")]
        private bool STMP_USE_SSL { get; set; } = true;

        public MailKitPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
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
            smtp.Connect(SMTP_HOST, SMTP_PORT, STMP_USE_SSL);
            smtp.Authenticate(MAIL_SENDER_EMAIL, MAIL_SENDER_PASS);
            return smtp.Send(email);
        }
    }
}
