using System;
using NUnit.Framework;

namespace Reown.Sign.Unity.Tests
{
    public class LinkerTests
    {
        [Test]
        public void BuildConnectionDeepLink_WithValidInputs_ReturnsCorrectDeepLink()
        {
            const string appLink = "myapp://";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var result = Linker.BuildConnectionDeepLink(appLink, wcUri);

            var expectedEncodedUri = Uri.EscapeDataString(wcUri);
            var expected = $"myapp://wc?uri={expectedEncodedUri}";
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void BuildConnectionDeepLink_WithAppLinkWithoutProtocol_AddsProtocolAndFormatsCorrectly()
        {
            const string appLink = "myapp";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var result = Linker.BuildConnectionDeepLink(appLink, wcUri);

            var expectedEncodedUri = Uri.EscapeDataString(wcUri);
            var expected = $"myapp://wc?uri={expectedEncodedUri}";
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void BuildConnectionDeepLink_WithAppLinkWithSlashesAndColons_CleansAndFormatsCorrectly()
        {
            const string appLink = "my:app/";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var result = Linker.BuildConnectionDeepLink(appLink, wcUri);

            var expectedEncodedUri = Uri.EscapeDataString(wcUri);
            var expected = $"myapp://wc?uri={expectedEncodedUri}";
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void BuildConnectionDeepLink_WithAppLinkWithoutTrailingSlash_AddsTrailingSlash()
        {
            const string appLink = "myapp://";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var result = Linker.BuildConnectionDeepLink(appLink, wcUri);

            var expectedEncodedUri = Uri.EscapeDataString(wcUri);
            var expected = $"myapp://wc?uri={expectedEncodedUri}";
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void BuildConnectionDeepLink_WithComplexWcUri_EncodesUriCorrectly()
        {
            const string appLink = "myapp://";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var result = Linker.BuildConnectionDeepLink(appLink, wcUri);

            var expectedEncodedUri = Uri.EscapeDataString(wcUri);
            var expected = $"myapp://wc?uri={expectedEncodedUri}";
            Assert.AreEqual(expected, result);

            Assert.IsTrue(result.Contains("wc%3A123456"));
        }

        [Test]
        public void BuildConnectionDeepLink_WithNullWcUri_ThrowsArgumentException()
        {
            const string appLink = "myapp://";
            string wcUri = null;

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Uri cannot be empty.", exception.Message);
        }

        [Test]
        public void BuildConnectionDeepLink_WithEmptyWcUri_ThrowsArgumentException()
        {
            const string appLink = "myapp://";
            const string wcUri = "";

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Uri cannot be empty.", exception.Message);
        }

        [Test]
        public void BuildConnectionDeepLink_WithWhitespaceWcUri_ThrowsArgumentException()
        {
            const string appLink = "myapp://";
            const string wcUri = "   ";

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Uri cannot be empty.", exception.Message);
        }

        [Test]
        public void BuildConnectionDeepLink_WithNullAppLink_ThrowsArgumentException()
        {
            string appLink = null;
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Native link cannot be empty.", exception.Message);
        }

        [Test]
        public void BuildConnectionDeepLink_WithEmptyAppLink_ThrowsArgumentException()
        {
            const string appLink = "";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Native link cannot be empty.", exception.Message);
        }

        [Test]
        public void BuildConnectionDeepLink_WithWhitespaceAppLink_ThrowsArgumentException()
        {
            const string appLink = "   ";
            const string wcUri = "wc:123456@2?relay-protocol=irn&symKey=abcdef";

            var exception = Assert.Throws<ArgumentException>(() =>
                Linker.BuildConnectionDeepLink(appLink, wcUri));
            Assert.AreEqual("[Linker] Native link cannot be empty.", exception.Message);
        }
    }
}