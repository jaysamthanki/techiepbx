using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Renders extensions.conf (the dialplan). Each extension gets an explicit entry rather
    /// than a pattern, so only numbers that exist in the database can be dialled.
    /// </summary>
    public static class ExtensionsConfRenderer
    {
        public const string InternalContext = "internal";
        public const string EchoTestNumber = "*43";

        public static string Render(IEnumerable<Extension> extensions)
        {
            var sb = new StringBuilder();
            sb.Append(ConfText.Header).Append('\n');

            sb.Append('\n');
            sb.Append($"[{InternalContext}]\n");
            sb.Append("; Echo test\n");
            sb.Append($"exten => {EchoTestNumber},1,Answer()\n");
            sb.Append(" same => n,Playback(demo-echotest)\n");
            sb.Append(" same => n,Echo()\n");
            sb.Append(" same => n,Hangup()\n");

            foreach (var extension in ConfText.EnabledInOrder(extensions))
            {
                var number = ConfText.Safe(extension.Number, "number");
                var name = ConfText.Safe(extension.Name, "name");

                sb.Append('\n');
                sb.Append($"; {name}\n");
                sb.Append($"exten => {number},1,Dial(PJSIP/{number},30)\n");
                sb.Append(" same => n,Hangup()\n");
            }

            return sb.ToString();
        }
    }
}
