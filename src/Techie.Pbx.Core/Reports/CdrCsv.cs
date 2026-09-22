using System.Globalization;
using System.Text;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Reports
{
    /// <summary>
    /// The call report as CSV (F5): RFC 4180, CRLF line ends, a field quoted only when it has to
    /// be. The columns are the stored record plus the derived direction and trunk, so a spreadsheet
    /// can be filtered the same way the page is.
    ///
    /// Caller ID comes from outside, so a caller can choose what lands in a cell. A value a
    /// spreadsheet would read as a formula gets an apostrophe in front of it, which the spreadsheet
    /// hides and which stops it running. A phone number that starts with + and is otherwise digits
    /// is left alone: that is a number, not a formula, and it is the most common value in the file.
    /// </summary>
    public static class CdrCsv
    {
        private static readonly string[] Header =
        {
            "StartUtc", "AnswerUtc", "EndUtc", "Direction", "Trunk", "Src", "Dst", "CallerID",
            "Disposition", "DurationSeconds", "BillSecSeconds", "Channel", "DestinationChannel",
            "Dcontext", "LastApplication", "LastData", "AccountCode", "AmaFlags", "UniqueID",
            "LinkedID", "Sequence",
        };

        /// <summary>
        /// The value with an apostrophe in front when a spreadsheet would otherwise take it for a
        /// formula: anything starting with = or @, a tab or a carriage return, and anything
        /// starting with + or - that is not simply a number.
        /// </summary>
        private static string Defused(string value)
        {
            if (value.Length == 0)
                return value;

            var first = value[0];

            if (first is '=' or '@' or '\t' or '\r')
                return "'" + value;

            if (first is '+' or '-' && !(value.Length > 1 && value.Skip(1).All(char.IsAsciiDigit)))
                return "'" + value;

            return value;
        }

        /// <summary>
        /// A field as RFC 4180 writes it: in double quotes, with quotes doubled, when it holds a
        /// comma, a quote or a line break, and as it is otherwise.
        /// </summary>
        public static string Field(string? value)
        {
            var text = Defused(value ?? "");

            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static void Line(StringBuilder sb, IEnumerable<string?> fields)
        {
            sb.Append(string.Join(',', fields.Select(Field)));
            sb.Append("\r\n");
        }

        private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

        public static string Render(IEnumerable<Cdr> cdrs, IReadOnlySet<string> trunkNames)
        {
            var sb = new StringBuilder();
            Line(sb, Header);

            foreach (var cdr in cdrs)
            {
                Line(sb, new[]
                {
                    cdr.StartUtc,
                    cdr.AnswerUtc,
                    cdr.EndUtc,
                    CdrChannels.Direction(cdr, trunkNames).ToString(),
                    CdrChannels.Trunk(cdr, trunkNames),
                    cdr.Src,
                    cdr.Dst,
                    cdr.CallerID,
                    cdr.Disposition,
                    Number(cdr.DurationSeconds),
                    Number(cdr.BillSecSeconds),
                    cdr.Channel,
                    cdr.DestinationChannel,
                    cdr.Dcontext,
                    cdr.LastApplication,
                    cdr.LastData,
                    cdr.AccountCode,
                    cdr.AmaFlags,
                    cdr.UniqueID,
                    cdr.LinkedID,
                    cdr.Sequence?.ToString(CultureInfo.InvariantCulture),
                });
            }

            return sb.ToString();
        }
    }
}
