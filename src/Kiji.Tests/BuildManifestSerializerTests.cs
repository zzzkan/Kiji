using System.Buffers;
using System.Text;
using System.Text.Json;
using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// The manifest is written and read by hand to keep a publish process short. The format
/// remains the serializer's, so a manifest written either way reads back the same.
/// </summary>
public sealed class BuildManifestSerializerTests
{
    [Fact]
    public void Serialize_WritesTheSerializersBytes()
    {
        var manifest = CreateManifest();

        Assert.Equal(
            Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(manifest, BuildManifestJsonContext.Default.BuildManifest)),
            Encoding.UTF8.GetString(Serialize(manifest)));
    }

    [Fact]
    public void Deserialize_ReadsWhatTheSerializerReads()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(CreateManifest(), BuildManifestJsonContext.Default.BuildManifest);

        var manifest = BuildManifestSerializer.Deserialize(json);

        Assert.NotNull(manifest);
        Assert.True(manifest.IsValid());
        Assert.Equal(Encoding.UTF8.GetString(json), Encoding.UTF8.GetString(Serialize(manifest)));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"Pages":null,"CodeDependencies":null,"OptionsHash":null}""")]
    [InlineData("""{"Unknown":{"nested":[1,{"a":null}]},"SchemaVersion":6,"SchemaVersion":7,"Pages":[null,{"Stamp":null,"Unknown":1,"Dependencies":[null,{}],"AdditionalOutputs":[{"Stamp":null},{}],"ImageRequests":[null,{}],"Images":[null]}]}""")]
    [InlineData("""{"Artifacts":["b","a"],"StaticFiles":[],"Pages":[{"Images":["x"],"HtmlLength":3,"OutputRelativePath":"index.html"}],"SchemaVersion":6}""")]
    public void Deserialize_TreatsUnusualMembersAsTheSerializerDoes(string json)
    {
        var expected = JsonSerializer.Deserialize(json, BuildManifestJsonContext.Default.BuildManifest)!;

        var actual = BuildManifestSerializer.Deserialize(Encoding.UTF8.GetBytes(json))!;

        Assert.Equal(
            Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(expected, BuildManifestJsonContext.Default.BuildManifest)),
            Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(actual, BuildManifestJsonContext.Default.BuildManifest)));
        Assert.Equal(expected.IsValid(), actual.IsValid());
    }

    [Fact]
    public void Deserialize_ReadsNullAsNoManifest()
    {
        Assert.Null(BuildManifestSerializer.Deserialize("null"u8));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"SchemaVersion":"6"}""")]
    [InlineData("""{"SchemaVersion":6.5}""")]
    [InlineData("""{"OptionsHash":1}""")]
    [InlineData("""{"Pages":{}}""")]
    [InlineData("""{"Pages":["page"]}""")]
    [InlineData("""{"Pages":[{"HtmlOffset":99999999999}]}""")]
    [InlineData("""{"Pages":[{"Stamp":{"LastWriteTimeUtc":"yesterday"}}]}""")]
    [InlineData("""{"Pages":[{"Dependencies":[1]}]}""")]
    [InlineData("""{"CodeDependencies":[["a"]]}""")]
    [InlineData("""{"SchemaVersion":6""")]
    [InlineData("""{"SchemaVersion":6} {}""")]
    public void Deserialize_RejectsWhatTheSerializerRejects(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, BuildManifestJsonContext.Default.BuildManifest));
        Assert.ThrowsAny<JsonException>(() => BuildManifestSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    private static byte[] Serialize(BuildManifest manifest)
    {
        var output = new ArrayBufferWriter<byte>();
        BuildManifestSerializer.Serialize(output, manifest);
        return output.WrittenSpan.ToArray();
    }

    private static BuildManifest CreateManifest()
    {
        var hash = new string('a', 64);
        var stamp = new OutputStamp(1234, new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9012));
        return new BuildManifest
        {
            SchemaVersion = BuildManifest.CurrentSchemaVersion,
            OptionsHash = hash,
            CodeDependencies = ["Site:mvid:0123", "Kiji:unavailable"],
            HtmlFile = "html-0123.bin",
            OutputDirectoryHash = new string('b', 64),
            Pages =
            [
                new BuildManifestPage("blog/日本語 <&'\"+>/index.html", "/blog/日本語 <&'\"+>/", hash, hash,
                    [new BuildManifestDependency("file", "content/a\\b\u0001.md", hash), new BuildManifestDependency("value", "", "x")],
                    [new BuildManifestOutput("_images/a.webp", hash) { Stamp = stamp }, new BuildManifestOutput("_images/b.webp", hash)])
                {
                    HtmlOffset = 12,
                    HtmlLength = 3456,
                    Stamp = stamp,
                    ImageRequests = [new BuildManifestImage("content/a.png", ".")],
                    Images = [hash],
                },
                new BuildManifestPage("index.html", "/", null, hash, [], []),
            ],
            StaticFiles = ["css/site.css"],
            Artifacts = ["feed.xml", "sitemap.xml"],
        };
    }
}
