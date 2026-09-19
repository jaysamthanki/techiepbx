using Techie.Pbx.Core.Diagnostics;

namespace Techie.Pbx.Tests.Core
{
    public class LogTailTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-logtail-").FullName;

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        [Fact]
        public void Missing_file_does_not_throw()
        {
            var result = LogTail.Read(Path.Combine(this.directory, "nope.log"), 100, null, 4096);

            Assert.False(result.Exists);
            Assert.Empty(result.Lines);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Empty_file_has_no_lines()
        {
            var path = this.WriteFile("");

            var result = LogTail.Read(path, 100, null, 4096);

            Assert.True(result.Exists);
            Assert.Empty(result.Lines);
        }

        [Fact]
        public void Only_the_last_N_lines_come_back_oldest_first()
        {
            var path = this.WriteFile("one\ntwo\nthree\nfour\nfive\n");

            var result = LogTail.Read(path, 3, null, 4096);

            Assert.Equal(new[] { "three", "four", "five" }, result.Lines);
        }

        [Fact]
        public void A_trailing_newline_does_not_produce_an_empty_last_line()
        {
            var path = this.WriteFile("one\ntwo\n");

            var result = LogTail.Read(path, 100, null, 4096);

            Assert.Equal(new[] { "one", "two" }, result.Lines);
        }

        [Fact]
        public void Crlf_line_endings_are_tolerated()
        {
            var path = this.WriteFile("one\r\ntwo\r\nthree\r\n");

            var result = LogTail.Read(path, 100, null, 4096);

            Assert.Equal(new[] { "one", "two", "three" }, result.Lines);
        }

        [Fact]
        public void A_filter_keeps_only_matching_lines_case_insensitively()
        {
            var path = this.WriteFile("apple\nBANANA\ncherry\nbanana split\n");

            var result = LogTail.Read(path, 100, "banana", 4096);

            Assert.Equal(new[] { "BANANA", "banana split" }, result.Lines);
        }

        [Fact]
        public void A_filter_with_no_matches_returns_no_lines_without_error()
        {
            var path = this.WriteFile("apple\nbanana\n");

            var result = LogTail.Read(path, 100, "kumquat", 4096);

            Assert.True(result.Exists);
            Assert.Empty(result.Lines);
            Assert.Null(result.Error);
        }

        [Fact]
        public void A_file_longer_than_the_scan_cap_drops_the_first_line_read_and_is_marked_capped()
        {
            // Each line is 5 bytes ("NNNN\n"); a 20 byte scan cap starts the read at offset 30,
            // the first byte of "0006". Whatever comes out first is treated as a fragment even
            // though this one happens to line up on a boundary, so "0006" is dropped along with it.
            var lines = Enumerable.Range(0, 10).Select(i => i.ToString("D4"));
            var path = this.WriteFile(string.Join("", lines.Select(l => l + "\n")));

            var result = LogTail.Read(path, 100, null, maxScanBytes: 20);

            Assert.True(result.ScanCapped);
            Assert.Equal(new[] { "0007", "0008", "0009" }, result.Lines);
        }

        [Fact]
        public void A_scan_cap_bigger_than_the_file_is_not_marked_capped()
        {
            var path = this.WriteFile("one\ntwo\n");

            var result = LogTail.Read(path, 100, null, maxScanBytes: 4096);

            Assert.False(result.ScanCapped);
        }

        private string WriteFile(string content)
        {
            var path = Path.Combine(this.directory, "test.log");
            File.WriteAllText(path, content);
            return path;
        }
    }
}
