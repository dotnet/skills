// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources.Models;

namespace Azure.ResourceManager
{
    [JsonSourceGenerationOptions(Converters = [typeof(ResponseErrorConverter)])]
    [JsonSerializable(typeof(ManagedServiceIdentityType))]
    [JsonSerializable(typeof(UserAssignedIdentity))]
    [JsonSerializable(typeof(SystemData))]
    [JsonSerializable(typeof(OperationStatusResult))]
    [JsonSerializable(typeof(ManagedServiceIdentity))]
    [JsonSerializable(typeof(ArmPlan))]
    [JsonSerializable(typeof(SubResource))]
    [JsonSerializable(typeof(ExtendedLocation))]
    [JsonSerializable(typeof(ArmEnvironment))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(Dictionary<string, Dictionary<string, JsonElement>>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class ResourceManagerJsonContext : JsonSerializerContext
    {
        private static JsonTypeInfo<ResponseError> _responseError;

        /// <summary>
        /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="ResponseError"/>.
        /// Manually defined because the built-in <see cref="JsonConverter"/> on <see cref="ResponseError"/>
        /// is internal and inaccessible to the System.Text.Json source generator.
        /// </summary>
        public JsonTypeInfo<ResponseError> ResponseError => _responseError ??= JsonMetadataServices.CreateValueInfo<ResponseError>(Options, new ResponseErrorConverter());
    }
}
