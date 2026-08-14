using System.Text;

namespace Netsoft.MessageQueues.Domain.Tests;

public sealed class MessagePayloadTests
{
    [Theory]
    [InlineData("""{"orderId":42}""")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    public void 有効なJSONからペイロードを作れる(string json)
    {
        MessagePayload payload = MessagePayload.From(json);

        Assert.Equal(json, payload.Json);
        Assert.False(payload.IsEmpty);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{'single':'quotes'}")]
    [InlineData("{\"trailing\":1,}")]
    [InlineData("not json")]
    public void JSONでないものは弾く(string json)
    {
        Assert.Throws<ArgumentException>(() => MessagePayload.From(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 空文字や空白だけは弾く(string json)
    {
        Assert.Throws<ArgumentException>(() => MessagePayload.From(json));
    }

    [Fact]
    public void 上限ちょうどのペイロードは作れる()
    {
        // "..." の引用符 2 バイトを差し引いた中身で、全体をちょうど 64KB にする。
        string json = "\"" + new string('a', MessagePayload.MaxBytes - 2) + "\"";
        Assert.Equal(MessagePayload.MaxBytes, Encoding.UTF8.GetByteCount(json));

        MessagePayload payload = MessagePayload.From(json);

        Assert.Equal(json, payload.Json);
    }

    [Fact]
    public void 上限を1バイトでも超えるペイロードは弾く()
    {
        string json = "\"" + new string('a', MessagePayload.MaxBytes - 1) + "\"";
        Assert.Equal(MessagePayload.MaxBytes + 1, Encoding.UTF8.GetByteCount(json));

        Assert.Throws<ArgumentException>(() => MessagePayload.From(json));
    }

    [Fact]
    public void サイズは文字数ではなくUTF8のバイト数で測る()
    {
        // 'あ' は UTF-8 で 3 バイト。文字数は上限のずっと下でも、バイト数で上限を超える。
        string json = "\"" + new string('あ', MessagePayload.MaxBytes / 3) + "\"";
        Assert.True(json.Length < MessagePayload.MaxBytes);
        Assert.True(Encoding.UTF8.GetByteCount(json) > MessagePayload.MaxBytes);

        Assert.False(MessagePayload.TryFrom(json, out _));
    }

    [Fact]
    public void 未初期化のペイロードは空として扱える()
    {
        MessagePayload payload = default;

        Assert.True(payload.IsEmpty);
        Assert.Equal(string.Empty, payload.Json);
    }
}
