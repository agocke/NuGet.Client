// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System.Text.Json.Serialization;

namespace NuGet.Protocol.Plugins
{
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(GetCredentialsRequest))]
    [JsonSerializable(typeof(GetServiceIndexRequest))]
    [JsonSerializable(typeof(MonitorNuGetProcessExitRequest))]
    [JsonSerializable(typeof(HandshakeRequest))]
    [JsonSerializable(typeof(LogRequest))]
    [JsonSerializable(typeof(Fault))]
    internal partial class PluginJsonContext : JsonSerializerContext
    {
    }
}
