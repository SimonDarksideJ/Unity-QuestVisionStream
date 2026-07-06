// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using NUnit.Framework;
using QuestVisionStream.Protocol;

namespace QuestVisionStream.Tests
{
    public class RemoteConfigTests
    {
        [Test]
        public void Parse_PublishedTailscaleUrl_WithEmbeddedToken()
        {
            const string json = "{\"server\":\"wss://mini.tail1234.ts.net/?token=s3cret\"}";

            Assert.That(RemoteConfig.TryParseServerUrl(json, out var url), Is.True);
            Assert.That(url, Is.EqualTo("wss://mini.tail1234.ts.net/?token=s3cret"));
        }

        [Test]
        public void Parse_PlainWsUrl()
        {
            Assert.That(RemoteConfig.TryParseServerUrl("{\"server\":\"ws://100.1.2.3:3000\"}", out var url), Is.True);
            Assert.That(url, Is.EqualTo("ws://100.1.2.3:3000"));
        }

        [TestCase("{\"server\":\"\"}")] // unset KV → empty string per the Pages function
        [TestCase("{\"server\":\"https://not-a-socket\"}")] // wrong scheme
        [TestCase("{\"other\":\"x\"}")]
        [TestCase("not json")]
        [TestCase("")]
        [TestCase(null)]
        public void Parse_Unusable_ReturnsFalse(string json)
        {
            Assert.That(RemoteConfig.TryParseServerUrl(json, out _), Is.False);
        }

        [Test]
        public void IsWebSocketUrl_AcceptsOnlyWsSchemes()
        {
            Assert.That(RemoteConfig.IsWebSocketUrl("wss://host"), Is.True);
            Assert.That(RemoteConfig.IsWebSocketUrl("ws://host:3000"), Is.True);
            Assert.That(RemoteConfig.IsWebSocketUrl("http://host"), Is.False);
            Assert.That(RemoteConfig.IsWebSocketUrl("host:3000"), Is.False);
        }
    }
}
