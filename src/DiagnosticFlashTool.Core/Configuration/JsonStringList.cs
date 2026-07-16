using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiagnosticFlashTool.Core.Configuration;

[JsonConverter(typeof(JsonStringListConverter))]
public sealed class JsonStringList : IEnumerable<string>
{
    private readonly List<string> _items = [];

    public static implicit operator JsonStringList(List<string> items)
    {
        var list = new JsonStringList();
        list._items.AddRange(items);
        return list;
    }

    public int Count => _items.Count;

    public string this[int index] => _items[index];

    public void Add(string value) => _items.Add(value);

    public List<string> ToList() => [.. _items];

    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class JsonStringListConverter : JsonConverter<JsonStringList>
{
    public override JsonStringList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = new JsonStringList();

        if (reader.TokenType == JsonTokenType.String)
        {
            values.Add(reader.GetString() ?? string.Empty);
            return values;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return values;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            values.Add(reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.GetInt64().ToString(),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                _ => string.Empty
            });
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, JsonStringList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
