using System.Buffers;
using System.Text.Json;

namespace Kiji.Generation;

internal static class BuildManifestSerializer
{
    private static readonly JsonEncodedText SchemaVersion = JsonEncodedText.Encode("SchemaVersion");
    private static readonly JsonEncodedText OptionsHash = JsonEncodedText.Encode("OptionsHash");
    private static readonly JsonEncodedText CodeDependencies = JsonEncodedText.Encode("CodeDependencies");
    private static readonly JsonEncodedText HtmlFile = JsonEncodedText.Encode("HtmlFile");
    private static readonly JsonEncodedText OutputDirectoryHash = JsonEncodedText.Encode("OutputDirectoryHash");
    private static readonly JsonEncodedText Pages = JsonEncodedText.Encode("Pages");
    private static readonly JsonEncodedText StaticFiles = JsonEncodedText.Encode("StaticFiles");
    private static readonly JsonEncodedText Artifacts = JsonEncodedText.Encode("Artifacts");
    private static readonly JsonEncodedText OutputRelativePath = JsonEncodedText.Encode("OutputRelativePath");
    private static readonly JsonEncodedText RoutePath = JsonEncodedText.Encode("RoutePath");
    private static readonly JsonEncodedText ParametersHash = JsonEncodedText.Encode("ParametersHash");
    private static readonly JsonEncodedText OutputHash = JsonEncodedText.Encode("OutputHash");
    private static readonly JsonEncodedText Dependencies = JsonEncodedText.Encode("Dependencies");
    private static readonly JsonEncodedText AdditionalOutputs = JsonEncodedText.Encode("AdditionalOutputs");
    private static readonly JsonEncodedText HtmlOffset = JsonEncodedText.Encode("HtmlOffset");
    private static readonly JsonEncodedText HtmlLength = JsonEncodedText.Encode("HtmlLength");
    private static readonly JsonEncodedText Stamp = JsonEncodedText.Encode("Stamp");
    private static readonly JsonEncodedText ImageRequests = JsonEncodedText.Encode("ImageRequests");
    private static readonly JsonEncodedText Images = JsonEncodedText.Encode("Images");
    private static readonly JsonEncodedText Kind = JsonEncodedText.Encode("Kind");
    private static readonly JsonEncodedText Key = JsonEncodedText.Encode("Key");
    private static readonly JsonEncodedText Fingerprint = JsonEncodedText.Encode("Fingerprint");
    private static readonly JsonEncodedText RelativePath = JsonEncodedText.Encode("RelativePath");
    private static readonly JsonEncodedText Hash = JsonEncodedText.Encode("Hash");
    private static readonly JsonEncodedText Source = JsonEncodedText.Encode("Source");
    private static readonly JsonEncodedText OutputDirectory = JsonEncodedText.Encode("OutputDirectory");
    private static readonly JsonEncodedText Length = JsonEncodedText.Encode("Length");
    private static readonly JsonEncodedText LastWriteTimeUtc = JsonEncodedText.Encode("LastWriteTimeUtc");

