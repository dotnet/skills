// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.ResourceManager
{
    /// <summary>
    /// A custom <see cref="JsonConverter{T}"/> for <see cref="ResponseError"/> that can be used
    /// with the System.Text.Json source generator, since the built-in converter on
    /// <see cref="ResponseError"/> is internal and inaccessible to source generation.
    /// </summary>
    internal sealed class ResponseErrorConverter : JsonConverter<ResponseError>
    {
        public override ResponseError? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadResponseError(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, ResponseError value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            if (value.Code is not null)
            {
                writer.WriteString("code"u8, value.Code);
            }

            if (value.Message is not null)
            {
                writer.WriteString("message"u8, value.Message);
            }

            writer.WriteEndObject();
        }

        private static ResponseError? ReadResponseError(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            string? code = null;
            if (element.TryGetProperty("code", out var property))
            {
                code = property.GetString();
            }

            string? message = null;
            if (element.TryGetProperty("message", out property))
            {
                message = property.GetString();
            }

            return new ResponseError(code, message);
        }
    }
}