    internal static void Serialize(IBufferWriter<byte> output, BuildManifest manifest)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { SkipValidation = true });
        writer.WriteStartObject();
        writer.WriteNumber(SchemaVersion, manifest.SchemaVersion);
        writer.WriteString(OptionsHash, manifest.OptionsHash);
        WriteStrings(writer, CodeDependencies, manifest.CodeDependencies);
        writer.WriteString(HtmlFile, manifest.HtmlFile);
        writer.WriteString(OutputDirectoryHash, manifest.OutputDirectoryHash);
        if (manifest.Pages is { } pages)
        {
            writer.WriteStartArray(Pages);
            for (var i = 0; i < pages.Count; i++) { WritePage(writer, pages[i]); }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull(Pages);
        }
        WriteStrings(writer, StaticFiles, manifest.StaticFiles);
        WriteStrings(writer, Artifacts, manifest.Artifacts);
        writer.WriteEndObject();
    }

    internal static BuildManifest? Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            reader.Read();
            var manifest = reader.TokenType == JsonTokenType.Null ? null : ReadManifest(ref reader);
            return reader.Read() ? throw new JsonException("Unexpected content after the manifest.") : manifest;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new JsonException("The manifest has an unexpected shape.", exception);
        }
    }

    private static void WritePage(Utf8JsonWriter writer, BuildManifestPage? page)
    {
        if (page is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString(OutputRelativePath, page.OutputRelativePath);
        writer.WriteString(RoutePath, page.RoutePath);
        writer.WriteString(ParametersHash, page.ParametersHash);
        writer.WriteString(OutputHash, page.OutputHash);
        if (page.Dependencies is { } dependencies)
        {
            writer.WriteStartArray(Dependencies);
            for (var i = 0; i < dependencies.Count; i++)
            {
                if (dependencies[i] is not { } dependency) { writer.WriteNullValue(); continue; }
                writer.WriteStartObject();
                writer.WriteString(Kind, dependency.Kind);
                writer.WriteString(Key, dependency.Key);
                writer.WriteString(Fingerprint, dependency.Fingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull(Dependencies);
        }
        if (page.AdditionalOutputs is { } outputs)
        {
            writer.WriteStartArray(AdditionalOutputs);
            for (var i = 0; i < outputs.Count; i++)
            {
                if (outputs[i] is not { } output) { writer.WriteNullValue(); continue; }
                writer.WriteStartObject();
                writer.WriteString(RelativePath, output.RelativePath);
                writer.WriteString(Hash, output.Hash);
                if (output.Stamp is { } stamp) { WriteStamp(writer, stamp); }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull(AdditionalOutputs);
        }
        writer.WriteNumber(HtmlOffset, page.HtmlOffset);
        writer.WriteNumber(HtmlLength, page.HtmlLength);
        if (page.Stamp is { } pageStamp) { WriteStamp(writer, pageStamp); }
        else { writer.WriteNull(Stamp); }
        if (page.ImageRequests is { } images)
        {
            writer.WriteStartArray(ImageRequests);
            for (var i = 0; i < images.Count; i++)
            {
                if (images[i] is not { } image) { writer.WriteNullValue(); continue; }
                writer.WriteStartObject();
                writer.WriteString(Source, image.Source);
                writer.WriteString(OutputDirectory, image.OutputDirectory);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull(ImageRequests);
        }
        WriteStrings(writer, Images, page.Images);
        writer.WriteEndObject();
    }

    private static void WriteStamp(Utf8JsonWriter writer, OutputStamp stamp)
    {
        writer.WriteStartObject(Stamp);
        writer.WriteNumber(Length, stamp.Length);
        writer.WriteString(LastWriteTimeUtc, stamp.LastWriteTimeUtc);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, JsonEncodedText name, IReadOnlyList<string>? values)
    {
        if (values is null) { writer.WriteNull(name); return; }
        writer.WriteStartArray(name);
        for (var i = 0; i < values.Count; i++) { writer.WriteStringValue(values[i]); }
        writer.WriteEndArray();
    }

    private static BuildManifest ReadManifest(ref Utf8JsonReader reader)
    {
        ExpectStartObject(ref reader);
        var schemaVersion = 0;
        string? optionsHash = null, htmlFile = null, outputDirectoryHash = null;
        IReadOnlyList<string>? codeDependencies = null, staticFiles = null, artifacts = null;
        IReadOnlyList<BuildManifestPage>? pages = null;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(SchemaVersion.EncodedUtf8Bytes)) { reader.Read(); schemaVersion = reader.GetInt32(); }
            else if (reader.ValueTextEquals(OptionsHash.EncodedUtf8Bytes)) { reader.Read(); optionsHash = reader.GetString(); }
            else if (reader.ValueTextEquals(CodeDependencies.EncodedUtf8Bytes)) { codeDependencies = ReadStrings(ref reader); }
            else if (reader.ValueTextEquals(HtmlFile.EncodedUtf8Bytes)) { reader.Read(); htmlFile = reader.GetString(); }
            else if (reader.ValueTextEquals(OutputDirectoryHash.EncodedUtf8Bytes)) { reader.Read(); outputDirectoryHash = reader.GetString(); }
            else if (reader.ValueTextEquals(Pages.EncodedUtf8Bytes)) { pages = ReadList(ref reader, ReadPage); }
            else if (reader.ValueTextEquals(StaticFiles.EncodedUtf8Bytes)) { staticFiles = ReadStrings(ref reader); }
            else if (reader.ValueTextEquals(Artifacts.EncodedUtf8Bytes)) { artifacts = ReadStrings(ref reader); }
            else { reader.Skip(); }
        }
        return new BuildManifest
        {
            SchemaVersion = schemaVersion,
            OptionsHash = optionsHash!,
            CodeDependencies = codeDependencies!,
            HtmlFile = htmlFile!,
            OutputDirectoryHash = outputDirectoryHash!,
            Pages = pages!,
            StaticFiles = staticFiles!,
            Artifacts = artifacts!,
        };
    }

    private static BuildManifestPage ReadPage(ref Utf8JsonReader reader)
    {
        ExpectStartObject(ref reader);
        string? outputRelativePath = null, routePath = null, parametersHash = null, outputHash = null;
        IReadOnlyList<BuildManifestDependency>? dependencies = null;
        IReadOnlyList<BuildManifestOutput>? additionalOutputs = null;
        IReadOnlyList<BuildManifestImage>? imageRequests = null;
        IReadOnlyList<string>? images = null;
        var htmlOffset = 0;
        var htmlLength = 0;
        OutputStamp? stamp = null;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(OutputRelativePath.EncodedUtf8Bytes)) { reader.Read(); outputRelativePath = reader.GetString(); }
            else if (reader.ValueTextEquals(RoutePath.EncodedUtf8Bytes)) { reader.Read(); routePath = reader.GetString(); }
            else if (reader.ValueTextEquals(ParametersHash.EncodedUtf8Bytes)) { reader.Read(); parametersHash = reader.GetString(); }
            else if (reader.ValueTextEquals(OutputHash.EncodedUtf8Bytes)) { reader.Read(); outputHash = reader.GetString(); }
            else if (reader.ValueTextEquals(Dependencies.EncodedUtf8Bytes)) { dependencies = ReadList(ref reader, ReadDependency); }
            else if (reader.ValueTextEquals(AdditionalOutputs.EncodedUtf8Bytes)) { additionalOutputs = ReadList(ref reader, ReadOutput); }
            else if (reader.ValueTextEquals(HtmlOffset.EncodedUtf8Bytes)) { reader.Read(); htmlOffset = reader.GetInt32(); }
            else if (reader.ValueTextEquals(HtmlLength.EncodedUtf8Bytes)) { reader.Read(); htmlLength = reader.GetInt32(); }
            else if (reader.ValueTextEquals(Stamp.EncodedUtf8Bytes)) { stamp = ReadStamp(ref reader); }
            else if (reader.ValueTextEquals(ImageRequests.EncodedUtf8Bytes)) { imageRequests = ReadList(ref reader, ReadImage); }
            else if (reader.ValueTextEquals(Images.EncodedUtf8Bytes)) { images = ReadStrings(ref reader); }
            else { reader.Skip(); }
        }
        return new BuildManifestPage(outputRelativePath!, routePath!, parametersHash, outputHash!, dependencies!, additionalOutputs!)
        {
            HtmlOffset = htmlOffset,
            HtmlLength = htmlLength,
            Stamp = stamp,
            ImageRequests = imageRequests!,
            Images = images!,
        };
    }

    private static BuildManifestDependency ReadDependency(ref Utf8JsonReader reader)
    {
        ExpectStartObject(ref reader);
        string? kind = null, key = null, fingerprint = null;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(Kind.EncodedUtf8Bytes)) { reader.Read(); kind = reader.GetString(); }
            else if (reader.ValueTextEquals(Key.EncodedUtf8Bytes)) { reader.Read(); key = reader.GetString(); }
            else if (reader.ValueTextEquals(Fingerprint.EncodedUtf8Bytes)) { reader.Read(); fingerprint = reader.GetString(); }
            else { reader.Skip(); }
        }
        return new BuildManifestDependency(kind!, key!, fingerprint!);
    }

    private static BuildManifestOutput ReadOutput(ref Utf8JsonReader reader)
    {
        ExpectStartObject(ref reader);
        string? relativePath = null, hash = null;
        OutputStamp? stamp = null;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(RelativePath.EncodedUtf8Bytes)) { reader.Read(); relativePath = reader.GetString(); }
            else if (reader.ValueTextEquals(Hash.EncodedUtf8Bytes)) { reader.Read(); hash = reader.GetString(); }
            else if (reader.ValueTextEquals(Stamp.EncodedUtf8Bytes)) { stamp = ReadStamp(ref reader); }
            else { reader.Skip(); }
        }
        return new BuildManifestOutput(relativePath!, hash!) { Stamp = stamp };
    }

    private static BuildManifestImage ReadImage(ref Utf8JsonReader reader)
    {
        ExpectStartObject(ref reader);
        string? source = null, outputDirectory = null;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(Source.EncodedUtf8Bytes)) { reader.Read(); source = reader.GetString(); }
            else if (reader.ValueTextEquals(OutputDirectory.EncodedUtf8Bytes)) { reader.Read(); outputDirectory = reader.GetString(); }
            else { reader.Skip(); }
        }
        return new BuildManifestImage(source!, outputDirectory!);
    }

    private static OutputStamp? ReadStamp(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null) { return null; }
        ExpectStartObject(ref reader);
        long length = 0;
        DateTime lastWriteTimeUtc = default;
        while (ReadProperty(ref reader))
        {
            if (reader.ValueTextEquals(Length.EncodedUtf8Bytes)) { reader.Read(); length = reader.GetInt64(); }
            else if (reader.ValueTextEquals(LastWriteTimeUtc.EncodedUtf8Bytes)) { reader.Read(); lastWriteTimeUtc = reader.GetDateTime(); }
            else { reader.Skip(); }
        }
        return new OutputStamp(length, lastWriteTimeUtc);
    }

    private static List<string>? ReadStrings(ref Utf8JsonReader reader)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null) { return null; }
        if (reader.TokenType != JsonTokenType.StartArray) { throw new JsonException("Expected an array."); }
        var values = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(reader.GetString()!);
        }
        return values;
    }

    private static List<T>? ReadList<T>(ref Utf8JsonReader reader, ElementReader<T> readElement)
        where T : class
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null) { return null; }
        if (reader.TokenType != JsonTokenType.StartArray) { throw new JsonException("Expected an array."); }
        var values = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(reader.TokenType == JsonTokenType.Null ? null! : readElement(ref reader));
        }
        return values;
    }

    private static void ExpectStartObject(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) { throw new JsonException("Expected an object."); }
    }

    private static bool ReadProperty(ref Utf8JsonReader reader) =>
        reader.Read() && reader.TokenType == JsonTokenType.PropertyName;

    private delegate T ElementReader<T>(ref Utf8JsonReader reader);
}
